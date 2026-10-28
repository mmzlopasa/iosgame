"""Building parts made of kit pieces: stone plinths, timber-framed walls, windows, doors, roofs, chimneys, props.
Every function takes the Kit first; coordinates as in kit.py (front = -Z)."""
import math

import numpy as np

from kit import rot_y, rot_x, rot_z, v3, frame, norm

DARK = (0.16, 0.12, 0.1)
TIMBER = (0.62, 0.42, 0.3)
TIMBER_DARK = (0.46, 0.3, 0.22)
WARM_WINDOW = (1.25, 0.86, 0.45)


def plinth(k, w, d, h=0.28, stone=0.3, inset=0.0, color=(1, 1, 1)):
    """A ring of rounded stones under the walls (w x d footprint, centred)."""
    hw, hd = w / 2 - inset, d / 2 - inset
    for (ax, az, bx, bz) in ((-hw, -hd, hw, -hd), (hw, -hd, hw, hd), (hw, hd, -hw, hd), (-hw, hd, -hw, -hd)):
        L = math.hypot(bx - ax, bz - az)
        n = max(2, int(round(L / stone)))
        for i in range(n):
            t = (i + 0.5) / n
            x, z = ax + (bx - ax) * t, az + (bz - az) * t
            s = stone * k.rng.uniform(0.9, 1.15)
            yaw = math.degrees(math.atan2(bx - ax, bz - az)) + k.rng.uniform(-8, 8)
            g = k.rng.uniform(0.85, 1.08)
            k.box((x, h * 0.45, z), (s * 0.62, h * k.rng.uniform(0.95, 1.15), s * 1.08), R=rot_y(yaw), tile="stone",
                  color=(color[0] * g, color[1] * g, color[2] * g), round_=0.75, seg=2, jitter=0.06)
    # a flat core so no gap shows between stones
    k.box((0, h * 0.4, 0), (w - 2 * inset - stone * 0.5, h * 0.8, d - 2 * inset - stone * 0.5), tile="stone",
          color=(0.75, 0.75, 0.75), vary=0)


def wall_plaster(k, a, b, y0, y1, thick=0.14, tile="plaster", color=(1, 0.97, 0.9), posts=True, post_w=0.14,
                 braces=True, rail=True, timber=TIMBER):
    """A wall from a to b (x, z on the ground) between heights y0..y1, its face outward to the right of a→b
    (seen from above, walking from a to b, the outside is on the left for a clockwise footprint)."""
    a, b = np.array(a, float), np.array(b, float)
    L = float(np.linalg.norm(b - a))
    d = (b - a) / L
    out = np.array([d[1], -d[0]])  # left normal for a→b in x,z: (dz, -dx)
    mid = (a + b) / 2
    yaw = math.degrees(math.atan2(d[0], d[1]))
    R = rot_y(yaw)  # local Z along the wall, local X across it
    k.box((mid[0], (y0 + y1) / 2, mid[1]), (thick, y1 - y0, L), R=R, tile=tile, color=color, vary=0.03, grain=1)
    if not posts:
        return
    f = thick / 2 + 0.02

    def pt(t, y, off=f):
        p = a + d * t
        return (p[0] + out[0] * off, y, p[1] + out[1] * off)

    pw = post_w
    for t in (pw / 2, L - pw / 2):
        k.beam(pt(t, y0 - 0.02), pt(t, y1 + 0.04), w=pw, h=pw, color=timber, up=(out[0], 0, out[1]))
    k.beam(pt(0, y1 - pw * 0.4), pt(L, y1 - pw * 0.4), w=pw * 0.9, h=pw * 0.85, color=timber, up=(0, 1, 0))
    k.beam(pt(0, y0 + pw * 0.4), pt(L, y0 + pw * 0.4), w=pw * 0.9, h=pw * 0.85, color=timber, up=(0, 1, 0))
    if rail and L > 1.2:
        k.beam(pt(pw, (y0 + y1) * 0.5 + 0.05), pt(L - pw, (y0 + y1) * 0.5 + 0.05), w=pw * 0.7, h=pw * 0.7,
               color=timber, up=(0, 1, 0))
    if braces and L > 1.0:
        k.beam(pt(pw, y0 + pw), pt(min(L * 0.38, 0.9), y1 - pw), w=pw * 0.65, h=pw * 0.7, color=timber,
               up=(out[0], 0, out[1]))
        k.beam(pt(L - pw, y0 + pw), pt(L - min(L * 0.38, 0.9), y1 - pw), w=pw * 0.65, h=pw * 0.7, color=timber,
               up=(out[0], 0, out[1]))


