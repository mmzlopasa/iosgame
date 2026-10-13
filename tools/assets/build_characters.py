#!/usr/bin/env python3
"""Builds the villagers (unity/Assets/Art/Resources/Models/Person_<Name>.fbx) and
Resources/people.json (who is male, female or elder).

Sources (CC0, Quaternius, free "Standard" versions on itch.io, downloaded with itch_fetch.py):
- Universal Base Characters: realistic heads with eyes and eyebrows, and the hairstyles
- Modular Character Outfits - Fantasy: peasant and ranger clothes (shirts, jerkins, hoods,
  trousers, boots) made for those bodies
Both use the Unreal-style skeleton of the Universal Animation Library, so the game's clips in
Villager.fbx (build_people.py) retarget onto them directly. blender_characters.py assembles each
person (see there), adds a long skirt for some women and a stoop for the elders, and attaches
the hand tools. The packs' PBR textures are baked per person onto the game mesh
(unity/Assets/Art/Textures/People/Person_<Name>_albedo.jpg, _normal.jpg, _mask.png; 1024 px); the game
dyes the main garment and the hair and shifts the skin tone per villager (people.json says how).

Usage: python3 tools/assets/build_characters.py [--preview] [Name ...]   (needs Blender 4.2 on PATH,
Python 3 with numpy and pillow; build Villager.fbx first with build_people.py, which also fetches
the Universal Animation Library 2 used for the extra "Lie" clip). --preview renders the finished
people (blender_people_preview.py) to tools/assets/.cache/packs/people_preview.png.
"""
import glob
import json
import os
import subprocess
import sys
import zipfile
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import itch_fetch  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CACHE = os.path.join(HERE, ".cache", "packs")
MODELS = os.path.join(ROOT, "unity", "Assets", "Art", "Resources", "Models")
MANIFEST = os.path.join(ROOT, "unity", "Assets", "Art", "Resources", "people.json")
TEXTURES = os.path.join(ROOT, "unity", "Assets", "Art", "Textures", "People")
UAL2 = os.path.join(HERE, ".cache", "ual", "Universal Animation Library 2 [Standard]", "Unreal-Godot", "UAL2_Standard.glb")
PACKS = {
    "ubc": ("https://quaternius.itch.io/universal-base-characters", "Standard", "Universal Base Characters[Standard]"),
    "outfits": ("https://quaternius.itch.io/modular-character-outfits-fantasy", "Standard",
                "Modular Character Outfits - Fantasy[Standard]"),
}

# Era colours (sRGB): undyed linen and wool, plant dyes, leather.
LINEN, WOOL, OAT = (0.78, 0.72, 0.58), (0.56, 0.53, 0.48), (0.66, 0.58, 0.44)
BROWN, DARK = (0.40, 0.29, 0.19), (0.24, 0.18, 0.13)
WOAD, MADDER, OCHRE, MOSS, RUST = (0.30, 0.40, 0.54), (0.56, 0.25, 0.18), (0.70, 0.55, 0.28), (0.36, 0.42, 0.25), (0.55, 0.33, 0.20)
LEATHER, DLEATHER = (0.42, 0.28, 0.17), (0.26, 0.18, 0.12)


