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

    def pnoise(self, period, cells, octaves=3, origin=(0.0, 0.0), seed=0, gain=0.5):
        """Periodic value noise in -1..1: repeats every `period` (px, py) points from `origin`, with `cells`
        (cx, cy) lattice cells per period at the first octave (doubling each octave). Tiled 9-slice skins use it
        so their middle and edges repeat without seams."""
        rnd = np.random.default_rng(seed)
        total = np.zeros_like(self.X)
        amp, norm = 1.0, 0.0
        for o in range(octaves):
            cx, cy = int(cells[0] * 2 ** o), int(cells[1] * 2 ** o)
            g = rnd.uniform(-1, 1, (cy, cx)).astype(np.float32)
            fx = (self.X - origin[0]) / period[0] * cx
            fy = (self.Y - origin[1]) / period[1] * cy
            ix, iy = np.floor(fx).astype(int), np.floor(fy).astype(int)
            tx, ty = fx - ix, fy - iy
            tx = tx * tx * tx * (tx * (tx * 6 - 15) + 10)
            ty = ty * ty * ty * (ty * (ty * 6 - 15) + 10)
            x0, x1, y0, y1 = ix % cx, (ix + 1) % cx, iy % cy, (iy + 1) % cy
            v = (g[y0, x0] * (1 - tx) * (1 - ty) + g[y0, x1] * tx * (1 - ty) + g[y1, x0] * (1 - tx) * ty +
                 g[y1, x1] * tx * ty)
            total += v * amp
            norm += amp
            amp *= gain
        return total / norm

    def polygon(self, pts):
        """Exact signed distance (pt) to a polygon; negative inside."""
        X, Y = self.X, self.Y
        d = np.full(X.shape, np.inf, np.float32)
        sgn = np.ones(X.shape, np.float32)
        n = len(pts)
        for i in range(n):
            ax, ay = pts[i]
            bx, by = pts[i - 1]
            ex, ey = bx - ax, by - ay
            wx, wy = X - ax, Y - ay
            t = np.clip((wx * ex + wy * ey) / (ex * ex + ey * ey), 0, 1)
            qx, qy = wx - ex * t, wy - ey * t
            d = np.minimum(d, qx * qx + qy * qy)
            c1, c2, c3 = Y >= ay, Y < by, ex * wy > ey * wx
            flip = (c1 & c2 & c3) | (~c1 & ~c2 & ~c3)
            sgn = np.where(flip, -sgn, sgn)
        return sgn * np.sqrt(d)

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
           grain=None, ink=INK, poly=None, gloss_edge=(0.38, 0.52), gloss_add=0.10, stripes=0.0, hi_w=1.3):
    """Draws one outlined, bevelled, lipped body. rect = (x0, y0, x1, y1) of the outline's outer edge.
    top/bottom: fill colours (vertical gradient); lip: the darker bottom band; ring: (colour, width) of a
    lighter inner border just inside the outline (the kit's rarity cells and parchment rim)."""
    x0, y0, x1, y1 = rect
    wob = cv.noise(6.0, 2, seed=7) * wobble if wobble else 0

    def base(oy):
        """Outer shape moved down by oy points (rounded rectangle, or a polygon rounded by r)."""
        if poly is None:
            return cv.rrect(x0, y0 + oy, x1, y1 + oy, r) + wob
        return cv.polygon([(x, y + oy) for x, y in poly]) - r + wob

    d = base(0)
    if drop:
        cv.over(np.zeros(3, np.float32), cv.cover(base(drop)) * drop_alpha)
    cv.over(rgb(ink), cv.cover(d))
    di = d + outline                       # inner body
    t = np.clip((cv.Y - (y0 + outline)) / max(1e-3, (y1 - y0 - 2 * outline)), 0, 1)
    col = lerp(rgb(top), rgb(bottom), t[..., None])
    if gloss:  # the kit's glossy upper half: a soft lighter band over the top ~45%
        band = 1 - smoothstep(gloss_edge[0], gloss_edge[1], t)
        col = lerp(col, np.minimum(1, col * 1.0 + gloss_add), (band * gloss)[..., None])
    if stripes:  # the kit's subtle diagonal stripe texture
        ph = ((cv.X + cv.Y) / 3.2) % 1.0
        col = lerp(col, np.minimum(1, col + 0.12), (smoothstep(0.0, 0.08, ph) * (1 - smoothstep(0.42, 0.5, ph))
                                                  * stripes)[..., None])
    if grain is not None:
        col = col * (1 + grain[..., None])
    if detail is not None and detail_k:
        col = col * (1 + detail[..., None] * detail_k)
    if ring is not None:
        rc, rw = ring
        ring_m = cv.cover(di) * (1 - cv.cover(di + rw))
        col = lerp(col, rgb(rc), ring_m[..., None] * 0.9)
    # lip: inner shape minus itself raised by lip_h
    raised = base(-lip_h) + outline
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
        lowered = base(hi_w) + outline
        hm = np.clip(cv.cover(di) - cv.cover(lowered), 0, 1) * (1 - lip_m)
        col = lerp(col, np.ones(3, np.float32), (hm * hi)[..., None])
    if glint:  # a soft white streak in the top-left corner (inside the corner slice)
        gx0, gy0, gw, gh = glint
        e = ((cv.X - gx0) / gw) ** 2 + ((cv.Y - gy0) / gh) ** 2
        col = lerp(col, np.ones(3, np.float32), (np.clip(1 - e, 0, 1) ** 1.5 * 0.65 * cv.cover(di))[..., None])
    cv.over(col, cv.cover(di))
    for pts in notches:
        cv.over(rgb(ink), cv.poly(pts) * cv.cover(d))
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


