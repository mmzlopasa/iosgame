#!/usr/bin/env python3
"""Paints the 9-slice UI skins (panel, plank, buttons, tabs, slots, rarity cells, bars, badges) in the
kit's style: thick dark outline with little chipped notches, a bevelled body lit from the top left, a
glossy top highlight, a darker bottom lip and a short drop shadow; real materials where the kit has
them (painted wood grain on planks and tiles from the CC0 Medieval Village MegaKit's WoodTrim
texture, fibrous parchment for panels).

Shapes are signed-distance fields rendered at 4x and box-filtered down, so edges are clean. Sizes
and slice borders come from tools/ui/build_ui_assets.py (SKINS), which also writes UiGenerated.cs;
all decoration that must not stretch (notches, glints) sits inside the corner slices, and what is
left in the stretched middle is either flat or tiles along the stretch direction (horizontal grain).

Usage: python3 tools/art/skins.py [out dir] [name ...]   (default out: the game's UI/Skin folder)
Needs: pillow, numpy.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "ui"))
TEX = os.path.join(ROOT, "unity", "Assets", "Art", "Textures")
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Resources", "UI", "Skin")

SCALE = 3      # pixels per point in the shipped textures
SS = 4         # supersampling
PPT = SCALE * SS
INK = "#2B1A0E"


def rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], np.float32)


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


class Canvas:
    def __init__(self, w, h, seed=1):
        self.w, self.h = w, h
        self.pw, self.ph = int(round(w * SCALE)) * SS, int(round(h * SCALE)) * SS
        ys, xs = np.mgrid[0:self.ph, 0:self.pw].astype(np.float32)
        self.X = (xs + 0.5) / PPT
        self.Y = (ys + 0.5) / PPT
        self.c = np.zeros((self.ph, self.pw, 3), np.float32)
        self.a = np.zeros((self.ph, self.pw), np.float32)
        self.rnd = np.random.default_rng(seed)

    def over(self, col, alpha):
        alpha = np.clip(alpha, 0, 1)
        col = np.broadcast_to(col, self.c.shape) if np.ndim(col) == 1 else col
        na = alpha + self.a * (1 - alpha)
        safe = np.where(na > 1e-6, na, 1)
        self.c = (col * alpha[..., None] + self.c * (self.a * (1 - alpha))[..., None]) / safe[..., None]
        self.a = na

    def save(self, path):
        pm = np.dstack([self.c * self.a[..., None], self.a])
        h, w = self.ph // SS, self.pw // SS
        pm = pm.reshape(h, SS, w, SS, 4).mean(axis=(1, 3))
        al = pm[..., 3:4]
        col = np.where(al > 1e-5, pm[..., :3] / np.maximum(al, 1e-5), 0)
        out = np.dstack([np.clip(col, 0, 1), np.clip(al, 0, 1)])
        Image.fromarray((out * 255 + 0.5).astype(np.uint8), "RGBA").save(path, optimize=True)

    # -- fields
    def rrect(self, x0, y0, x1, y1, r):
        """Signed distance (pt) to a rounded rectangle; negative inside."""
        cx, cy, hx, hy = (x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2, (y1 - y0) / 2
        qx = np.abs(self.X - cx) - (hx - r)
        qy = np.abs(self.Y - cy) - (hy - r)
        return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r

    def noise(self, cell, octaves=3, aspect=(1.0, 1.0), seed=None):
        """Smooth value noise in -1..1 with feature size `cell` points (aspect stretches x/y)."""
        rnd = np.random.default_rng(seed) if seed is not None else self.rnd
        total = np.zeros_like(self.X)
        amp, norm = 1.0, 0.0
        for o in range(octaves):
            cs = cell / (2 ** o)
            gx = int(self.w / (cs * aspect[0])) + 3
            gy = int(self.h / (cs * aspect[1])) + 3
            g = rnd.uniform(-1, 1, (gy, gx)).astype(np.float32)
            fx = self.X / (cs * aspect[0])
            fy = self.Y / (cs * aspect[1])
            ix, iy = np.floor(fx).astype(int), np.floor(fy).astype(int)
            tx, ty = fx - ix, fy - iy
            tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
            v = (g[iy, ix] * (1 - tx) * (1 - ty) + g[iy, ix + 1] * tx * (1 - ty) + g[iy + 1, ix] * (1 - tx) * ty +
                 g[iy + 1, ix + 1] * tx * ty)
            total += v * amp
            norm += amp
            amp *= 0.5
        return total / norm

    def cover(self, d):
        return np.clip(0.5 - d * PPT, 0, 1)

    def poly(self, pts):
        """Coverage mask of a polygon given in points."""
        img = Image.new("L", (self.pw, self.ph), 0)
        ImageDraw.Draw(img).polygon([(x * PPT, y * PPT) for x, y in pts], fill=255)
        return np.asarray(img, np.float32) / 255


def texture(name, crop=None, size=None):
    im = Image.open(os.path.join(TEX, name)).convert("RGB")
    if crop:
        im = im.crop(crop)
    if size:
        im = im.resize(size, Image.LANCZOS)
    return np.asarray(im, np.float32) / 255


def tile_tex(cv, tex, scale_px, offset=(0, 0)):
    """Samples a texture across the canvas (repeating), scale_px = texture pixels per canvas pixel."""
    th, tw = tex.shape[:2]
    ys = ((np.arange(cv.ph) * scale_px + offset[1]) % th).astype(int)
    xs = ((np.arange(cv.pw) * scale_px + offset[0]) % tw).astype(int)
    return tex[ys[:, None], xs[None, :]]


def luminance_detail(tex):
    lum = tex @ np.array([0.3, 0.59, 0.11], np.float32)
    return lum / max(lum.mean(), 1e-3) - 1.0


# ---------------------------------------------------------------- the chunky element
def chunky(cv, rect, r, top, bottom, lip, hi=0.55, outline=2.5, lip_h=3.5, drop=2.5, drop_alpha=0.35, bevel=2.2,
           bevel_k=0.22, wobble=0.18, detail=None, detail_k=0.0, notches=(), glint=None, ring=None, gloss=0.0,
           grain=None):
    """Draws one outlined, bevelled, lipped body. rect = (x0, y0, x1, y1) of the outline's outer edge.
    top/bottom: fill colours (vertical gradient); lip: the darker bottom band; ring: (colour, width) of a
    lighter inner border just inside the outline (the kit's rarity cells and parchment rim)."""
    x0, y0, x1, y1 = rect
    wob = cv.noise(6.0, 2, seed=7) * wobble if wobble else 0
    d = cv.rrect(x0, y0, x1, y1, r) + wob
    if drop:
        ds = cv.rrect(x0, y0 + drop, x1, y1 + drop, r) + wob
        cv.over(np.zeros(3, np.float32), cv.cover(ds) * drop_alpha)
    cv.over(rgb(INK), cv.cover(d))
    di = d + outline                       # inner body
    ri = max(r - outline, 0.5)
    t = np.clip((cv.Y - (y0 + outline)) / max(1e-3, (y1 - y0 - 2 * outline)), 0, 1)
    col = lerp(rgb(top), rgb(bottom), t[..., None])
    if gloss:  # the kit's glossy upper half: a soft lighter band over the top ~45%
        band = 1 - smoothstep(0.38, 0.52, t)
        col = lerp(col, np.minimum(1, col * 1.0 + 0.10), (band * gloss)[..., None])
    if grain is not None:
        col = col * (1 + grain[..., None])
    if detail is not None and detail_k:
        col = col * (1 + detail[..., None] * detail_k)
    if ring is not None:
        rc, rw = ring
        ring_m = cv.cover(di) * (1 - cv.cover(di + rw))
        col = lerp(col, rgb(rc), ring_m[..., None] * 0.9)
    # lip: inner shape minus itself raised by lip_h
    raised = cv.rrect(x0 + outline, y0 + outline - lip_h, x1 - outline, y1 - outline - lip_h, ri) + wob
    lip_m = np.clip(cv.cover(di) - cv.cover(raised), 0, 1)
    col = lerp(col, rgb(lip), lip_m[..., None])
    # bevel lighting from the SDF gradient (edges facing up/left brighter, down/right darker)
    gy, gx = np.gradient(di)
    n = np.hypot(gx, gy) + 1e-6
    gx, gy = gx / n, gy / n
    band = smoothstep(-bevel, 0.0, di) * (1 - lip_m)
    shade = (-gy * 0.85 - gx * 0.35) * band * bevel_k
    col = np.clip(col * (1 + shade[..., None]), 0, 1)
    # thin top highlight just inside the outline
    if hi:
        lowered = cv.rrect(x0 + outline, y0 + outline + 1.3, x1 - outline, y1 - outline + 1.3, ri) + wob
        hm = np.clip(cv.cover(di) - cv.cover(lowered), 0, 1) * (1 - lip_m)
        col = lerp(col, np.ones(3, np.float32), (hm * hi)[..., None])
    if glint:  # a soft white streak in the top-left corner (inside the corner slice)
        gx0, gy0, gw, gh = glint
        e = ((cv.X - gx0) / gw) ** 2 + ((cv.Y - gy0) / gh) ** 2
        col = lerp(col, np.ones(3, np.float32), (np.clip(1 - e, 0, 1) ** 1.5 * 0.65 * cv.cover(di))[..., None])
    cv.over(col, cv.cover(di))
    for pts in notches:
        cv.over(rgb(INK), cv.poly(pts) * cv.cover(d))
    return d


def notch(x, y, side, size=1.6):
    """A little chipped nick into the body from the outline. side: 'top'|'bottom'|'left'|'right'."""
    s = size
    if side == "top":
        return [(x - s, y - 0.5), (x + s, y - 0.5), (x + s * 0.2, y + s * 1.1)]
    if side == "bottom":
        return [(x - s, y + 0.5), (x + s, y + 0.5), (x - s * 0.2, y - s * 1.1)]
    if side == "left":
        return [(x - 0.5, y - s), (x - 0.5, y + s), (x + s * 1.1, y + s * 0.2)]
    return [(x + 0.5, y - s), (x + 0.5, y + s), (x - s * 1.1, y - s * 0.2)]


# ---------------------------------------------------------------- skins
def parchment_detail(cv):
    fib = cv.noise(5.0, 3, aspect=(3.0, 0.6), seed=11) * 0.5 + cv.noise(2.0, 2, seed=12) * 0.5
    blot = cv.noise(14.0, 2, seed=13)
    return fib * 0.6 + blot * 0.4


def wood_grain(cv, strength=0.35, scale=0.42, offset=(0, 30)):
    tex = texture("medieval__MI_WoodTrim_albedo.jpg", crop=(0, 8, 1024, 296))
    det = luminance_detail(tile_tex(cv, tex, scale / SS * 3, offset))
    return np.clip(det, -0.6, 0.6) * strength


def btn(name, light, base, dark, lip):
    def draw():
        cv = Canvas(48, 42.5, seed=hash(name) % 1000)
        chunky(cv, (0, 0, 48, 40), 7, light, dark, lip, hi=0.5, gloss=0.9, bevel_k=0.18,
               detail=cv.noise(4.0, 2, seed=3), detail_k=0.025,
               notches=[notch(8.5, 2.2, "top", 1.6), notch(40.5, 37.8, "bottom", 1.6)],
               glint=(6.5, 6.5, 4.0, 1.6))
        return cv
    return draw


def tab_on():
    cv = Canvas(48, 34.5, seed=5)
    chunky(cv, (0, 0, 48, 32), 7, "#FFE193", "#F2B23E", "#C98420", hi=0.6, lip_h=3, gloss=0.8,
           notches=[notch(8.5, 2.2, "top", 1.2)], glint=(6.5, 6.5, 3.6, 1.5))
    return cv


def tab_off():
    cv = Canvas(48, 33.5, seed=6)
    g = wood_grain(cv, 0.22)
    chunky(cv, (0, 0, 48, 32), 7, "#BD7F4E", "#A0643A", "#7A4522", hi=0.22, lip_h=2.5, drop=1.5, grain=g,
           notches=[notch(39.5, 2.2, "top", 1.2)])
    return cv


def panel():
    cv = Canvas(72, 74.5, seed=8)
    det = parchment_detail(cv)
    chunky(cv, (0, 0, 72, 72), 11, "#EED7B4", "#E6C89F", "#C9A275", hi=0.0, lip_h=3.5, bevel=2.0, bevel_k=0.12,
           detail=det, detail_k=0.07, ring=("#FBEEDA", 1.6), wobble=0.22,
           notches=[notch(0.8, 9.0, "left", 2.3), notch(71.2, 62.0, "right", 2.3), notch(10.0, 0.8, "top", 2.0),
                    notch(60.0, 71.2, "bottom", 2.0)])
    # speckles of darker fibre (sparse)
    sp = (cv.noise(0.8, 1, seed=21) > 0.82).astype(np.float32) * 0.18
    cv.over(rgb("#9A6A3A"), sp * cv.cover(cv.rrect(4, 4, 68, 67, 8)))
    return cv


def plank():
    cv = Canvas(80, 36.5, seed=9)
    g = wood_grain(cv, 0.42, 0.5)
    chunky(cv, (0, 0, 80, 34), 6, "#E7A869", "#C98450", "#9A5A2E", hi=0.45, lip_h=3.5, bevel=2.2, bevel_k=0.2,
           grain=g, wobble=0.3,
           notches=[notch(0.8, 8, "left", 2.1), notch(79.2, 24, "right", 2.1), notch(11, 0.8, "top", 1.8),
                    notch(68, 33.2, "bottom", 1.8)],
           glint=(7.0, 6.0, 4.0, 1.4))
    return cv


def section():
    cv = Canvas(48, 48, seed=10)
    d = cv.rrect(0.3, 0.3, 47.7, 47.7, 8) + cv.noise(6.0, 2, seed=7) * 0.12
    det = parchment_detail(cv)
    t = np.clip(cv.Y / 48, 0, 1)
    col = lerp(rgb("#DDB78A"), rgb("#E2BE93"), t[..., None]) * (1 + det[..., None] * 0.05)
    # inner shadow along the top and left, light lip at the bottom (an inset in the parchment)
    lowered = cv.rrect(0.3, 2.2, 47.7, 49.6, 8)
    sh = np.clip(cv.cover(d) - cv.cover(lowered), 0, 1)
    col = lerp(col, rgb("#B98D5E"), sh[..., None] * 0.7)
    edge = np.clip(cv.cover(d) - cv.cover(d + 1.2), 0, 1)
    col = lerp(col, rgb("#C49A6A"), edge[..., None] * 0.8)
    raised = cv.rrect(0.3, -1.4, 47.7, 46.3, 8)
    lip = np.clip(cv.cover(d) - cv.cover(raised), 0, 1)
    col = lerp(col, rgb("#F4E2C6"), lip[..., None] * 0.9)
    cv.over(col, cv.cover(d))
    return cv


def slot():
    cv = Canvas(48, 50.5, seed=12)
    chunky(cv, (0, 0, 48, 48), 8, "#F6E7CD", "#ECD3AC", "#CFA978", hi=0.6, lip_h=3, bevel_k=0.12,
           detail=parchment_detail(cv), detail_k=0.05, notches=[notch(0.8, 9, "left", 1.3)])
    return cv


def tile():
    cv = Canvas(48, 50.5, seed=13)
    g = wood_grain(cv, 0.38, 0.45, (200, 60))
    chunky(cv, (0, 0, 48, 48), 9, "#F0B97C", "#D0905A", "#995A2E", hi=0.45, lip_h=4, bevel_k=0.22, grain=g,
           ring=("#F9CF98", 1.2), notches=[notch(0.8, 9, "left", 1.4), notch(39, 47.2, "bottom", 1.3)],
           glint=(6.5, 6.5, 3.5, 1.5))
    return cv


def cell(top, bottom, ring, lip):
    def draw():
        cv = Canvas(48, 50.5, seed=14)
        chunky(cv, (0, 0, 48, 48), 9, top, bottom, lip, hi=0.0, lip_h=4, bevel=1.6, bevel_k=0.1,
               ring=(ring, 2.0), outline=2.6, wobble=0.12)
        # the kit's little light tick on the left edge and a bevel shine along the top ring
        tick = cv.poly([(2.6, 6.2), (2.6, 9.4), (5.0, 7.8)])
        cv.over(np.minimum(1, rgb(ring) + 0.25), tick * 0.9)
        return cv
    return draw


def capsule():
    cv = Canvas(40, 26, seed=15)
    d = cv.rrect(0.4, 0.4, 39.6, 25.6, 11)
    cv.over(rgb("#1A0F08"), cv.cover(d) * 0.82)
    di = d + 1.5
    t = np.clip(cv.Y / 26, 0, 1)
    col = lerp(rgb("#3A2618"), rgb("#24170E"), t[..., None])
    hi = np.clip(cv.cover(di) - cv.cover(cv.rrect(1.9, 3.2, 38.1, 27.4, 9.5)), 0, 1)
    col = lerp(col, rgb("#8A6A50"), hi[..., None] * 0.6)
    cv.over(col, cv.cover(di) * 0.72)
    return cv


def bar_track():
    cv = Canvas(30, 14, seed=16)
    d = cv.rrect(0, 0, 30, 14, 7)
    cv.over(rgb(INK), cv.cover(d))
    di = d + 1.5
    col = np.broadcast_to(rgb("#4A2E1A"), cv.c.shape).copy()
    sh = np.clip(cv.cover(di) - cv.cover(cv.rrect(1.5, 3.6, 28.5, 14.6, 5.5)), 0, 1)
    col = lerp(col, rgb("#24140A"), sh[..., None] * 0.9)
    lo = np.clip(cv.cover(di) - cv.cover(cv.rrect(1.5, 0.5, 28.5, 11.6, 5.5)), 0, 1)
    col = lerp(col, rgb("#6A4428"), lo[..., None] * 0.7)
    cv.over(col, cv.cover(di))
    return cv


def fill(light, base, dark, line):
    def draw():
        cv = Canvas(30, 14, seed=17)
        d = cv.rrect(0, 0, 30, 14, 7)
        cv.over(rgb(line), cv.cover(d))
        di = d + 1.4
        t = np.clip(cv.Y / 14, 0, 1)
        col = np.where((t < 0.5)[..., None], lerp(rgb(light), rgb(base), (t / 0.5)[..., None]),
                       lerp(rgb(base), rgb(dark), ((t - 0.5) / 0.5)[..., None]))
        band = cv.cover(cv.rrect(3.5, 2.6, 26.5, 5.6, 1.5))
        col = lerp(col, np.ones(3, np.float32), band[..., None] * 0.45)
        cv.over(col, cv.cover(di))
        return cv
    return draw


def badge(top, bottom, lip, r, name):
    def draw():
        cv = Canvas(20, 21.5, seed=18)
        chunky(cv, (0, 0, 20, 20), r, top, bottom, lip, hi=0.5, outline=2.0, lip_h=2.2, drop=1.5, bevel=1.4,
               bevel_k=0.15, wobble=0.0, gloss=0.8, glint=(5.0, 5.0, 2.4, 1.1))
        return cv
    return draw


SKINS = {
    "panel": panel,
    "plank": plank,
    "section": section,
    "btn_green": btn("btn_green", "#B9E66A", "#9ACC41", "#86BA30", "#5E8E1E"),
    "btn_blue": btn("btn_blue", "#BCD6FA", "#94B9EF", "#7FA4E4", "#5476BC"),
    "btn_orange": btn("btn_orange", "#FFD877", "#FDB939", "#F4A11E", "#C67314"),
    "btn_grey": btn("btn_grey", "#D2D2D2", "#B3B3B3", "#A2A2A2", "#767676"),
    "btn_red": btn("btn_red", "#F0806F", "#DD5444", "#CB4132", "#922418"),
    "btn_brown": btn("btn_brown", "#ECB47A", "#D69553", "#C38045", "#87491E"),
    "tab_on": tab_on,
    "tab_off": tab_off,
    "slot": slot,
    "tile": tile,
    "cell_common": cell("#8E8E8E", "#DADADA", "#BDBDBD", "#686868"),
    "cell_uncommon": cell("#4C9024", "#B2E879", "#8AD13E", "#3A7418"),
    "cell_rare": cell("#1E7EA8", "#8EDCF6", "#3FB8EA", "#176688"),
    "cell_epic": cell("#9446C6", "#EFA8F8", "#C67CEA", "#7534A4"),
    "cell_legendary": cell("#D4691C", "#FBC27A", "#F49E48", "#B0531A"),
    "capsule": capsule,
    "bar_track": bar_track,
    "fill_green": fill("#C9F27E", "#8BD040", "#69AE2A", "#2F5A14"),
    "fill_gold": fill("#FFE792", "#FDB939", "#E6961E", "#7A4A10"),
    "fill_blue": fill("#C6DEFB", "#94B9EF", "#6C96DA", "#2C4A80"),
    "fill_red": fill("#F6998B", "#DD5444", "#BF3A2A", "#6E1A10"),
    "badge_green": badge("#B2E35E", "#76BA2E", "#55871C", 5, "badge_green"),
    "badge_red": badge("#F37B6A", "#D6402F", "#94241A", 10, "badge_red"),
}


def check_sizes():
    """The textures must match the sizes the slice table (build_ui_assets.SKINS) was made for."""
    try:
        import build_ui_assets as B
    except Exception:  # pragma: no cover - chromium / fonttools missing is fine here
        return
    for name, fn in SKINS.items():
        _html, w, h, *_s = B.SKINS[name]
        cv = fn()
        if (cv.pw // SS, cv.ph // SS) != (int(round(w * SCALE)), int(round(h * SCALE))):
            raise SystemExit(f"{name}: size {cv.pw // SS}x{cv.ph // SS} != {w}x{h} pt in build_ui_assets.SKINS")


def main(argv):
    out = argv[0] if argv else OUT
    names = argv[1:] or list(SKINS)
    os.makedirs(out, exist_ok=True)
    for n in names:
        cv = SKINS[n]()
        cv.save(os.path.join(out, n + ".png"))
        print("skin", n, cv.pw // SS, "x", cv.ph // SS)


if __name__ == "__main__":
    main(sys.argv[1:])