def wall_planks(k, a, b, y0, y1, thick=0.1, tile="planks", color=(1, 1, 1), board=0.22, vertical=True):
    """A wall of separate boards (vertical) with a top and bottom rail."""
    a, b = np.array(a, float), np.array(b, float)
    L = float(np.linalg.norm(b - a))
    d = (b - a) / L
    out = np.array([d[1], -d[0]])
    yaw = math.degrees(math.atan2(d[0], d[1]))
    R = rot_y(yaw)
    mid = (a + b) / 2
    k.box((mid[0] - out[0] * 0.03, (y0 + y1) / 2, mid[1] - out[1] * 0.03), (thick * 0.6, y1 - y0, L), R=R,
          tile=tile, color=tuple(c * 0.55 for c in color), vary=0)
    n = max(2, int(round(L / board)))
    for i in range(n):
        t = (i + 0.5) / n
        p = a + d * L * t
        top = y1 + k.rng.uniform(-0.03, 0.02)
        g = k.rng.uniform(0.86, 1.1)
        k.box((p[0] + out[0] * 0.01, (y0 + top) / 2, p[1] + out[1] * 0.01), (thick, top - y0, L / n * 0.96),
              R=R @ rot_x(k.rng.uniform(-0.6, 0.6)), tile=tile, color=tuple(c * g for c in color), vary=0.02, grain=1)


def wall_logs(k, a, b, y0, y1, r=0.11, color=(1, 1, 1), over=0.14):
    """A wall of stacked round logs that stick out past the corners."""
    a, b = np.array(a, float), np.array(b, float)
    L = float(np.linalg.norm(b - a))
    d = (b - a) / L
    n = max(2, int((y1 - y0) / (r * 1.8)))
    for i in range(n):
        y = y0 + r + i * (y1 - y0 - r) / max(1, n - 1) * 0.98
        pa = a - d * over
        pb = b + d * over
        g = k.rng.uniform(0.88, 1.08)
        k.cyl((pa[0], y, pa[1]), (pb[0], y, pb[1]), r * k.rng.uniform(0.92, 1.05), sides=9,
              color=(color[0] * g, color[1] * g, color[2] * g), wobble=0.04)


def opening_frame(k, center, out, w, h, y0, timber=TIMBER, depth=0.1):
    """Posts and lintel around a door or window on a wall whose outward normal is `out` (x, z)."""
    cx, cz = center
    side = np.array([-out[1], out[0]])
    f = 0.1
    p = lambda s, y, o=f: (cx + side[0] * s + out[0] * o, y, cz + side[1] * s + out[1] * o)
    k.beam(p(-w / 2 - 0.05, y0 - 0.02), p(-w / 2 - 0.05, y0 + h + 0.06), w=0.1, h=0.1, color=timber, up=(out[0], 0, out[1]))
    k.beam(p(w / 2 + 0.05, y0 - 0.02), p(w / 2 + 0.05, y0 + h + 0.06), w=0.1, h=0.1, color=timber, up=(out[0], 0, out[1]))
    k.beam(p(-w / 2 - 0.16, y0 + h + 0.06), p(w / 2 + 0.16, y0 + h + 0.06), w=0.12, h=0.13, color=timber)


