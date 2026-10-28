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
    stones_ring(k, 0.0, -1.85)
    k.anchor("fire", (0.0, 0.04, -1.85), 0.3)
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


def stones_ring(k, x, z, r=0.32):
    for i in range(8):
        a = 2 * math.pi * i / 8
        k.box((x + math.cos(a) * r, 0.06, z + math.sin(a) * r), (0.16, 0.12, 0.13), R=rot_y(-math.degrees(a)),
              tile="stone", color=(0.9, 0.9, 0.9), round_=0.7, seg=2, jitter=0.04)


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
    k.anchor("smoke", (0, ridge + 0.65, 0))
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
    k.anchor("fire", (-0.3, 0.12, -0.55), 0.3)
    k.anchor("smoke", (-0.3, 1.3, 0.1))
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
    k.anchor("fire", (0, 1.3, 0), 0.45)
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
    k.anchor("fire", (-0.45, 0.62, 0.35), 0.3)
    k.anchor("smoke", (-0.45, 1.98, 0.55))
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


# MARK: - The medieval town (native footprints: 4 m, barn / church / market 6 m)

def joist_ends(k, w, d, y, step=0.32):
    """Floor joists poking out under a jettied storey."""
    for s in (-1, 1):
        n = int(w / step)
        for i in range(n):
            x = -w / 2 + (i + 0.5) * w / n
            k.box((x, y, s * (d / 2 + 0.06)), (0.1, 0.1, 0.24), tile="log", color=pt.TIMBER)


def town_house(k, w, d, floors=2, roof="shingle_red", ridge_axis="x", plaster=(1.0, 0.95, 0.84), shutters=(0.32, 0.45, 0.62),
               stone_ground=True, jetty=0.22, storey=1.55, roof_h=1.7, door_x=0.0, chimney_at=None, flowers=True):
    base = 0.3
    pt.plinth(k, w + 0.12, d + 0.12, h=base, stone=0.34)
    y = base
    for f in range(floors):
        ww, dd = w + 2 * jetty * f, d + 2 * jetty * f
        y1 = y + storey
        if f == 0 and stone_ground:
            hw, hd = ww / 2, dd / 2
            for a, b in (((-hw, -hd), (hw, -hd)), ((hw, -hd), (hw, hd)), ((hw, hd), (-hw, hd)), ((-hw, hd), (-hw, -hd))):
                pt.wall_plaster(k, a, b, y, y1, thick=0.2, tile="stone", color=(0.92, 0.85, 0.74), posts=False)
            for (x, z) in ((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)):
                k.box((x, (y + y1) / 2, z), (0.3, y1 - y, 0.3), tile="stone", color=(0.85, 0.85, 0.85), round_=0.3, seg=2)
        else:
            walls_box(k, ww, dd, y, y1, color=plaster)
        if f > 0:
            joist_ends(k, ww - 2 * jetty, dd - 2 * jetty, y - 0.02)
        # openings on the front and back
        if f == 0:
            pt.door(k, (door_x, -dd / 2 - (0.05 if stone_ground else 0)), (0, -1), w=0.7, h=1.25, y0=y, color=(0.6, 0.4, 0.28))
            for x in (-ww / 3, ww / 3):
                if abs(x - door_x) > 0.6:
                    pt.window(k, (x, -dd / 2 - (0.05 if stone_ground else 0)), (0, -1), y + 0.6, w=0.42, h=0.42,
                              shutters=shutters, flowers=FLOWERS if flowers else None)
        else:
            for x in (-ww / 3, 0, ww / 3) if ww > 3.5 else (-ww / 4, ww / 4):
                pt.window(k, (x, -dd / 2), (0, -1), y + 0.5, w=0.42, h=0.46, shutters=shutters,
                          flowers=FLOWERS if (flowers and x > 0) else None)
        for x in (-ww / 4, ww / 4):
            pt.window(k, (x, dd / 2), (0, 1), y + 0.55, w=0.4, h=0.42, shutters=shutters)
        for zz in (0,):
            pt.window(k, (ww / 2, zz), (1, 0), y + 0.55, w=0.4, h=0.42, shutters=shutters)
            pt.window(k, (-ww / 2, zz), (-1, 0), y + 0.55, w=0.4, h=0.42, shutters=shutters)
        y = y1
    ww, dd = w + 2 * jetty * (floors - 1), d + 2 * jetty * (floors - 1)
    eave, ridge = y, y + roof_h
    pt.gable_ends(k, ww, dd, eave, ridge, ridge_axis=ridge_axis, tile="plaster", color=plaster)
    kind = "thatch" if roof == "thatch" else "shingle"
    pt.gable_roof(k, ww, dd, eave, ridge, over=0.34, over_end=0.3, kind=kind, ridge_axis=ridge_axis,
                  shingle_tile=roof if roof != "thatch" else "shingle", color=THATCH if roof == "thatch" else (1.05, 0.95, 0.9))
    if chimney_at:
        pt.chimney(k, chimney_at[0], chimney_at[1], eave - 0.4, ridge + 0.6)
    return eave, ridge


