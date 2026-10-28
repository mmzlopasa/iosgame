"""The village's buildings, one function per building sprite. Run build.py to write the .glb files."""
import math

from kit import Kit, rot_y, v3
import parts as pt

THATCH = (0.78, 0.63, 0.48)
FLOWERS = [(1.0, 0.35, 0.3), (1.0, 0.85, 0.3), (0.95, 0.55, 0.85), (1.0, 1.0, 0.95), (0.5, 0.6, 1.0)]


def walls_box(k, w, d, y0, y1, front=None, **kw):
    hw, hd = w / 2, d / 2
    sides = [((-hw, -hd), (hw, -hd)), ((hw, -hd), (hw, hd)), ((hw, hd), (-hw, hd)), ((-hw, hd), (-hw, -hd))]
    for a, b in sides:
        (front or pt.wall_plaster)(k, a, b, y0, y1, **kw)


def daub_house(seed=3):
    """Wattle-and-daub cottage: stone plinth, white walls in dark timber, a fat thatch roof, chimney."""
    k = Kit(seed)
    w, d = 2.1, 1.7
    base, eave, ridge = 0.26, 1.55, 2.55
    pt.plinth(k, w + 0.1, d + 0.1, h=base)
    walls_box(k, w, d, base, eave, color=(1.0, 0.96, 0.86))
    pt.door(k, (-0.35, -d / 2), (0, -1), w=0.56, h=1.05, y0=base)
    pt.window(k, (0.5, -d / 2), (0, -1), base + 0.55, w=0.42, h=0.38, flowers=FLOWERS)
    pt.window(k, (w / 2, 0.1), (1, 0), base + 0.55, w=0.38, h=0.36, shutters=(0.55, 0.42, 0.3))
    pt.window(k, (-w / 2, 0.1), (-1, 0), base + 0.55, w=0.38, h=0.36, shutters=(0.55, 0.42, 0.3))
    pt.gable_ends(k, w, d, eave, ridge, tile="plaster", color=(1.0, 0.95, 0.85), thick=0.1)
    pt.gable_roof(k, w, d, eave, ridge, over=0.32, over_end=0.28, kind="thatch", color=THATCH)
    pt.chimney(k, 0.6, 0.42, 1.9, 3.0)
    pt.barrel(k, 1.25, -0.62)
    pt.firewood(k, -1.28, 0.3, w=0.75, yaw=90)
    pt.flower_patch(k, 0.05, -1.25, FLOWERS, n=5, spread=0.18)
    return k


def shelter(seed=5):
    """The first homes: an A-frame of poles under thick thatch, a hide over the door, firewood by it."""
    k = Kit(seed)
    w, d = 1.9, 2.3  # ridge along z, door at -z end
    ridge = 2.05
    # sloping sides straight from the ground
    S = w / 2 + 0.25
    L = d / 2
    for s in (-1, 1):
        for r in range(3):
            t0, t1 = max(0.0, r / 3 - 0.08 * (r > 0)), (r + 1) / 3
            up = (2 - r) * 0.08

            def at(z, t):
                return v3(s * S * t, ridge + up + (0.02 - ridge) * t, z)

            g = k.rng.uniform(0.94, 1.04)
            col = tuple(c * g for c in THATCH)
            if s > 0:
                k.quad_slab(at(L + 0.1, t1), at(-L - 0.15, t1), at(-L - 0.15, t0), at(L + 0.1, t0), 0.24, "thatch", color=col)
            else:
                k.quad_slab(at(-L - 0.15, t1), at(L + 0.1, t1), at(L + 0.1, t0), at(-L - 0.15, t0), 0.24, "thatch", color=col)
            if r < 2:
                lip = at(0, t1)
                k.cyl(v3(lip[0], lip[1] - 0.12, -L - 0.17), v3(lip[0], lip[1] - 0.12, L + 0.12), 0.12, sides=9,
                      tile="thatch", end_tile="thatch", color=tuple(c * 0.9 for c in col), rings=4, wobble=0.07)
    k.cyl(v3(0, ridge + 0.12, -L - 0.2), v3(0, ridge + 0.12, L + 0.15), 0.22, sides=12, tile="thatch",
          end_tile="thatch", color=tuple(c * 0.9 for c in THATCH), rings=4, wobble=0.07)
    # front: two poles crossing at the top, a hide door, back wall of wattle
    for s in (-1, 1):
        k.cyl(v3(s * (S - 0.15), 0, -L - 0.25), v3(-s * 0.18, ridge + 0.45, -L - 0.25), 0.07, r2=0.05, sides=8)
        k.cyl(v3(s * (S - 0.15), 0, L + 0.2), v3(-s * 0.18, ridge + 0.45, L + 0.2), 0.07, r2=0.05, sides=8)
    k.tri_panel(v3(-S + 0.3, 0.0, -L + 0.05), v3(S - 0.3, 0.0, -L + 0.05), v3(0, ridge - 0.2, -L + 0.05), 0.08,
                "wattle")
    k.tri_panel(v3(S - 0.3, 0.0, L), v3(-S + 0.3, 0.0, L), v3(0, ridge - 0.2, L), 0.08, "wattle")
    # door hide
    k.quad_slab(v3(-0.32, 0.02, -L - 0.03), v3(0.32, 0.02, -L - 0.03), v3(0.16, 1.15, -L - 0.03),
                v3(-0.16, 1.15, -L - 0.03), 0.03, "hide", color=(1.0, 0.9, 0.78))
    k.box((0, 0.55, -L + 0.0), (0.5, 1.0, 0.04), tile="planks", color=pt.DARK, vary=0)
    pt.firewood(k, 1.05, -0.9, w=0.55, yaw=0, rows=2)
    pt.pot(k, -0.9, -1.25)
    pt.sack(k, -1.15, -0.95, s=0.26)
    return k


