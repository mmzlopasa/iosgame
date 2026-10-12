#!/usr/bin/env python3
"""Builds the villagers (unity/Assets/Art/Resources/Models/Person_<Name>.fbx) from the CC0
Quaternius "Ultimate Animated Character Pack" (OpenGameArt): stylized men and women with faces,
hair and clothes, each given every work animation of the game (retargeted from Villager.fbx,
see blender_characters.py). Also writes Resources/people.json (who is male, female or elder).

Usage: python3 tools/assets/build_characters.py [--preview]   (needs Blender 4.2 on PATH;
build Villager.fbx first with build_people.py)
"""
import json
import os
import subprocess
import sys
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CACHE = os.path.join(HERE, ".cache", "packs", "characters")
URL = "https://opengameart.org/sites/default/files/ultimate_animated_character_pack_by_quaternius.zip"
MODELS = os.path.join(ROOT, "unity", "Assets", "Art", "Resources", "Models")
MANIFEST = os.path.join(ROOT, "unity", "Assets", "Art", "Resources", "people.json")

# Pack file -> (gender, age). Picked to suit a prehistoric and medieval valley.
PEOPLE = {
    "Viking_Male": ("male", "adult"), "Viking_Female": ("female", "adult"),
    "Casual_Male": ("male", "adult"), "Casual_Female": ("female", "adult"),
    "Casual2_Male": ("male", "adult"), "Casual2_Female": ("female", "adult"),
    "Casual3_Male": ("male", "adult"), "Casual3_Female": ("female", "adult"),
    "OldClassy_Male": ("male", "elder"), "OldClassy_Female": ("female", "elder"),
}


def main():
    os.makedirs(CACHE, exist_ok=True)
    zpath = os.path.join(CACHE, "chars.zip")
    if not os.path.exists(zpath):
        urllib.request.urlretrieve(URL, zpath)
    with zipfile.ZipFile(zpath) as z:
        z.extractall(CACHE)
    fbx_dir = os.path.join(CACHE, "Ultimate Animated Character Pack - Nov 2019", "FBX")
    source = os.path.join(ROOT, "unity", "Assets", "Art", "Source", "Villager.fbx")
    entries = []
    for name, (gender, age) in PEOPLE.items():
        out = os.path.join(MODELS, f"Person_{name}.fbx")
        args = ["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_characters.py"), "--",
                source, os.path.join(fbx_dir, name + ".fbx"), out]
        if "--preview" in sys.argv:
            args += ["--preview", os.path.join(CACHE, "preview_" + name)]
        subprocess.run(args, check=True, stdout=subprocess.DEVNULL)
        entries.append({"model": f"Models/Person_{name}", "gender": gender, "age": age})
        print("person:", name, os.path.getsize(out) // 1024, "KB")
    with open(MANIFEST, "w") as f:
        json.dump({"source": "Quaternius Ultimate Animated Character Pack (CC0)", "people": entries}, f, indent=1)
    print("manifest:", MANIFEST)


if __name__ == "__main__":
    main()