# ---------------------------------------------------------------- procedural materials (CC0 by construction)
# Real-looking wood, parchment and wrought iron, painted from periodic noise so the tiled skins (panel, plank,
# section: -unity-slice-type: tiled) repeat their middle and edges without seams. Every material returns a colour
# (H x W x 3) and, where it has relief, a height map that light_relief() turns into shading (light from the top left).

LIGHT = np.array([-0.45, -0.65], np.float32)    # screen-space direction the light comes from (x, y)


def light_relief(cv, height, strength):
    """Shading factor (~1) from a height map: slopes facing the light are brighter, the others darker."""
    gy, gx = np.gradient(height, 1.0 / PPT)
    return 1 + (gx * LIGHT[0] + gy * LIGHT[1]) * strength


def wood(cv, period, origin, along="x", light="#C98A52", dark="#7A4422", ring=7.0, seed=1, knots=True):
    """Sawn oak/walnut grain running `along` x or y, periodic over `period` points: warped growth rings with dark
    latewood lines, fine fibres, scattered pores and the odd knot. Returns (colour, height)."""
    if along == "y":
        # paint it lying down and turn it: grain along y with the period along y
        X, Y = cv.X, cv.Y
        cv.X, cv.Y = Y, X
        col, h = wood(cv, (period[1], period[0]), (origin[1], origin[0]), "x", light, dark, ring, seed, knots)
        cv.X, cv.Y = X, Y
        return col, h
    pu = period[0]
    warp = cv.pnoise((pu, pu), (2, 3), 3, origin, seed) * 2.6 + cv.pnoise((pu, pu), (1, 2), 2, origin, seed + 1) * 3.5
    if knots:  # a knot or two: the rings swirl round a point
        rnd = np.random.default_rng(seed + 7)
        for _ in range(2):
            kx, ky = origin[0] + rnd.uniform(0.1, 0.9) * pu, origin[1] + rnd.uniform(0.2, 0.8) * period[1]
            for sx in (-pu, 0, pu):  # periodic copies
                dx, dy = (cv.X - kx - sx) / 3.2, (cv.Y - ky) / 1.6
                warp += np.exp(-(dx * dx + dy * dy)) * 2.2
    v = cv.Y / ring + warp * 0.55
    ph = v - np.floor(v)
    # the dark latewood line of each ring: thin, its strength drifting along the board
    late = smoothstep(0.7, 0.86, ph) * (1 - smoothstep(0.88, 0.97, ph))
    fade = 0.55 + 0.45 * cv.pnoise((pu, pu), (3, 2), 2, origin, seed + 5)
    late = late * np.clip(fade, 0.15, 1)
    early = smoothstep(0.0, 0.5, ph) * 0.18                               # earlywood darkens towards the line
    fib = cv.pnoise((pu, pu), (int(pu / 10), int(pu / 0.55)), 3, origin, seed + 2)   # long thin fibres
    pores = (cv.pnoise((pu, pu), (int(pu / 2.2), int(pu / 0.4)), 1, origin, seed + 3) > 0.7).astype(np.float32)
    blot = cv.pnoise((pu, pu), (3, 2), 3, origin, seed + 4)
    t = np.clip(0.22 + early + late * 0.6 + fib * 0.16 + pores * 0.1 + blot * 0.14, 0, 1)
    col = lerp(rgb(light), rgb(dark), t[..., None])
    height = -late * 0.25 - pores * 0.18 + fib * 0.1
    return col, height