def door(k, center, out, w=0.62, h=1.2, y0=0.0, color=(0.78, 0.55, 0.38), timber=TIMBER, arch=False):
    cx, cz = center
    out = np.array(out, float)
    side = np.array([-out[1], out[0]])
    yaw = math.degrees(math.atan2(side[0], side[1]))
    R = rot_y(yaw)
    # dark doorway
    k.box((cx + out[0] * 0.06, y0 + h / 2, cz + out[1] * 0.06), (0.06, h, w + 0.04), R=R, tile="planks",
          color=DARK, vary=0)
    # door leaf of boards, slightly ajar on its hinge
    n = 4
    for i in range(n):
        s = -w / 2 + (i + 0.5) * w / n
        g = k.rng.uniform(0.9, 1.08)
        k.box((cx + side[0] * s + out[0] * 0.1, y0 + h / 2 - 0.01, cz + side[1] * s + out[1] * 0.1),
              (0.05, h - 0.03 + k.rng.uniform(-0.02, 0.0), w / n * 0.95), R=R, tile="planks",
              color=tuple(c * g for c in color), vary=0.02, grain=1)
    for y in (y0 + 0.22, y0 + h - 0.24):
        k.box((cx + out[0] * 0.135, y, cz + out[1] * 0.135), (0.03, 0.08, w * 0.9), R=R, tile="iron",
              color=(0.6, 0.6, 0.62), vary=0)
    k.box((cx + side[0] * w * 0.3 + out[0] * 0.15, y0 + h * 0.5, cz + side[1] * w * 0.3 + out[1] * 0.15),
          (0.04, 0.05, 0.05), R=R, tile="iron", color=(0.7, 0.66, 0.5), vary=0)
    opening_frame(k, center, out, w, h, y0, timber=timber)
    # a worn stone step
    k.box((cx + out[0] * 0.32, 0.06, cz + out[1] * 0.32), (0.36, 0.14, w + 0.3), R=R, tile="stone",
          color=(0.95, 0.95, 0.95), round_=0.7, seg=2, jitter=0.04)


def window(k, center, out, y0, w=0.46, h=0.42, shutters=(0.42, 0.56, 0.62), lit=True, flowers=None,
           timber=TIMBER):
    cx, cz = center
    out = np.array(out, float)
    side = np.array([-out[1], out[0]])
    yaw = math.degrees(math.atan2(side[0], side[1]))
    R = rot_y(yaw)
    glow = WARM_WINDOW if lit else DARK
    k.box((cx + out[0] * 0.06, y0 + h / 2, cz + out[1] * 0.06), (0.06, h, w), R=R, tile="cloth_ochre",
          color=glow, vary=0)
    # mullion cross
    k.box((cx + out[0] * 0.1, y0 + h / 2, cz + out[1] * 0.1), (0.04, h, 0.045), R=R, tile="planks", color=TIMBER_DARK, vary=0)
    k.box((cx + out[0] * 0.1, y0 + h / 2, cz + out[1] * 0.1), (0.04, 0.045, w), R=R, tile="planks", color=TIMBER_DARK, vary=0)
    opening_frame(k, center, out, w, h, y0, timber=timber)
    # sill
    k.box((cx + out[0] * 0.16, y0 - 0.03, cz + out[1] * 0.16), (0.16, 0.06, w + 0.24), R=R, tile="planks", color=timber)
    if shutters:
        for s in (-1, 1):
            off = s * (w / 2 + 0.05 + w * 0.26)
            k.box((cx + side[0] * off + out[0] * 0.13, y0 + h / 2, cz + side[1] * off + out[1] * 0.13),
                  (0.04, h + 0.04, w * 0.5), R=R @ rot_y(s * 12), tile="planks", color=shutters, vary=0.05, grain=1)
    if flowers:
        k.box((cx + out[0] * 0.24, y0 - 0.1, cz + out[1] * 0.24), (0.14, 0.13, w + 0.12), R=R, tile="planks",
              color=TIMBER_DARK)
        for i in range(5):
            s = -w / 2 + (i + 0.5) * w / 5
            c = flowers[i % len(flowers)]
            k.box((cx + side[0] * s + out[0] * 0.25, y0 - 0.01 + k.rng.uniform(0, 0.04), cz + side[1] * s + out[1] * 0.25),
                  (0.11, 0.1, 0.11), tile="cloth_red", color=c, round_=0.9, seg=2, vary=0.1)


