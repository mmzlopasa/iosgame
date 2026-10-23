#!/usr/bin/env python3
"""Finishes the raw Blender icon renders (512 px RGBA) into the game's 128 px icons.

Every icon gets the same treatment so the set reads as one family:
- a mild grade (a touch more saturation and contrast, like a painted render)
- the kit's thick dark outline (#2B1A0E): the alpha dilated by a soft-edged disc
- a short, soft drop shadow under the outline
- premultiplied Lanczos downsample to 128 px and a light unsharp mask

Usage: python3 tools/art/post_icons.py <raw dir> <out dir> [name ...]
       python3 tools/art/post_icons.py --sheet <icons dir> <out.png> [--ref <ref image>] [--cols N] [--bg #RRGGBB]
Needs: pillow, numpy.
"""
import glob
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
FONT = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Resources", "UI", "Fonts", "ChangaOne-Regular.ttf")
INK = np.array([0x2B, 0x1A, 0x0E], np.float32) / 255
RAW = 512
OUT_PX = 128
# The painted style (see the reference sheet): no thick cartoon outline, only a thin dark edge so the
# icon still reads on bright and dark backgrounds; soft dark creases between parts; a brushy finish.
OUTLINE = 7.0      # px at 512 (~1.7 px at 128)
INNER = 3.5        # crease lines between parts, px at 512
INNER_ALPHA = 0.55
SHADOW = (0, 9)    # px at 512
SHADOW_ALPHA = 0.32
EDGE_INK = np.array([0x1E, 0x14, 0x22], np.float32) / 255
PAINT_R = 4        # Kuwahara radius at 512 px: flattens the render into brush dabs

# Per-icon overrides: outline radius (simple glyphs and tiny marks can take a slightly thinner line).
OVERRIDES = {}


def dilate(alpha, r):
    """Max of the alpha over a disc of radius r with a 1 px soft edge (anti-aliased outline)."""
    h, w = alpha.shape
    R = int(np.ceil(r)) + 1
    pad = np.pad(alpha, R)
    out = np.zeros_like(alpha)
    for dy in range(-R, R + 1):
        for dx in range(-R, R + 1):
            d = (dx * dx + dy * dy) ** 0.5
            wgt = min(1.0, max(0.0, r + 0.5 - d))
            if wgt <= 0:
                continue
            sl = pad[R + dy:R + dy + h, R + dx:R + dx + w]
            np.maximum(out, sl * wgt if wgt < 1 else sl, out=out)
    return out


def shift(a, dx, dy):
    out = np.zeros_like(a)
    h, w = a.shape[:2]
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    out[yd, xd] = a[ys, xs]
    return out


def blur(a, radius):
    img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.GaussianBlur(radius)), np.float32) / 255


def grade(img):
    img = ImageEnhance.Color(img).enhance(1.04)
    img = ImageEnhance.Contrast(img).enhance(1.06)
    return img


def box_mean(a, r):
    """Mean over a (2r+1)^2 box (edge-padded), via an integral image."""
    p = np.pad(a, ((r + 1, r), (r + 1, r)) + ((0, 0),) * (a.ndim - 2), mode="edge")
    ii = p.cumsum(0).cumsum(1)
    k = 2 * r + 1
    return (ii[k:, k:] - ii[:-k, k:] - ii[k:, :-k] + ii[:-k, :-k]) / (k * k)