def medieval_cottage(seed=101):
    """A stone-and-plaster cottage under deep thatch, a dormer, a fenced herb plot."""
    k = Kit(seed)
    eave, ridge = town_house(k, 3.0, 2.5, floors=1, roof="thatch", storey=1.75, roof_h=1.65, door_x=-0.5,
                             chimney_at=(0.85, 0.55), shutters=(0.42, 0.56, 0.42))
    # dormer
    k.box((0.2, eave + 0.55, -0.95), (0.7, 0.6, 0.5), tile="plaster", color=(1.0, 0.95, 0.84))
    pt.window(k, (0.2, -1.2), (0, -1), eave + 0.38, w=0.36, h=0.32, shutters=None)
    k.quad_slab(v3(-0.25, eave + 0.8, -1.4), v3(0.2, eave + 1.15, -1.4), v3(0.2, eave + 1.15, -0.6),
                v3(-0.25, eave + 0.8, -0.6), 0.14, "thatch", color=THATCH)
    k.quad_slab(v3(0.2, eave + 1.15, -1.4), v3(0.65, eave + 0.8, -1.4), v3(0.65, eave + 0.8, -0.6),
                v3(0.2, eave + 1.15, -0.6), 0.14, "thatch", color=THATCH)
    # garden fence
    for i in range(7):
        x = 0.4 + i * 0.22
        pt.post(k, x, -1.85, 0.55, r=0.035)
    k.beam(v3(0.35, 0.42, -1.85), v3(1.8, 0.42, -1.85), w=0.04, h=0.04)
    k.beam(v3(1.8, 0.42, -1.85), v3(1.8, 0.42, -1.4), w=0.04, h=0.04)
    pt.flower_patch(k, 1.1, -1.55, FLOWERS, n=8, spread=0.3)
    pt.barrel(k, -1.85, -0.6)
    pt.firewood(k, -1.85, 0.5, w=0.8, yaw=90)
    return k


def medieval_house(seed=103):
    """A two-storey timber-framed house, the upper floor jutting out, red shingles, a chimney."""
    k = Kit(seed)
    town_house(k, 2.9, 2.4, floors=2, roof="shingle_red", storey=1.5, roof_h=1.55, door_x=-0.45,
               chimney_at=(-0.9, 0.5))
    pt.bench(k, 0.7, -1.55)
    pt.barrel(k, 1.75, -1.0)
    pt.flower_patch(k, -1.6, -1.45, FLOWERS, n=5)
    return k


def medieval_townhouse(seed=107):
    """A tall narrow townhouse (gable to the street), three storeys, a shop window with an awning."""
    k = Kit(seed)
    eave, ridge = town_house(k, 2.8, 4.2, floors=3, roof="shingle", ridge_axis="z", storey=1.35, roof_h=1.9,
                             plaster=(0.98, 0.9, 0.78), shutters=(0.62, 0.32, 0.26), door_x=0.6, chimney_at=(0.8, 1.0))
    # awning over the shop window
    k.quad_slab(v3(-1.35, 1.75, -2.85), v3(0.15, 1.75, -2.85), v3(0.15, 2.05, -2.15), v3(-1.35, 2.05, -2.15), 0.03,
                "cloth_red", color=(1, 1, 1))
    k.box((-0.6, 0.85, -2.45), (1.2, 0.08, 0.4), tile="planks", color=(0.95, 0.75, 0.55), grain=0)
    for i in range(4):
        pt.pot(k, -1.05 + i * 0.3, -2.45, r=0.08, h=0.14)
    # sign
    k.beam(v3(1.6, 2.6, -2.35), v3(1.6, 2.6, -1.75), w=0.05, h=0.05)
    k.box((1.6, 2.32, -2.15), (0.05, 0.42, 0.42), tile="planks", color=(0.85, 0.65, 0.45), grain=1)
    pt.crate(k, 1.75, -1.0, s=0.34)
    pt.sack(k, 1.75, -0.55)
    return k


def medieval_barn(seed=109):
    """A big plank barn with a deep roof, open doors full of hay, bales stacked outside."""
    k = Kit(seed)
    w, d = 4.6, 3.6
    eave, ridge = 2.2, 4.3
    pt.plinth(k, w + 0.1, d + 0.1, h=0.22, stone=0.4)
    walls_box(k, w, d, 0.2, eave, front=pt.wall_planks, color=(0.95, 0.62, 0.48), board=0.3)
    for x in (-w / 2, -w / 6, w / 6, w / 2):
        for z in (-d / 2, d / 2):
            k.beam(v3(x, 0.15, z + (0.06 if z > 0 else -0.06)), v3(x, eave + 0.05, z + (0.06 if z > 0 else -0.06)),
                   w=0.16, h=0.16, color=pt.TIMBER_DARK)
    # big doorway with open doors and hay inside
    k.box((0, 1.05, -d / 2 - 0.02), (1.6, 1.7, 0.06), tile="planks", color=pt.DARK, vary=0)
    for s in (-1, 1):
        k.box((s * 1.25, 1.0, -d / 2 - 0.45), (0.8, 1.6, 0.07), R=rot_y(s * 70), tile="planks",
              color=(0.9, 0.55, 0.42), grain=1)
    k.box((0, 0.45, -d / 2 + 0.2), (1.3, 0.8, 0.5), tile="straw", color=(1.1, 0.95, 0.65), round_=0.8, seg=3, jitter=0.08)
    pt.gable_ends(k, w, d, eave, ridge, tile="planks", color=(0.92, 0.6, 0.47))
    pt.gable_roof(k, w, d, eave, ridge, over=0.4, over_end=0.35, kind="shingle", shingle_tile="shingle",
                  color=(0.85, 0.78, 0.72))
    k.box((0, ridge - 0.65, -d / 2 - 0.05), (0.5, 0.45, 0.06), tile="planks", color=pt.DARK, vary=0)
    for i, (x, z) in enumerate(((2.75, -1.2), (2.75, -0.4), (2.75, 0.4))):
        k.box((x, 0.28, z), (0.6, 0.55, 0.75), tile="straw", color=(1.1, 0.95, 0.65), round_=0.6, seg=2)
    k.box((2.75, 0.83, -0.8), (0.6, 0.55, 0.75), tile="straw", color=(1.05, 0.9, 0.62), round_=0.6, seg=2)
    k.cyl(v3(-2.9, 0.0, -1.3), v3(-2.9, 0.9, -1.3), 0.03, sides=6)
    return k