def gable_roof(k, w, d, eave, ridge, over=0.3, over_end=0.25, kind="thatch", thick=None, ridge_axis="x",
               color=(1, 1, 1), sag=0.0, rolls=True, shingle_tile="shingle"):
    """A two-sided roof over a w x d footprint. ridge_axis 'x': the ridge runs along x (slopes face ±z)."""
    thick = thick or (0.24 if kind.startswith("thatch") else 0.08)
    if ridge_axis == "x":
        L, S = w / 2 + over_end, d / 2 + over
        half = d / 2
    else:
        L, S = d / 2 + over_end, w / 2 + over
        half = w / 2
    # slope: from ridge (0, ridge) down to (S, y_low), extended past the wall by `over`
    drop = (ridge - eave) / half
    y_low = ridge - drop * S
    tile = "thatch" if kind == "thatch" else ("thatch_old" if kind == "thatch_old" else shingle_tile)

    def P(along, across, y):
        return (along, y, across) if ridge_axis == "x" else (across, y, along)

    # the slab lies on the rafters: its underside clears the wall plates and their timbers
    lift = thick + 0.1
    thatch = kind.startswith("thatch")
    courses = 3 if thatch and rolls else 1
    for s in (-1, 1):
        for r in range(courses):
            # thatch is laid in courses from the eave up: each course's lip overlaps the one below
            t0 = max(0.0, r / courses - (0.08 if r else 0))
            t1 = (r + 1) / courses
            up = (courses - 1 - r) * thick * 0.32
            g = k.rng.uniform(0.94, 1.04) * (1 - 0.04 * r)
            col = tuple(c * g for c in color)

            def at(a, t):
                return P(a, s * S * t, ridge + lift + up + (y_low - ridge) * t)

            if s < 0:
                k.quad_slab(at(L, t1), at(-L, t1), at(-L, t0), at(L, t0), thick, tile, color=col)
            else:
                k.quad_slab(at(-L, t1), at(L, t1), at(L, t0), at(-L, t0), thick, tile, color=col)
            if thatch and rolls:
                lip = at(0, t1)
                y = lip[1] - thick * 0.55
                k.cyl(P(-L - 0.02, s * S * t1, y), P(L + 0.02, s * S * t1, y), thick * 0.52, sides=10,
                      tile=tile, end_tile=tile, color=tuple(c * 0.9 for c in col), rings=5, wobble=0.07)
        if not thatch:
            shingles(k, P, s, L, S, ridge + lift, y_low + lift, shingle_tile, color)
    if thatch and rolls:
        # a soft rounded cap along the ridge
        top = ridge + lift + (courses - 1) * thick * 0.32
        k.box(P(0, 0, top - 0.02), (2 * L + 0.1, thick * 1.2, thick * 2.1) if ridge_axis == "x" else
              (thick * 2.1, thick * 1.2, 2 * L + 0.1), tile=tile, color=tuple(c * 0.88 for c in color),
              round_=0.85, seg=4, grain=0 if ridge_axis == "x" else 2)
    elif not thatch:
        # ridge board
        k.beam(P(-L - 0.03, 0, ridge + lift + 0.05), P(L + 0.03, 0, ridge + lift + 0.05), w=0.16, h=0.12,
               tile="planks", color=TIMBER_DARK)
    return y_low


def shingles(k, P, s, L, S, y_top, y_low, tile, color, row_h=0.2, w_sh=0.24):
    """Rows of separate shingles over one roof side (s = -1 / +1)."""
    slope_len = math.hypot(S, y_top - y_low)
    rows = max(2, int(slope_len / row_h))
    n = max(2, int(2 * L / w_sh))
    for r in range(rows):
        t0 = r / rows
        t1 = (r + 1.35) / rows
        g_row = k.rng.uniform(0.92, 1.06)
        for i in range(n):
            if k.rng.random() < 0.04:
                continue
            a0 = -L + (i + (0.5 if r % 2 else 0)) * 2 * L / n
            a1 = min(L, a0 + 2 * L / n * 0.94)
            a0 = max(-L, a0)
            if a1 - a0 < 0.05:
                continue
            lift = 0.025 + 0.02 * (1 - t0)
            j = k.rng.uniform(-0.012, 0.012)

            def q(a, t, up):
                return P(a, s * S * t, y_top + (y_low - y_top) * t + lift + up + j)

            g = g_row * k.rng.uniform(0.86, 1.1)
            col = tuple(c * g for c in color)
            if s < 0:
                k.quad_slab(q(a1, min(t1, 1.0) + 0.02, 0), q(a0, min(t1, 1.0) + 0.02, 0), q(a0, t0, 0.0),
                            q(a1, t0, 0.0), 0.035, tile, color=col, vary=0.03)
            else:
                k.quad_slab(q(a0, min(t1, 1.0) + 0.02, 0), q(a1, min(t1, 1.0) + 0.02, 0), q(a1, t0, 0.0),
                            q(a0, t0, 0.0), 0.035, tile, color=col, vary=0.03)