def woven_shelter(seed=7):
    """A round wattle hut under a cone of thatch with a little topknot."""
    k = Kit(seed)
    r, h = 1.0, 1.25
    sides = 14
    for i in range(sides):
        a0 = 2 * math.pi * i / sides
        a1 = 2 * math.pi * (i + 1) / sides
        p0 = (math.sin(a0) * r, math.cos(a0) * r)
        p1 = (math.sin(a1) * r, math.cos(a1) * r)
        if abs(math.degrees(a0) - 180) < 20:  # door gap at -z
            continue
        pt.wall_plaster(k, p1, p0, 0.0, h, thick=0.12, tile="wattle", color=(1, 1, 1), posts=False)
    for i in range(sides):
        a = 2 * math.pi * (i + 0.5) / sides
        pt.post(k, math.sin(a) * (r + 0.07), math.cos(a) * (r + 0.07), h + 0.05, r=0.05)
    # cone roof made of rings
    rings = [(r + 0.42, h - 0.05), (r * 0.8, h + 0.45), (r * 0.45, h + 0.95), (0.12, h + 1.35)]
    segs = 18
    for j in range(len(rings) - 1):
        (r0, y0), (r1, y1) = rings[j], rings[j + 1]
        for i in range(segs):
            a0 = 2 * math.pi * i / segs
            a1 = 2 * math.pi * (i + 1) / segs
            q0 = v3(math.sin(a0) * r0, y0, math.cos(a0) * r0)
            q1 = v3(math.sin(a1) * r0, y0, math.cos(a1) * r0)
            q2 = v3(math.sin(a1) * r1, y1, math.cos(a1) * r1)
            q3 = v3(math.sin(a0) * r1, y1, math.cos(a0) * r1)
            k.quad_slab(q1, q0, q3, q2, 0.22, "thatch", color=THATCH)
    for (rr, yy) in rings[1:3]:
        for i in range(segs):
            a0 = 2 * math.pi * i / segs
            a1 = 2 * math.pi * (i + 1) / segs
            k.cyl(v3(math.sin(a0) * rr * 1.06, yy - 0.04, math.cos(a0) * rr * 1.06),
                  v3(math.sin(a1) * rr * 1.06, yy - 0.04, math.cos(a1) * rr * 1.06), 0.1, sides=8, tile="thatch",
                  end_tile="thatch", color=tuple(c * 0.88 for c in THATCH), caps=False)
    k.cyl(v3(0, h + 1.2, 0), v3(0, h + 1.65, 0), 0.2, r2=0.05, sides=10, tile="thatch", end_tile="thatch",
          color=tuple(c * 0.9 for c in THATCH), rings=2)
    # eave roll
    for i in range(segs):
        a0 = 2 * math.pi * i / segs
        a1 = 2 * math.pi * (i + 1) / segs
        rr = r + 0.42
        k.cyl(v3(math.sin(a0) * rr, h - 0.12, math.cos(a0) * rr), v3(math.sin(a1) * rr, h - 0.12, math.cos(a1) * rr),
              0.13, sides=8, tile="thatch", end_tile="thatch", color=tuple(c * 0.85 for c in THATCH), caps=False)
    # doorway
    k.box((0, 0.6, -r + 0.05), (0.55, 1.1, 0.08), tile="planks", color=pt.DARK, vary=0)
    k.quad_slab(v3(-0.33, 0.02, -r - 0.08), v3(0.05, 0.02, -r - 0.12), v3(0.0, 1.1, -r - 0.1),
                v3(-0.3, 1.12, -r - 0.08), 0.03, "hide", color=(1.0, 0.88, 0.72))
    pt.opening_frame(k, (0, -r + 0.02), (0, -1), 0.55, 1.1, 0.0)
    pt.pot(k, 0.8, -0.95, color=(0.95, 0.6, 0.4))
    pt.pot(k, 1.0, -0.75, r=0.09, h=0.16)
    pt.sack(k, -0.95, -0.8, s=0.28)
    return k


