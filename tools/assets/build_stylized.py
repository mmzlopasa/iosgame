#!/usr/bin/env python3
"""Moves the game to a stylized, hand-painted look (in the spirit of mobile survival builders).

Sources, all CC0:
- Quaternius "Stylized Nature MegaKit" (Standard, OpenGameArt): trees, rocks, bushes, plants
- Kenney "Survival Kit" (kenney.nl): campfire, tents, workbench, anvil, resource piles, tools, fences
- Kenney "Nature Kit" (kenney.nl): logs, stumps and small props

What it does:
1. tree, grass and flower billboards (the names the game already uses: PineA/B/C, LeafTree,
   FirSapling, GrassTuft, Wildflowers) are re-rendered from the stylized trees
2. stylized rocks, bushes, plants and survival props become models (ids sn_* and k_*)
3. the terrain and surface textures are repainted: flat, saturated colour with soft
   painterly variation instead of photographs; photo-scanned model textures are toon-filtered

Usage: python3 tools/assets/build_stylized.py [--only models|impostors|textures]
"""
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_assets as ba  # noqa: E402

PACKS = os.path.join(HERE, ".cache", "packs")
SN = os.path.join(PACKS, "stylized_nature", "glTF")
KS = os.path.join(PACKS, "kenney_survival", "Models", "GLB format")
KN = os.path.join(PACKS, "kenney_nature", "Models", "GLTF format")
WORK = os.path.join(HERE, ".cache", "stylized")
SRC_SN = "https://opengameart.org/content/stylized-nature-megakit (Quaternius, CC0)"
SRC_KS = "https://kenney.nl/assets/survival-kit (Kenney, CC0)"

IMPOSTORS = {
    "PineA": "Pine_1", "PineB": "Pine_2", "PineC": "Pine_3",
    "LeafTree": "CommonTree_1", "FirSapling": "Pine_5",
    "GrassTuft": "Grass_Common_Short", "Wildflowers": "Flower_3_Group",
}

# id: (pack folder, file, triangle budget)
MODELS = {
    "sn_rock_1": (SN, "Rock_Medium_1.gltf", 400), "sn_rock_2": (SN, "Rock_Medium_2.gltf", 400),
    "sn_rock_3": (SN, "Rock_Medium_3.gltf", 400), "sn_pebble": (SN, "Pebble_Round_3.gltf", 120),
    "sn_bush": (SN, "Bush_Common.gltf", 900), "sn_bush_flowers": (SN, "Bush_Common_Flowers.gltf", 1100),
    "sn_fern": (SN, "Fern_1.gltf", 500), "sn_plant": (SN, "Plant_1.gltf", 500), "sn_plant_big": (SN, "Plant_7_Big.gltf", 700),
    "sn_mushroom": (SN, "Mushroom_Common.gltf", 300), "sn_deadtree": (SN, "DeadTree_1.gltf", 900),
    "sn_flowers": (SN, "Flower_4_Group.gltf", 600), "sn_clover": (SN, "Clover_1.gltf", 300),
    "k_campfire": (KS, "campfire-pit.glb", 400), "k_campfire_stand": (KS, "campfire-stand.glb", 400),
    "k_tent": (KS, "tent.glb", 300), "k_tent_canvas": (KS, "tent-canvas.glb", 300),
    "k_workbench": (KS, "workbench.glb", 400), "k_anvil": (KS, "workbench-anvil.glb", 400),
    "k_wood": (KS, "resource-wood.glb", 200), "k_planks": (KS, "resource-planks.glb", 200),
    "k_stone": (KS, "resource-stone.glb", 300), "k_stone_large": (KS, "resource-stone-large.glb", 400),
    "k_rock_a": (KS, "rock-a.glb", 200), "k_rock_b": (KS, "rock-b.glb", 200), "k_rock_c": (KS, "rock-c.glb", 200),
    "k_log": (KS, "tree-log.glb", 300), "k_trunk": (KS, "tree-trunk.glb", 300),
    "k_chest": (KS, "chest.glb", 300), "k_barrel": (KS, "barrel.glb", 300), "k_box": (KS, "box.glb", 200),
    "k_bucket": (KS, "bucket.glb", 300), "k_bedroll": (KS, "bedroll.glb", 300),
    "k_fence": (KS, "fence.glb", 300), "k_fence_fortified": (KS, "fence-fortified.glb", 400),
    "k_signpost": (KS, "signpost.glb", 200), "k_fish_stand": (KS, "campfire-fishing-stand.glb", 400),
    "k_axe": (KS, "tool-axe.glb", 200), "k_pickaxe": (KS, "tool-pickaxe.glb", 200), "k_hoe": (KS, "tool-hoe.glb", 200),
}


def to_gltf(src, dst_dir, name):
    """Re-exports a model as glTF with separate textures (so the pipeline can read them)."""
    os.makedirs(dst_dir, exist_ok=True)
    out = os.path.join(dst_dir, name + ".gltf")
    if os.path.exists(out):
        return out
    script = ("import bpy,sys;a=sys.argv[sys.argv.index('--')+1:];bpy.ops.wm.read_factory_settings(use_empty=True);"
              "bpy.ops.import_scene.gltf(filepath=a[0]);"
              "bpy.ops.export_scene.gltf(filepath=a[1],export_format='GLTF_SEPARATE',export_image_format='AUTO')")
    subprocess.run(["blender", "-b", "--factory-startup", "--python-expr", script, "--", src, out],
                   check=True, stdout=subprocess.DEVNULL)
    return out


def build_models(manifest):
    for mid, (folder, file, budget) in MODELS.items():
        work = os.path.join(WORK, mid)
        gltf = to_gltf(os.path.join(folder, file), work, mid)
        ba.build_model_from_gltf(mid, gltf, work, budget, SRC_SN if folder == SN else SRC_KS, manifest)