def medieval_smithy(seed=113):
    """The smithy: a stone forge with a tall chimney under an open-sided shed, anvil and quench tub."""
    k = Kit(seed)
    w, d = 3.2, 4.4
    eave, ridge = 2.0, 3.4
    for x in (-w / 2, w / 2):
        for z in (-d / 2, 0, d / 2):
            pt.post(k, x, z, eave + 0.05, r=0.11)
    k.beam(v3(-w / 2, eave, -d / 2), v3(-w / 2, eave, d / 2), w=0.14, h=0.16)
    k.beam(v3(w / 2, eave, -d / 2), v3(w / 2, eave, d / 2), w=0.14, h=0.16)
    pt.wall_plaster(k, (w / 2, d / 2), (-w / 2, d / 2), 0, eave, tile="stone", color=(0.92, 0.9, 0.88), posts=False)
    pt.gable_ends(k, w, d, eave, ridge, ridge_axis="z", tile="planks_old", color=(1, 1, 1))
    pt.gable_roof(k, w, d, eave, ridge, over=0.35, over_end=0.3, kind="shingle", ridge_axis="z",
                  shingle_tile="shingle", color=(0.8, 0.75, 0.72))
    # forge
    k.box((0.55, 0.45, 1.6), (1.2, 0.9, 0.9), tile="stone", color=(0.88, 0.86, 0.84), round_=0.35, seg=3)
    k.box((0.55, 0.92, 1.6), (0.8, 0.06, 0.55), tile="planks", color=(1.4, 0.55, 0.15), vary=0)
    k.anchor("fire", (0.55, 0.95, 1.6), 0.35)
    k.anchor("smoke", (0.55, 3.85, 1.85))
    k.box((0.55, 2.3, 1.85), (0.6, 2.8, 0.5), tile="stone", color=(0.85, 0.83, 0.82), round_=0.25, seg=2)
    k.box((0.55, 3.75, 1.85), (0.7, 0.12, 0.6), tile="stone", color=(0.8, 0.8, 0.8))
    # anvil, tub, rack
    stump(k, -0.4, 0.3, r=0.22, h=0.45)
    k.box((-0.4, 0.55, 0.3), (0.45, 0.14, 0.2), tile="iron", color=(0.75, 0.78, 0.85), round_=0.3, seg=2)
    k.box((-0.15, 0.57, 0.3), (0.18, 0.08, 0.12), tile="iron", color=(0.75, 0.78, 0.85), round_=0.4, seg=2)
    k.cyl(v3(-1.0, 0, -0.6), v3(-1.0, 0.45, -0.6), 0.3, sides=14, tile="planks", end_tile="stone", color=(0.85, 0.7, 0.55))
    k.box((-1.0, 0.44, -0.6), (0.5, 0.02, 0.5), tile="iron", color=(0.35, 0.45, 0.55), vary=0)
    for i in range(4):
        k.cyl(v3(-1.45, 0.3 + i * 0.32, 1.2), v3(-1.45, 0.3 + i * 0.32, 2.0), 0.02, sides=5, tile="iron",
              color=(0.7, 0.72, 0.8))
    pt.crate(k, 1.3, -1.6, s=0.36)
    pt.barrel(k, 1.35, -1.0)
    stone_heap(k, -1.2, -1.7, n=6, spread=0.25, size=0.15, tint=(0.4, 0.4, 0.45))
    return k


def medieval_bakery(seed=127):
    """A bakery: a cottage with a domed bread oven at its side and loaves cooling on a table."""
    k = Kit(seed)
    town_house(k, 2.7, 2.3, floors=1, roof="shingle_red", storey=1.75, roof_h=1.5, door_x=-0.4,
               plaster=(1.0, 0.92, 0.78), shutters=(0.55, 0.42, 0.3), chimney_at=(-0.8, 0.55))
    k.box((1.85, 0.5, 0.2), (1.1, 1.0, 1.2), tile="hide", color=(0.85, 0.6, 0.48), round_=0.95, seg=5)
    k.box((1.85, 0.25, -0.4), (0.4, 0.32, 0.12), tile="planks", color=(1.4, 0.6, 0.2), vary=0)
    k.anchor("fire", (1.85, 0.15, -0.45), 0.25)
    k.anchor("smoke", (1.95, 1.5, 0.45))
    k.cyl(v3(1.95, 0.85, 0.45), v3(1.95, 1.45, 0.45), 0.13, r2=0.11, sides=10, tile="stone", end_tile="stone")
    k.box((0.9, 0.62, -1.75), (1.0, 0.07, 0.45), tile="planks", color=(0.95, 0.75, 0.55), grain=0)
    for x in (0.5, 1.3):
        k.box((x, 0.3, -1.75), (0.07, 0.6, 0.38), tile="planks", color=pt.TIMBER_DARK)
    for i in range(5):
        k.box((0.55 + i * 0.18, 0.7, -1.75 + (i % 2) * 0.1), (0.15, 0.09, 0.24), tile="hide", color=(1.15, 0.8, 0.5),
              round_=0.9, seg=2)
    pt.sack(k, -1.75, -1.0)
    pt.sack(k, -1.75, -0.6, s=0.27)
    pt.firewood(k, 1.9, 1.2, w=0.7, yaw=0)
    return k


def medieval_tavern(seed=131):
    """The tavern: a wide two-storey house with a hanging sign, barrels and benches out front."""
    k = Kit(seed)
    town_house(k, 3.3, 2.5, floors=2, roof="shingle", storey=1.5, roof_h=1.6, door_x=0.0,
               plaster=(1.0, 0.9, 0.72), shutters=(0.38, 0.52, 0.32), chimney_at=(1.1, 0.5))
    k.beam(v3(-1.0, 2.75, -1.55), v3(-1.0, 2.75, -2.15), w=0.06, h=0.06)
    k.box((-1.0, 2.42, -2.0), (0.06, 0.48, 0.5), tile="planks", color=(0.9, 0.7, 0.45), grain=1)
    k.box((-1.03, 2.42, -2.0), (0.02, 0.22, 0.22), tile="cloth_ochre", color=(1.2, 1.05, 0.6), vary=0)
    for x in (-1.2, 1.1):
        pt.bench(k, x, -2.05)
    pt.barrel(k, 1.95, -1.3)
    pt.barrel(k, 1.95, -0.9, r=0.15, h=0.36)
    pt.barrel(k, 1.95, -1.1, y=0.42, lying=True, r=0.14, h=0.34, yaw=90)
    return k


