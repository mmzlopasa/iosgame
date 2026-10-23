#!/usr/bin/env python3
"""Repaints finished building pictures (UI/Buildings/*.png) in the painted icon style without
re-rendering them: the thick cartoon outline is thinned to a fine dark edge, the shading is
flattened into brush dabs (Kuwahara filter, the same one post_icons.py uses), and a teal rim light is
painted along the right and lower edges, like the reference sheet.

Run it on pictures made by render_buildings.py (which writes the old thick outline); it is
idempotent only in the sense that a second run thins nothing further but paints again, so run it
once per fresh render.

Usage: python3 tools/art/paint_pictures.py [<dir>] [name ...]   (default: unity/.../UI/Buildings)
Needs: pillow, numpy.
"""
import glob
import os
import sys

import numpy as np
from PIL import Image, ImageEnhance, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
import post_icons  # noqa: E402  (kuwahara, blur, shift)

DIR = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Resources", "UI", "Buildings")
THIN = 4           # px (at 256) of the old 7.5 px outline to take away
RIM = np.array([0x7D, 0xE3, 0xF0], np.float32) / 255
RIM_STRENGTH = 0.75
PAINT_R = 3


def erode(a, r):
    """Min of the alpha over a disc of radius r."""
    h, w = a.shape
    pad = np.pad(a, r, constant_values=0)
    out = np.ones_like(a)
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy > r * r + 0.5:
                continue
            np.minimum(out, pad[r + dy:r + dy + h, r + dx:r + dx + w], out=out)
    return out


def repaint(src, dst):
    im = Image.open(src).convert("RGBA")
    W, H = im.size
    big = im.resize((W * 2, H * 2), Image.LANCZOS)
    arr = np.asarray(big, np.float32) / 255
    c, a = arr[..., :3], arr[..., 3]
    solid = (a > 0.9).astype(np.float32)
    # thin the outline: drop the outer band of the solid silhouette, keep soft shadows as they are
    core = erode(solid, THIN * 2)
    keep = post_icons.blur(core, 1.0)
    ink_like = (c.max(-1) < 0.26).astype(np.float32)  # the outline is a dark brown
    cut = solid * (1 - keep) * ink_like
    a2 = a * (1 - cut)
    # brush dabs
    c2 = c * 0.3 + post_icons.kuwahara(c, PAINT_R * 2) * 0.7
    # teal rim on the right and lower inner edges of the subject
    inner = (keep > 0.5).astype(np.float32)
    band = inner - erode(inner, 7)
    edge_r = np.clip(inner - post_icons.shift(inner, -6, 0), 0, 1)  # pixels whose right neighbour is empty
    edge_b = np.clip(inner - post_icons.shift(inner, 0, -6), 0, 1)  # ... whose lower neighbour is empty
    rim = post_icons.blur(np.clip(band * (edge_r + edge_b * 0.7), 0, 1), 2.5) * inner * RIM_STRENGTH
    lum = c2 @ np.array([0.299, 0.587, 0.114], np.float32)
    rim *= np.clip(lum * 2.2, 0.25, 1.0)  # not on the black outline itself
    c2 = 1 - (1 - c2) * (1 - RIM * rim[..., None])
    out = Image.fromarray((np.dstack([np.clip(c2, 0, 1), np.clip(a2, 0, 1)]) * 255 + 0.5).astype(np.uint8), "RGBA")
    out = out.resize((W, H), Image.LANCZOS)
    rgb = ImageEnhance.Color(out.convert("RGB")).enhance(1.05)
    rgb = ImageEnhance.Contrast(rgb).enhance(1.05).filter(ImageFilter.UnsharpMask(radius=0.8, percent=35, threshold=1))
    Image.merge("RGBA", (*rgb.split(), out.split()[3])).save(dst, optimize=True)


def main(argv):
    d = argv[0] if argv and os.path.isdir(argv[0]) else DIR
    names = [n for n in argv if not os.path.isdir(n)]
    for f in sorted(glob.glob(os.path.join(d, "*.png"))):
        n = os.path.basename(f)[:-4]
        if names and n not in names:
            continue
        repaint(f, f)
        print("painted", n)


if __name__ == "__main__":
    main(sys.argv[1:])