def longhouse(seed=11):
    """A long timber hall for many families: plank walls, a great curved thatch roof, a porch on posts."""
    k = Kit(seed)
    w, d = 3.8, 2.3
    base, eave, ridge = 0.22, 1.45, 2.95
    pt.plinth(k, w + 0.1, d + 0.1, h=base, stone=0.34)
    walls_box(k, w, d, base, eave, front=pt.wall_planks, color=(1.0, 0.92, 0.82))
    for x in (-w / 2, -w / 6, w / 6, w / 2):
        for z in (-d / 2, d / 2):
            pt.post(k, x, z + (0.08 if z > 0 else -0.08), eave + 0.05, r=0.09, color=(0.9, 0.82, 0.75))
    pt.door(k, (0, -d / 2), (0, -1), w=0.7, h=1.12, y0=base, color=(0.7, 0.48, 0.34))
    for x in (-1.2, 1.2):
        pt.window(k, (x, -d / 2), (0, -1), base + 0.55, w=0.36, h=0.32, shutters=(0.62, 0.36, 0.26))
        pt.window(k, (x, d / 2), (0, 1), base + 0.55, w=0.36, h=0.32, shutters=(0.62, 0.36, 0.26))
    pt.gable_ends(k, w, d, eave, ridge, tile="planks", color=(0.95, 0.88, 0.78))
    pt.gable_roof(k, w, d, eave, ridge, over=0.38, over_end=0.32, kind="thatch", color=THATCH)
    pt.crossed_gable_poles(k, w + 0.3, d, ridge + 0.1, length=0.6)
    # porch: a small lean-to over the door
    k.quad_slab(v3(-0.75, eave - 0.05, -d / 2 - 0.85), v3(0.75, eave - 0.05, -d / 2 - 0.85),
                v3(0.75, eave + 0.35, -d / 2 - 0.1), v3(-0.75, eave + 0.35, -d / 2 - 0.1), 0.14, "thatch",
                color=THATCH)
    for x in (-0.65, 0.65):
        pt.post(k, x, -d / 2 - 0.75, eave - 0.05, r=0.07)
    k.beam(v3(-0.75, eave - 0.08, -d / 2 - 0.75), v3(0.75, eave - 0.08, -d / 2 - 0.75), w=0.1, h=0.1)
    pt.bench(k, -1.3, -d / 2 - 0.35)
    pt.firewood(k, 2.2, 0.3, w=0.9, yaw=90)
    pt.barrel(k, 1.35, -d / 2 - 0.35)
    pt.barrel(k, 1.7, -d / 2 - 0.25, r=0.15, h=0.36)
    pt.sack(k, -2.1, -0.9)
    return k