def medieval_church(seed=137):
    """A small stone church: a long nave, a square tower with a spire, round-headed windows."""
    k = Kit(seed)
    w, d = 3.4, 5.6
    eave, ridge = 2.8, 4.6
    pt.plinth(k, w + 0.2, d + 0.2, h=0.3, stone=0.4)
    hw, hd = w / 2, d / 2
    for a, b in (((-hw, -hd), (hw, -hd)), ((hw, -hd), (hw, hd)), ((hw, hd), (-hw, hd)), ((-hw, hd), (-hw, -hd))):
        pt.wall_plaster(k, a, b, 0.28, eave, thick=0.26, tile="stone", color=(0.95, 0.88, 0.78), posts=False)
    for z in (-1.6, 0, 1.6):
        for s in (-1, 1):
            k.box((s * (hw + 0.1), 1.6, z), (0.12, 1.1, 0.4), tile="cloth_ochre", color=(0.75, 0.85, 1.2), vary=0)
            k.box((s * (hw + 0.14), 2.2, z), (0.1, 0.12, 0.52), tile="stone", color=(0.85, 0.85, 0.85), round_=0.5, seg=2)
            k.box((s * (hw + 0.12), 1.4, z + 0.55), (0.3, 2.3, 0.3), tile="stone", color=(0.9, 0.9, 0.9), round_=0.3, seg=2)
    pt.gable_ends(k, w, d, eave, ridge, ridge_axis="z", tile="stone", color=(1.0, 0.97, 0.93), beams=False, thick=0.26)
    pt.gable_roof(k, w, d, eave, ridge, over=0.25, over_end=0.2, kind="shingle", ridge_axis="z",
                  shingle_tile="shingle_red", color=(0.95, 0.88, 0.85))
    # tower at the front
    t = 1.7
    tz = -hd - t / 2 + 0.2
    for a, b in (((-t / 2, tz - t / 2), (t / 2, tz - t / 2)), ((t / 2, tz - t / 2), (t / 2, tz + t / 2)),
                 ((t / 2, tz + t / 2), (-t / 2, tz + t / 2)), ((-t / 2, tz + t / 2), (-t / 2, tz - t / 2))):
        pt.wall_plaster(k, a, b, 0.0, 6.0, thick=0.28, tile="stone", color=(0.92, 0.85, 0.75), posts=False)
    pt.door(k, (0, tz - t / 2 - 0.05), (0, -1), w=0.7, h=1.4, y0=0.0, color=(0.55, 0.32, 0.22))
    for s in (-1, 1):
        k.box((0, 5.0, tz + s * (t / 2 + 0.1)), (0.45, 0.8, 0.12), tile="planks", color=pt.DARK, vary=0)
        k.box((s * (t / 2 + 0.1), 5.0, tz), (0.12, 0.8, 0.45), tile="planks", color=pt.DARK, vary=0)
    k.box((0, 6.05, tz), (t + 0.25, 0.14, t + 0.25), tile="stone", color=(0.88, 0.88, 0.88))
    apex = v3(0, 8.4, tz)
    c = t / 2 + 0.08
    corners = [v3(-c, 6.1, tz - c), v3(c, 6.1, tz - c), v3(c, 6.1, tz + c), v3(-c, 6.1, tz + c)]
    for i in range(4):
        a, b = corners[i], corners[(i + 1) % 4]
        k.quad_slab(a, b, apex + (b - apex) * 0.02, apex + (a - apex) * 0.02, 0.1, "shingle_red", color=(0.95, 0.88, 0.85))
    k.cyl(apex - v3(0, 0.1, 0), apex + v3(0, 0.55, 0), 0.03, sides=6, tile="iron", end_tile="iron", color=(1.2, 0.95, 0.5))
    k.box((0, 8.82, tz), (0.28, 0.04, 0.04), tile="iron", color=(1.2, 0.95, 0.5), vary=0)
    return k


def medieval_mill(seed=139):
    """A windmill: a round stone tower, a wooden cap and four big sails."""
    k = Kit(seed)
    k.cyl(v3(0, -0.05, 0), v3(0, 4.2, 0), 1.5, r2=1.05, sides=20, tile="stone", end_tile="stone", rings=3,
          color=(0.95, 0.88, 0.78))
    for i in range(20):
        a = 2 * math.pi * i / 20
        k.box((math.sin(a) * 1.5, 0.15, math.cos(a) * 1.5), (0.4, 0.32, 0.26), R=rot_y(math.degrees(a)), tile="stone",
              color=(0.85, 0.85, 0.85), round_=0.6, seg=2, jitter=0.05)
    pt.door(k, (0, -1.47), (0, -1), w=0.7, h=1.3, y0=0.0, color=(0.55, 0.38, 0.28))
    pt.window(k, (0, -1.25), (0, -1), 2.4, w=0.4, h=0.42, shutters=(0.42, 0.56, 0.62))
    # cap
    k.box((0, 4.45, 0), (2.3, 0.6, 2.4), tile="planks", color=(0.9, 0.75, 0.6), round_=0.4, seg=3)
    k.box((0, 4.9, 0), (1.6, 0.5, 2.2), tile="shingle", color=(0.85, 0.8, 0.75), round_=0.85, seg=3)
    hub = v3(0, 4.5, -1.35)
    k.cyl(v3(0, 4.5, -0.9), hub, 0.12, sides=10, tile="log")
    for i in range(4):
        a = math.radians(i * 90 + 20)
        dirv = v3(math.cos(a), math.sin(a), 0)
        side = v3(-math.sin(a), math.cos(a), 0)
        tip = hub + dirv * 3.0
        k.beam(hub, tip, w=0.1, h=0.1)
        for j in range(6):
            t0 = 0.25 + j * 0.12
            p = hub + dirv * (3.0 * t0)
            k.beam(p, p + side * 0.62, w=0.04, h=0.04, up=(0, 0, 1))
        a0, a1 = hub + dirv * 0.75, tip
        k.quad_slab(a0 + side * 0.05 - v3(0, 0, 0.03), a1 + side * 0.05 - v3(0, 0, 0.03), a1 + side * 0.6 - v3(0, 0, 0.03),
                    a0 + side * 0.6 - v3(0, 0, 0.03), 0.015, "cloth_ochre", color=(1.25, 1.2, 1.1))
    for i in range(3):
        pt.sack(k, 1.5 + i * 0.3, -1.2 + (i % 2) * 0.2, s=0.3)
    return k