def gable_ends(k, w, d, eave, ridge, ridge_axis="x", tile="planks", color=(1, 1, 1), thick=0.08, beams=True,
               timber=TIMBER):
    """Triangles closing the roof at both ends (walls end at `eave`)."""
    for s in (-1, 1):
        if ridge_axis == "x":
            x = s * (w / 2)
            a, b, c = (x, eave, -d / 2), (x, eave, d / 2), (x, ridge, 0)
            o = (s, 0)
        else:
            z = s * (d / 2)
            a, b, c = (-w / 2, eave, z), (w / 2, eave, z), (0, ridge, z)
            o = (0, s)
        pa, pb, pc = np.array(a), np.array(b), np.array(c)
        n = norm(np.cross(pb - pa, pc - pa))
        if np.dot(n, np.array([o[0], 0, o[1]])) < 0:
            pa, pb = pb, pa
        if tile == "planks" or tile == "planks_old":
            # vertical boards
            nb = 7
            span_a, span_b = pa, pb
            for i in range(nb):
                t = (i + 0.5) / nb
                base = span_a + (span_b - span_a) * t
                top_y = eave + (ridge - eave) * (1 - abs(2 * t - 1))
                if top_y - eave < 0.06:
                    continue
                g = k.rng.uniform(0.85, 1.08)
                wdt = np.linalg.norm(span_b - span_a) / nb * 0.95
                yaw = math.degrees(math.atan2(*(span_b - span_a)[[0, 2]]))
                k.box((base[0], (eave + top_y) / 2 - 0.02, base[2]), (wdt, top_y - eave, thick),
                      R=rot_y(yaw), tile=tile, color=tuple(c * g for c in color), vary=0.02, grain=1)
        else:
            k.tri_panel(pa, pb, pc, thick, tile, color=color)
        if beams:
            off = np.array([o[0], 0, o[1]]) * (thick / 2 + 0.06)
            k.beam(pa + off + [0, 0.02, 0], pc + off, w=0.12, h=0.12, color=timber)
            k.beam(pb + off + [0, 0.02, 0], pc + off, w=0.12, h=0.12, color=timber)
            k.beam(pa + off, pb + off, w=0.13, h=0.13, color=timber)
            k.beam(((pa + pb) / 2) + off, pc + off - [0, 0.06, 0], w=0.1, h=0.1, color=timber)


def crossed_gable_poles(k, w, d, ridge, ridge_axis="x", length=0.45, color=TIMBER):
    """Crossed rafter ends sticking up over each gable (a classic early-village look)."""
    for s in (-1, 1):
        for t in (-1, 1):
            if ridge_axis == "x":
                base = v3(s * (w / 2 + 0.2), ridge - 0.1, 0)
                tip = base + v3(0, length * 0.8, -t * length * 0.7)
            else:
                base = v3(0, ridge - 0.1, s * (d / 2 + 0.2))
                tip = base + v3(-t * length * 0.7, length * 0.8, 0)
            k.cyl(base - (tip - base) * 0.3, tip, 0.045, r2=0.035, sides=7, color=color)


def chimney(k, x, z, y0, y1, w=0.38, color=(1, 1, 1)):
    y = y0
    h = 0.17
    i = 0
    while y < y1:
        g = k.rng.uniform(0.82, 1.06)
        for dx, dz, sx, sz in ((-1, 0, 0.5, 1), (1, 0, 0.5, 1), (0, -1, 1, 0.5), (0, 1, 1, 0.5)):
            off = w * 0.25
            k.box((x + dx * off, y + h / 2, z + dz * off), (w * sx * (1.02 if i % 2 else 0.98), h * 0.98, w * sz),
                  tile="stone", color=(color[0] * g, color[1] * g, color[2] * g), round_=0.55, seg=2, jitter=0.05)
        y += h
        i += 1
    k.box((x, y + 0.03, z), (w + 0.1, 0.07, w + 0.1), tile="stone", color=(0.82, 0.82, 0.82))
    k.box((x, y + 0.05, z), (w * 0.5, 0.06, w * 0.5), tile="stone", color=(0.15, 0.13, 0.12), vary=0)


# MARK: - props