def people(ubc, outfits):
    base = os.path.join(ubc, "Base Characters", "Godot - UE")
    hair = os.path.join(ubc, "Hairstyles", "Rigged to Head Bone", "glTF (Godot -Unreal)")
    out = os.path.join(outfits, "Exports", "glTF (Godot-Unreal)", "Outfits")
    male = os.path.join(base, "Superhero_Male_FullBody.gltf")
    female = os.path.join(base, "Superhero_Female_FullBody.gltf")

    def h(*names):
        return [os.path.join(hair, n + ".gltf") for n in names]

    def outfit(sex, kind, *pieces):
        return [[os.path.join(out, f"{sex}_{kind}.gltf"), f"{sex}_{kind}_{p}"] for p in pieces]

    mp = outfit("Male", "Peasant", "Arms", "Body", "Legs", "Feet")
    fp = outfit("Female", "Peasant", "Arms", "Body", "Legs", "Feet")
    return {
        # name: (gender, age, spec)
        "Farmer_Male": ("male", "adult", dict(base=male, parts=mp, hair=h("Hair_SimpleParted"),
                                              colors=dict(Main=LINEN, Trousers=BROWN, Boots=LEATHER))),
        "Woodcutter_Male": ("male", "adult", dict(base=male, parts=mp, hair=h("Hair_Buzzed", "Hair_Beard"),
                                                  colors=dict(Main=MOSS, Trousers=WOOL, Boots=DLEATHER))),
        "Hunter_Male": ("male", "adult", dict(dye=False, base=male, hair=h("Hair_Long", "Hair_Beard"),
                                              parts=outfit("Male", "Ranger", "Arms", "Arms_Bracer", "Body", "Body_Belt_1",
                                                           "Legs", "Feet_Boots"),
                                              colors=dict(Main=LEATHER, Trousers=DARK, Boots=DLEATHER))),
        "Hooded_Male": ("male", "adult", dict(dye=False, base=male, hair=h("Hair_Beard"), hood=True,
                                              parts=outfit("Male", "Ranger", "Body", "Body_Belt_1", "Head_Hood", "Arms")
                                              + outfit("Male", "Peasant", "Legs", "Feet"),
                                              colors=dict(Main=RUST, Trousers=WOOL, Boots=LEATHER, Hood=BROWN))),
        "Elder_Male": ("male", "elder", dict(base=male, parts=mp, hair=h("Hair_Buzzed", "Hair_Beard"), elder=True,
                                             colors=dict(Main=WOOL, Trousers=DARK, Boots=DLEATHER))),
        "Farmer_Female": ("female", "adult", dict(base=female, parts=fp, hair=h("Hair_Buns"), skirt=True, fold=0.4,
                                                  colors=dict(Main=WOAD, Top=LINEN, Boots=LEATHER))),
        "Weaver_Female": ("female", "adult", dict(base=female, parts=fp, hair=h("Hair_Long"), skirt=True, fold=0.4,
                                                  colors=dict(Main=MADDER, Top=OAT, Boots=DLEATHER))),
        "Hunter_Female": ("female", "adult", dict(dye=False, base=female, hair=h("Hair_BuzzedFemale"), hood=True,
                                                  parts=outfit("Female", "Ranger", "Arms", "Arms_Bracer", "Body",
                                                               "Body_Belt_1", "Head_Hood", "Legs", "Feet"),
                                                  colors=dict(Main=MOSS, Trousers=BROWN, Boots=DLEATHER, Hood=MOSS))),
        "Gatherer_Female": ("female", "adult", dict(base=female, hair=h("Hair_Long"), skirt=True,
                                                    parts=outfit("Female", "Ranger", "Arms", "Body", "Body_Belt_1")
                                                    + outfit("Female", "Peasant", "Feet"),
                                                    colors=dict(Main=OCHRE, Top=LEATHER, Boots=LEATHER))),
        "Elder_Female": ("female", "elder", dict(base=female, parts=fp, hair=h("Hair_Buns"), skirt=True, fold=0.4, elder=True,
                                                 colors=dict(Main=DARK, Top=WOOL, Boots=DLEATHER))),
    }


def fetch_pack(key):
    url, upload, folder = PACKS[key]
    d = os.path.join(CACHE, key)
    zpath = itch_fetch.fetch(url, upload, os.path.join(d, key + "_standard.zip"))
    root = os.path.join(d, folder)
    if not os.path.isdir(root):
        with zipfile.ZipFile(zpath) as z:
            z.extractall(d)
    lic = open(glob.glob(os.path.join(glob.escape(root), "License*.txt"))[0]).read()
    if "CC0" not in lic:
        sys.exit(f"{key}: licence is not CC0")
    return root