def medieval_market(seed=149):
    """A market square: a well in the middle and four striped stalls with goods."""
    k = Kit(seed)
    for i in range(10):
        a = 2 * math.pi * i / 10
        k.box((math.sin(a) * 0.55, 0.3, math.cos(a) * 0.55), (0.3, 0.6, 0.22), R=rot_y(math.degrees(a)), tile="stone",
              color=(0.95, 0.95, 0.95), round_=0.6, seg=2, jitter=0.04)
    k.cyl(v3(0, 0.55, 0), v3(0, 0.56, 0), 0.45, sides=12, tile="iron", end_tile="iron", color=(0.3, 0.4, 0.5), caps=True)
    for s in (-1, 1):
        k.beam(v3(s * 0.55, 0.5, 0), v3(s * 0.55, 1.6, 0), w=0.08, h=0.08)
    k.beam(v3(-0.65, 1.55, 0), v3(0.65, 1.55, 0), w=0.1, h=0.1)
    for s in (-1, 1):
        k.quad_slab(v3(-0.7, 1.55, s * 0.65), v3(0.7, 1.55, s * 0.65), v3(0.7, 1.95, 0), v3(-0.7, 1.95, 0), 0.06,
                    "shingle", color=(0.9, 0.82, 0.75)) if s < 0 else             k.quad_slab(v3(0.7, 1.55, s * 0.65), v3(-0.7, 1.55, s * 0.65), v3(-0.7, 1.95, 0), v3(0.7, 1.95, 0), 0.06,
                        "shingle", color=(0.9, 0.82, 0.75))
    goods = [[(0.85, 0.15, 0.2), (1.0, 0.6, 0.15)], [(0.95, 0.85, 0.5)], [(0.5, 0.75, 0.35), (0.9, 0.3, 0.2)],
             [(0.75, 0.55, 0.4)]]
    cloths = ["cloth_red", "cloth_ochre", "cloth_red", "cloth_ochre"]
    for i, (x, z, yaw) in enumerate(((-1.9, -1.7, 0), (1.9, -1.7, 0), (-1.9, 1.7, 180), (1.9, 1.7, 180))):
        k.place((x, 0, z), yaw)
        for px in (-0.6, 0.6):
            for pz in (-0.35, 0.35):
                pt.post(k, px, pz, 1.7 if pz > 0 else 1.45, r=0.04)
        k.quad_slab(v3(-0.75, 1.42, -0.55), v3(0.75, 1.42, -0.55), v3(0.75, 1.72, 0.45), v3(-0.75, 1.72, 0.45), 0.02,
                    cloths[i], color=(1, 1, 1))
        k.box((0, 0.7, -0.15), (1.3, 0.08, 0.55), tile="planks", color=(0.95, 0.75, 0.55), grain=0)
        k.box((0, 0.35, -0.15), (1.2, 0.6, 0.45), tile="planks", color=(0.75, 0.58, 0.42), grain=1)
        for j in range(6):
            c = goods[i][j % len(goods[i])]
            k.box((-0.45 + j * 0.18, 0.79, -0.15 + (j % 2) * 0.12), (0.13, 0.11, 0.13), tile="cloth_red", color=c,
                  round_=0.9, seg=2)
        pt.crate(k, 0.85, 0.25, s=0.3)
        k.place()
    pt.barrel(k, 0.0, -2.3)
    pt.sack(k, 0.4, 2.3)
    return k


# MARK: - Yards, shrines and the rest of the town

def drying_rack(seed=151):
    """Racks of fish and strips of meat drying over a smoky fire pit."""
    k = Kit(seed)
    for x in (-0.85, 0.85):
        for z in (-0.35, 0.35):
            k.cyl(v3(x, -0.05, z), v3(x * 0.98, 1.45, 0), 0.04, sides=7)
    for y in (1.35, 0.95):
        k.cyl(v3(-1.0, y, 0), v3(1.0, y, 0), 0.03, sides=6)
        for i in range(7):
            x = -0.7 + i * 0.23
            k.box((x, y - 0.17, 0), (0.08, 0.3, 0.13), tile="hide", color=(1.1, 0.75, 0.55) if y > 1 else (1.0, 0.9, 0.7),
                  round_=0.85, seg=2)
    stones_ring(k, 0.0, -0.85, r=0.26)
    k.anchor("fire", (0.0, 0.05, -0.85), 0.35)
    pt.firewood(k, 0.95, -0.75, w=0.5, rows=2)
    pt.pot(k, -0.9, -0.7)
    return k


