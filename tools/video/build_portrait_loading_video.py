#!/usr/bin/env python3
"""Builds the portrait loading film unity/Assets/StreamingAssets/loading_portrait.mp4 from the landscape one
(loading.mp4, see build_loading_video.py).

Aspect-filling a 16:9 film on a 9:19.5 phone shows only a narrow, blown-up strip of its middle. The portrait cut
keeps the island whole instead: the film, scaled so the island spans most of the width, sits across the middle of
a tall frame, over a soft, blurred backdrop made from its own sky (top rows above, lowest rows below); the two blend over a
long vertical fade so no edge shows. LoadingScreen plays it whenever the screen is taller than wide.

Usage: python3 tools/video/build_portrait_loading_video.py   (needs ffmpeg)
"""
import os
import subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(ROOT, "unity", "Assets", "StreamingAssets", "loading.mp4")
OUT = os.path.join(ROOT, "unity", "Assets", "StreamingAssets", "loading_portrait.mp4")

W, H = 1080, 2340          # 9:19.5, a modern iPhone held upright (cropped a little on squarer screens)
BAND_W = 2160              # the film's width in the middle band: the island spans ~85% of the screen
FADE = 220                 # pixels over which the band melts into the blurred backdrop


def main():
    band_h = BAND_W * 9 // 16
    graph = (
        f"[0:v]split=3[a][c][b];"
        # the backdrop: the film's own sky (its top rows) stretched over the upper half, its lowest rows over the
        # lower half, blurred: the colours of each moment (night, dusk, day) with nothing recognisable in them
        f"[a]crop=iw:ih*0.14:0:0,scale={W}:{H // 2}[top];"
        f"[c]crop=iw:ih*0.14:0:ih*0.86,scale={W}:{H // 2}[low];"
        f"[top][low]vstack,boxblur=48:2,eq=brightness=-0.04[bg];"
        f"[b]scale={BAND_W}:{band_h},crop={W}:{band_h},format=rgba,"
        f"geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='255*min(1,min(Y,H-1-Y)/{FADE})'[fg];"
        f"[bg][fg]overlay=0:(H-h)/2,format=yuv420p[out]"
    )
    cmd = ["ffmpeg", "-v", "error", "-y", "-i", SRC, "-filter_complex", graph, "-map", "[out]", "-an",
           "-c:v", "libx264", "-profile:v", "high", "-preset", "slow", "-crf", "25", "-movflags", "+faststart", OUT]
    subprocess.run(cmd, check=True)
    print("wrote", os.path.relpath(OUT, ROOT), os.path.getsize(OUT) // 1024, "KB")


if __name__ == "__main__":
    main()