def save_map(npy, path, mode):
    """A baked map (float rows bottom to top) as an 8-bit image for Unity: JPEG for colour and normals
    (like the other textures of the game), PNG for the mask (it needs the alpha channel)."""
    import numpy as np
    from PIL import Image
    a = np.load(npy)[::-1]
    im = Image.fromarray(np.round(a * 255).astype(np.uint8), mode)
    if path.endswith(".jpg"):
        im.save(path, quality=92, subsampling=0, optimize=True)
    else:
        im.save(path, optimize=True)
    os.remove(npy)


def build(item):
    name, (gender, age, spec), preview = item
    out = os.path.join(MODELS, f"Person_{name}.fbx")
    spec_path = os.path.join(CACHE, "people", name + ".json")
    os.makedirs(os.path.dirname(spec_path), exist_ok=True)
    os.makedirs(TEXTURES, exist_ok=True)
    tex = os.path.join(CACHE, "people", f"Person_{name}")
    spec = dict(spec, textures=tex, tex_size=1024, tris=7400, lod_tris=2400)
    if os.path.exists(UAL2):
        spec["extra_anims"] = [[UAL2, "LayToIdle", "Lie", 0.0]]
    json.dump(spec, open(spec_path, "w"))
    args = ["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_characters.py"), "--",
            os.path.join(ROOT, "unity", "Assets", "Art", "Source", "Villager.fbx"), spec_path, out]
    r = subprocess.run(args, capture_output=True, text=True)
    line = next((l for l in r.stdout.splitlines() if l.startswith("CHARACTER_OK")), None)
    if r.returncode or not line:
        sys.exit(f"{name} failed:\n{r.stdout[-3000:]}\n{r.stderr[-3000:]}")
    info = json.loads(line.split(" ", 1)[1])
    maps = {}
    for kind, mode, ext in (("albedo", "RGB", "jpg"), ("normal", "RGB", "jpg"), ("mask", "RGBA", "png")):
        path = os.path.join(TEXTURES, f"Person_{name}_{kind}.{ext}")
        save_map(f"{tex}_{kind}.npy", path, mode)
        maps[kind] = os.path.relpath(path, os.path.join(ROOT, "unity", "Assets")).replace(os.sep, "/")
    print(f"person: {name} {info['tris']} tris (LOD1 {info['lod_tris']}) {os.path.getsize(out) // 1024} KB "
          f"{info['materials']} clips {len(info['clips'])}", flush=True)
    return {"model": f"Models/Person_{name}", "gender": gender, "age": age, "textures": maps,
            "skin": info["skin"], "main": [round(c, 3) for c in info["main"]], "hair": [round(c, 3) for c in info["hair"]],
            "neutral": info["neutral"], "dye": info["dye"], "tris": info["tris"], "lodTris": info["lod_tris"]}


def main():
    preview = "--preview" in sys.argv
    ubc, outfits = fetch_pack("ubc"), fetch_pack("outfits")
    roster = people(ubc, outfits)
    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    items = [(n, v, preview) for n, v in roster.items() if not only or n in only]
    with ThreadPoolExecutor(max_workers=int(os.environ.get("JOBS", "2"))) as ex:
        entries = list(ex.map(build, items))
    if only:
        # Partial rebuild: update those people in the existing manifest.
        old = json.load(open(MANIFEST))["people"] if os.path.exists(MANIFEST) else []
        new = {e["model"]: e for e in entries}
        entries = [new.pop(e["model"], e) for e in old] + list(new.values())
    # Drop people that are no longer in the roster (and their Unity .meta files).
    keep = {f"Person_{n}.fbx" for n in roster}
    for f in glob.glob(os.path.join(MODELS, "Person_*.fbx")):
        if os.path.basename(f) not in keep:
            os.remove(f)
            if os.path.exists(f + ".meta"):
                os.remove(f + ".meta")
    with open(MANIFEST, "w") as f:
        json.dump({"source": "Quaternius Universal Base Characters + Modular Character Outfits - Fantasy (CC0)",
                   "people": entries}, f, indent=1)
    print("manifest:", MANIFEST)
    if preview:
        subprocess.run(["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_people_preview.py"), "--",
                        MANIFEST, os.path.join(CACHE, "people_preview.png")], check=True)


if __name__ == "__main__":
    main()