def herb_garden(seed=157):
    """Raised beds of herbs and flowers inside a little wattle fence, a watering pot."""
    k = Kit(seed)
    greens = [(0.45, 0.72, 0.32), (0.35, 0.6, 0.35), (0.6, 0.78, 0.38)]
    blooms = [(0.75, 0.55, 1.0), (1.0, 0.9, 0.4), (1.0, 0.5, 0.45), (1.0, 1.0, 0.95)]
    for j, z in enumerate((-0.45, 0.45)):
        k.box((0, 0.1, z), (1.5, 0.2, 0.6), tile="planks", color=(0.8, 0.6, 0.45), grain=0)
        k.box((0, 0.2, z), (1.42, 0.04, 0.52), tile="hide", color=(0.55, 0.38, 0.28), vary=0)
        for i in range(7):
            x = -0.6 + i * 0.2
            g = greens[(i + j) % 3]
            k.box((x, 0.3, z + k.rng.uniform(-0.1, 0.1)), (0.2, 0.22, 0.2), tile="straw", color=g, round_=0.9, seg=2,
                  jitter=0.12)
            if (i + j) % 2 == 0:
                k.box((x, 0.44, z), (0.08, 0.08, 0.08), tile="cloth_red", color=blooms[(i + j) % 4], round_=0.9, seg=2)
    for (a, b) in (((-0.95, -0.95), (0.95, -0.95)), ((0.95, -0.95), (0.95, 0.95)), ((0.95, 0.95), (-0.95, 0.95)),
                   ((-0.95, 0.95), (-0.95, -0.95))):
        pt.wall_plaster(k, a, b, 0.0, 0.38, thick=0.05, tile="wattle", color=(1, 1, 1), posts=False)
    for (x, z) in ((-0.95, -0.95), (0.95, -0.95), (0.95, 0.95), (-0.95, 0.95)):
        pt.post(k, x, z, 0.48, r=0.04)
    pt.pot(k, 0.6, -1.15, color=(0.8, 0.5, 0.35))
    return k


def apiary(seed=163):
    """Straw bee skeps on a plank stand under a tiny roof, wildflowers around."""
    k = Kit(seed)
    k.box((0, 0.45, 0), (1.6, 0.06, 0.6), tile="planks", color=(0.95, 0.75, 0.55), grain=0)
    for x in (-0.7, 0.7):
        for z in (-0.25, 0.25):
            k.box((x, 0.22, z), (0.07, 0.45, 0.07), tile="planks", color=pt.TIMBER_DARK)
    for i, x in enumerate((-0.5, 0.0, 0.5)):
        for j in range(4):
            r = 0.22 - j * 0.045
            k.cyl(v3(x, 0.48 + j * 0.1, 0), v3(x, 0.58 + j * 0.1, 0), r, r2=r - 0.03, sides=12, tile="straw",
                  end_tile="straw", color=(1.15, 0.95, 0.6))
        k.box((x, 0.53, -0.21), (0.08, 0.06, 0.03), tile="planks", color=pt.DARK, vary=0)
    lean_to(k, -0.9, 0.9, 0.45, -0.45, 1.25, 1.05, tile="planks", color=(0.85, 0.72, 0.6))
    pt.flower_patch(k, -0.6, -0.85, FLOWERS, n=6, spread=0.25)
    pt.flower_patch(k, 0.7, -0.8, FLOWERS, n=5, spread=0.2)
    return k


def ancestor_shrine(seed=167):
    """Standing stones in a ring around a carved post, offerings and an ever-burning fire."""
    k = Kit(seed)
    r = 1.5
    for i in range(8):
        a = 2 * math.pi * (i + 0.5) / 8
        h = k.rng.uniform(0.9, 1.4)
        k.box((math.sin(a) * r, h / 2 - 0.05, math.cos(a) * r), (0.4, h, 0.28), R=rot_y(math.degrees(a)), tile="stone",
              color=(0.92, 0.9, 0.86), round_=0.55, seg=3, jitter=0.06)
    k.cyl(v3(0, 0, 0.35), v3(0, 2.1, 0.35), 0.16, r2=0.13, sides=10, color=(0.9, 0.75, 0.6))
    for j, c in enumerate([(1.1, 0.4, 0.3), (0.5, 0.7, 1.0), (1.1, 0.95, 0.5)]):
        k.cyl(v3(0, 0.6 + j * 0.45, 0.35), v3(0, 0.75 + j * 0.45, 0.35), 0.175, sides=10, tile="cloth_ochre",
              end_tile="cloth_ochre", color=c)
    k.box((0, 2.15, 0.35), (0.7, 0.12, 0.12), tile="log", color=(0.9, 0.75, 0.6))
    stones_ring(k, 0.0, -0.55, r=0.28)
    k.anchor("fire", (0.0, 0.05, -0.55), 0.35)
    for i in range(4):
        pt.pot(k, -0.5 + i * 0.32, 0.85, r=0.08, h=0.14, color=(0.95, 0.6, 0.42))
    pt.flower_patch(k, 0.6, 0.0, FLOWERS, n=5, spread=0.2)
    return k


def toolsmith(seed=173):
    """A toolsmith's hut: plaster walls, a forge under the eaves, tools hanging by the door."""
    k = Kit(seed)
    w, d = 1.8, 1.5
    base, eave, ridge = 0.2, 1.45, 2.35
    pt.plinth(k, w + 0.08, d + 0.08, h=base)
    walls_box(k, w, d, base, eave, color=(0.98, 0.92, 0.82))
    pt.door(k, (0.35, -d / 2), (0, -1), w=0.52, h=1.0, y0=base, color=(0.55, 0.38, 0.28))
    pt.gable_ends(k, w, d, eave, ridge, tile="plaster", color=(0.98, 0.92, 0.82))
    pt.gable_roof(k, w, d, eave, ridge, over=0.45, over_end=0.25, kind="shingle", shingle_tile="shingle_red",
                  color=(1.0, 0.92, 0.88))
    k.box((-0.45, 0.35, -1.05), (0.6, 0.55, 0.45), tile="stone", color=(0.88, 0.86, 0.84), round_=0.4, seg=3)
    k.box((-0.45, 0.63, -1.05), (0.4, 0.04, 0.3), tile="planks", color=(1.4, 0.6, 0.2), vary=0)
    k.anchor("fire", (-0.45, 0.62, -1.05), 0.25)
    stump(k, 0.4, -1.3, r=0.16, h=0.36)
    k.box((0.4, 0.43, -1.3), (0.3, 0.1, 0.13), tile="iron", color=(0.78, 0.8, 0.88), round_=0.3, seg=2)
    for i in range(3):
        x = 0.75 - i * 0.12
        k.cyl(v3(x, 0.55, -0.8), v3(x, 1.1, -0.8), 0.018, sides=5, tile="planks", color=(0.95, 0.75, 0.55))
        k.box((x, 1.08, -0.81), (0.1, 0.06, 0.03), tile="iron", color=(0.85, 0.85, 0.9), vary=0)
    pt.crate(k, -1.15, 0.3, s=0.3)
    return k


