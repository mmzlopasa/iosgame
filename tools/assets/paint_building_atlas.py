#!/usr/bin/env python3
"""Paints the texture atlas of the detailed (Foundation-style) buildings.

One 2048 px atlas of 4x4 painted tiles (512 px each) is shared by every detailed building, so a
whole building is one mesh with one material (one draw call). Tiles are hand-painted in look:
soft low-frequency colour, brush-stroke grain along the wood, darker gaps and lighter worn edges.
blender_detailed_buildings.py maps each part of a building into one tile (see TILES there).

Writes unity/Assets/Art/Textures/bw_atlas_albedo.png, bw_atlas_normal.png (from the painted
height) and bw_atlas_mask.png (URP mask: R metallic, G occlusion, A smoothness - matte).

python3 tools/assets/paint_building_atlas.py [--atlas-preview out.png]
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "Textures")
T = 512          # tile size
N = 4            # tiles per side

# Tile order (row-major, row 0 at the top of the image). Keep in sync with blender_detailed_buildings.py.
TILES = ["planks", "planks_old", "log", "logend",
         "shingle", "shingle_red", "thatch", "thatch_old",
         "stone", "plaster", "cloth_red", "cloth_ochre",
         "hide", "iron", "wattle", "straw"]

rng = np.random.default_rng(7)


def hexc(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def noise(shape, scale, stretch=(1, 1), seed=None):
    """Smooth value noise in 0..1: random grid upsampled bicubically (stretch makes streaks)."""
    r = np.random.default_rng(seed) if seed is not None else rng
    h, w = shape
    gh = max(2, int(h / scale / stretch[0]))
    gw = max(2, int(w / scale / stretch[1]))
    g = r.random((gh, gw)).astype(np.float32)
    img = Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return np.asarray(img).astype(np.float32) / 255.0


def fbm(shape, scale, octaves=4, stretch=(1, 1), seed=None):
    out = np.zeros(shape, np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        out += noise(shape, scale / (2 ** o), stretch, None if seed is None else seed + o) * amp
        tot += amp
        amp *= 0.5
    return out / tot


def blur(a, r):
    img = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.GaussianBlur(r))).astype(np.float32) / 255.0


def mix(c0, c1, t):
    t = t[..., None]
    return c0 * (1 - t) + c1 * t


def wood_grain(shape, base, light, dark, boards=1, seed=0, gap=True):
    """Boards running vertically (grain along v): brush streaks, a few knots, dark gaps between boards."""
    h, w = shape
    streak = fbm(shape, 90, 4, stretch=(8, 1), seed=seed)
    fine = fbm(shape, 20, 3, stretch=(10, 1), seed=seed + 10)
    col = mix(base, light, np.clip((streak - 0.35) * 1.6, 0, 1))
    col = mix(col, dark, np.clip((0.45 - fine) * 1.4, 0, 1) * 0.55)
    height = streak * 0.5 + fine * 0.3
    x = np.arange(w)[None, :].repeat(h, 0)
    y = np.arange(h)[:, None].repeat(w, 1)
    if boards > 1:
        bw = w / boards
        local = (x % bw) / bw
        # Per board tint and a darker, rounded edge (painted bevel).
        idx = (x // bw).astype(int)
        tints = np.random.default_rng(seed + 3).uniform(-0.07, 0.07, boards + 1)
        col = col * (1 + tints[idx])[..., None]
        edge = np.minimum(local, 1 - local) * bw
        if gap:
            g = np.clip(1 - edge / 3.0, 0, 1)
            col = mix(col, dark * 0.45, g)
            height = height * np.clip(edge / 6.0, 0.2, 1)
        hl = np.clip(1 - np.abs(edge - 7) / 4.0, 0, 1) * 0.12
        col = col + hl[..., None]
    # Knots
    r = np.random.default_rng(seed + 5)
    for _ in range(3):
        cx, cy, rad = r.uniform(0, w), r.uniform(0, h), r.uniform(6, 13)
        d = np.sqrt(((x - cx) / rad) ** 2 + ((y - cy) / (rad * 2.2)) ** 2)
        k = np.clip(1 - d, 0, 1)
        col = mix(col, dark * 0.7, np.clip(k * 2, 0, 1) * 0.8)
        height = height - k * 0.3
    return col, height


def tile_planks():
    return wood_grain((T, T), hexc("#94704d"), hexc("#b48a60"), hexc("#5a3f2a"), boards=5, seed=11)


def tile_planks_old():
    return wood_grain((T, T), hexc("#857563"), hexc("#a59580"), hexc("#4d4236"), boards=4, seed=23)


def tile_log():
    """A debarked log along v: warm light wood, long streaks, a few bark remnants at the sides."""
    col, height = wood_grain((T, T), hexc("#b98c5f"), hexc("#d6ab7a"), hexc("#7b5636"), boards=1, seed=31)
    x = np.linspace(0, 1, T)[None, :].repeat(T, 0)
    shade = 0.82 + 0.18 * np.sin(x * np.pi)
    col *= shade[..., None]
    side = np.clip((np.abs(x - 0.5) - 0.3) * 6, 0, 1)
    bark = (fbm((T, T), 24, 3, stretch=(5, 1), seed=37) * (0.7 + side * 0.5) > 0.66).astype(np.float32)
    bark = blur(bark, 1.2)
    col = mix(col, hexc("#5b4330"), bark * 0.75)
    return col, height + bark * 0.4


def tile_logend():
    """End of a sawn log: rings, radial cracks, a darker bark rim (mapped as a disc)."""
    y, x = np.mgrid[0:T, 0:T].astype(np.float32)
    cx = cy = T / 2
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / (T / 2)
    wob = fbm((T, T), 60, 3, seed=41) * 0.08
    rings = 0.5 + 0.5 * np.sin((d + wob) * 2 * np.pi * 6.5)
    base = mix(hexc("#e2c193"), hexc("#c9a06f"), np.clip(d * 1.2, 0, 1))
    col = mix(base, hexc("#9f7449"), np.clip((rings - 0.6) * 2.4, 0, 1) * 0.6)
    a = np.arctan2(y - cy, x - cx)
    crack = np.zeros_like(d)
    for ang in (0.6, 2.3, 4.4):
        da = np.abs(np.angle(np.exp(1j * (a - ang))))
        crack += np.clip(1 - da * 40, 0, 1) * (d > 0.25) * (d < 0.85)
    col = mix(col, hexc("#6b4a2e"), np.clip(crack, 0, 1) * 0.8)
    rim = np.clip((d - 0.86) * 12, 0, 1)
    col = mix(col, hexc("#5e4330"), rim)
    col = mix(col, hexc("#e8cfa6"), np.clip(1 - d * 6, 0, 1) * 0.5)
    height = 0.6 - rings * 0.1 - crack * 0.4 + rim * 0.2
    return col, height


def tile_shingle(base, light, dark, seed):
    """One painted wooden shingle per tile column: grain, a darker lower edge, chipped corners.
    The tile holds 4 shingles side by side so buildings can pick one at random (u offset)."""
    col, height = wood_grain((T, T), base, light, dark, boards=4, seed=seed, gap=True)
    y = np.linspace(0, 1, T)[:, None].repeat(T, 1)
    # Lower edge (v=0 at the bottom of the tile image) darker and thicker; top fades under the next row.
    col *= (0.78 + 0.3 * np.clip(y * 1.4, 0, 1))[..., None]
    lip = np.clip((y - 0.9) * 10, 0, 1)
    col = mix(col, dark * 0.8, lip * 0.6)
    tint = fbm((T, T), 200, 2, seed=seed + 2)
    col *= (0.9 + tint * 0.2)[..., None]
    return col, height + (1 - y) * 0.3


def tile_thatch(base, light, dark, seed):
    """Straw laid downhill: many thin strands along v, clumps of lighter and darker straw."""
    straws = fbm((T, T), 4, 2, stretch=(22, 1), seed=seed)
    long = fbm((T, T), 12, 2, stretch=(12, 1), seed=seed + 4)
    clumps = fbm((T, T), 90, 2, stretch=(2.5, 1), seed=seed + 1)
    col = mix(base, light, np.clip((straws - 0.5) * 3.4, 0, 1) * 0.85)
    col = mix(col, dark, np.clip((0.42 - straws) * 3.0, 0, 1) * 0.45)
    col = mix(col, light, np.clip((long - 0.62) * 3, 0, 1) * 0.3)
    col *= (0.94 + clumps * 0.1)[..., None]
    y = np.linspace(0, 1, T)[:, None].repeat(T, 1)
    # Butt ends at the lower edge of the tile darker (shadowed).
    col *= (0.8 + 0.2 * np.clip(y * 3, 0, 1))[..., None]
    return col, straws * 0.7 + long * 0.3


def tile_stone():
    """Painted blue-grey stone: soft light top-left, mossy darker bottom, a few chips.
    Each stone of a building maps a random sub-rectangle, so the tile is one big rounded face."""
    y, x = np.mgrid[0:T, 0:T].astype(np.float32) / T
    big = fbm((T, T), 120, 4, seed=51)
    col = mix(hexc("#727d84"), hexc("#9ea8ad"), np.clip((big - 0.3) * 1.5, 0, 1))
    col = mix(col, hexc("#5c6266"), np.clip((0.4 - big) * 2, 0, 1) * 0.7)
    moss = np.clip((fbm((T, T), 60, 3, seed=55) - 0.58) * 4, 0, 1) * np.clip(y * 1.5 - 0.3, 0, 1)
    col = mix(col, hexc("#6e7f4a"), moss * 0.7)
    # Mortar-like cracks
    cr = fbm((T, T), 30, 2, seed=57)
    crack = np.clip(1 - np.abs(cr - 0.5) * 60, 0, 1) * (fbm((T, T), 90, 2, seed=59) > 0.55)
    col = mix(col, hexc("#4a525a"), crack * 0.35)
    return col, big * 0.8 - crack * 0.3


def tile_plaster():
    big = fbm((T, T), 140, 4, seed=61)
    col = mix(hexc("#a69e88"), hexc("#bcb49d"), np.clip((big - 0.4) * 2, 0, 1))
    col = mix(col, hexc("#8f8670"), np.clip((0.4 - big) * 3, 0, 1) * 0.6)
    y = np.linspace(0, 1, T)[:, None].repeat(T, 1)
    dirt = fbm((T, T), 50, 3, stretch=(3, 1), seed=63) * np.clip(y * 2 - 1.2, 0, 1)
    col = mix(col, hexc("#8a7a60"), dirt * 0.7)
    return col, big * 0.5


def tile_cloth(base, seed):
    weave = (np.sin(np.arange(T) * 1.3)[None, :] * np.sin(np.arange(T) * 1.3)[:, None]) * 0.03
    folds = fbm((T, T), 120, 3, stretch=(1, 3), seed=seed)
    col = base[None, None, :] * (0.78 + folds * 0.35)[..., None] + weave[..., None]
    y = np.linspace(0, 1, T)[:, None].repeat(T, 1)
    hem = np.clip(1 - np.abs(y - 0.93) * 40, 0, 1)
    col = mix(col, base * 0.6, hem)
    return col, folds


def tile_hide():
    big = fbm((T, T), 100, 4, seed=71)
    col = mix(hexc("#9a7350"), hexc("#c09670"), np.clip((big - 0.35) * 1.8, 0, 1))
    spots = np.clip((fbm((T, T), 30, 2, seed=73) - 0.6) * 4, 0, 1)
    col = mix(col, hexc("#6d4c32"), spots * 0.5)
    return col, big * 0.4


def tile_iron():
    big = fbm((T, T), 60, 4, seed=81)
    col = mix(hexc("#35383c"), hexc("#5d6266"), big)
    rust = np.clip((fbm((T, T), 40, 3, seed=83) - 0.62) * 4, 0, 1)
    col = mix(col, hexc("#7a4a2c"), rust * 0.6)
    return col, big * 0.3


def tile_wattle():
    """Woven hazel rods: horizontal rods passing in front of and behind upright stakes."""
    y, x = np.mgrid[0:T, 0:T].astype(np.float32)
    rod = 0.5 + 0.5 * np.cos(y / T * 2 * np.pi * 12)
    weave = np.sin(x / T * 2 * np.pi * 4 + np.floor(y / T * 12) * np.pi)
    col = mix(hexc("#5a4130"), hexc("#9a7550"), rod * (0.65 + 0.35 * weave))
    col *= (0.85 + fbm((T, T), 30, 3, stretch=(1, 6), seed=91) * 0.3)[..., None]
    return col, rod * (0.6 + 0.4 * weave)


def tile_straw():
    """Hay and herbs: loose golden-green strands (hay bales, drying bundles)."""
    s = fbm((T, T), 10, 3, stretch=(6, 1), seed=95)
    col = mix(hexc("#c4a660"), hexc("#e6cf8a"), np.clip((s - 0.4) * 2, 0, 1))
    col = mix(col, hexc("#7f8a3c"), np.clip((fbm((T, T), 70, 2, seed=97) - 0.55) * 3, 0, 1) * 0.6)
    return col, s


def paint():
    tiles = {
        "planks": tile_planks(),
        "planks_old": tile_planks_old(),
        "log": tile_log(),
        "logend": tile_logend(),
        "shingle": tile_shingle(hexc("#8d6243"), hexc("#ad7d55"), hexc("#55392a"), 101),
        "shingle_red": tile_shingle(hexc("#8e5a3e"), hexc("#a8714d"), hexc("#553223"), 111),
        "thatch": tile_thatch(hexc("#bf9c5e"), hexc("#e2c886"), hexc("#7a5d38"), 121),
        "thatch_old": tile_thatch(hexc("#a88c5c"), hexc("#ccb07a"), hexc("#665036"), 131),
        "stone": tile_stone(),
        "plaster": tile_plaster(),
        "cloth_red": tile_cloth(hexc("#b3302a"), 141),
        "cloth_ochre": tile_cloth(hexc("#d68f2c"), 151),
        "hide": tile_hide(),
        "iron": tile_iron(),
        "wattle": tile_wattle(),
        "straw": tile_straw(),
    }
    albedo = np.zeros((T * N, T * N, 3), np.float32)
    height = np.zeros((T * N, T * N), np.float32)
    for i, name in enumerate(TILES):
        c, h = tiles[name]
        r, k = divmod(i, N)
        albedo[r * T:(r + 1) * T, k * T:(k + 1) * T] = np.clip(c, 0, 1)
        hh = (h - h.min()) / max(1e-6, h.max() - h.min())
        height[r * T:(r + 1) * T, k * T:(k + 1) * T] = hh
    return albedo, height


def normal_from_height(h, strength=2.2):
    gy, gx = np.gradient(blur(h, 1.0))
    nx, ny, nz = -gx * strength * 8, gy * strength * 8, np.ones_like(h)
    l = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return (n * 0.5 + 0.5)


def main():
    albedo, height = paint()
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray((albedo * 255).astype(np.uint8)).save(os.path.join(OUT, "bw_atlas_albedo.png"))
    Image.fromarray((normal_from_height(height) * 255).astype(np.uint8)).save(os.path.join(OUT, "bw_atlas_normal.png"))
    mask = np.zeros((T * N, T * N, 4), np.uint8)
    mask[..., 1] = (190 + height * 65).astype(np.uint8)   # occlusion: crevices darker
    mask[..., 3] = 18                                       # smoothness: matte painted look
    iron = TILES.index("iron")
    r, k = divmod(iron, N)
    mask[r * T:(r + 1) * T, k * T:(k + 1) * T, 0] = 90
    mask[r * T:(r + 1) * T, k * T:(k + 1) * T, 3] = 60
    Image.fromarray(mask, "RGBA").save(os.path.join(OUT, "bw_atlas_mask.png"))
    if "--atlas-preview" in sys.argv:
        Image.fromarray((albedo * 255).astype(np.uint8)).resize((1024, 1024)).save(sys.argv[sys.argv.index("--atlas-preview") + 1])
    print("atlas written to", OUT)


if __name__ == "__main__":
    main()