def parchment(cv, period, origin, base="#ECD6B0", seed=11, stains=True):
    """Aged parchment, periodic over `period`: cloudy mottling, laid fibres, flecks and faint tide-mark stains.
    Returns (colour, height)."""
    p = period
    cloud = cv.pnoise(p, (3, 3), 4, origin, seed) * 0.6 + cv.pnoise(p, (1, 1), 2, origin, seed + 1) * 0.4
    fib = cv.pnoise(p, (int(p[0] / 9), int(p[1] / 1.2)), 2, origin, seed + 2) * 0.5 + \
        cv.pnoise(p, (int(p[0] / 1.2), int(p[1] / 7)), 2, origin, seed + 3) * 0.5
    grain = cv.pnoise(p, (int(p[0] / 0.7), int(p[1] / 0.7)), 1, origin, seed + 4)
    fleck = (cv.pnoise(p, (int(p[0] / 1.1), int(p[1] / 1.1)), 1, origin, seed + 5) > 0.8).astype(np.float32)
    col = rgb(base) * (1 + cloud[..., None] * 0.045 + fib[..., None] * 0.04 + grain[..., None] * 0.025)
    col = lerp(col, rgb("#B58552"), (np.clip(-cloud - 0.35, 0, 1) * 0.3)[..., None])   # darker age patches
    col = lerp(col, rgb("#8A5A30"), (fleck * 0.2)[..., None])
    if stains:  # faint tide marks: thin darker rims of dried spills
        s = cv.pnoise(p, (2, 2), 3, origin, seed + 6)
        rim = np.exp(-((s - 0.45) / 0.02) ** 2) * 0.06
        col = lerp(col, rgb("#A87A48"), rim[..., None])
    height = fib * 0.05 + grain * 0.04
    return np.clip(col, 0, 1), height


def iron(cv, seed=21):
    """Hammered wrought iron: dark blue-grey with pitting and a few lighter worn spots. Returns (colour, height)."""
    pit = cv.noise(1.2, 2, seed=seed)
    wear = cv.noise(4.0, 2, seed=seed + 1)
    col = rgb("#4C5058") * (1 + pit[..., None] * 0.12)
    col = lerp(col, rgb("#7E848C"), (np.clip(wear - 0.3, 0, 1) * 0.6)[..., None])
    return col, pit * 0.12


def metal_piece(cv, d, seed=21, outline=0.9, bevel=1.4):
    """Draws a wrought-iron piece whose outline is the signed distance `d` (pt): dark edge, bevelled, lit, a hard
    contact shadow under it."""
    shadow = np.roll(cv.cover(d), int(0.8 * PPT), axis=0)
    cv.over(np.zeros(3, np.float32), shadow * 0.35)
    cv.over(rgb("#15171C"), cv.cover(d))
    di = d + outline
    col, h = iron(cv, seed)
    gy, gx = np.gradient(di, 1.0 / PPT)
    n = np.hypot(gx, gy) + 1e-6
    band = smoothstep(-bevel, 0.0, di)
    shade = (-(gx / n) * LIGHT[0] - (gy / n) * LIGHT[1]) * band * 0.55
    col = np.clip(col * (light_relief(cv, h, 0.6) + shade)[..., None], 0, 1)
    cv.over(col, cv.cover(di))


def rivet(cv, x, y, r=1.35, metal="#7A808A"):
    """A domed rivet head: a small sphere lit from the top left with a sharp highlight and a dark rim."""
    dx, dy = (cv.X - x) / r, (cv.Y - y) / r
    rr = dx * dx + dy * dy
    inside = np.clip((1 - rr) * r * PPT * 0.5, 0, 1)
    shadow = np.clip((1 - ((cv.X - x - 0.35) ** 2 + (cv.Y - y - 0.55) ** 2) / (r * 1.25) ** 2) * 3, 0, 1)
    cv.over(np.zeros(3, np.float32), shadow * 0.45)
    nz = np.sqrt(np.clip(1 - rr, 0, 1))
    lam = np.clip(-dx * LIGHT[0] * 0.8 - dy * LIGHT[1] * 0.8 + nz * 0.55, 0, 1.4)
    spec = np.clip(1 - ((dx + 0.38) ** 2 + (dy + 0.45) ** 2) / 0.09, 0, 1) ** 2
    col = rgb(metal) * (0.35 + lam[..., None] * 0.8) + spec[..., None] * 0.85
    rim = smoothstep(0.62, 1.0, rr)
    col = lerp(col, rgb("#1A1C22"), (rim * 0.75)[..., None])
    cv.over(np.clip(col, 0, 1), inside)


