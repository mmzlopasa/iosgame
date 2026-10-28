"""The village's buildings, one function per building sprite. Run build.py to write the .glb files."""
import math

from kit import Kit, rot_y, rot_z, v3
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


def lean_to(k, x0, x1, z0, z1, y_hi, y_lo, tile="planks", color=(0.9, 0.8, 0.7), posts=True, thatch=False):
    """A sloped roof on posts: high along z0, low along z1 (x0..x1 wide)."""
    col = THATCH if thatch else color
    t = "thatch" if thatch else tile
    k.quad_slab(v3(x0, y_lo, z1), v3(x1, y_lo, z1), v3(x1, y_hi, z0), v3(x0, y_hi, z0), 0.16 if thatch else 0.06, t,
                color=col)
    if thatch:
        k.cyl(v3(x0 - 0.02, y_lo - 0.06, z1), v3(x1 + 0.02, y_lo - 0.06, z1), 0.09, sides=9, tile="thatch",
              end_tile="thatch", color=tuple(c * 0.88 for c in THATCH), rings=4, wobble=0.07)
    if posts:
        for x in (x0 + 0.1, x1 - 0.1):
            pt.post(k, x, z1 - 0.08, y_lo - 0.02, r=0.06)
            pt.post(k, x, z0 + 0.08, y_hi - 0.04, r=0.06)
        k.beam(v3(x0, y_lo - 0.06, z1 - 0.08), v3(x1, y_lo - 0.06, z1 - 0.08), w=0.09, h=0.09)
        k.beam(v3(x0, y_hi - 0.08, z0 + 0.08), v3(x1, y_hi - 0.08, z0 + 0.08), w=0.09, h=0.09)


def log_pile(k, x, z, length=1.2, rows=3, yaw=0, r=0.12):
    R = rot_y(yaw)
    for j in range(rows):
        n = rows - j + 1
        for i in range(n):
            off = (i - (n - 1) / 2) * r * 2.05
            p = v3(off, r + j * r * 1.75, 0) @ R.T
            a = v3(x, 0, z) + p + v3(0, 0, -length / 2) @ R.T
            b = v3(x, 0, z) + p + v3(0, 0, length / 2) @ R.T
            g = k.rng.uniform(0.85, 1.1)
            k.cyl(a, b, r * k.rng.uniform(0.9, 1.08), sides=10, color=(g, g * 0.95, g * 0.9), wobble=0.04)


def stump(k, x, z, r=0.2, h=0.32, axe=False):
    k.cyl(v3(x, -0.03, z), v3(x, h, z), r, r2=r * 0.92, sides=11, bulge=0.01, rings=2, wobble=0.06)
    if axe:
        k.cyl(v3(x - 0.05, h - 0.02, z), v3(x + 0.25, h + 0.42, z + 0.06), 0.022, sides=6, tile="planks",
              color=(0.9, 0.7, 0.5))
        k.box((x - 0.04, h + 0.02, z), (0.16, 0.06, 0.03), R=rot_z(-55), tile="iron", color=(0.85, 0.85, 0.9))


