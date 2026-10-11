#!/usr/bin/env python3
"""Fetches the game's CC0 sounds and music from OpenGameArt and converts them to small
Ogg Vorbis files in unity/Assets/Art/Audio/Resources/Audio (Unity loads them by name).

Usage: python3 tools/assets/build_audio.py   (needs ffmpeg)
"""
import os
import subprocess
import tempfile
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "Audio", "Resources", "Audio")
CACHE = os.path.join(HERE, ".cache", "audio")
UA = {"User-Agent": "FoundersAssetPipeline/1.0"}
F = "https://opengameart.org/sites/default/files/"

# name: (url, zip member or None, ffmpeg filter args, quality, source page) — all CC0.
SOUNDS = {
    "fire": (F + "fire.wav", None, ["-t", "20"], 3, "https://opengameart.org/content/fireplace-sound-loop"),
    "river": (F + "park_ambience_river.wav", None, ["-t", "40"], 3, "https://opengameart.org/content/park-ambiences"),
    "wind": (F + "park_ambience_wind.wav", None, ["-t", "40"], 3, "https://opengameart.org/content/park-ambiences"),
    "birds": (F + "birds-isaiah658_0.ogg", None, [], 3, "https://opengameart.org/content/ambient-bird-sounds"),
    "music_forest": (F + "Forest_Ambience.mp3", None, [], 4, "https://opengameart.org/content/forest-ambience"),
    "music_first_light": (F + "first_light_particles_0.wav", None, [], 4,
                          "https://opengameart.org/content/first-light-particles-%E2%80%93-cc0-atmospheric-pianoambient-track"),
}
UI_ZIP = F + "UI_SFX_Set.zip"  # https://opengameart.org/content/51-ui-sound-effects-buttons-switches-and-clicks
UI_PICK = {"ui_tap": "click1", "ui_open": "switch2", "ui_confirm": "rollover3"}


def fetch(url, name):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name)
    if not os.path.exists(path):
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=600) as r, open(path, "wb") as f:
            f.write(r.read())
    return path


def to_ogg(src, dst, extra, quality, mono=True):
    cmd = ["ffmpeg", "-y", "-loglevel", "error", "-i", src] + extra + (["-ac", "1"] if mono else []) + \
          ["-ar", "44100", "-c:a", "libvorbis", "-q:a", str(quality), dst]
    subprocess.run(cmd, check=True)


def main():
    os.makedirs(OUT, exist_ok=True)
    credits = []
    for name, (url, member, extra, q, page) in SOUNDS.items():
        src = fetch(url, os.path.basename(url))
        to_ogg(src, os.path.join(OUT, name + ".ogg"), extra, q, mono=not name.startswith("music"))
        credits.append(f"{name}.ogg  <- {page} (CC0)")
    z = fetch(UI_ZIP, "UI_SFX_Set.zip")
    with zipfile.ZipFile(z) as zf, tempfile.TemporaryDirectory() as tmp:
        names = zf.namelist()
        for out, key in UI_PICK.items():
            member = next((n for n in names if key in os.path.basename(n).lower() and n.lower().endswith((".wav", ".ogg"))), None)
            if member is None:
                member = next(n for n in names if n.lower().endswith((".wav", ".ogg")))
            zf.extract(member, tmp)
            to_ogg(os.path.join(tmp, member), os.path.join(OUT, out + ".ogg"), [], 4)
            credits.append(f"{out}.ogg  <- {member} from https://opengameart.org/content/51-ui-sound-effects-buttons-switches-and-clicks (CC0)")
    with open(os.path.join(ROOT, "unity", "Assets", "Art", "Audio", "CREDITS.txt"), "w") as f:
        f.write("All sounds and music are CC0 (public domain) from OpenGameArt.org:\n" + "\n".join(credits) + "\n")
    for n in sorted(os.listdir(OUT)):
        print(n, os.path.getsize(os.path.join(OUT, n)) // 1024, "KB")


if __name__ == "__main__":
    main()
