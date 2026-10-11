#!/usr/bin/env python3
"""Builds the villager character (unity/Assets/Art/Resources/Models/Villager.fbx).

Source: Quaternius "Universal Animation Library" (Standard), CC0, mirrored on OpenGameArt.
The mannequin is dressed in Blender (blender_people.py): hide tunic and wraps, hair, and
a set of animations (Idle, Walk, Work, Gather, Talk, Sit, Carry, Jog).

Usage: python3 tools/assets/build_people.py   (needs Blender 4.2 on PATH)
"""
import os
import subprocess
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CACHE = os.path.join(HERE, ".cache", "ual")
URL = "https://opengameart.org/sites/default/files/universal_animation_librarystandard.zip"
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "Resources", "Models", "Villager.fbx")


def main():
    os.makedirs(CACHE, exist_ok=True)
    zpath = os.path.join(CACHE, "ual.zip")
    if not os.path.exists(zpath):
        urllib.request.urlretrieve(URL, zpath)
    with zipfile.ZipFile(zpath) as z:
        name = next(n for n in z.namelist() if n.endswith(".glb"))
        z.extract(name, CACHE)
        lic = next((n for n in z.namelist() if n.endswith("License.txt")), None)
        if lic:
            z.extract(lic, CACHE)
    glb = os.path.join(CACHE, name)
    subprocess.run(["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_people.py"), "--", glb, OUT],
                   check=True)
    print("villager:", OUT, os.path.getsize(OUT) // 1024, "KB")


if __name__ == "__main__":
    main()
