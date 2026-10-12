#!/usr/bin/env python3
"""Builds the loading-screen video unity/Assets/StreamingAssets/loading.mp4 ("The Long Rise").

The game plays it by URL behind the title, dimmed, looping. The clip is a Blender time-lapse
(tools/video/blender_rise.py): a floating island where a lone campfire at night grows into a
medieval town by golden day. Landscape 1920x1080; the island stays inside the centre 1920x886
(2.17:1, wide iPhones) and 1440x1080 (4:3, iPads) so "cover" scaling never crops it. It is rendered
at half size with Cycles and upscaled here.

Steps (each is skipped when its output already exists, so an interrupted build resumes):
  1. assemble the CC0 Medieval Village MegaKit houses (tools/assets/blender_medieval.py)
     into tools/video/.cache/medieval (needs the kit in tools/assets/.cache/packs/medieval/glTF,
     see tools/assets/PACKS.md);
  2. render frames 0..263 at 960x540 to tools/video/.cache/frames (Blender 4.2 + Cycles on the CPU;
     about 10 s per frame at the defaults on 4 cores, i.e. roughly 45 minutes);
  3. encode: frames 24..263 play, and the last second cross-fades into frames 0..23 (the camera
     swing and the fire are periodic over 240 frames), so the 10 s clip loops seamlessly. H.264
     yuv420p, faststart, no audio.

Usage: python3 tools/video/build_loading_video.py [--res 960] [--samples 16] [--crf 24] [--encode-only] [out.mp4]
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "StreamingAssets", "loading.mp4")
CACHE = os.path.join(HERE, ".cache")
MEDIEVAL = os.path.join(CACHE, "medieval")
FRAMES = os.path.join(CACHE, "frames")
KIT = os.path.join(ROOT, "tools", "assets", ".cache", "packs", "medieval", "glTF")
W, H, FPS = 1920, 1080, 24
LOOP, FADE = 240, 24      # frames in the loop; frames of the closing cross-fade
LAST = LOOP + FADE - 1    # 263


def arg(name, default):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def main():
    res, samples, crf = arg("--res", "960"), arg("--samples", "16"), arg("--crf", "24")
    rest = [a for i, a in enumerate(sys.argv[1:], 1)
            if not a.startswith("--") and sys.argv[i - 1] not in ("--res", "--samples", "--crf")]
    out = rest[0] if rest else OUT

    if "--encode-only" not in sys.argv:
        if not os.path.exists(os.path.join(MEDIEVAL, "medieval_church", "medieval_church.gltf")):
            subprocess.run(["blender", "-b", "--factory-startup", "-P",
                            os.path.join(ROOT, "tools", "assets", "blender_medieval.py"), "--", KIT, MEDIEVAL], check=True)
        subprocess.run(["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_rise.py"), "--",
                        FRAMES, "--medieval", MEDIEVAL, "--res", res, "--samples", samples, "--frames", f"0-{LAST}"],
                       check=True)

    missing = [f for f in range(LAST + 1) if not os.path.exists(os.path.join(FRAMES, f"f_{f:04d}.png"))]
    if missing:
        sys.exit(f"{len(missing)} frames missing in {FRAMES} (first: {missing[0]})")
    pattern = os.path.join(FRAMES, "f_%04d.png")
    scale = f"scale={W}:{H}:flags=lanczos,setsar=1,format=yuv420p"
    graph = (f"[0:v]trim=end_frame={LOOP},setpts=PTS-STARTPTS,{scale}[a];"
             f"[1:v]trim=end_frame={FADE},setpts=PTS-STARTPTS,{scale}[b];"
             f"[a][b]xfade=transition=fade:duration={FADE / FPS}:offset={(LOOP - FADE) / FPS}[v]")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error",
                    "-framerate", str(FPS), "-start_number", str(FADE), "-i", pattern,
                    "-framerate", str(FPS), "-start_number", "0", "-i", pattern,
                    "-filter_complex", graph, "-map", "[v]", "-an", "-r", str(FPS),
                    "-c:v", "libx264", "-profile:v", "high", "-level", "4.1", "-pix_fmt", "yuv420p",
                    "-crf", crf, "-preset", "slow", "-tune", "animation", "-g", str(FPS * 2),
                    "-movflags", "+faststart", out], check=True)
    print("loading video:", out, os.path.getsize(out) // 1024, "KB,", f"{LOOP / FPS:.1f}s loop")


if __name__ == "__main__":
    main()
