#!/usr/bin/env python3
"""Prepares the Adventure UI Kit (Smashy Tech, licensed, private repository mmzlopasa/iosgame-uikit) for the
Godot build, as the Unity editor step FoundersEditor.AdventureUiKit did: the pictures chosen in
AdventureUiKit.txt are copied to godot/art/ui/Kit/Skin/<name>.png, their purple stone turned blue-grey
(#202734 / #36415A), and their 9-slice borders (from the kit's Unity .meta files) written to slices.txt.
CI only: the output is never committed (godot/art/ui/Kit is in .gitignore).

usage: kit_skins.py <kit checkout>/Kit <AdventureUiKit.txt> <out folder>
"""
import colorsys, os, re, sys
import numpy as np
from PIL import Image

RAMP = [(0.000, (0x07, 0x09, 0x0D)), (0.164, (0x20, 0x27, 0x34)), (0.324, (0x36, 0x41, 0x5A)),
        (0.550, (0x6A, 0x78, 0x94)), (0.800, (0xAE, 0xB9, 0xCD)), (1.000, (0xF2, 0xF5, 0xFA))]


def smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def inv(a, b, x):
    return np.clip((x - a) / (b - a), 0, 1)


def recolor(img):
    a = np.asarray(img.convert('RGBA')).astype(np.float32) / 255.0
    rgb = a[..., :3]
    mx, mn = rgb.max(-1), rgb.min(-1)
    v = mx
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    d = np.maximum(mx - mn, 1e-6)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    h = np.where(mx == mn, 0, h)
    hue = smooth(inv(215, 232, h)) * (1 - smooth(inv(335, 350, h))) * smooth(inv(0.04, 0.10, s))
    dark = (1 - smooth(inv(0.06, 0.14, s))) * (1 - smooth(inv(0.30, 0.45, v)))
    w = np.maximum(hue, dark) * (a[..., 3] > 0)
    y = r * 0.299 + g * 0.587 + b * 0.114
    stone = np.zeros_like(rgb)
    ys = [p[0] for p in RAMP]
    for c in range(3):
        stone[..., c] = np.interp(y, ys, [p[1][c] / 255.0 for p in RAMP])
    out = rgb + (stone - rgb) * w[..., None]
    res = np.concatenate([out, a[..., 3:4]], -1)
    return Image.fromarray((np.clip(res, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA')


def borders(path, size):
    meta = path + '.meta'
    if os.path.exists(meta):
        m = re.search(r'spriteBorder:\s*\{x:\s*([\d.]+),\s*y:\s*([\d.]+),\s*z:\s*([\d.]+),\s*w:\s*([\d.]+)\}', open(meta, encoding='utf-8', errors='ignore').read())
        if m:
            x, y, z, w = (round(float(v)) for v in m.groups())
            if x or y or z or w:
                return x, w, z, y  # left, top, right, bottom
    s = round(min(size) * 0.32) if size[0] > 0 else 16
    return s, s, s, s


def middle_light(img):
    a = np.asarray(img.convert('RGBA')).astype(np.float32) / 255.0
    hgt, wid = a.shape[:2]
    mid = a[hgt // 3: hgt * 2 // 3, wid // 3: wid * 2 // 3]
    mask = mid[..., 3] >= 0.5
    if not mask.any():
        return None
    lum = (mid[..., 0] * 0.299 + mid[..., 1] * 0.587 + mid[..., 2] * 0.114)[mask].mean()
    return lum > 0.55


def main(kit, mapfile, out):
    os.makedirs(os.path.join(out, 'Skin'), exist_ok=True)
    lines = ['# name left top right bottom light|dark   (pixels; written by tools/godot/kit_skins.py)']
    used = 0
    for raw in open(mapfile, encoding='utf-8'):
        line = raw.strip()
        if not line or line.startswith('#') or '=' not in line:
            continue
        key, val = (p.strip() for p in line.split('=', 1))
        if not key.startswith('skin:') or val == 'none':
            continue
        name = key[5:]
        rel = val[len('Assets/'):] if val.startswith('Assets/') else val
        src = os.path.join(kit, rel)
        if not os.path.exists(src):
            print(f'  {key}: not in the kit ({rel}), the game\'s own is kept')
            continue
        img = Image.open(src)
        img.load()
        l, t, r, b = borders(src, img.size)
        img = recolor(img)
        img.save(os.path.join(out, 'Skin', name + '.png'))
        light = middle_light(img)
        lines.append(f'{name} {l} {t} {r} {b}' + ('' if light is None else (' light' if light else ' dark')))
        used += 1
    open(os.path.join(out, 'Skin', 'slices.txt'), 'w').write('\n'.join(lines) + '\n')
    print(f'Adventure UI Kit: {used} skins prepared.')


if __name__ == '__main__':
    main(*sys.argv[1:4])
