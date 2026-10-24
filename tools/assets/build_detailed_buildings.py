#!/usr/bin/env python3
"""Builds the detailed, Foundation-style buildings for the game.

1. paint_building_atlas.py paints the shared texture atlas (Art/Textures/bw_atlas_*)
2. blender_detailed_buildings.py models every building from bw_kit.py parts and exports one FBX each
3. the FBX files go to Art/Resources/Models/bw_<sprite>.fbx and are registered in art_manifest.json
   with the one atlas material; BuildingModels.Create uses a bw_ model whenever one exists.

Usage: python3 tools/assets/build_detailed_buildings.py [--only shelter,granary] [--preview DIR]
       [--blender CMD]   (default: blender on PATH; CMD is run as: CMD -b --factory-startup -P script -- args)
       [--skip-blender]  (register what is already in tools/assets/.cache/detailed)
"""
import json
import os
import shlex
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_assets as ba  # noqa: E402
import paint_building_atlas  # noqa: E402

WORK = os.path.join(HERE, ".cache", "detailed")
ATLAS = {
    "albedo": "Art/Textures/bw_atlas_albedo.png",
    "normal": "Art/Textures/bw_atlas_normal.png",
    "mask": "Art/Textures/bw_atlas_mask.png",
    "alphaClip": False,
    "doubleSided": False,
}


def arg(flag, default=None):
    return sys.argv[sys.argv.index(flag) + 1] if flag in sys.argv else default


def main():
    paint_building_atlas.main()
    if "--skip-blender" not in sys.argv:
        cmd = shlex.split(arg("--blender", "blender")) + ["-b", "--factory-startup", "-P",
                                                           os.path.join(HERE, "blender_detailed_buildings.py"), "--", WORK]
        if arg("--only"):
            cmd += ["--only", arg("--only")]
        if arg("--preview"):
            cmd += ["--preview", arg("--preview")]
        subprocess.run(cmd, check=True)
    info = json.load(open(os.path.join(WORK, "info.json")))
    manifest = json.load(open(ba.MANIFEST))
    os.makedirs(ba.OUT_MODELS, exist_ok=True)
    only = arg("--only")
    for mid, meta in sorted(info.items()):
        if only and mid[3:] not in only.split(","):
            continue
        shutil.copyfile(os.path.join(WORK, mid + ".fbx"), os.path.join(ba.OUT_MODELS, mid + ".fbx"))
        manifest["models"][mid] = {
            "materialOrder": ["Atlas"],
            "materials": {"Atlas": dict(ATLAS)},
            "resource": "Models/" + mid,
            "size": meta["size"],
            "source": "tools/assets/blender_detailed_buildings.py (modelled for the game)",
            "triangles": meta["triangles"],
        }
        print(f"{mid}: {meta['triangles']} triangles, {meta['size']}")
    with open(ba.MANIFEST, "w") as f:
        json.dump(manifest, f, indent=1, sort_keys=True)


if __name__ == "__main__":
    main()