def build_impostors(manifest):
    for name, model in IMPOSTORS.items():
        work = os.path.join(WORK, "impostor_" + name)
        os.makedirs(work, exist_ok=True)
        info_out = os.path.join(work, "info.json")
        subprocess.run(["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_impostor.py"), "--",
                        os.path.join(SN, model + ".gltf"), os.path.join(ba.OUT_TEX, f"{name}_impostor"), info_out],
                       check=True, stdout=subprocess.DEVNULL)
        info = json.load(open(info_out))
        manifest["impostors"][name] = {
            "source": SRC_SN,
            "side": f"Art/Textures/{name}_impostor_side.png",
            "side2": f"Art/Textures/{name}_impostor_side2.png",
            "top": f"Art/Textures/{name}_impostor_top.png",
            "height": info["height"], "width": info["width"],
        }
        print("impostor", name, "<-", model, info)


# MARK: - Painted textures

def noise(size, scale, seed):
    """Smooth value noise that tiles: the coarse grid wraps around before it is upsampled."""
    rng = np.random.default_rng(seed)
    n = max(2, size // scale)
    small = rng.random((n, n))
    pad = 3
    wrapped = np.pad(small, pad, mode="wrap")
    big = size * (n + 2 * pad) // n
    img = Image.fromarray((wrapped * 255).astype(np.uint8)).resize((big, big), Image.BICUBIC)
    off = size * pad // n
    return np.asarray(img).astype(np.float32)[off:off + size, off:off + size] / 255.0


def painted(base, dark, light, seed, size=512, blotch=0.5, speckle=0.12):
    """Flat colour with large soft blotches and fine painterly speckles; tiles seamlessly."""
    n1 = noise(size, 64, seed)
    n2 = noise(size, 16, seed + 1)
    n3 = noise(size, 4, seed + 2)
    t = np.clip(0.5 + (n1 - 0.5) * blotch * 2 + (n2 - 0.5) * 0.35 + (n3 - 0.5) * speckle * 2, 0, 1)[..., None]
    base, dark, light = (np.array(c, np.float32) for c in (base, dark, light))
    col = np.where(t < 0.5, dark + (base - dark) * (t / 0.5), base + (light - base) * ((t - 0.5) / 0.5))
    return Image.fromarray(np.clip(col, 0, 255).astype(np.uint8))


PAINT = {
    # name: base, dark, light
    "Grass": ((112, 176, 70), (84, 146, 56), (150, 200, 86)),
    "ForestFloor": ((86, 128, 58), (66, 98, 46), (118, 140, 70)),
    "Mud": ((150, 112, 74), (124, 90, 60), (176, 138, 94)),
    "Sand": ((226, 204, 150), (204, 180, 128), (240, 222, 176)),
    "Riverbed": ((96, 150, 156), (74, 120, 130), (126, 170, 170)),
    "RockGround": ((150, 150, 146), (118, 120, 120), (182, 180, 172)),
    "Bark": ((128, 88, 58), (100, 66, 44), (150, 108, 72)),
    "Hide": ((176, 122, 78), (150, 100, 62), (198, 146, 98)),
    "Thatch": ((214, 176, 96), (186, 146, 76), (232, 200, 120)),
    "RoughWood": ((156, 112, 72), (128, 90, 58), (180, 134, 88)),
    "DarkRock": ((120, 120, 124), (96, 98, 104), (148, 148, 150)),
}


def flat_normal(path, size=64):
    Image.new("RGB", (size, size), (128, 128, 255)).save(path, quality=90)


def toon(path):
    """Toon-filters a photo texture: smooth the detail away, fewer tones, more colour."""
    img = Image.open(path)
    alpha = img.getchannel("A") if img.mode == "RGBA" else None
    rgb = img.convert("RGB").filter(ImageFilter.ModeFilter(5)).filter(ImageFilter.SMOOTH_MORE)
    a = np.asarray(rgb).astype(np.float32)
    grey = a.mean(axis=2, keepdims=True)
    a = grey + (a - grey) * 1.35                      # saturation
    a = np.round(a / 24.0) * 24.0                     # posterize into painted bands
    a = (a - 128) * 0.9 + 140                         # softer contrast, a little brighter
    out = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).filter(ImageFilter.SMOOTH)
    if alpha is not None:
        out.putalpha(alpha)
    out.save(path, quality=92) if path.endswith(".jpg") else out.save(path)


def build_textures(manifest):
    for i, (name, (base, dark, light)) in enumerate(PAINT.items()):
        t = manifest["textures"].get(name)
        if not t:
            continue
        painted(base, dark, light, 100 + i).save(os.path.join(ba.ART, "..", t["albedo"]), quality=92)
        if t.get("normal"):
            flat_normal(os.path.join(ba.ART, "..", t["normal"]))
    # Photo-scanned props on the old buildings: toon-filter their colour maps.
    stylized = set(MODELS) | {m for m in manifest["models"] if m.startswith("medieval")}
    for mid, m in manifest["models"].items():
        if mid in stylized or m.get("toon"):
            continue
        m["toon"] = True
        for mat in m.get("materials", {}).values():
            if mat.get("albedo"):
                toon(os.path.join(ba.ART, "..", mat["albedo"]))
    print("painted", len(PAINT), "surfaces")


def main():
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    manifest = json.load(open(ba.MANIFEST))
    if only in (None, "impostors"):
        build_impostors(manifest)
    if only in (None, "models"):
        build_models(manifest)
    if only in (None, "textures"):
        build_textures(manifest)
    with open(ba.MANIFEST, "w") as f:
        json.dump(manifest, f, indent=1, sort_keys=True)
    print("manifest:", ba.MANIFEST)


if __name__ == "__main__":
    main()
