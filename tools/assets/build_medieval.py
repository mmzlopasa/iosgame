#!/usr/bin/env python3
"""Builds the medieval buildings for the game from the CC0 Quaternius "Medieval Village MegaKit".

1. blender_medieval.py assembles each building from the kit modules (glTF, separate textures)
2. blender_decimate.py turns each one into an FBX (pivot at the bottom centre, Unity axes)
3. the kit textures are written once (Art/Textures/medieval__*) and shared by every building,
   and each building is registered in art_manifest.json like the other models

Usage: python3 tools/assets/build_medieval.py   (needs Blender 4.2 on PATH; the kit is
downloaded into tools/assets/.cache/packs, see PACKS.md)
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_assets as ba  # noqa: E402

KIT = os.path.join(HERE, ".cache", "packs", "medieval", "glTF")
WORK = os.path.join(HERE, ".cache", "medieval_out")
SOURCE = "https://opengameart.org/content/medieval-village-megakit (Quaternius, CC0)"

# Triangle budgets: the kit is already light; only the church is reduced.
BUDGET = {
    "medieval_cottage": 6000, "medieval_house": 10000, "medieval_townhouse": 15500,
    "medieval_church": 26000, "medieval_market": 6500, "medieval_mill": 11000, "medieval_smithy": 7500,
    "medieval_barn": 9000,
}


def main():
    subprocess.run(["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_medieval.py"), "--", KIT, WORK],
                   check=True, stdout=subprocess.DEVNULL)
    manifest = json.load(open(ba.MANIFEST))
    shared = {}
    os.makedirs(ba.OUT_TEX, exist_ok=True)
    os.makedirs(ba.OUT_MODELS, exist_ok=True)
    for mid, target in BUDGET.items():
        folder = os.path.join(WORK, mid)
        gltf = os.path.join(folder, mid + ".gltf")
        fbx = os.path.join(ba.OUT_MODELS, mid + ".fbx")
        info_path = os.path.join(folder, "info.json")
        subprocess.run(["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_decimate.py"), "--",
                        gltf, fbx, str(target), info_path], check=True, stdout=subprocess.DEVNULL)
        info = json.load(open(info_path))
        doc = json.load(open(gltf))
        images = [img.get("uri") for img in doc.get("images", [])]
        tex_img = [t.get("source") for t in doc.get("textures", [])]

        def tex(ref):
            return None if ref is None else os.path.join(folder, images[tex_img[ref["index"]]])

        mats = {}
        for m in doc.get("materials", []):
            name = m.get("name")
            if name not in info["materials"]:
                continue
            if name not in shared:
                pbr = m.get("pbrMetallicRoughness", {})
                alpha = m.get("alphaMode", "OPAQUE") != "OPAQUE"
                key = "medieval__" + name
                entry = {"alphaClip": alpha, "doubleSided": bool(m.get("doubleSided", False))}
                base, normal, arm = tex(pbr.get("baseColorTexture")), tex(m.get("normalTexture")), tex(pbr.get("metallicRoughnessTexture"))
                if base:
                    dst = os.path.join(ba.OUT_TEX, f"{key}_albedo.{'png' if alpha else 'jpg'}")
                    (ba.save_albedo_alpha(base, None, dst, 1024) if alpha else ba.save_albedo(base, dst, 1024))
                    entry["albedo"] = "Art/Textures/" + os.path.basename(dst)
                if normal:
                    ba.save_normal(normal, os.path.join(ba.OUT_TEX, f"{key}_normal.jpg"), 512)
                    entry["normal"] = f"Art/Textures/{key}_normal.jpg"
                if arm:
                    ba.save_mask(arm, os.path.join(ba.OUT_TEX, f"{key}_mask.png"), 512)
                    entry["mask"] = f"Art/Textures/{key}_mask.png"
                shared[name] = entry
            mats[name] = shared[name]
        manifest["models"][mid] = {
            "source": SOURCE,
            "resource": f"Models/{mid}",
            "triangles": info["triangles"],
            "size": info["size"],
            "materials": mats,
            "materialOrder": info["materials"],
        }
        print(f"{mid}: {info['source_triangles']} -> {info['triangles']} tris, size {info['size']}, {len(mats)} materials")
    with open(ba.MANIFEST, "w") as f:
        json.dump(manifest, f, indent=1, sort_keys=True)
    print("manifest:", ba.MANIFEST)


if __name__ == "__main__":
    main()