def elders_lodge(seed=13):
    """The elders' lodge: a proud timber-framed house with a red shingle roof, a porch and banners."""
    k = Kit(seed)
    w, d = 2.2, 1.9
    base, eave, ridge = 0.32, 1.75, 2.85
    pt.plinth(k, w + 0.12, d + 0.12, h=base, stone=0.32)
    walls_box(k, w, d, base, eave, color=(1.0, 0.93, 0.8))
    pt.door(k, (0, -d / 2), (0, -1), w=0.6, h=1.12, y0=base, color=(0.62, 0.38, 0.26))
    for x in (-0.65, 0.65):
        pt.window(k, (x, -d / 2), (0, -1), base + 0.62, w=0.36, h=0.38, shutters=(0.32, 0.45, 0.62),
                  flowers=FLOWERS if x > 0 else None)
    pt.window(k, (w / 2, 0), (1, 0), base + 0.62, w=0.38, h=0.38, shutters=(0.32, 0.45, 0.62))
    pt.window(k, (-w / 2, 0), (-1, 0), base + 0.62, w=0.38, h=0.38, shutters=(0.32, 0.45, 0.62))
    pt.gable_ends(k, w, d, eave, ridge, tile="plaster", color=(1.0, 0.93, 0.8))
    pt.gable_roof(k, w, d, eave, ridge, over=0.3, over_end=0.26, kind="shingle", shingle_tile="shingle_red",
                  color=(1.08, 0.95, 0.9))
    pt.chimney(k, -0.55, 0.45, 2.0, 3.25)
    # banners on poles either side of the door
    for x in (-0.55, 0.55):
        pt.post(k, x * 1.9, -d / 2 - 0.45, 2.0, r=0.045, color=(0.85, 0.7, 0.55))
        k.beam(v3(x * 1.9 - 0.2, 1.95, -d / 2 - 0.45), v3(x * 1.9 + 0.2, 1.95, -d / 2 - 0.45), w=0.05, h=0.05)
        k.box((x * 1.9, 1.6, -d / 2 - 0.45), (0.36, 0.66, 0.03), tile="cloth_red", color=(1, 1, 1), vary=0)
        k.box((x * 1.9, 1.6, -d / 2 - 0.47), (0.12, 0.12, 0.02), tile="cloth_ochre", color=(1.1, 1.0, 0.7), vary=0)
    pt.bench(k, 0.85, -d / 2 - 0.25)
    pt.flower_patch(k, -0.95, -d / 2 - 0.3, FLOWERS, n=6)
    return k


def healers_hut(seed=17):
    """A small whitewashed hut with an ochre awning, herbs drying and jars by the door."""
    k = Kit(seed)
    w, d = 1.8, 1.6
    base, eave, ridge = 0.2, 1.45, 2.4
    pt.plinth(k, w + 0.08, d + 0.08, h=base, stone=0.28)
    walls_box(k, w, d, base, eave, color=(0.98, 0.98, 0.94), braces=False)
    pt.door(k, (-0.3, -d / 2), (0, -1), w=0.54, h=1.0, y0=base, color=(0.55, 0.62, 0.48))
    pt.window(k, (0.45, -d / 2), (0, -1), base + 0.55, w=0.36, h=0.34, shutters=(0.42, 0.6, 0.45))
    pt.gable_ends(k, w, d, eave, ridge, tile="plaster", color=(0.98, 0.97, 0.92))
    pt.gable_roof(k, w, d, eave, ridge, over=0.28, over_end=0.24, kind="thatch_old", color=(0.95, 0.85, 0.7))
    # awning
    k.quad_slab(v3(-0.1, 1.05, -d / 2 - 0.7), v3(0.95, 1.05, -d / 2 - 0.7), v3(0.95, 1.35, -d / 2 - 0.05),
                v3(-0.1, 1.35, -d / 2 - 0.05), 0.03, "cloth_ochre", color=(1, 1, 1))
    for x in (-0.05, 0.9):
        pt.post(k, x, -d / 2 - 0.68, 1.06, r=0.035)
    # herb rack
    k.beam(v3(-1.2, 1.1, -0.5), v3(-1.2, 1.1, 0.5), w=0.05, h=0.05)
    for z in (-0.5, 0.5):
        pt.post(k, -1.2, z, 1.12, r=0.035)
    for i in range(5):
        z = -0.4 + i * 0.2
        k.box((-1.2, 0.92, z), (0.1, 0.3, 0.1), tile="straw", color=(0.55, 0.8, 0.4), round_=0.8, seg=2, jitter=0.1)
    pt.pot(k, 0.55, -1.25, color=(0.9, 0.5, 0.35))
    pt.pot(k, 0.78, -1.15, r=0.1, h=0.18, color=(0.75, 0.7, 0.6))
    pt.pot(k, 0.35, -1.38, r=0.09, h=0.15)
    pt.flower_patch(k, 1.1, -0.6, [(0.7, 0.5, 1.0), (1, 1, 1)], n=5, spread=0.2)
    return k