def mine(seed=179):
    """A mine mouth in a rocky hill: a timbered entrance, rails, an ore cart and a lantern."""
    k = Kit(seed)
    for (x, z, sx, sy, sz) in ((-0.55, 0.35, 1.3, 1.5, 1.3), (0.6, 0.45, 1.2, 1.3, 1.2), (0.0, 0.75, 1.6, 1.9, 1.0),
                               (-0.9, -0.3, 0.7, 0.7, 0.7), (0.95, -0.25, 0.6, 0.55, 0.6)):
        k.box((x, sy * 0.38, z), (sx, sy, sz), R=rot_y(k.rng.uniform(0, 40)), tile="stone", color=(0.85, 0.82, 0.78),
              round_=0.6, seg=3, jitter=0.1)
    k.box((0, 0.6, -0.1), (0.8, 1.2, 0.3), tile="planks", color=pt.DARK, vary=0)
    for x in (-0.45, 0.45):
        k.beam(v3(x, 0, -0.3), v3(x, 1.3, -0.3), w=0.14, h=0.14)
    k.beam(v3(-0.6, 1.3, -0.3), v3(0.6, 1.3, -0.3), w=0.16, h=0.16)
    for x in (-0.2, 0.2):
        k.beam(v3(x, 0.03, -0.3), v3(x, 0.03, -1.3), w=0.04, h=0.04, color=(0.7, 0.7, 0.75))
    for i in range(5):
        z = -0.4 - i * 0.22
        k.beam(v3(-0.3, 0.015, z), v3(0.3, 0.015, z), w=0.08, h=0.03, up=(0, 0, 1))
    k.box((0, 0.3, -1.0), (0.45, 0.3, 0.55), tile="planks", color=(0.75, 0.6, 0.45))
    for (x, z) in ((-0.2, -0.8), (0.2, -0.8), (-0.2, -1.2), (0.2, -1.2)):
        k.cyl(v3(x - 0.04 if x < 0 else x + 0.04, 0.1, z), v3(x - 0.08 if x < 0 else x + 0.08, 0.1, z), 0.08, sides=10,
              tile="iron", end_tile="iron", color=(0.5, 0.5, 0.55))
    stone_heap(k, 0.0, -1.0, n=5, spread=0.12, size=0.12, tint=(1.0, 0.65, 0.5))
    k.anchor("torch", (0.5, 1.25, -0.4), 0.35)
    return k


def community_circle(seed=181):
    """The gathering place: log benches in a ring around a big fire, a carved pole with banners."""
    k = Kit(seed)
    for i in range(6):
        a = 2 * math.pi * i / 6
        c = v3(math.sin(a) * 1.35, 0, math.cos(a) * 1.35)
        t = v3(math.cos(a), 0, -math.sin(a)) * 0.55
        k.cyl(c - t + v3(0, 0.2, 0), c + t + v3(0, 0.2, 0), 0.17, sides=10, wobble=0.04)
    stones_ring(k, 0.0, 0.0, r=0.48)
    k.anchor("fire", (0.0, 0.08, 0.0), 0.75)
    k.cyl(v3(1.6, -0.05, 1.3), v3(1.6, 2.6, 1.3), 0.09, r2=0.07, sides=9, color=(0.9, 0.75, 0.6))
    for j, c in enumerate(["cloth_red", "cloth_ochre"]):
        k.quad_slab(v3(1.62, 2.5 - j * 0.6, 1.3), v3(2.1, 2.45 - j * 0.6, 1.3), v3(2.05, 2.15 - j * 0.6, 1.3),
                    v3(1.62, 2.1 - j * 0.6, 1.3), 0.015, c, color=(1, 1, 1))
    pt.firewood(k, -1.7, 1.1, w=0.7, yaw=40)
    pt.pot(k, 1.0, -1.6)
    return k


def medieval_warehouse(seed=191):
    """A big storehouse: stone ground floor, a hoist beam over the loft door, crates and barrels."""
    k = Kit(seed)
    eave, ridge = town_house(k, 4.4, 3.6, floors=2, roof="shingle", storey=1.55, roof_h=1.8, door_x=0.0,
                             plaster=(0.95, 0.88, 0.76), shutters=(0.45, 0.4, 0.35), flowers=False, jetty=0.0)
    k.beam(v3(0, eave - 0.1, -1.6), v3(0, eave - 0.1, -2.5), w=0.14, h=0.14)
    k.cyl(v3(0, eave - 0.15, -2.4), v3(0, 1.4, -2.4), 0.012, sides=5, tile="straw", color=(1, 0.9, 0.7), caps=False)
    pt.crate(k, 0.0, -2.4, s=0.3, y=1.1)
    for i in range(3):
        pt.crate(k, -1.9 + i * 0.4, -2.35, s=0.36, yaw=k.rng.uniform(-10, 10))
    pt.crate(k, -1.7, -2.35, s=0.32, y=0.36)
    for i in range(3):
        pt.barrel(k, 1.4 + i * 0.38, -2.3)
    pt.sack(k, 2.55, -1.6)
    return k