def corner_brackets(cv, x0, y0, x1, y1, leg=15.0, width=5.0, inset=1.0, r=1.4, seed=40):
    """Wrought-iron L brackets on the four corners of a frame (each inside its corner slice), three rivets each."""
    for sx, sy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        cx = x0 + inset if sx == 0 else x1 - inset
        cy = y0 + inset if sy == 0 else y1 - inset
        ex = 1 if sx == 0 else -1
        ey = 1 if sy == 0 else -1
        a = cv.rrect(min(cx, cx + ex * leg), min(cy, cy + ey * width), max(cx, cx + ex * leg), max(cy, cy + ey * width), r)
        b = cv.rrect(min(cx, cx + ex * width), min(cy, cy + ey * leg), max(cx, cx + ex * width), max(cy, cy + ey * leg), r)
        d = np.minimum(a, b)
        # cut the inner corner round, like a forged bracket
        metal_piece(cv, d, seed + sx + 2 * sy)
        h = width / 2
        for px, py in ((h, h), (leg - 2.6, h), (h, leg - 2.6)):
            rivet(cv, cx + ex * px, cy + ey * py, 1.15)


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
    wc, wh = wood(cv, (48, 48), (0, 0), "x", "#C88A58", "#7A4626", 4.0, seed=6, knots=False)
    g = ((wc @ np.array([0.3, 0.59, 0.11], np.float32)) / 0.5 - 1 + wh) * 0.4
    chunky(cv, (0, 0, 48, 32), 7, "#BD7F4E", "#A0643A", "#7A4522", hi=0.22, lip_h=2.5, drop=1.5, grain=g,
           notches=[notch(39.5, 2.2, "top", 1.2)])
    return cv


PANEL_TILE = 160    # the repeating middle of the tiled panel (points); its slices are 16/16/16/19
PANEL_W, PANEL_BODY = 16 + PANEL_TILE + 16, 16 + PANEL_TILE + 16


def frame_profile(cv, outer, inner, bevel=1.8):
    """Height (0..1) of a moulded frame between two signed distances: rounded up from the outer edge and
    down again into the inner opening."""
    a = np.clip(-outer / bevel, 0, 1)
    b = np.clip(inner / bevel, 0, 1)
    return np.sin(np.minimum(a, b) * np.pi / 2)


def panel():
    """The popup: parchment in a mitred walnut frame with wrought-iron corner brackets. Tiled: the middle
    (parchment) and the edges (frame) repeat every PANEL_TILE points without seams."""
    W, B = PANEL_W, PANEL_BODY
    cv = Canvas(W, B + 3, seed=8)
    o = (16.0, 16.0)
    per = (PANEL_TILE, PANEL_TILE)
    d = cv.rrect(0, 0, W, B, 11)
    cv.over(np.zeros(3, np.float32), cv.cover(cv.rrect(0, 2.5, W, B + 2.5, 11)) * 0.38)   # drop shadow
    cv.over(rgb(INK), cv.cover(d))
    # the frame: horizontal grain on the top and bottom rails, vertical on the stiles, mitred at the corners
    fi = 9.0                                    # frame inner edge (from the outside)
    inner = cv.rrect(fi, fi, W - fi, B - fi, 4.5)
    wh, hh = wood(cv, per, o, "x", "#B97A48", "#5A3218", 2.6, seed=3)
    wv, hv = wood(cv, per, o, "y", "#B97A48", "#5A3218", 2.6, seed=4)
    dist_h = np.minimum(cv.Y, B - cv.Y)
    dist_v = np.minimum(cv.X, W - cv.X)
    horiz = dist_h <= dist_v
    wcol = np.where(horiz[..., None], wh, wv)
    wht = np.where(horiz, hh, hv)
    prof = frame_profile(cv, d + 1.8, inner, 2.2)
    wcol = wcol * light_relief(cv, wht * 0.6 + prof * 1.6, 0.18)[..., None]
    mitre = np.exp(-((dist_h - dist_v) / 0.25) ** 2) * (dist_h < fi + 0.5)
    wcol = lerp(wcol, rgb("#24130A"), (mitre * 0.7)[..., None])
    cv.over(np.clip(wcol, 0, 1), cv.cover(d + 1.8))
    # a dark groove where the parchment meets the frame, then the parchment itself
    cv.over(rgb("#2A170B"), cv.cover(inner - 0.9))
    pcol, ph = parchment(cv, per, o, "#EDD8B3", seed=11)
    pcol = pcol * light_relief(cv, ph, 0.12)[..., None]
    # the frame's shadow on the parchment (top and left) and a little age burn along every edge; both fade out
    # inside the slices so the tiled middle stays clean
    din = np.clip(-inner, 0, 9)
    burn = (1 - smoothstep(0.0, 6.5, din)) * 0.22
    shadow_in = np.clip(-cv.rrect(fi + 0.6, fi + 1.6, W - fi + 0.6, B - fi + 1.6, 4.5), 0, 9)
    sh = (1 - smoothstep(0.0, 2.6, shadow_in)) * 0.35 * cv.cover(inner)
    pcol = lerp(pcol, rgb("#8E5E32"), burn[..., None])
    pcol = pcol * (1 - sh[..., None])
    cv.over(np.clip(pcol, 0, 1), cv.cover(inner))
    corner_brackets(cv, 0, 0, W, B, leg=14.5, width=5.4, inset=0.6)
    return cv