def hunters_lodge(seed=19):
    """A log cabin with a plank roof, antlers over the door and hides stretched to dry."""
    k = Kit(seed)
    w, d = 2.0, 1.7
    eave, ridge = 1.45, 2.4
    pt.plinth(k, w + 0.1, d + 0.1, h=0.18, stone=0.3)
    hw, hd = w / 2, d / 2
    for a, b in (((-hw, -hd), (hw, -hd)), ((hw, -hd), (hw, hd)), ((hw, hd), (-hw, hd)), ((-hw, hd), (-hw, -hd))):
        pt.wall_logs(k, a, b, 0.16, eave, r=0.11, color=(0.95, 0.85, 0.75))
    pt.door(k, (0.25, -d / 2 - 0.05), (0, -1), w=0.56, h=1.0, y0=0.18, color=(0.7, 0.5, 0.36))
    pt.window(k, (-0.5, -d / 2 - 0.05), (0, -1), 0.75, w=0.34, h=0.3, shutters=(0.55, 0.38, 0.28))
    pt.gable_ends(k, w, d, eave, ridge, tile="planks_old", color=(1, 1, 1))
    pt.gable_roof(k, w, d, eave, ridge, over=0.3, over_end=0.28, kind="shingle", shingle_tile="shingle",
                  color=(0.95, 0.88, 0.82))
    # antlers
    for s in (-1, 1):
        base_ = v3(0.25 + s * 0.06, 1.42, -d / 2 - 0.2)
        tip = base_ + v3(s * 0.28, 0.3, 0)
        k.cyl(base_, tip, 0.025, r2=0.015, sides=6, tile="straw", color=(1.1, 1.05, 0.95))
        k.cyl(base_ + (tip - base_) * 0.5, base_ + (tip - base_) * 0.5 + v3(s * 0.02, 0.17, 0), 0.02, r2=0.01,
              sides=6, tile="straw", color=(1.1, 1.05, 0.95))
    # hide on a drying frame
    fx, fz = -1.45, -0.2
    for z in (-0.4, 0.4):
        pt.post(k, fx, fz + z, 1.3, r=0.04)
    k.beam(v3(fx, 1.25, fz - 0.45), v3(fx, 1.25, fz + 0.45), w=0.05, h=0.05)
    k.beam(v3(fx, 0.3, fz - 0.45), v3(fx, 0.3, fz + 0.45), w=0.05, h=0.05)
    k.quad_slab(v3(fx, 0.38, fz + 0.32), v3(fx, 0.38, fz - 0.32), v3(fx, 1.18, fz - 0.36), v3(fx, 1.18, fz + 0.36),
                0.03, "hide", color=(1.05, 0.92, 0.78))
    pt.firewood(k, 1.4, 0.2, w=0.6, yaw=90)
    pt.barrel(k, 1.25, -0.75, r=0.15, h=0.38)
    return k


def weavers_hut(seed=23):
    """A weaver's cottage: a loom under a lean-to and bolts of dyed cloth."""
    k = Kit(seed)
    w, d = 1.9, 1.7
    base, eave, ridge = 0.22, 1.5, 2.45
    pt.plinth(k, w + 0.1, d + 0.1, h=base)
    walls_box(k, w, d, base, eave, color=(1.0, 0.94, 0.84))
    pt.door(k, (-0.4, -d / 2), (0, -1), w=0.54, h=1.02, y0=base, color=(0.6, 0.42, 0.55))
    pt.window(k, (0.42, -d / 2), (0, -1), base + 0.55, w=0.38, h=0.34, shutters=(0.7, 0.42, 0.62), flowers=FLOWERS)
    pt.gable_ends(k, w, d, eave, ridge, tile="plaster", color=(1.0, 0.94, 0.84))
    pt.gable_roof(k, w, d, eave, ridge, over=0.3, over_end=0.25, kind="thatch", color=THATCH)
    # lean-to on the right with a loom
    x0 = w / 2
    k.quad_slab(v3(x0 + 0.85, 1.05, -0.7), v3(x0 + 0.85, 1.05, 0.7), v3(x0 + 0.02, 1.45, 0.7),
                v3(x0 + 0.02, 1.45, -0.7), 0.06, "planks", color=(0.9, 0.8, 0.7))
    for z in (-0.6, 0.6):
        pt.post(k, x0 + 0.78, z, 1.06, r=0.045)
    lx = x0 + 0.42
    for z in (-0.32, 0.32):
        k.beam(v3(lx, 0, z), v3(lx, 0.95, z), w=0.06, h=0.06)
    k.beam(v3(lx, 0.92, -0.36), v3(lx, 0.92, 0.36), w=0.05, h=0.05)
    k.beam(v3(lx, 0.25, -0.36), v3(lx, 0.25, 0.36), w=0.05, h=0.05)
    k.box((lx, 0.6, 0), (0.02, 0.6, 0.6), tile="cloth_red", color=(0.6, 0.75, 1.1), vary=0)
    for i, c in enumerate([(1, 1, 1), (0.6, 0.8, 1.2), (1.1, 1.0, 0.6)]):
        k.cyl(v3(-1.25, 0.1 + i * 0.0, -0.5 + i * 0.32), v3(-1.25 + 0.0, 0.1, -0.2 + i * 0.32), 0.1, sides=10,
              tile="cloth_ochre" if i == 2 else "cloth_red", end_tile="cloth_red", color=c)
    pt.crate(k, -1.2, 0.45, s=0.3, yaw=15)
    return k


