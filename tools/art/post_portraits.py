#!/usr/bin/env python3
"""Finishes the founder portraits (tools/art/blender_portraits.py renders, 768 px RGBA) into the game's
256 px icons: a painted backdrop in the interface's blue-grey with a warm glow behind the head, a
light brush-stroke finish (edge-preserving smoothing, as the painted villagers and buildings have),
a soft dark edge round the figure and rounded corners.

python3 tools/art/post_portraits.py <render dir> <Icons dir> [face_m1 ...]
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

OUT_PX = 256
src_dir, out_dir = sys.argv[1], sys.argv[2]
only = sys.argv[3:]


def backdrop(n, seed):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:n, 0:n] / n
    # Blue-grey (#36415A) round a lighter, warmer middle, darker (#202734) at the corners.
    r = np.hypot(xx - 0.5, yy - 0.42) / 0.75
    inner, outer = np.array([0x5A, 0x6B, 0x8A]) / 255, np.array([0x20, 0x27, 0x34]) / 255
    img = inner[None, None] * (1 - np.clip(r, 0, 1)[..., None]) + outer[None, None] * np.clip(r, 0, 1)[..., None]
    # Painted texture: soft blotches.
    noise = Image.fromarray((rng.random((24, 24)) * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC)
    img *= 0.93 + 0.14 * (np.asarray(noise, float)[..., None] / 255)
    # Warm glow behind the head.
    g = np.exp(-(((xx - 0.5) / 0.28) ** 2 + ((yy - 0.36) / 0.26) ** 2))
    img = img + g[..., None] * np.array([0.30, 0.22, 0.10]) * 0.55
    return np.clip(img, 0, 1)


def paint(rgb):
    try:
        import cv2
        bgr = (rgb[..., ::-1] * 255).astype(np.uint8)
        bgr = cv2.edgePreservingFilter(bgr, flags=cv2.RECURS_FILTER, sigma_s=12, sigma_r=0.18)
        return bgr[..., ::-1].astype(float) / 255
    except ImportError:
        return np.asarray(Image.fromarray((rgb * 255).astype(np.uint8)).filter(ImageFilter.ModeFilter(3)), float) / 255


def finish(name, seed):
    im = Image.open(os.path.join(src_dir, name + ".png")).convert("RGBA")
    n = im.size[0]
    a = np.asarray(im, float) / 255
    fig, alpha = a[..., :3], a[..., 3:4]
    # A thin, soft dark edge round the figure so it stands off the backdrop.
    edge = np.asarray(Image.fromarray((alpha[..., 0] * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7))
                      .filter(ImageFilter.GaussianBlur(3)), float)[..., None] / 255
    bg = backdrop(n, seed)
    bg = bg * (1 - 0.55 * np.clip(edge - alpha, 0, 1))
    img = fig * alpha + bg * (1 - alpha)
    img = paint(img)
    # Rounded corners (the frame cells and the player's frame are rounded squares).
    yy, xx = np.mgrid[0:n, 0:n] + 0.5
    rad = n * 0.16
    cx = np.clip(xx, rad, n - rad)
    cy = np.clip(yy, rad, n - rad)
    d = np.hypot(xx - cx, yy - cy)
    mask = np.clip(rad - d + 0.5, 0, 1)
    out = np.dstack([img, mask]) * 255
    big = Image.fromarray(out.astype(np.uint8), "RGBA")
    small = big.resize((OUT_PX, OUT_PX), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1.2, percent=60, threshold=2))
    small.save(os.path.join(out_dir, name + ".png"))
    print("portrait", name)


names = only or sorted(f[:-4] for f in os.listdir(src_dir) if f.startswith("face_") and f.endswith(".png"))
for i, nm in enumerate(names):
    finish(nm, 11 + i)