PLANK_TILE = 160


def plank():
    """The title board: a thick oak plank with an engraved border, iron straps and rivets at both ends. Tiled
    along its length (the grain repeats every PLANK_TILE points); exactly Theme.PlankH (38) tall."""
    W, H, body = 14 + PLANK_TILE + 14, 38, 35.5
    cv = Canvas(W, H, seed=9)
    d = cv.rrect(0, 0, W, body, 6)
    cv.over(np.zeros(3, np.float32), cv.cover(cv.rrect(0, 2.5, W, body + 2.5, 6)) * 0.4)
    cv.over(rgb(INK), cv.cover(d))
    di = d + 2.0
    col, h = wood(cv, (PLANK_TILE, 64), (14.0, 0.0), "x", "#DE9A60", "#7E4220", 3.2, seed=9, knots=False)
    # the board is rounded over at its edges, and its lower third falls into shade
    prof = np.clip(-di / 2.4, 0, 1)
    t = np.clip(cv.Y / body, 0, 1)
    col = col * (1.06 - 0.16 * smoothstep(0.45, 1.0, t))[..., None]
    col = col * light_relief(cv, h * 0.7 + np.sin(prof * np.pi / 2) * 1.4, 0.2)[..., None]
    # engraved border: a dark cut with a light lower lip (a recessed line in the wood)
    eng = cv.rrect(11.5, 4.2, W - 11.5, body - 5.2, 3)
    cut = np.exp(-(eng / 0.45) ** 2)
    lip = np.exp(-((eng + 0.75) / 0.4) ** 2) * (cv.Y > body / 2) + np.exp(-((eng - 0.75) / 0.4) ** 2) * (cv.Y < body / 2)
    col = lerp(col, rgb("#4A2610"), (cut * 0.75)[..., None])
    col = lerp(col, rgb("#F4C38A"), (lip * 0.35)[..., None])
    # a soft sheen along the top edge
    hi = np.clip(cv.cover(di) - cv.cover(cv.rrect(0, 1.3, W, body + 1.3, 6) + 2.0), 0, 1)
    col = lerp(col, rgb("#FFE2B8"), (hi * 0.55)[..., None])
    cv.over(np.clip(col, 0, 1), cv.cover(di))
    # iron straps over both ends
    for x0 in (2.6, W - 9.6):
        strap = cv.rrect(x0, -0.6, x0 + 7.0, body + 0.6, 1.2)
        metal_piece(cv, np.maximum(strap, d + 0.8), seed=50 + int(x0))
        for y in (7.0, body - 8.0):
            rivet(cv, x0 + 3.5, y, 1.3)
    return cv


SECTION_TILE = 96