def medieval_scriptorium(seed=193):
    """A scholars' house of pale stone with tall windows, a lantern by the door, a small bell cote."""
    k = Kit(seed)
    eave, ridge = town_house(k, 3.0, 2.6, floors=2, roof="shingle_red", storey=1.5, roof_h=1.7, door_x=0.0,
                             plaster=(0.98, 0.96, 0.9), shutters=(0.3, 0.42, 0.6), jetty=0.12)
    k.box((0, ridge + 0.45, 0), (0.45, 0.6, 0.45), tile="planks", color=(0.9, 0.78, 0.62))
    k.box((0, ridge + 0.45, -0.24), (0.25, 0.35, 0.04), tile="planks", color=pt.DARK, vary=0)
    for i in range(4):
        a = v3([(-0.3, 0, -0.3), (0.3, 0, -0.3), (0.3, 0, 0.3), (-0.3, 0, 0.3)][i]) + v3(0, ridge + 0.75, 0)
        k.quad_slab(a, v3([(0.3, 0, -0.3), (0.3, 0, 0.3), (-0.3, 0, 0.3), (-0.3, 0, -0.3)][i]) + v3(0, ridge + 0.75, 0),
                    v3(0, ridge + 1.15, 0), v3(0, ridge + 1.15, 0) + (a - v3(0, ridge + 1.15, 0)) * 0.02, 0.05,
                    "shingle_red", color=(0.95, 0.88, 0.85), face_up=True)
    k.anchor("lantern", (0.6, 1.5, -1.6), 0.3)
    pt.bench(k, -0.9, -1.7)
    return k


def medieval_carpenter(seed=197):
    """A carpenter's yard: a workshop with a wide open front, sawhorses, planks and logs."""
    k = Kit(seed)
    w, d = 3.0, 2.2
    eave, ridge = 1.9, 3.1
    pt.plinth(k, w + 0.1, d + 0.1, h=0.2)
    hw, hd = w / 2, d / 2
    for a, b in (((hw, -hd), (hw, hd)), ((hw, hd), (-hw, hd)), ((-hw, hd), (-hw, -hd))):
        pt.wall_planks(k, a, b, 0.18, eave, color=(0.98, 0.82, 0.65))
    for x in (-hw, 0, hw):
        pt.post(k, x, -hd, eave + 0.05, r=0.1)
    k.beam(v3(-hw, eave, -hd), v3(hw, eave, -hd), w=0.14, h=0.16)
    pt.gable_ends(k, w, d, eave, ridge, tile="planks", color=(0.95, 0.8, 0.62))
    pt.gable_roof(k, w, d, eave, ridge, over=0.35, over_end=0.3, kind="shingle", color=(0.95, 0.88, 0.8))
    k.box((0.2, 0.62, 0.4), (1.6, 0.08, 0.5), tile="planks", color=(0.95, 0.75, 0.55), grain=0)
    for x in (-0.5, 0.9):
        k.box((x, 0.3, 0.4), (0.08, 0.6, 0.42), tile="planks", color=pt.TIMBER_DARK)
    for x in (-0.6, 0.6):
        for z in (-1.75, -1.35):
            k.beam(v3(x - 0.15, 0, z), v3(x, 0.55, z), w=0.05, h=0.05)
            k.beam(v3(x + 0.15, 0, z), v3(x, 0.55, z), w=0.05, h=0.05)
        k.beam(v3(x, 0.55, -1.8), v3(x, 0.55, -1.3), w=0.06, h=0.06)
    k.box((0, 0.62, -1.55), (1.6, 0.05, 0.26), tile="planks", color=(1.05, 0.88, 0.68), grain=0)
    log_pile(k, 2.1, 0.0, length=1.4, rows=2, yaw=0, r=0.13)
    for i in range(4):
        k.box((-2.05, 0.04 + i * 0.05, 0.2), (0.24, 0.045, 1.6), tile="planks", color=(1.05, 0.88, 0.68), grain=2)
    return k


def medieval_bathhouse(seed=199):
    """A bathhouse: a plastered house with a steaming pool outside, a stove with a chimney."""
    k = Kit(seed)
    eave, ridge = town_house(k, 2.6, 2.2, floors=1, roof="shingle_red", storey=1.8, roof_h=1.5, door_x=0.45,
                             plaster=(0.96, 0.95, 0.92), shutters=(0.3, 0.55, 0.6), chimney_at=(0.7, 0.5))
    for i in range(12):
        a = 2 * math.pi * i / 12
        k.box((1.3 + math.sin(a) * 0.62, 0.1, -1.6 + math.cos(a) * 0.5), (0.28, 0.22, 0.2), R=rot_y(math.degrees(a)),
              tile="stone", color=(0.92, 0.9, 0.88), round_=0.6, seg=2, jitter=0.04)
    k.cyl(v3(1.3, 0.12, -1.6), v3(1.3, 0.13, -1.6), 0.55, sides=14, tile="iron", end_tile="iron",
          color=(0.45, 0.75, 0.85))
    k.anchor("smoke", (1.3, 0.25, -1.6))
    pt.bench(k, -1.0, -1.65)
    pt.barrel(k, -1.95, -0.9)
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
    "medieval_cottage": medieval_cottage,
    "medieval_house": medieval_house,
    "medieval_townhouse": medieval_townhouse,
    "medieval_barn": medieval_barn,
    "medieval_smithy": medieval_smithy,
    "medieval_bakery": medieval_bakery,
    "medieval_tavern": medieval_tavern,
    "medieval_church": medieval_church,
    "medieval_mill": medieval_mill,
    "medieval_market": medieval_market,
    "drying_rack": drying_rack,
    "herb_garden": herb_garden,
    "apiary": apiary,
    "ancestor_shrine": ancestor_shrine,
    "toolsmith": toolsmith,
    "mine": mine,
    "community_circle": community_circle,
    "medieval_warehouse": medieval_warehouse,
    "medieval_scriptorium": medieval_scriptorium,
    "medieval_carpenter": medieval_carpenter,
    "medieval_bathhouse": medieval_bathhouse,
}