def stone_heap(k, x, z, n=8, spread=0.35, size=0.24, tint=(1, 1, 1)):
    for i in range(n):
        a = k.rng.uniform(0, 6.28)
        rr = k.rng.uniform(0, spread)
        s = size * k.rng.uniform(0.7, 1.2)
        y = s * 0.4 + (0.12 if rr < spread * 0.4 and i > n // 2 else 0)
        g = k.rng.uniform(0.85, 1.08)
        k.box((x + math.cos(a) * rr, y, z + math.sin(a) * rr), (s, s * 0.75, s * 0.9), R=rot_y(k.rng.uniform(0, 90)),
              tile="stone", color=(tint[0] * g, tint[1] * g, tint[2] * g), round_=0.7, seg=2, jitter=0.08)


def wood_camp(seed=31):
    """Woodcutters: a thatched lean-to, a stack of trunks, a chopping stump with its axe, split logs."""
    k = Kit(seed)
    lean_to(k, -1.3, 0.3, 0.9, -0.2, 1.75, 1.15, thatch=True)
    log_pile(k, -0.5, 0.35, length=1.4, rows=3, yaw=90)
    stump(k, 0.75, -0.45, axe=True)
    pt.firewood(k, 0.9, 0.55, w=0.8, yaw=0, rows=3)
    for i in range(4):
        a = k.rng.uniform(0, 6.28)
        k.cyl(v3(0.45 + math.cos(a) * 0.3, 0.06, -0.85 + math.sin(a) * 0.2),
              v3(0.45 + math.cos(a) * 0.3 + 0.2, 0.06, -0.85 + math.sin(a) * 0.2 + 0.1), 0.06, sides=7)
    k.cyl(v3(-1.4, 0.15, -0.8), v3(0.0, 0.15, -1.1), 0.15, sides=10, wobble=0.05)
    return k


def stone_site(seed=37):
    """A stone cutters' yard: a rocky outcrop, cut blocks on skids, a lifting tripod."""
    k = Kit(seed)
    for (x, z, s) in ((-0.6, 0.5, 0.9), (0.1, 0.75, 0.7), (-0.95, -0.1, 0.6)):
        k.box((x, s * 0.35, z), (s, s * 0.8, s * 0.85), R=rot_y(k.rng.uniform(0, 60)), tile="stone",
              color=(0.95, 0.95, 0.95), round_=0.6, seg=3, jitter=0.1)
    for i, (x, z) in enumerate(((0.55, -0.35), (0.95, -0.35), (0.75, -0.35))):
        y = 0.16 if i < 2 else 0.47
        if i == 2:
            x, z = 0.75, -0.35
        k.box((x, y, z), (0.36, 0.3, 0.5), tile="stone", color=(1.02, 1.0, 0.96), round_=0.35, seg=2)
    k.beam(v3(0.3, 0.02, -0.65), v3(1.2, 0.02, -0.65), w=0.08, h=0.06)
    k.beam(v3(0.3, 0.02, -0.05), v3(1.2, 0.02, -0.05), w=0.08, h=0.06)
    top = v3(-0.1, 2.0, -0.55)
    for a in (0, 120, 240):
        foot = top + v3(math.sin(math.radians(a)) * 0.75, -2.0, math.cos(math.radians(a)) * 0.75)
        k.cyl(foot, top + v3(0, 0.15, 0), 0.05, r2=0.04, sides=7)
    k.cyl(top, top + v3(0, -1.2, 0), 0.012, sides=5, tile="straw", color=(1.0, 0.9, 0.7), caps=False)
    k.box((-0.1, 0.62, -0.55), (0.3, 0.26, 0.3), tile="stone", color=(1, 1, 1), round_=0.5, seg=2)
    stone_heap(k, -0.55, -0.85, n=7, spread=0.3, size=0.18)
    return k


def foraging_camp(seed=41):
    """Gatherers: a hide awning over baskets of berries, mushrooms and roots."""
    k = Kit(seed)
    for x in (-0.8, 0.8):
        pt.post(k, x, -0.55, 1.45, r=0.05)
        pt.post(k, x, 0.6, 1.2, r=0.05)
    k.quad_slab(v3(-0.95, 1.12, 0.75), v3(0.95, 1.12, 0.75), v3(0.95, 1.5, -0.7), v3(-0.95, 1.5, -0.7), 0.03,
                "cloth_ochre", color=(1.0, 0.95, 0.85))
    for x in (-0.95, -0.32, 0.32, 0.95):
        k.quad_slab(v3(x - 0.1, 1.5, -0.71), v3(x + 0.1, 1.5, -0.71), v3(x + 0.1, 1.3, -0.73), v3(x - 0.1, 1.3, -0.73),
                    0.02, "cloth_red", color=(1, 1, 1))
    berries = [(0.85, 0.15, 0.25), (0.35, 0.25, 0.65), (0.95, 0.75, 0.6), (0.9, 0.4, 0.2)]
    for i, (x, z) in enumerate(((-0.45, -0.1), (0.05, 0.15), (0.5, -0.15), (-0.2, 0.45))):
        k.cyl(v3(x, 0, z), v3(x, 0.26, z), 0.2, r2=0.24, sides=12, tile="wattle", end_tile="wattle",
              color=(1.0, 0.9, 0.75))
        for j in range(5):
            a = j * 1.3
            k.box((x + math.cos(a) * 0.1, 0.28, z + math.sin(a) * 0.1), (0.1, 0.1, 0.1), tile="cloth_red",
                  color=berries[i], round_=0.9, seg=2, vary=0.12)
    pt.sack(k, 0.8, 0.3)
    pt.sack(k, -0.85, 0.25, s=0.26)
    pt.pot(k, 0.85, -0.7)
    return k


def storage_pit(seed=43):
    """A stone-lined store under a low thatched cone, with jars and baskets around it."""
    k = Kit(seed)
    r = 0.8
    for i in range(12):
        a = 2 * math.pi * i / 12
        k.box((math.sin(a) * r, 0.12, math.cos(a) * r), (0.34, 0.26, 0.22), R=rot_y(math.degrees(a)), tile="stone",
              color=(0.95, 0.95, 0.95), round_=0.7, seg=2, jitter=0.06)
    segs = 14
    for i in range(segs):
        a0, a1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs
        q0 = v3(math.sin(a0) * (r + 0.2), 0.3, math.cos(a0) * (r + 0.2))
        q1 = v3(math.sin(a1) * (r + 0.2), 0.3, math.cos(a1) * (r + 0.2))
        top = v3(0, 1.35, 0)
        k.quad_slab(q1, q0, top + (q0 - top) * 0.02, top + (q1 - top) * 0.02, 0.18, "thatch", color=THATCH)
    k.cyl(v3(0, 1.25, 0), v3(0, 1.55, 0), 0.14, r2=0.04, sides=9, tile="thatch", end_tile="thatch",
          color=tuple(c * 0.9 for c in THATCH))
    k.box((0, 0.35, -r - 0.05), (0.5, 0.45, 0.1), tile="planks", color=pt.DARK, vary=0)
    for (x, z) in ((0.95, -0.65), (1.15, -0.4), (-1.0, -0.6)):
        pt.pot(k, x, z, color=(0.92, 0.58, 0.4))
    pt.sack(k, -1.1, -0.2)
    return k


def toolmaker(seed=47):
    """The toolmaker: a workbench under a shingled lean-to, tools on a rack, a grinding stone."""
    k = Kit(seed)
    lean_to(k, -1.1, 1.1, 0.8, -0.35, 1.8, 1.3, tile="shingle", color=(0.95, 0.88, 0.8))
    pt.wall_planks(k, (1.1, 0.8), (-1.1, 0.8), 0, 1.75, color=(0.95, 0.85, 0.72))
    # bench
    k.box((0, 0.62, 0.35), (1.3, 0.08, 0.45), tile="planks", color=(0.95, 0.75, 0.55), grain=0)
    for x in (-0.55, 0.55):
        for z in (0.17, 0.53):
            k.box((x, 0.3, z), (0.07, 0.6, 0.07), tile="planks", color=pt.TIMBER_DARK)
    # tools on the back wall
    for i in range(5):
        x = -0.8 + i * 0.38
        k.cyl(v3(x, 0.85, 0.72), v3(x, 1.45, 0.72), 0.02, sides=6, tile="planks", color=(0.95, 0.75, 0.55))
        k.box((x, 1.42, 0.7), (0.14, 0.08, 0.03), tile="iron" if i % 2 else "stone", color=(0.9, 0.9, 0.95), vary=0)
    for i in range(4):
        k.box((-0.4 + i * 0.25, 0.7, 0.35), (0.14, 0.05, 0.06), tile="stone", color=(0.7, 0.7, 0.75), round_=0.6, seg=2)
    # grinding stone
    k.cyl(v3(0.95, 0.45, -0.6), v3(0.95, 0.45, -0.45), 0.3, sides=14, tile="stone", end_tile="stone", color=(1, 1, 1))
    for z in (-0.68, -0.37):
        k.beam(v3(0.8, 0, z), v3(0.95, 0.5, z), w=0.06, h=0.06)
        k.beam(v3(1.1, 0, z), v3(0.95, 0.5, z), w=0.06, h=0.06)
    stump(k, -0.8, -0.55)
    pt.crate(k, -1.3, 0.2, s=0.32)
    return k


def sawpit(seed=53):
    """A saw pit: a trestle with a log on it, a long saw, a stack of fresh planks."""
    k = Kit(seed)
    for x in (-0.75, 0.75):
        for z in (-0.25, 0.25):
            k.beam(v3(x, 0, z), v3(x * 0.95, 0.95, 0), w=0.08, h=0.08)
        k.beam(v3(x, 0.95, -0.3), v3(x, 0.95, 0.3), w=0.1, h=0.1)
    k.cyl(v3(-1.2, 1.12, 0), v3(1.2, 1.12, 0), 0.16, sides=11, wobble=0.04)
    k.box((0.2, 1.05, 0.0), (0.03, 0.9, 0.22), R=rot_z(8), tile="iron", color=(0.85, 0.85, 0.9), vary=0)
    for i in range(5):
        y = 0.04 + i * 0.05
        k.box((0.1 + k.rng.uniform(-0.05, 0.05), y, -0.85), (1.6, 0.045, 0.24), R=rot_y(k.rng.uniform(-3, 3)),
              tile="planks", color=(1.05, 0.88, 0.68), grain=0)
    for i in range(4):
        k.box((-0.3 + i * 0.15, 0.03, 0.6 + k.rng.uniform(-0.05, 0.05)), (0.22, 0.04, 0.12), tile="straw",
              color=(1.1, 0.95, 0.7), round_=0.8, seg=2)
    lean_to(k, -1.35, 1.35, 1.0, 0.45, 1.65, 1.35, thatch=True, posts=True)
    return k


def smokehouse(seed=59):
    """A tall narrow plank hut that leaks smoke, fish and meat hanging on a rack outside."""
    k = Kit(seed)
    w, d = 1.4, 1.3
    eave, ridge = 1.9, 2.7
    pt.plinth(k, w + 0.1, d + 0.1, h=0.2, stone=0.28)
    walls_box(k, w, d, 0.18, eave, front=pt.wall_planks, color=(0.8, 0.7, 0.62))
    pt.door(k, (0, -d / 2), (0, -1), w=0.5, h=1.05, y0=0.2, color=(0.6, 0.45, 0.35))
    pt.gable_ends(k, w, d, eave, ridge, ridge_axis="z", tile="planks_old", color=(0.85, 0.8, 0.75))
    pt.gable_roof(k, w, d, eave, ridge, over=0.25, over_end=0.2, kind="shingle", ridge_axis="z",
                  color=(0.75, 0.7, 0.68))
    # louvre on the ridge
    k.box((0, ridge + 0.42, 0), (0.4, 0.22, 0.5), tile="planks", color=pt.DARK, vary=0)
    k.box((0, ridge + 0.6, 0), (0.6, 0.06, 0.7), tile="planks", color=(0.7, 0.62, 0.55))
    # drying rack with fish
    rx = 1.15
    for z in (-0.5, 0.5):
        pt.post(k, rx, z, 1.3, r=0.04)
    k.beam(v3(rx, 1.25, -0.55), v3(rx, 1.25, 0.55), w=0.05, h=0.05)
    for i in range(5):
        z = -0.4 + i * 0.2
        k.box((rx, 0.98, z), (0.05, 0.4, 0.12), tile="iron", color=(1.0, 0.85, 0.6), round_=0.85, seg=2)
    pt.firewood(k, -1.1, 0.0, w=0.6, yaw=90)
    return k


def pottery_kiln(seed=61):
    """A dome kiln of clay and stone, its firing mouth glowing, pots drying on a plank shelf."""
    k = Kit(seed)
    k.box((-0.3, 0.42, 0.1), (1.3, 1.0, 1.3), tile="hide", color=(0.88, 0.62, 0.5), round_=0.95, seg=5)
    k.cyl(v3(-0.3, 0.8, 0.1), v3(-0.3, 1.25, 0.1), 0.18, r2=0.14, sides=10, tile="hide", end_tile="stone",
          color=(0.95, 0.65, 0.5))
    k.box((-0.3, 0.2, -0.5), (0.36, 0.3, 0.15), tile="planks", color=(1.4, 0.6, 0.2), vary=0)
    k.box((-0.3, 0.38, -0.53), (0.5, 0.08, 0.14), tile="stone", color=(0.9, 0.9, 0.9), round_=0.5, seg=2)
    k.box((0.75, 0.55, 0.35), (0.25, 0.05, 1.0), tile="planks", color=(0.95, 0.75, 0.55), grain=2)
    for z in (-0.05, 0.75):
        k.box((0.75, 0.27, z), (0.2, 0.54, 0.05), tile="planks", color=pt.TIMBER_DARK)
    for i in range(4):
        k.cyl(v3(0.75, 0.58, 0.0 + i * 0.24), v3(0.75, 0.78, 0.0 + i * 0.24), 0.07, r2=0.05, bulge=0.04, rings=3,
              sides=9, tile="hide", end_tile="hide", color=(1.0, 0.7, 0.5))
    pt.firewood(k, -1.0, -0.6, w=0.55, yaw=90, rows=2)
    pt.pot(k, 0.5, -0.7, r=0.14, h=0.26, color=(1.05, 0.65, 0.45))
    pt.pot(k, 0.8, -0.6, color=(0.85, 0.5, 0.35))
    return k


def copper_smelter(seed=67):
    """A squat stone furnace with a clay chimney, bellows and a heap of green ore."""
    k = Kit(seed)
    for j in range(4):
        r = 0.55 - j * 0.07
        for i in range(10):
            a = 2 * math.pi * (i + 0.5 * (j % 2)) / 10
            k.box((math.sin(a) * r, 0.12 + j * 0.22, math.cos(a) * r), (0.3, 0.22, 0.22), R=rot_y(math.degrees(a)),
                  tile="stone", color=(0.92, 0.9, 0.88), round_=0.6, seg=2, jitter=0.05)
    k.cyl(v3(0, 0.85, 0), v3(0, 1.4, 0), 0.25, r2=0.17, sides=12, tile="hide", end_tile="stone",
          color=(1.0, 0.7, 0.55))
    k.box((0, 0.2, -0.5), (0.25, 0.22, 0.12), tile="planks", color=(1.4, 0.55, 0.15), vary=0)
    # bellows
    k.box((0.75, 0.2, -0.1), (0.4, 0.14, 0.3), tile="hide", color=(1.0, 0.85, 0.7), round_=0.8, seg=2)
    k.cyl(v3(0.55, 0.22, -0.1), v3(0.4, 0.24, -0.1), 0.03, sides=6, tile="iron", color=(0.7, 0.6, 0.5))
    stone_heap(k, -0.85, -0.45, n=8, spread=0.3, size=0.16, tint=(0.62, 0.95, 0.75))
    pt.firewood(k, -0.8, 0.55, w=0.5, yaw=0, rows=2)
    return k


def bronze_workshop(seed=71):
    """A bronze smith's shed: anvil, a small hearth, moulds and finished blades."""
    k = Kit(seed)
    lean_to(k, -1.1, 1.1, 0.85, -0.45, 2.0, 1.45, tile="shingle_red", color=(1.0, 0.92, 0.85))
    pt.wall_plaster(k, (1.1, 0.85), (-1.1, 0.85), 0, 1.95, color=(0.98, 0.92, 0.82))
    # hearth
    k.box((-0.45, 0.3, 0.35), (0.7, 0.6, 0.6), tile="stone", color=(0.9, 0.88, 0.86), round_=0.4, seg=3)
    k.box((-0.45, 0.62, 0.35), (0.45, 0.06, 0.38), tile="planks", color=(1.4, 0.6, 0.2), vary=0)
    k.box((-0.45, 1.3, 0.55), (0.35, 1.3, 0.3), tile="stone", color=(0.88, 0.86, 0.85), round_=0.3, seg=2)
    # anvil on a stump
    stump(k, 0.45, 0.1, r=0.18, h=0.38)
    k.box((0.45, 0.47, 0.1), (0.36, 0.12, 0.16), tile="iron", color=(0.8, 0.82, 0.88), round_=0.3, seg=2)
    k.box((0.65, 0.49, 0.1), (0.14, 0.07, 0.1), tile="iron", color=(0.8, 0.82, 0.88), round_=0.4, seg=2)
    for i in range(3):
        k.box((0.95, 0.04, -0.6 + i * 0.22), (0.3, 0.08, 0.16), tile="stone", color=(0.85, 0.75, 0.6), round_=0.4, seg=2)
        k.box((0.95, 0.09, -0.6 + i * 0.22), (0.22, 0.02, 0.05), tile="iron", color=(1.3, 0.85, 0.45), vary=0)
    pt.barrel(k, -1.2, -0.45, r=0.15, h=0.36)
    pt.crate(k, -0.9, -0.75, s=0.3)
    return k


def lookout_tower(seed=73):
    """A tall log tower: braced legs, a ladder, a railed platform under a little thatched cap, a pennant."""
    k = Kit(seed)
    s, top = 0.65, 3.4
    legs = [(-s, -s), (s, -s), (s, s), (-s, s)]
    for (x, z) in legs:
        k.cyl(v3(x * 1.15, -0.05, z * 1.15), v3(x * 0.85, top + 1.0, z * 0.85), 0.08, r2=0.065, sides=9)
    for y in (1.2, 2.4):
        for i in range(4):
            (x0, z0), (x1, z1) = legs[i], legs[(i + 1) % 4]
            f = 1.15 - (y / (top + 1.0)) * 0.3
            k.cyl(v3(x0 * f, y, z0 * f), v3(x1 * f, y, z1 * f), 0.05, sides=7)
            k.cyl(v3(x0 * 1.1, y - 1.1, z0 * 1.1), v3(x1 * f, y, z1 * f), 0.04, sides=6)
    k.box((0, top, 0), (2 * s * 0.95 + 0.4, 0.1, 2 * s * 0.95 + 0.4), tile="planks", color=(0.95, 0.8, 0.62), grain=0)
    e = s * 0.95 + 0.2
    for (x0, z0, x1, z1) in ((-e, -e, e, -e), (e, -e, e, e), (e, e, -e, e), (-e, e, -e, -e)):
        k.beam(v3(x0, top + 0.55, z0), v3(x1, top + 0.55, z1), w=0.06, h=0.06)
        for t in (0.25, 0.5, 0.75):
            k.beam(v3(x0 + (x1 - x0) * t, top, z0 + (z1 - z0) * t), v3(x0 + (x1 - x0) * t, top + 0.55, z0 + (z1 - z0) * t),
                   w=0.04, h=0.04)
    # cap
    segs = 4
    cap_y, cap_top = top + 1.05, top + 1.75
    c = e + 0.25
    corners = [v3(-c, cap_y, -c), v3(c, cap_y, -c), v3(c, cap_y, c), v3(-c, cap_y, c)]
    apex = v3(0, cap_top, 0)
    for i in range(segs):
        a, b = corners[i], corners[(i + 1) % segs]
        k.quad_slab(a, b, apex + (b - apex) * 0.02, apex + (a - apex) * 0.02, 0.16, "thatch", color=THATCH)
    k.cyl(apex - v3(0, 0.15, 0), apex + v3(0, 0.6, 0), 0.03, sides=6)
    k.quad_slab(apex + v3(0.03, 0.55, 0), apex + v3(0.45, 0.47, 0), apex + v3(0.45, 0.35, 0), apex + v3(0.03, 0.3, 0),
                0.01, "cloth_red", color=(1, 1, 1))
    # ladder
    for x in (-0.18, 0.18):
        k.beam(v3(x, 0, -s * 1.25), v3(x, top + 0.05, -s * 0.98), w=0.05, h=0.05)
    for i in range(10):
        t = (i + 0.5) / 10
        k.beam(v3(-0.18, t * top, -s * 1.25 + t * s * 0.27), v3(0.18, t * top, -s * 1.25 + t * s * 0.27), w=0.035,
               h=0.035, up=(0, 0, 1))
    pt.barrel(k, 0.85, 0.55, r=0.15, h=0.36)
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
    "wood_camp": wood_camp,
    "stone_site": stone_site,
    "foraging_camp": foraging_camp,
    "storage_pit": storage_pit,
    "toolmaker": toolmaker,
    "sawpit": sawpit,
    "smokehouse": smokehouse,
    "pottery_kiln": pottery_kiln,
    "copper_smelter": copper_smelter,
    "bronze_workshop": bronze_workshop,
    "lookout_tower": lookout_tower,
}