def kuwahara(c, r):
    """Kuwahara filter: each pixel takes the mean of its least varied quadrant, which turns smooth
    shading into flat painted dabs while keeping edges crisp."""
    h, w = c.shape[:2]
    lum = c @ np.array([0.299, 0.587, 0.114], np.float32)
    m = box_mean(c, r // 2 if r > 1 else 1)
    v = box_mean(lum * lum, r // 2 if r > 1 else 1) - box_mean(lum, r // 2 if r > 1 else 1) ** 2
    q = r // 2 if r > 1 else 1
    best = np.full((h, w), np.inf, np.float32)
    out = c.copy()
    for dy in (-q, q):
        for dx in (-q, q):
            vs = shift(v, dx, dy) if True else v
            ms = shift(m, dx, dy)
            take = vs < best
            best = np.where(take, vs, best)
            out = np.where(take[..., None], ms, out)
    return out


def finish(src, dst, outline=OUTLINE, size=OUT_PX, shadow=True):
    im = Image.open(src).convert("RGBA")
    if im.size[0] != RAW:
        im = im.resize((RAW, RAW), Image.LANCZOS)
    rgb = grade(im.convert("RGB"))
    a = np.asarray(im, np.float32)[..., 3] / 255
    c = np.asarray(rgb, np.float32) / 255
    if PAINT_R:
        c = c * 0.25 + kuwahara(c, PAINT_R) * 0.75
    ol = dilate(a, outline) if outline > 0 else a
    idf = src[:-4] + "_id.png"
    if os.path.exists(idf):
        ids = np.asarray(Image.open(idf).convert("L"), np.int16)
        edge = np.zeros(ids.shape, np.float32)
        for dx, dy in ((1, 0), (0, 1), (1, 1), (1, -1)):
            sh_ = np.roll(np.roll(ids, dy, 0), dx, 1)
            diff = (ids != sh_) & (ids > 0) & (sh_ > 0)
            edge = np.maximum(edge, diff.astype(np.float32))
        line = blur(dilate(edge, INNER / 2), 1.2) * a * INNER_ALPHA
        c = c * (1 - line[..., None]) + c * 0.35 * line[..., None]
    # layers, back to front: shadow, outline, subject (straight alpha "over")
    out_c = np.zeros_like(c)
    out_a = np.zeros_like(a)

    def over(col, al):
        nonlocal out_c, out_a
        na = al + out_a * (1 - al)
        safe = np.where(na > 1e-6, na, 1)
        out_c = (col * al[..., None] + out_c * (out_a * (1 - al))[..., None]) / safe[..., None]
        out_a = na

    if shadow:
        sh = blur(shift(ol, *SHADOW), 3.0) * SHADOW_ALPHA
        over(np.zeros_like(c), sh)
    over(np.broadcast_to(EDGE_INK, c.shape), ol)
    over(c, a)
    # premultiplied downsample
    pm = np.dstack([out_c * out_a[..., None], out_a])
    big = Image.fromarray((np.clip(pm, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    # PIL treats RGBA as straight alpha when resizing; we feed premultiplied data so resize per channel
    chans = [Image.fromarray(np.asarray(big)[..., i]).resize((size, size), Image.LANCZOS) for i in range(4)]
    arr = np.dstack([np.asarray(ch, np.float32) / 255 for ch in chans])
    al = np.clip(arr[..., 3], 0, 1)
    col = np.where(al[..., None] > 1e-4, arr[..., :3] / np.maximum(al[..., None], 1e-4), 0)
    small = Image.fromarray((np.dstack([np.clip(col, 0, 1), al]) * 255 + 0.5).astype(np.uint8), "RGBA")
    rgb_s = small.convert("RGB").filter(ImageFilter.UnsharpMask(radius=0.8, percent=45, threshold=1))
    small = Image.merge("RGBA", (*rgb_s.split(), small.split()[3]))
    small.save(dst, optimize=True)


def sheet(icons_dir, out, ref=None, cols=10, bg="#6B5A9C", cell=150, names=None, label=True):
    files = sorted(glob.glob(os.path.join(icons_dir, "*.png")))
    if names:
        files = [f for f in files if os.path.basename(f)[:-4] in names]
    rows = (len(files) + cols - 1) // cols
    W = cols * cell
    H = rows * (cell + (24 if label else 0))
    canvas = Image.new("RGB", (W, H), bg)
    font = ImageFont.truetype(FONT, 17) if os.path.exists(FONT) else None
    d = ImageDraw.Draw(canvas)
    for i, f in enumerate(files):
        x, y = (i % cols) * cell, (i // cols) * (cell + (24 if label else 0))
        im = Image.open(f).convert("RGBA")
        canvas.paste(im, (x + (cell - im.size[0]) // 2, y + 6), im)
        if label:
            name = os.path.basename(f)[:-4]
            tw = d.textlength(name, font=font)
            for ox, oy in ((-1, 0), (1, 0), (0, -1), (0, 1), (0, 2)):
                d.text((x + (cell - tw) / 2 + ox, y + cell - 12 + oy), name, font=font, fill="#2B1A0E")
            d.text((x + (cell - tw) / 2, y + cell - 12), name, font=font, fill="#FFFFFF")
    if ref:
        r = Image.open(ref).convert("RGB")
        r = r.resize((W, int(r.size[1] * W / r.size[0])), Image.LANCZOS)
        both = Image.new("RGB", (W, H + r.size[1]), bg)
        both.paste(r, (0, 0))
        both.paste(canvas, (0, r.size[1]))
        canvas = both
    canvas.save(out)


def main(argv):
    if argv and argv[0] == "--sheet":
        icons_dir, out = argv[1], argv[2]
        ref = argv[argv.index("--ref") + 1] if "--ref" in argv else None
        cols = int(argv[argv.index("--cols") + 1]) if "--cols" in argv else 10
        bg = argv[argv.index("--bg") + 1] if "--bg" in argv else "#6B5A9C"
        names = argv[argv.index("--names") + 1].split(",") if "--names" in argv else None
        sheet(icons_dir, out, ref, cols, bg, names=names)
        return
    raw, dst = argv[0], argv[1]
    names = argv[2:]
    os.makedirs(dst, exist_ok=True)
    files = sorted(f for f in glob.glob(os.path.join(raw, "*.png")) if not f.endswith("_id.png"))
    for f in files:
        n = os.path.basename(f)[:-4]
        if names and n not in names:
            continue
        finish(f, os.path.join(dst, n + ".png"), OVERRIDES.get(n, OUTLINE))
        print("icon", n)


if __name__ == "__main__":
    main(sys.argv[1:])
