#!/usr/bin/env python3
"""Builds the loading-screen montage (unity/Assets/Art/Resources/Video/loading.mp4) from the
game's own no-UI captures (the iOS Simulator preview screenshots).

Each shot slowly zooms and drifts (Ken Burns) and cross-fades into the next; the clip loops
seamlessly because the last shot fades back into the first. Portrait H.264, no audio, small.

Usage: python3 tools/video/build_loading_video.py <screenshots dir> [out.mp4]
       (download the screenshots with: gh run download <run> -R mmzlopasa/iosgame -n screenshots -D <dir>)
"""
import os
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "Resources", "Video", "loading.mp4")

# The montage: the valley, the people at work, the seasons, the town at night.
SHOTS = [
    "06c_era2_village", "07e_mine", "06g_town_layout", "06k_builders", "06d_autumn",
    "07d_stone_hills", "07c_mountains", "06f_night", "09b_valley",
]
W, H = 720, 1558          # the capture aspect (1125×2436), even sizes for H.264
FPS = 24
SHOT = 3.2                # seconds per shot
FADE = 0.8                # cross-fade


def main():
    src = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else OUT
    shots = [os.path.join(src, s + ".png") for s in SHOTS if os.path.exists(os.path.join(src, s + ".png"))]
    if len(shots) < 3:
        sys.exit(f"need at least 3 screenshots in {src}, found {len(shots)}")
    shots.append(shots[0])  # loop back to the start
    frames = int(SHOT * FPS)
    args = ["ffmpeg", "-y", "-loglevel", "error"]
    for s in shots:
        args += ["-loop", "1", "-t", f"{SHOT}", "-i", s]
    parts = []
    for i in range(len(shots)):
        # Alternate zooming in and out, drifting a little sideways.
        zin = i % 2 == 0
        z = f"'min(1+0.10*on/{frames},1.10)'" if zin else f"'max(1.10-0.10*on/{frames},1.0)'"
        x = f"'(iw-iw/zoom)*({0.3 if zin else 0.7}+{0.4 if zin else -0.4}*on/{frames})'"
        parts.append(
            f"[{i}:v]scale={W * 2}:{H * 2},zoompan=z={z}:x={x}:y='(ih-ih/zoom)/2':d={frames}:s={W}x{H}:fps={FPS},"
            f"setsar=1,format=yuv420p[v{i}]")
    chain, last = [], "v0"
    for i in range(1, len(shots)):
        offset = i * (SHOT - FADE)
        tag = f"x{i}"
        chain.append(f"[{last}][v{i}]xfade=transition=fade:duration={FADE}:offset={offset:.2f}[{tag}]")
        last = tag
    total = (len(shots) - 1) * (SHOT - FADE)  # cut where the first shot has fully returned
    graph = ";".join(parts + chain)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    args += ["-filter_complex", graph, "-map", f"[{last}]", "-t", f"{total:.2f}", "-an",
             "-c:v", "libx264", "-profile:v", "main", "-pix_fmt", "yuv420p", "-crf", "27",
             "-preset", "slow", "-movflags", "+faststart", out]
    subprocess.run(args, check=True)
    print("loading video:", out, os.path.getsize(out) // 1024, "KB,", f"{total:.1f}s from {len(shots) - 1} shots")


if __name__ == "__main__":
    main()
