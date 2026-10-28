"""Writes godot/art/models/hw_<sprite>.glb for every building in houses.py, adds them to the art manifest,
and renders the build-menu pictures. Usage: python3 build.py [--only a,b] [--preview DIR] [--no-pictures]"""
import argparse
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from houses import BUILDINGS  # noqa: E402
from render import render  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
GODOT = os.path.join(ROOT, "godot")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--preview", default="")
    ap.add_argument("--no-pictures", action="store_true")
    ap.add_argument("--no-write", action="store_true")
    a = ap.parse_args()
    only = [s for s in a.only.split(",") if s]
    atlas = np.array(Image.open(os.path.join(GODOT, "art/textures/bw_atlas_albedo.png")).convert("RGB"))
    mpath = os.path.join(GODOT, "data/art_manifest.json")
    manifest = json.load(open(mpath))
    for sprite, fn in BUILDINGS.items():
        if only and sprite not in only:
            continue
        mesh = fn().build()
        mesh.bake_shading()
        lo, hi = mesh.bounds()
        tris = len(mesh.I) // 3
        print(f"{sprite}: {tris} triangles, size {np.round(hi - lo, 2)}")
        if a.preview:
            os.makedirs(a.preview, exist_ok=True)
            render(mesh, atlas, size=512).save(os.path.join(a.preview, sprite + ".png"))
        if a.no_write:
            continue
        mid = "hw_" + sprite
        mesh.write_glb(os.path.join(GODOT, "art/models", mid + ".glb"))
        manifest["models"][mid] = {
            "materialOrder": ["Atlas"],
            "materials": {"Atlas": {
                "albedo": "Art/Textures/bw_atlas_albedo.png",
                "normal": "Art/Textures/bw_atlas_normal.png",
                "alphaClip": False, "doubleSided": True, "vertexColor": True}},
            "resource": "Models/" + mid,
            "format": "glb",
            "size": [round(float(x), 3) for x in hi - lo],
            "source": "tools/godot/houses (modelled for the game)",
            "triangles": tris,
        }
        if not a.no_pictures:
            pic = render(mesh, atlas, size=512).resize((256, 256), Image.LANCZOS)
            pic.save(os.path.join(GODOT, "art/ui/Buildings", sprite + ".png"))
    if not a.no_write:
        with open(mpath, "w") as f:
            json.dump(manifest, f, indent=1, sort_keys=True)
            f.write("\n")


if __name__ == "__main__":
    main()