def barrel(k, x, z, r=0.17, h=0.42, yaw=0, y=0.0, lying=False):
    if lying:
        a, b = v3(x - h / 2, y + r, z), v3(x + h / 2, y + r, z)
        R = rot_y(yaw)
        a, b = (a - v3(x, 0, z)) @ R.T + v3(x, 0, z), (b - v3(x, 0, z)) @ R.T + v3(x, 0, z)
    else:
        a, b = v3(x, y, z), v3(x, y + h, z)
    k.cyl(a, b, r * 0.88, r2=r * 0.88, bulge=r * 0.16, rings=4, sides=12, tile="planks", end_tile="logend",
          color=(0.9, 0.7, 0.55))
    for t in (0.18, 0.82):
        p = a + (b - a) * t
        rr = r * 0.88 + r * 0.16 * math.sin(math.pi * t) + 0.012
        d = norm(b - a) * 0.025
        k.cyl(p - d, p + d, rr, sides=12, tile="iron", end_tile="iron", color=(0.55, 0.55, 0.58), caps=False)


def crate(k, x, z, s=0.36, yaw=0, y=0.0):
    k.box((x, y + s / 2, z), (s, s, s), R=rot_y(yaw), tile="planks", color=(0.95, 0.78, 0.6))
    R = rot_y(yaw)
    for dx in (-1, 1):
        for dz in (-1, 1):
            p = v3(dx * s / 2, 0, dz * s / 2) @ R.T
            k.box((x + p[0], y + s / 2, z + p[2]), (0.05, s + 0.01, 0.05), R=R, tile="planks", color=TIMBER_DARK)


def sack(k, x, z, s=0.3, y=0.0, color=(0.95, 0.88, 0.7)):
    k.box((x, y + s * 0.42, z), (s * 0.85, s * 0.85, s * 0.7), tile="straw", color=color, round_=0.9, seg=3,
          jitter=0.05)
    k.box((x, y + s * 0.88, z), (s * 0.3, s * 0.18, s * 0.26), tile="straw", color=color, round_=0.8, seg=2)


def firewood(k, x, z, w=0.7, h=0.45, yaw=0, rows=3):
    R = rot_y(yaw)
    r = 0.06
    per = max(2, int(w / (r * 2.1)))
    for j in range(rows):
        for i in range(per - (j % 2)):
            off = -w / 2 + r + i * (w - 2 * r) / max(1, per - 1) + (r if j % 2 else 0)
            p = v3(off, r + j * r * 1.75, 0) @ R.T
            a = v3(x, 0, z) + p + v3(0, 0, -0.22) @ R.T
            b = v3(x, 0, z) + p + v3(0, 0, 0.22) @ R.T
            g = k.rng.uniform(0.85, 1.1)
            k.cyl(a, b, r * k.rng.uniform(0.85, 1.1), sides=7, color=(g, g * 0.95, g * 0.9))


def bench(k, x, z, w=0.8, yaw=0):
    R = rot_y(yaw)
    k.box((x, 0.3, z), (w, 0.06, 0.24), R=R, tile="planks", color=(0.9, 0.7, 0.5), grain=0)
    for s in (-1, 1):
        p = v3(s * (w / 2 - 0.1), 0, 0) @ R.T
        k.box((x + p[0], 0.14, z + p[2]), (0.06, 0.28, 0.2), R=R, tile="planks", color=TIMBER_DARK)


def pot(k, x, z, r=0.12, h=0.22, color=(0.9, 0.55, 0.38)):
    k.cyl(v3(x, 0, z), v3(x, h, z), r * 0.7, r2=r * 0.6, bulge=r * 0.5, rings=4, sides=10, tile="hide",
          end_tile="hide", color=color)


def post(k, x, z, h, r=0.07, color=(1, 1, 1)):
    k.cyl(v3(x, -0.05, z), v3(x, h, z), r, r2=r * 0.92, sides=8, color=color)


def flower_patch(k, x, z, colors, n=6, spread=0.25):
    for i in range(n):
        c = colors[i % len(colors)]
        a = k.rng.uniform(0, 6.28)
        rr = k.rng.uniform(0, spread)
        k.box((x + math.cos(a) * rr, 0.06, z + math.sin(a) * rr), (0.12, 0.12, 0.12), tile="cloth_red", color=c,
              round_=0.9, seg=2)
        k.box((x + math.cos(a) * rr, 0.02, z + math.sin(a) * rr), (0.16, 0.05, 0.16), tile="straw",
              color=(0.45, 0.7, 0.3), round_=0.9, seg=2)
