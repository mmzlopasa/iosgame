#!/usr/bin/env python3
"""Builds the rendered UI art: icons (Blender), 9-slice skins (numpy) and the app icon (Blender),
plus review contact sheets. See tools/art/README.md.

Usage: python3 tools/art/build_art.py [icons|skins|appicon|sheets ...] [--only name,name] [--samples N]
       (no step = all; needs Blender 4.2 on PATH or at /usr/local/bin/blender, pillow, numpy)
"""
import glob
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
UI = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Resources", "UI")
CACHE = os.path.join(HERE, ".cache")
RAW = os.path.join(CACHE, "raw")
SHEETS = os.path.join(CACHE, "sheets")


def blender():
    return shutil.which("blender") or "/usr/local/bin/blender"


def arg(flag, default=None):
    return sys.argv[sys.argv.index(flag) + 1] if flag in sys.argv else default


def icons(only):
    cmd = [blender(), "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_icons.py"), "--", RAW] + only
    if arg("--samples"):
        cmd += ["--samples", arg("--samples")]
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL)
    out = os.path.join(UI, "Icons")
    subprocess.run([sys.executable, os.path.join(HERE, "post_icons.py"), RAW, out] + only, check=True)


def skins():
    subprocess.run([sys.executable, os.path.join(HERE, "skins.py")], check=True)


def appicon():
    subprocess.run([blender(), "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_appicon.py")], check=True,
                   stdout=subprocess.DEVNULL)


def sheets():
    os.makedirs(SHEETS, exist_ok=True)
    subprocess.run([sys.executable, os.path.join(HERE, "post_icons.py"), "--sheet", os.path.join(UI, "Icons"),
                    os.path.join(SHEETS, "icons.png"), "--cols", "10"], check=True)
    subprocess.run([sys.executable, os.path.join(HERE, "preview_ui.py"), SHEETS], check=True)
    print("sheets in", SHEETS)


def main():
    steps = [a for a in sys.argv[1:] if a in ("icons", "skins", "appicon", "sheets")] or \
        ["icons", "skins", "appicon", "sheets"]
    only = [n for n in (arg("--only", "") or "").split(",") if n]
    for s in steps:
        print("==", s, flush=True)
        if s == "icons":
            icons(only)
        else:
            globals()[s]()


if __name__ == "__main__":
    main()
