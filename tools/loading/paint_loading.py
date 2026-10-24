#!/usr/bin/env python3
"""Turns the Blender key art (blender_loading_art.py) into the loading-screen pictures.

For each shot: aerial haze from the mist pass (warm towards the sun, cool in the shade), a painted finish
(edge-preserving smoothing and a Kuwahara pass so surfaces read as brush strokes while edges stay crisp),
soft bloom on the highlights, a warm/cool split grade, a vignette, then a JPEG for the game:
unity/Assets/Art/UI/Resources/UI/Images/loading_<n>.jpg (1920 wide, Unity compresses it on import).

Usage: python3 tools/loading/paint_loading.py <render dir> [--out DIR] [--width 1920]
Needs numpy, Pillow and opencv-python-headless.
"""
import os
import sys

import cv2
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_OUT = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Resources", "UI", "Images")

# The order the loading screen shows them in (loading_1 ... loading_5).
SHOTS = ["hollow", "village", "caravan", "market", "dusk"]
LOOK = {
    #           haze colour        haze  warm  bloom  sat   vignette
    "hollow":  ((0.98, 0.86, 0.70), 0.55, 0.10, 0.30, 1.10, 0.30),
    "village": ((0.98, 0.86, 0.68), 0.35, 0.10, 0.25, 1.08, 0.32),
    "caravan": ((1.00, 0.84, 0.64), 0.45, 0.14, 0.35, 1.08, 0.34),
    "market":  ((0.98, 0.88, 0.72), 0.30, 0.08, 0.25, 1.08, 0.30),
    "dusk":    ((0.36, 0.42, 0.62), 0.45, -0.05, 0.55, 1.04, 0.40),
}


def arg(flag, default):
    return sys.argv[sys.argv.index(flag) + 1] if flag in sys.argv else default


def load(path):
    a = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    if a is None:
        raise FileNotFoundError(path)
    if a.ndim == 2:
        a = np.repeat(a[..., None], 3, axis=2)
    a = a[..., :3][..., ::-1].astype(np.float32)
    return a / (65535.0 if a.max() > 255 else 255.0)


def kuwahara(img, r):
    """Classic Kuwahara: each pixel takes the mean of the quadrant (of four) with the least variance."""
    k = r + 1
    means, vars_ = [], []
    lum = img.mean(axis=2)
    for dy in (0, r):
        for dx in (0, r):
            m = cv2.blur(img, (k, k), anchor=(dx, dy), borderType=cv2.BORDER_REFLECT)
            m2 = cv2.blur(lum * lum, (k, k), anchor=(dx, dy), borderType=cv2.BORDER_REFLECT)
            ml = cv2.blur(lum, (k, k), anchor=(dx, dy), borderType=cv2.BORDER_REFLECT)
            means.append(m)
            vars_.append(m2 - ml * ml)
    idx = np.argmin(np.stack(vars_), axis=0)
    out = np.zeros_like(img)
    for i, m in enumerate(means):
        out[idx == i] = m[idx == i]
    return out


def paint(name, src, mist, width):
    haze_col, haze_k, warm, bloom_k, sat, vig = LOOK.get(name, LOOK["village"])
    img = src.copy()
    h, w = img.shape[:2]
    # Aerial perspective: far things take the colour of the air.
    if mist is not None:
        m = np.clip(mist, 0, 1) ** 1.35
        img = img * (1 - m[..., None] * haze_k) + np.array(haze_col, np.float32) * (m[..., None] * haze_k)
    # Painted finish: smooth inside shapes, keep their edges; then brushy quadrants at two sizes.
    u8 = (np.clip(img, 0, 1) * 255).astype(np.uint8)
    smooth = cv2.edgePreservingFilter(u8, flags=cv2.RECURS_FILTER, sigma_s=18, sigma_r=0.22).astype(np.float32) / 255
    img = img * 0.35 + smooth * 0.65
    img = kuwahara(img, max(2, w // 640)) * 0.6 + img * 0.4
    # Gentle unsharp mask so the outlines read like a confident brush.
    blur = cv2.GaussianBlur(img, (0, 0), 1.6)
    img = np.clip(img + (img - blur) * 0.45, 0, 1)
    # Bloom: the bright parts glow a little.
    lum = img.mean(axis=2, keepdims=True)
    hi = np.clip((img - 0.72) / 0.28, 0, 1) * img
    glow = cv2.GaussianBlur(hi, (0, 0), w / 90) + cv2.GaussianBlur(hi, (0, 0), w / 30)
    img = 1 - (1 - img) * (1 - glow * bloom_k)
    # Grade: warm highlights, cool shadows, a touch more colour, soft S-curve.
    lum = img.mean(axis=2, keepdims=True)
    img = img + warm * np.concatenate([lum * 0.10, lum * 0.03, -lum * 0.08], axis=2) \
        - warm * np.concatenate([(1 - lum) * 0.03, (1 - lum) * 0.0, -(1 - lum) * 0.05], axis=2)
    grey = img.mean(axis=2, keepdims=True)
    img = grey + (img - grey) * sat
    img = np.clip(img, 0, 1)
    img = img * img * (3 - 2 * img) * 0.25 + img * 0.75
    # Vignette.
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2) / np.sqrt(2)
    img = img * (1 - vig * np.clip(d - 0.35, 0, 1) ** 1.6)[..., None]
    out = Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8))
    if out.width != width:
        out = out.resize((width, round(out.height * width / out.width)), Image.LANCZOS)
    return out


def main():
    src = sys.argv[1]
    out_dir = arg("--out", DEFAULT_OUT)
    width = int(arg("--width", 1920))
    os.makedirs(out_dir, exist_ok=True)
    for i, name in enumerate(SHOTS, 1):
        path = os.path.join(src, name + ".png")
        if not os.path.exists(path):
            print("missing", path)
            continue
        mp = os.path.join(src, name + "_mist.png")
        mist = load(mp)[..., 0] if os.path.exists(mp) else None
        pic = paint(name, load(path), mist, width)
        dst = os.path.join(out_dir, f"loading_{i}.jpg")
        pic.save(dst, quality=90, optimize=True, progressive=False)
        print(dst, pic.size)


if __name__ == "__main__":
    main()
