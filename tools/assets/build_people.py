#!/usr/bin/env python3
"""Builds the villager characters (unity/Assets/Art/Resources/Models/Villager.fbx and
VillagerF.fbx).

Sources (CC0, Quaternius, mirrored on OpenGameArt):
- Universal Animation Library 2 (Standard): the male and female mannequins on the Unreal rig,
  and the work animations (tree chopping, planting, harvesting, watering, carrying, throwing)
- Universal Animation Library (Standard), Unreal version: walking, talking, sitting, kneeling
  work and more, on the same skeleton

The mannequins are dressed in Blender (blender_people.py): hide tunic or dress, wraps and hair,
plus hand tools (axe, pickaxe, hammer, spear) the game shows per job.

Usage: python3 tools/assets/build_people.py [--preview]   (needs Blender 4.2 on PATH)
"""
import os
import subprocess
import sys
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CACHE = os.path.join(HERE, ".cache", "ual")
URL1 = "https://opengameart.org/sites/default/files/universal_animation_librarystandard.zip"
URL2 = "https://opengameart.org/sites/default/files/universal_animation_library_2standard.zip"
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "Resources", "Models")


def fetch(url, name):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name)
    if not os.path.exists(path):
        urllib.request.urlretrieve(url, path)
    return zipfile.ZipFile(path)


def extract(z, suffix):
    name = next(n for n in z.namelist() if n.replace("\\", "/").endswith(suffix))
    z.extract(name, CACHE)
    return os.path.join(CACHE, name)


def main():
    z1 = fetch(URL1, "ual.zip")
    z2 = fetch(URL2, "ual2.zip")
    ual1 = extract(z1, "Unreal Engine/AL_Standard.fbx")
    ual2 = extract(z2, "Unreal-Godot/UAL2_Standard.glb")
    female = extract(z2, "Female Mannequin/Unreal-Godot/Mannequin_F.glb")
    for z in (z1, z2):
        lic = next((n for n in z.namelist() if n.endswith("License.txt")), None)
        if lic:
            z.extract(lic, CACHE)
    script = os.path.join(HERE, "blender_people.py")
    for base, out, extra in ((ual2, "Villager.fbx", []), (female, "VillagerF.fbx", ["--female"])):
        dst = os.path.join(OUT, out)
        args = ["blender", "-b", "--factory-startup", "-P", script, "--", base, ual2, ual1, dst] + extra
        if "--preview" in sys.argv:
            args += ["--preview", os.path.join(CACHE, "preview_" + out[:-4])]
        subprocess.run(args, check=True)
        print("villager:", dst, os.path.getsize(dst) // 1024, "KB")


if __name__ == "__main__":
    main()