def granary(seed=29):
    """Grain kept dry: a plank store raised on stone pillars with mushroom caps, a ladder to the door."""
    k = Kit(seed)
    w, d = 1.7, 1.6
    lift = 0.55
    eave, ridge = lift + 1.15, lift + 2.1
    for x in (-w / 2 + 0.15, w / 2 - 0.15):
        for z in (-d / 2 + 0.15, d / 2 - 0.15):
            k.cyl(v3(x, 0, z), v3(x, lift - 0.12, z), 0.11, r2=0.08, sides=9, tile="stone", end_tile="stone",
                  color=(0.95, 0.95, 0.95))
            k.cyl(v3(x, lift - 0.12, z), v3(x, lift - 0.04, z), 0.2, r2=0.17, sides=12, tile="stone", end_tile="stone",
                  color=(0.95, 0.95, 0.95))
    k.box((0, lift + 0.02, 0), (w + 0.1, 0.1, d + 0.1), tile="planks", color=(0.8, 0.65, 0.5), grain=0)
    walls_box(k, w, d, lift + 0.07, eave, front=pt.wall_planks, color=(1.0, 0.9, 0.78))
    for x in (-w / 2, w / 2):
        for z in (-d / 2, d / 2):
            k.beam(v3(x, lift, z), v3(x, eave + 0.04, z), w=0.12, h=0.12, color=pt.TIMBER)
    pt.gable_ends(k, w, d, eave, ridge, ridge_axis="z", tile="planks", color=(0.95, 0.85, 0.72))
    pt.gable_roof(k, w, d, eave, ridge, over=0.28, over_end=0.3, kind="thatch", ridge_axis="z",
                  color=THATCH)
    # door + ladder
    k.box((0.2, lift + 0.55, -d / 2 - 0.06), (0.5, 0.8, 0.05), tile="planks", color=(0.62, 0.45, 0.32), grain=1)
    pt.opening_frame(k, (0.2, -d / 2), (0, -1), 0.5, 0.8, lift + 0.12)
    for x in (0.02, 0.38):
        k.beam(v3(x, 0, -d / 2 - 0.62), v3(x, lift + 0.15, -d / 2 - 0.12), w=0.05, h=0.05)
    for i in range(4):
        t = (i + 0.5) / 4
        y = t * (lift + 0.15)
        z = -d / 2 - 0.62 + t * 0.5
        k.beam(v3(0.0, y, z), v3(0.4, y, z), w=0.04, h=0.04, up=(0, 0, 1))
    for i in range(3):
        pt.sack(k, -0.75 + i * 0.28, -1.0 + (i % 2) * 0.12, s=0.3)
    pt.sack(k, -0.62, -0.92, s=0.27, y=0.24)
    return k


BUILDINGS = {
    "daub_house": daub_house,
    "shelter": shelter,
    "woven_shelter": woven_shelter,
    "longhouse": longhouse,
    "elders_lodge": elders_lodge,
    "healers_hut": healers_hut,
    "hunters_lodge": hunters_lodge,
    "weavers_hut": weavers_hut,
    "granary": granary,
}