def section():
    """An inset in the parchment (tiled; slices 12): a slightly darker, recessed sheet with a shadow under its
    top edge and a light lip along the bottom."""
    W = 12 + SECTION_TILE + 12
    cv = Canvas(W, W, seed=10)
    d = cv.rrect(0.3, 0.3, W - 0.3, W - 0.3, 8)
    col, h = parchment(cv, (SECTION_TILE, SECTION_TILE), (12.0, 12.0), "#E2C397", seed=31, stains=False)
    col = col * light_relief(cv, h, 0.1)[..., None]
    lowered = cv.rrect(0.3, 2.4, W - 0.3, W + 1.6, 8)
    sh = np.clip(cv.cover(d) - cv.cover(lowered), 0, 1)
    col = lerp(col, rgb("#A97C4E"), sh[..., None] * 0.7)
    inside = np.clip(-d, 0, 8)
    soft = (1 - smoothstep(0, 5.5, inside)) * 0.12
    col = col * (1 - soft[..., None])
    edge = np.clip(cv.cover(d) - cv.cover(d + 1.1), 0, 1)
    col = lerp(col, rgb("#B88C5C"), edge[..., None] * 0.85)
    raised = cv.rrect(0.3, -1.4, W - 0.3, W - 1.7, 8)
    lip = np.clip(cv.cover(d) - cv.cover(raised), 0, 1)
    col = lerp(col, rgb("#F6E6CC"), lip[..., None] * 0.9)
    cv.over(np.clip(col, 0, 1), cv.cover(d))
    return cv


def slot():
    cv = Canvas(48, 50.5, seed=12)
    pc, ph = parchment(cv, (48, 48), (0, 0), "#F3E2C4", seed=41, stains=False)
    det = (pc @ np.array([0.3, 0.59, 0.11], np.float32)) / 0.85 - 1 + ph * 0.5
    chunky(cv, (0, 0, 48, 48), 8, "#F6E7CD", "#ECD3AC", "#CFA978", hi=0.6, lip_h=3, bevel_k=0.16,
           detail=det, detail_k=0.6, notches=[notch(0.8, 9, "left", 1.3)])
    return cv


def tile():
    cv = Canvas(48, 50.5, seed=13)
    wc, wh = wood(cv, (48, 48), (0, 0), "x", "#F2BE84", "#B87440", 4.5, seed=13, knots=False)
    g = (wc @ np.array([0.3, 0.59, 0.11], np.float32)) / 0.62 - 1 + wh
    chunky(cv, (0, 0, 48, 48), 9, "#F0B97C", "#D0905A", "#995A2E", hi=0.45, lip_h=4, bevel_k=0.22, grain=g * 0.5,
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


# ---------------------------------------------------------------- blue casual kit (HUD and buttons)
NAVY = "#1B1F4A"


def kbtn(light, base, lip, stripes=0.0):
    """Blue-kit button: thick navy outline, bright glossy top half, strong darker bottom lip."""
    def draw():
        cv = Canvas(48, 42.5, seed=31)
        # enamel over a pressed tin body: a faint orange-peel texture, a bevelled rim and a hard specular streak
        peel = cv.noise(0.9, 2, seed=5) * 0.6 + cv.noise(3.0, 2, seed=6) * 0.4
        chunky(cv, (0, 0, 48, 40), 6.5, light, base, lip, hi=0.7, outline=2.6, lip_h=6.0, drop=2.5,
               drop_alpha=0.4, bevel=2.0, bevel_k=0.24, wobble=0.0, gloss=1.0, gloss_edge=(0.44, 0.5),
               gloss_add=0.09, stripes=stripes, ink=NAVY, glint=(7.0, 6.5, 3.2, 1.3), detail=peel, detail_k=0.035)
        streak = cv.cover(cv.rrect(10.5, 3.6, 37.5, 5.0, 0.7)) * 0.32
        cv.over(np.ones(3, np.float32), streak)
        return cv
    return draw


def hexagon(cx, cy, w, h, pointy_top, inset=0.0):
    """Hexagon points (pointy top, or pointy left/right), shrunk by inset points for rounding."""
    hw, hh = w / 2 - inset, h / 2 - inset
    if pointy_top:
        return [(cx, cy - hh), (cx + hw, cy - hh / 2), (cx + hw, cy + hh / 2), (cx, cy + hh), (cx - hw, cy + hh / 2),
                (cx - hw, cy - hh / 2)]
    return [(cx - hw, cy), (cx - hw / 2, cy - hh), (cx + hw / 2, cy - hh), (cx + hw, cy), (cx + hw / 2, cy + hh),
            (cx - hw / 2, cy + hh)]


def hud_capsule():
    cv = Canvas(60, 26, seed=32)
    d = cv.rrect(0, 0, 60, 26, 12)
    cv.over(rgb("#11153A"), cv.cover(d))
    di = d + 2.0
    t = np.clip(cv.Y / 26, 0, 1)
    col = lerp(rgb("#1C2254"), rgb("#2A3270"), t[..., None])
    sh = np.clip(cv.cover(di) - cv.cover(cv.rrect(2, 4.6, 58, 26.6, 10)), 0, 1)   # inner shade along the top
    col = lerp(col, rgb("#0E1236"), sh[..., None] * 0.8)
    lo = np.clip(cv.cover(di) - cv.cover(cv.rrect(2, 0.8, 58, 22.8, 10)), 0, 1)   # light bottom edge
    col = lerp(col, rgb("#4A56A8"), lo[..., None] * 0.7)
    cv.over(col, cv.cover(di))
    # a thin bevelled steel bezel just inside the outline (lit on top, dark below) and a brushed sheen
    ring = np.clip(cv.cover(d + 1.2) - cv.cover(d + 2.4), 0, 1)
    t2 = np.clip(cv.Y / 26, 0, 1)
    steel = lerp(rgb("#B9C6E8"), rgb("#3A4478"), smoothstep(0.15, 0.85, t2)[..., None])
    cv.over(steel, ring * 0.85)
    brush = cv.noise(0.6, 2, aspect=(14.0, 0.4), seed=36) * 0.5 + 0.5
    cv.over(rgb("#5A68B8"), cv.cover(di) * brush * 0.06)
    return cv


def hud_plus():
    cv = Canvas(22, 23.5, seed=33)
    chunky(cv, (0, 0, 22, 22), 5.0, "#7BE83A", "#53CE17", "#2E8A0A", hi=0.5, outline=2.0, lip_h=3.0, drop=1.5,
           bevel=1.2, bevel_k=0.12, wobble=0.0, gloss=0.8, gloss_edge=(0.45, 0.52), ink=NAVY)
    for (x0, y0, x1, y1) in ((9.2, 4.6, 12.8, 15.4), (5.6, 8.2, 16.4, 11.8)):
        cv.over(rgb("#1E6A08"), cv.cover(cv.rrect(x0, y0 + 0.8, x1, y1 + 0.8, 1.4)) * 0.6)
    for (x0, y0, x1, y1) in ((9.2, 4.6, 12.8, 15.4), (5.6, 8.2, 16.4, 11.8)):
        cv.over(np.ones(3, np.float32), cv.cover(cv.rrect(x0, y0, x1, y1, 1.4)))
    return cv


def hud_hex():
    cv = Canvas(48, 48.5, seed=34)
    chunky(cv, (0, 0, 48, 46), 2.5, "#7CC8FF", "#2E8EEA", "#1550A8", hi=0.6, outline=2.6, lip_h=4.0, drop=2.5,
           bevel=2.4, bevel_k=0.22, wobble=0.0, gloss=0.9, gloss_edge=(0.42, 0.5), ink=NAVY,
           poly=hexagon(24, 23, 48, 46, False, 2.5))
    return cv


def hud_hex_frame():
    cv = Canvas(64, 72, seed=35)
    chunky(cv, (0, 0, 64, 70), 3.0, "#8FD0FF", "#2F86E8", "#1648A0", hi=0.6, outline=2.8, lip_h=4.5, drop=2.0,
           bevel=2.6, bevel_k=0.25, wobble=0.0, gloss=0.6, ink=NAVY, poly=hexagon(32, 35, 64, 70, True, 3.0))
    # the avatar well: a deep navy hexagon with its own dark rim and an inner shadow at the top
    well = cv.polygon(hexagon(32, 34.5, 46, 50, True, 2.5)) - 2.5
    cv.over(rgb(NAVY), cv.cover(well))
    wi = well + 1.6
    t = np.clip((cv.Y - 10) / 50, 0, 1)
    col = lerp(rgb("#0F1A5C"), rgb("#2350B8"), t[..., None])
    inner = cv.polygon([(x, y + 3.0) for x, y in hexagon(32, 34.5, 46, 50, True, 2.5)]) - 2.5 + 1.6
    sh = np.clip(cv.cover(wi) - cv.cover(inner), 0, 1)
    col = lerp(col, rgb("#070C30"), sh[..., None] * 0.8)
    cv.over(col, cv.cover(wi))
    return cv


def hud_round(light, base, lip):
    def draw():
        cv = Canvas(24, 25.5, seed=36)
        chunky(cv, (0, 0, 24, 24), 12, light, base, lip, hi=0.55, outline=2.2, lip_h=3.2, drop=1.5, bevel=1.4,
               bevel_k=0.15, wobble=0.0, gloss=0.85, gloss_edge=(0.45, 0.52), ink=NAVY, glint=(7.0, 6.0, 2.6, 1.1))
        return cv
    return draw


def hud_square_blue():
    cv = Canvas(40, 42.5, seed=37)
    chunky(cv, (0, 0, 40, 40), 7, "#4DA2F7", "#1474E0", "#0A4AA6", hi=0.55, outline=2.6, lip_h=5.0, drop=2.5,
           drop_alpha=0.4, bevel=1.6, bevel_k=0.14, wobble=0.0, gloss=1.0, gloss_edge=(0.44, 0.5), ink=NAVY,
           glint=(7.0, 6.5, 3.0, 1.2))
    return cv


def hud_bar_track():
    cv = Canvas(90, 24, seed=38)
    d = cv.rrect(12, 4.5, 90, 19.5, 7.5)
    cv.over(rgb("#11153A"), cv.cover(d))
    di = d + 1.8
    col = np.broadcast_to(rgb("#232A63"), cv.c.shape).copy()
    sh = np.clip(cv.cover(di) - cv.cover(cv.rrect(13.8, 9.0, 88.2, 21.0, 5.7)), 0, 1)
    col = lerp(col, rgb("#0E1236"), sh[..., None] * 0.85)
    cv.over(col, cv.cover(di))
    # the hexagon cap at the left end (an icon such as the heart sits on it)
    chunky(cv, (0, 0, 22, 24), 2.0, "#5AB0FF", "#2A6FD6", "#173E96", hi=0.5, outline=2.2, lip_h=3.0, drop=0.0,
           bevel=1.6, bevel_k=0.2, wobble=0.0, gloss=0.7, ink=NAVY, poly=hexagon(11, 12, 22, 24, True, 2.0))
    return cv


def hud_bar_fill_blue():
    cv = Canvas(30, 14, seed=39)
    d = cv.rrect(0, 0, 30, 14, 7)
    cv.over(rgb("#0E58B8"), cv.cover(d))
    di = d + 1.2
    t = np.clip(cv.Y / 14, 0, 1)
    col = lerp(rgb("#7FEBFF"), rgb("#2AA8F5"), t[..., None])
    col = lerp(col, rgb("#1C7FE0"), smoothstep(0.55, 1.0, t)[..., None])
    band = cv.cover(cv.rrect(3.5, 2.4, 26.5, 5.2, 1.4))
    col = lerp(col, np.ones(3, np.float32), band[..., None] * 0.55)
    cv.over(col, cv.cover(di))
    return cv


def hud_rank():
    cv = Canvas(40, 16, seed=40)
    d = cv.rrect(0, 0, 40, 16, 8)
    cv.over(rgb(NAVY), cv.cover(d))
    ring = d + 1.0
    cv.over(rgb("#FAC602"), cv.cover(ring))
    di = ring + 1.7
    t = np.clip(cv.Y / 16, 0, 1)
    col = lerp(rgb("#1C2254"), rgb("#2E3778"), t[..., None])
    sh = np.clip(cv.cover(di) - cv.cover(cv.rrect(2.7, 5.0, 37.3, 16.3, 5.3)), 0, 1)
    col = lerp(col, rgb("#0E1236"), sh[..., None] * 0.7)
    cv.over(col, cv.cover(di))
    return cv


SKINS = {
    "panel": panel,
    "plank": plank,
    "section": section,
    # buttons in the blue casual kit's style (the warm-kit versions are btn(...) above, kept for reference)
    "btn_green": kbtn("#7FE23C", "#4FC216", "#2A8A0A"),
    "btn_blue": kbtn("#4DA2F7", "#1474E0", "#0A4AA6"),
    "btn_orange": kbtn("#FFDB3A", "#FAC602", "#E8860A", stripes=0.55),   # the kit's yellow "level up"
    "btn_grey": kbtn("#CDD2DC", "#A9B0BE", "#737B90"),
    "btn_red": kbtn("#FF7468", "#EC3B3B", "#A21E2A"),
    "btn_brown": kbtn("#5A5E8E", "#40436C", "#26284A"),                  # the kit's dark navy secondary
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
    # HUD pieces in the blue casual kit's style
    "hud_capsule": hud_capsule,
    "hud_plus": hud_plus,
    "hud_hex": hud_hex,
    "hud_hex_frame": hud_hex_frame,
    "hud_round_blue": hud_round("#5CB0FF", "#1C7CE6", "#0C4AA8"),
    "hud_round_yellow": hud_round("#FFDF4A", "#FAC602", "#E08000"),
    "hud_square_blue": hud_square_blue,
    "hud_bar_track": hud_bar_track,
    "hud_bar_fill_blue": hud_bar_fill_blue,
    "hud_rank": hud_rank,
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
