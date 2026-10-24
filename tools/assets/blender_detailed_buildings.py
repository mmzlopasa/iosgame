"""Blender (4.2): models the detailed, Foundation-style buildings and exports one FBX each.

Every building is assembled from the parts in bw_kit.py (planks, logs with sawn ends, single roof
shingles, ragged thatch courses, footing stones, doors, shutters, barrels, crates, log piles ...)
into one mesh with one material that uses the shared painted atlas (paint_building_atlas.py).
Sizes are game metres: one grid cell is 2 m, the door faces the street (+Y here, -Z in Unity).

blender -b --factory-startup -P tools/assets/blender_detailed_buildings.py -- <out dir> [--only a,b] [--preview dir]
Writes <out>/<id>.fbx and <out>/info.json ({id: {triangles, size}}); with --preview also renders
<preview>/<id>.png for a look before it goes into the game.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from bw_kit import Kit  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
TEX = os.path.join(ROOT, "unity", "Assets", "Art", "Textures")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def opt(flag, default=None):
    if flag in argv:
        i = argv.index(flag)
        v = argv[i + 1]
        del argv[i:i + 2]
        return v
    return default


ONLY = opt("--only")
PREVIEW = opt("--preview")
OUT = argv[0] if argv else os.path.join(HERE, ".cache", "detailed")


# MARK: - Buildings (one function per model id; k is a fresh Kit)

def house_walls(k, w, d, wall_h, kind="plaster", door_x=0.0, door_w=0.55, door_h=1.05, windows=((-0.6, 0.62),), z0=0.18):
    """Rectangular walls centred on the origin (w along x, d along y), door on the street side (+y).
    kind: plaster (timber frame + daub panels), logs (log cabin), planks (vertical boards)."""
    x0, x1, y0, y1 = -w / 2, w / 2, -d / 2, d / 2
    if kind == "logs":
        gaps = [(w / 2 + door_x - door_w / 2 - 0.02, w / 2 + door_x + door_w / 2 + 0.02, z0 + door_h)]
        k.log_wall((x0, y1), (x1, y1), z0, wall_h, r=0.085, skip=gaps)
        k.log_wall((x1, y0), (x1, y1), z0 + 0.085, wall_h - 0.06, r=0.085)
        k.log_wall((x0, y0), (x1, y0), z0, wall_h, r=0.085)
        k.log_wall((x0, y0), (x0, y1), z0 + 0.085, wall_h - 0.06, r=0.085)
    elif kind == "planks":
        skip = [(w / 2 + door_x - door_w / 2, w / 2 + door_x + door_w / 2)]
        k.plank_wall((x0, y1), (x1, y1), z0, wall_h, skip=skip)
        k.plank_wall((x1, y1), (x1, y0), z0, wall_h)
        k.plank_wall((x1, y0), (x0, y0), z0, wall_h)
        k.plank_wall((x0, y0), (x0, y1), z0, wall_h)
        # lintel above the door
        k.plank_wall((door_x - door_w / 2, y1), (door_x + door_w / 2, y1), z0 + door_h + 0.1, wall_h - door_h - 0.1)
        for cx, cy in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
            k.beam((cx, cy, 0.05), (cx, cy, z0 + wall_h + 0.05), w=0.12, tile="log")
    else:
        # Timber frame: corner posts, sill and top plates, braces, daub panels set back.
        inset = 0.03
        for (ax, ay), (bx, by) in (((x0, y1), (x1, y1)), ((x1, y1), (x1, y0)), ((x1, y0), (x0, y0)), ((x0, y0), (x0, y1))):
            L = math.hypot(bx - ax, by - ay)
            dx, dy = (bx - ax) / L, (by - ay) / L
            nx, ny = dy, -dx  # outward normal (walls run clockwise seen from above)
            front = abs(ay - y1) < 1e-4 and abs(by - y1) < 1e-4
            # panel
            pc = ((ax + bx) / 2 - nx * inset, (ay + by) / 2 - ny * inset, z0 + wall_h / 2)
            yaw = math.degrees(math.atan2(dy, dx))
            if front:
                # leave the door open: two panels either side + one above
                dl = door_x - door_w / 2 - 0.08
                dr = door_x + door_w / 2 + 0.08
                for s0, s1 in ((x0, dl), (dr, x1)):
                    if s1 - s0 > 0.05:
                        k.box(((s0 + s1) / 2, y1 - inset, z0 + wall_h / 2), (s1 - s0, 0.1, wall_h), (0, 0, 0), tile="plaster", bevel=0.02, grain=2)
                k.box((door_x, y1 - inset, z0 + door_h + 0.1 + (wall_h - door_h - 0.1) / 2), (dr - dl, 0.1, wall_h - door_h - 0.1), (0, 0, 0), tile="plaster", bevel=0.02)
            else:
                k.box(pc, (L, 0.1, wall_h), (0, 0, yaw), tile="plaster", bevel=0.02, grain=2)
            # sill and top plate
            k.beam((ax + nx * 0.02, ay + ny * 0.02, z0 + 0.04), (bx + nx * 0.02, by + ny * 0.02, z0 + 0.04), w=0.11, h=0.1, tile="planks")
            k.beam((ax + nx * 0.02, ay + ny * 0.02, z0 + wall_h - 0.04), (bx + nx * 0.02, by + ny * 0.02, z0 + wall_h - 0.04), w=0.11, h=0.1, tile="planks")
            # studs and a brace
            n = max(1, int(L / 0.75))
            for i in range(1, n):
                t = i / n
                sx, sy = ax + (bx - ax) * t, ay + (by - ay) * t
                if front and abs(sx - door_x) < door_w / 2 + 0.15:
                    continue
                k.beam((sx + nx * 0.02, sy + ny * 0.02, z0), (sx + nx * 0.02, sy + ny * 0.02, z0 + wall_h), w=0.08, h=0.07, tile="planks")
            if not front:
                t0, t1 = 0.06, min(0.45, 0.6 / L)
                k.beam((ax + (bx - ax) * t0 + nx * 0.03, ay + (by - ay) * t0 + ny * 0.03, z0 + 0.08),
                       (ax + (bx - ax) * (t0 + t1) + nx * 0.03, ay + (by - ay) * (t0 + t1) + ny * 0.03, z0 + wall_h - 0.08), w=0.07, h=0.06, tile="planks")
        for cx, cy in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
            k.beam((cx, cy, 0.0), (cx, cy, z0 + wall_h + 0.04), w=0.13, tile="planks")
    k.door(door_x, y1 + (0.06 if kind == "logs" else 0.02), z0, w=door_w, h=door_h, open_angle=-30)
    for wx, wz in windows:
        k.window(wx, y1 + (0.06 if kind == "logs" else 0.04), z0 + wz, facing=(0, 1))
    # threshold step
    k.rock((door_x, y1 + 0.28, 0.06), (door_w * 1.2, 0.3, 0.12), rot=(0, 0, 0), rough=0.08)


def daub_house(k):
    """A thatched cottage: timber frame with daub panels on a dry-stone footing, a shaggy thatch roof."""
    w, d, wall_h = 2.3, 1.75, 1.25
    k.stone_footing(-w / 2, -d / 2, w / 2, d / 2, h=0.24, size=0.26, gaps=[(0.25, d / 2, 0.3)])
    house_walls(k, w, d, wall_h, "plaster", door_x=0.25, windows=((-0.55, 0.6),))
    k.gable_roof(0, 0, 0.18 + wall_h, w, d, 1.05, kind="thatch", overhang=0.28, thatch_thick=0.26, cross_ends=False)
    # gable ends: planks under the thatch
    for e in (-1, 1):
        x = e * (w / 2 + 0.01)
        k.plank_wall((x, -d / 2), (x, d / 2), 0.18 + wall_h, 0.02, gable_rise=0.98, tile="planks_old")
    k.firewood(-0.85, d / 2 + 0.32, 0.0, w=0.6, h=0.42, depth=0.22)
    k.barrel((1.0, d / 2 + 0.35, 0.0))
    k.bench(-0.3, -d / 2 - 0.3, yaw=0)


def wood_camp(k):
    """The woodcutter's (Foundation's sawmill): an open log frame under a shingle roof with its gable to
    the street, a truss of logs with sawn ends, a big log pile, sawhorse, chopping block and firewood."""
    w, d, h = 1.6, 2.0, 1.4
    rise = 0.95
    posts = [(-w / 2, -d / 2), (w / 2, -d / 2), (-w / 2, d / 2), (w / 2, d / 2), (-w / 2, 0), (w / 2, 0)]
    for x, y in posts:
        k.box((x, y, 0.13), (0.3, 0.3, 0.26), tile="stone", bevel=0.04, jitter=0.012)
        k.log((x, y, 0.24), (x, y, h + 0.05), r=0.075, sides=8)
    # wall plates along the sides, tie beams across the gables (sawn ends sticking out)
    for x in (-w / 2, w / 2):
        k.log((x, -d / 2 - 0.16, h), (x, d / 2 + 0.16, h), r=0.075)
        for y in (-d / 2, 0, d / 2):
            for sy in (-1, 1):
                if (y == -d / 2 and sy < 0) or (y == d / 2 and sy > 0):
                    continue
                k.log((x, y, h - 0.4), (x, y + sy * 0.4, h - 0.02), r=0.04, sides=6)
    for y in (-d / 2, d / 2):
        k.log((-w / 2 - 0.16, y, h + 0.1), (w / 2 + 0.16, y, h + 0.1), r=0.07)
        # king post and struts of the truss
        k.log((0, y, h + 0.12), (0, y, h + rise - 0.05), r=0.05, sides=7)
        for sx in (-1, 1):
            k.log((sx * w * 0.32, y, h + 0.14), (0, y, h + rise * 0.62), r=0.035, sides=6)
    # back wall of boards and the back gable
    k.plank_wall((w / 2, -d / 2 - 0.03), (-w / 2, -d / 2 - 0.03), 0.2, h - 0.2, tile="planks_old")
    k.plank_wall((w / 2, -d / 2 - 0.03), (-w / 2, -d / 2 - 0.03), h + 0.14, 0.02, gable_rise=rise - 0.1, tile="planks")
    k.plank_wall((-w / 2 - 0.03, -d / 2), (-w / 2 - 0.03, 0.0), 0.2, h - 0.25, tile="planks_old")
    k.gable_roof(0, 0, h + 0.14, w, d, rise, kind="shingle", overhang=0.24, along_x=False)
    # a work bench under the roof with a plank on it
    k.box((0.15, -0.55, 0.42), (0.95, 0.42, 0.07), tile="planks", grain=0)
    for x in (-0.25, 0.55):
        k.box((x, -0.55, 0.2), (0.07, 0.36, 0.4), tile="planks")
    k.box((0.1, -0.5, 0.5), (0.8, 0.18, 0.04), tile="planks_old", grain=0, rot=(0, 0, 4))
    # a big pile of logs by the side, sawhorse with a log, chopping block, firewood
    k.log_pile(w / 2 + 0.55, 0.05, 0.0, length=1.9, rows=3, r=0.11, along_x=False)
    k.sawhorse(-0.2, d / 2 + 0.45, yaw=0, L=0.75, h=0.42)
    k.log((-0.75, d / 2 + 0.45, 0.52), (0.4, d / 2 + 0.5, 0.52), r=0.09)
    k.stump(-w / 2 - 0.35, d / 2 + 0.1, r=0.15, h=0.3)
    k.firewood(-0.2, -d / 2 - 0.28, 0.0, w=1.0, h=0.42, depth=0.24)


def granary(k):
    """The storehouse on stone stilts (Foundation's granary): plank walls, a shingle roof with its gable to the
    street, a red cloth awning over the loading door, a ladder, crates, a barrel and sacks."""
    w, d = 1.45, 1.75
    lift = 0.62
    for x in (-w / 2 + 0.12, w / 2 - 0.12):
        for y in (-d / 2 + 0.12, d / 2 - 0.12):
            k.box((x, y, lift / 2), (0.28, 0.28, lift), tile="stone", bevel=0.04, jitter=0.012)
            k.box((x, y, lift + 0.03), (0.34, 0.34, 0.06), tile="planks", bevel=0.01)
    for x in (-w / 2 + 0.12, w / 2 - 0.12):
        k.beam((x, -d / 2 - 0.12, lift + 0.1), (x, d / 2 + 0.12, lift + 0.1), w=0.12, tile="planks")
    for i in range(9):
        y = -d / 2 - 0.05 + (i + 0.5) * (d + 0.4) / 9
        k.box((0, y, lift + 0.2), (w + 0.2, (d + 0.4) / 9 * 0.94, 0.05), tile="planks_old", grain=0)
    wall_h = 1.0
    z0 = lift + 0.23
    door = (w / 2 - 0.42, w / 2 + 0.42)
    k.plank_wall((-w / 2, d / 2), (w / 2, d / 2), z0, wall_h, skip=[door], tile="planks")
    k.plank_wall((-w / 2, d / 2), (w / 2, d / 2), z0 + wall_h, 0.02, gable_rise=0.92, tile="planks")
    k.plank_wall((w / 2, d / 2), (w / 2, -d / 2), z0, wall_h, tile="planks")
    k.plank_wall((w / 2, -d / 2), (-w / 2, -d / 2), z0, wall_h, tile="planks")
    k.plank_wall((w / 2, -d / 2), (-w / 2, -d / 2), z0 + wall_h, 0.02, gable_rise=0.92, tile="planks")
    k.plank_wall((-w / 2, -d / 2), (-w / 2, d / 2), z0, wall_h, tile="planks")
    k.box((0.0, d / 2 - 0.05, z0 + wall_h * 0.45), (0.8, 0.02, wall_h * 0.9), tile="iron", bevel=0, sub=(0.1, 0.1, 0.2, 0.2))
    for x in (-w / 2, w / 2):
        for y in (-d / 2, d / 2):
            k.beam((x, y, z0 - 0.04), (x, y, z0 + wall_h + 0.05), w=0.11, tile="log")
    k.beam((-0.45, d / 2 + 0.03, z0 + wall_h * 0.92), (0.45, d / 2 + 0.03, z0 + wall_h * 0.92), w=0.09, tile="log")
    k.gable_roof(0, 0, z0 + wall_h, w, d, 0.95, kind="shingle", overhang=0.24, along_x=False)
    # the red cloth awning over the loading door, held out on two poles
    top = z0 + wall_h * 0.92
    k.awning((-0.46, d / 2 + 0.04, top), (0.46, d / 2 + 0.04, top), depth=0.55, drop=0.38, tile="cloth_red", poles=False, sag=0.06)
    for x in (-0.46, 0.46):
        k.log((x, d / 2 + 0.02, top + 0.05), (x * 1.04, d / 2 + 0.62, top - 0.42), r=0.032, sides=6)
    # ladder, crates, a barrel and sacks
    k.ladder((-w / 2 - 0.05, d / 2 + 0.7, 0.0), (-w / 2 + 0.2, d / 2 + 0.12, lift + 0.25), w=0.3)
    k.barrel((w / 2 + 0.2, d / 2 + 0.3, 0.0))
    k.crate((w / 2 + 0.05, d / 2 + 0.72, 0.0), s=0.32, yaw=12)
    k.crate((w / 2 + 0.1, d / 2 + 0.72, 0.33), s=0.26, yaw=40)
    k.sack((0.1, d / 2 + 0.75, 0.0))
    k.sack((-0.15, d / 2 + 0.9, 0.0), s=0.26)


def shelter(k):
    """The first hut: an A-frame of poles thatched down to the ground, its wattle gable to the street with a hide door."""
    w, d, rise = 1.7, 1.9, 1.75
    # pole frame: crossed rafters at both gables and the middle
    for y in (-d / 2, 0.0, d / 2):
        for sgn in (-1, 1):
            k.log((sgn * (w / 2 + 0.1), y, 0.0), (-sgn * 0.22, y, rise + 0.32), r=0.05, sides=7)
    k.gable_roof(0, 0, 0.12, w, d - 0.1, rise - 0.1, kind="thatch", overhang=0.12, thatch_thick=0.2, cross_ends=False, along_x=False)
    for y in (-d / 2 - 0.04, d / 2 + 0.04):
        k.plank_wall((-w / 2 + 0.1, y), (w / 2 - 0.1, y), 0.05, 0.05, gable_rise=rise - 0.15, tile="wattle", board=(0.3, 0.4),
                     skip=[(w / 2 - 0.1 - 0.28, w / 2 - 0.1 + 0.28)] if y > 0 else None)
    # door opening: a hide curtain hung from a pole
    k.box((0, d / 2 + 0.02, 0.5), (0.5, 0.02, 1.0), tile="iron", bevel=0, sub=(0.1, 0.1, 0.2, 0.2))
    k.quad_strip([(-0.28, d / 2 + 0.08, 0.05), (0.05, d / 2 + 0.12, 0.05)], [(-0.27, d / 2 + 0.07, 1.02), (0.12, d / 2 + 0.07, 1.02)], "hide")
    k.log((-0.35, d / 2 + 0.07, 1.04), (0.35, d / 2 + 0.07, 1.04), r=0.025, sides=5)
    # outside: firewood, chopping stump, a basket, a stone ring with ashes
    k.firewood(-w / 2 - 0.3, 0.0, 0.0, w=0.22, h=0.36, depth=0.8)
    k.stump(0.6, d / 2 + 0.45, r=0.13, h=0.28)
    k.basket((-0.55, d / 2 + 0.4, 0.0))
    for i in range(7):
        ang = i / 7 * math.tau
        k.rock((w / 2 + 0.45 + math.cos(ang) * 0.22, -0.4 + math.sin(ang) * 0.22, 0.04), (0.12, 0.1, 0.09), rough=0.1)
    k.box((w / 2 + 0.45, -0.4, 0.01), (0.3, 0.3, 0.02), tile="iron", bevel=0.0)


def woven_shelter(k):
    """A round house: a wattle wall woven between stakes on a stone footing, a conical thatch roof in courses."""
    r, wall = 0.85, 1.05
    for i in range(16):
        ang = i / 16 * math.tau
        k.rock((math.cos(ang) * (r + 0.03), math.sin(ang) * (r + 0.03), 0.07), (0.3, 0.24, 0.17), rough=0.12)
    k.log((0, 0, 0.1), (0, 0, wall + 0.1), r=r, sides=18, tile="wattle", ends=False, taper=1.0, wobble=0.02, uv_around=False)
    for i in range(12):
        ang = (i + 0.5) / 12 * math.tau
        if abs(math.atan2(math.sin(ang - math.pi / 2), math.cos(ang - math.pi / 2))) < 0.35:
            continue
        p = Vector((math.cos(ang) * (r + 0.02), math.sin(ang) * (r + 0.02), 0))
        k.log(p, p + Vector((0, 0, wall + 0.12)), r=0.035, sides=6)
    k.cone_thatch(0, 0, wall + 0.02, r + 0.4, 1.45, courses=5)
    k.door(0, r + 0.04, 0.12, w=0.5, h=0.92, open_angle=-75)
    k.basket((0.65, r + 0.35, 0.0))
    k.pot((-0.6, r + 0.3, 0.0))
    k.pot((-0.78, r + 0.18, 0.0), h=0.22, r=0.1)
    k.firewood(-0.95, -0.2, 0.0, w=0.22, h=0.4, depth=0.7) 
    k.bench(0.0, -r - 0.4, yaw=0, L=0.8)


def longhouse(k):
    """The longhouse: a log cabin with sawn log ends at the corners, a long thatch roof, a bench and stores."""
    w, d, wall_h = 3.5, 2.1, 1.2
    k.stone_footing(-w / 2, -d / 2, w / 2, d / 2, h=0.2, size=0.3, gaps=[(0.6, d / 2, 0.35)])
    house_walls(k, w, d, wall_h, "logs", door_x=0.6, door_w=0.6, door_h=1.05, windows=((-0.9, 0.65),), z0=0.15)
    k.gable_roof(0, 0, 0.15 + wall_h, w + 0.1, d + 0.1, 1.25, kind="thatch", overhang=0.3, thatch_thick=0.28, cross_ends=False)
    for e in (-1, 1):
        x = e * (w / 2 + 0.05)
        k.plank_wall((x, -d / 2), (x, d / 2), 0.15 + wall_h + 0.02, 0.02, gable_rise=1.18, tile="planks_old")
    k.bench(-0.4, d / 2 + 0.38, L=1.0)
    k.firewood(-1.35, d / 2 + 0.3, 0.0, w=0.9, h=0.5, depth=0.24)
    k.barrel((1.35, d / 2 + 0.35, 0.0))
    k.barrel((1.62, d / 2 + 0.2, 0.0), h=0.36, r=0.13)
    k.hay(-w / 2 - 0.35, -0.3, s=0.55)
    k.crate((w / 2 + 0.3, -0.4, 0.0), s=0.34, yaw=20)


def elders_lodge(k):
    """The elders' lodge: a stone lower course, timber-framed daub above, a steep shingle roof and banners."""
    w, d, wall_h = 2.15, 1.7, 1.35
    k.stone_footing(-w / 2, -d / 2, w / 2, d / 2, h=0.36, size=0.3, gaps=[(0.0, d / 2, 0.35)])
    house_walls(k, w, d, wall_h, "plaster", door_x=0.0, door_w=0.6, door_h=1.1, windows=((-0.65, 0.7), (0.65, 0.7)), z0=0.25)
    k.gable_roof(0, 0, 0.25 + wall_h, w, d, 1.15, kind="shingle", overhang=0.26)
    for e in (-1, 1):
        x = e * (w / 2 + 0.01)
        k.plank_wall((x, -d / 2), (x, d / 2), 0.25 + wall_h + 0.02, 0.02, gable_rise=1.1, tile="planks")
    for x in (-0.55, 0.55):
        k.banner(x, d / 2 + 0.45, h=1.9, tile="cloth_red")
    k.bench(0.0, -d / 2 - 0.35, L=1.0)


def healers_hut(k):
    """The healer's: a small daub cottage under thatch, herbs drying on a rack, pots of salve."""
    w, d, wall_h = 1.85, 1.5, 1.15
    k.stone_footing(-w / 2, -d / 2, w / 2, d / 2, h=0.22, size=0.25, gaps=[(-0.25, d / 2, 0.3)])
    house_walls(k, w, d, wall_h, "plaster", door_x=-0.25, door_w=0.52, door_h=0.98, windows=((0.45, 0.55),), z0=0.17)
    k.gable_roof(0, 0, 0.17 + wall_h, w, d, 0.95, kind="thatch", overhang=0.26, thatch_thick=0.22, cross_ends=False)
    for e in (-1, 1):
        x = e * (w / 2 + 0.01)
        k.plank_wall((x, -d / 2), (x, d / 2), 0.17 + wall_h + 0.02, 0.02, gable_rise=0.88, tile="planks_old")
    k.pole_frame(-w / 2 - 0.35, 0.35, 0.9, 1.2, yaw=90, tile_hang="straw", n_hang=5)
    k.pot((0.75, d / 2 + 0.35, 0.0))
    k.pot((0.95, d / 2 + 0.2, 0.0), h=0.22, r=0.1)
    k.basket((0.45, d / 2 + 0.5, 0.0))
    k.barrel((w / 2 + 0.2, -0.3, 0.0))


def storage_pit(k):
    """The store: an open-fronted plank shed under a lean-to shingle roof, packed with crates, barrels and sacks."""
    w, d = 2.0, 1.5
    hb, hf = 1.25, 1.6
    for x in (-w / 2, w / 2):
        for y, h in ((-d / 2, hb), (d / 2, hf)):
            k.box((x, y, 0.1), (0.24, 0.24, 0.2), tile="stone", bevel=0.03, jitter=0.01)
            k.log((x, y, 0.18), (x, y, h), r=0.07, sides=8)
    k.ground_patch(-w / 2, -d / 2, w / 2, d / 2, tile="planks_old")
    k.plank_wall((w / 2, -d / 2 - 0.03), (-w / 2, -d / 2 - 0.03), 0.06, hb - 0.08, tile="planks")
    k.plank_wall((-w / 2 - 0.03, -d / 2), (-w / 2 - 0.03, d / 2), 0.06, hb - 0.08, gable_rise=0.0, tile="planks")
    k.plank_wall((w / 2 + 0.03, d / 2), (w / 2 + 0.03, -d / 2), 0.06, hb - 0.08, tile="planks")
    for x in (-w / 2, w / 2):
        k.log((x, -d / 2 - 0.15, hb + 0.02), (x, d / 2 + 0.15, hf + 0.02), r=0.06)
    k.log((-w / 2 - 0.15, d / 2, hf - 0.02), (w / 2 + 0.15, d / 2, hf - 0.02), r=0.07)
    k.lean_to(-w / 2, w / 2, d / 2 + 0.05, -d / 2 - 0.05, hf + 0.1, hb + 0.1, overhang=0.22)
    k.barrel((-0.6, -0.35, 0.06))
    k.barrel((-0.3, -0.45, 0.06))
    k.barrel((-0.45, -0.4, 0.5), h=0.36, r=0.13)
    k.crate((0.2, -0.4, 0.06), s=0.38, yaw=4)
    k.crate((0.6, -0.42, 0.06), s=0.36, yaw=-6)
    k.crate((0.4, -0.4, 0.44), s=0.32, yaw=10)
    k.sack((0.55, 0.15, 0.06))
    k.sack((0.75, 0.0, 0.06), s=0.27)
    k.sack((-0.65, 0.2, 0.06), s=0.28)
    k.basket((0.0, d / 2 + 0.3, 0.0))
    k.crate((-0.8, d / 2 + 0.35, 0.0), s=0.32, yaw=25)


def foraging_camp(k):
    """The gatherers' hut: a little plank shed, an ochre awning over a table of baskets, berries and herbs drying."""
    w, d = 1.3, 1.0
    k.stone_footing(-w / 2, -d / 2 - 0.4, w / 2, d / 2 - 0.4, h=0.16, size=0.22)
    yb = -0.4
    k.plank_wall((-w / 2, yb + d / 2), (w / 2, yb + d / 2), 0.1, 1.05, skip=[(0.35, 0.8)], tile="planks_old")
    k.plank_wall((w / 2, yb + d / 2), (w / 2, yb - d / 2), 0.1, 1.05, tile="planks_old")
    k.plank_wall((w / 2, yb - d / 2), (-w / 2, yb - d / 2), 0.1, 1.05, tile="planks_old")
    k.plank_wall((-w / 2, yb - d / 2), (-w / 2, yb + d / 2), 0.1, 1.05, tile="planks_old")
    for x in (-w / 2, w / 2):
        for y in (yb - d / 2, yb + d / 2):
            k.beam((x, y, 0.05), (x, y, 1.18), w=0.1, tile="log")
    k.gable_roof(0, yb, 1.15, w, d, 0.7, kind="thatch", overhang=0.22, thatch_thick=0.18, cross_ends=False)
    for e in (-1, 1):
        k.plank_wall((e * (w / 2 + 0.01), yb - d / 2), (e * (w / 2 + 0.01), yb + d / 2), 1.17, 0.02, gable_rise=0.66, tile="planks")
    k.door(0.15, yb + d / 2 + 0.02, 0.1, w=0.45, h=0.9, open_angle=-30)
    # awning over the sorting table
    k.awning((-1.0, 0.55, 1.15), (0.2, 0.55, 1.15), depth=0.6, drop=0.2, tile="cloth_ochre", poles=True)
    k.box((-0.4, 0.75, 0.5), (0.9, 0.42, 0.05), tile="planks", grain=0)
    for x in (-0.78, -0.02):
        k.box((x, 0.75, 0.25), (0.06, 0.36, 0.5), tile="planks")
    for x in (-0.65, -0.35, -0.1):
        k.basket((x, 0.75, 0.53), h=0.14, r=0.11, full="cloth_red" if x < -0.5 else "straw")
    k.basket((0.55, 0.65, 0.0))
    k.basket((0.75, 0.35, 0.0), h=0.18, r=0.14, full="cloth_red")
    k.pole_frame(0.85, -0.35, 0.7, 1.05, yaw=90, tile_hang="straw", n_hang=4)


def toolmaker(k):
    """The toolmaker's workshop: a half-open timber shed, a workbench with tools, a grindstone and crates."""
    w, d, h = 1.8, 1.3, 1.35
    for x in (-w / 2, w / 2):
        for y in (-d / 2, d / 2):
            k.box((x, y, 0.1), (0.24, 0.24, 0.2), tile="stone", bevel=0.03, jitter=0.01)
            k.log((x, y, 0.18), (x, y, h + 0.04), r=0.07, sides=8)
    for y in (-d / 2, d / 2):
        k.log((-w / 2 - 0.14, y, h), (w / 2 + 0.14, y, h), r=0.065)
    k.plank_wall((w / 2, -d / 2 - 0.03), (-w / 2, -d / 2 - 0.03), 0.08, h - 0.08, tile="planks")
    k.plank_wall((-w / 2 - 0.03, -d / 2), (-w / 2 - 0.03, d / 2), 0.08, h - 0.08, tile="planks")
    k.gable_roof(0, 0, h + 0.06, w, d, 0.85, kind="shingle", overhang=0.22, tile="shingle_red")
    for e in (-1, 1):
        k.plank_wall((e * (w / 2 + 0.02), -d / 2), (e * (w / 2 + 0.02), d / 2), h + 0.08, 0.02, gable_rise=0.8, tile="planks_old")
    k.ground_patch(-w / 2, -d / 2, w / 2, d / 2)
    # bench along the back wall with tools
    k.box((0.1, -d / 2 + 0.28, 0.5), (1.2, 0.42, 0.06), tile="planks", grain=0)
    for x in (-0.45, 0.65):
        k.box((x, -d / 2 + 0.28, 0.26), (0.07, 0.38, 0.48), tile="planks")
    for i, x in enumerate((-0.3, 0.0, 0.3)):
        k.log((x, -d / 2 + 0.2, 0.55), (x + 0.05, -d / 2 + 0.42, 0.55), r=0.018, sides=5, tile="planks")
        k.box((x, -d / 2 + 0.18, 0.56), (0.1, 0.03, 0.06), tile="iron", bevel=0.005)
    # tools hanging on the wall
    for i in range(4):
        x = -0.5 + i * 0.3
        k.log((x, -d / 2 + 0.02, 0.75), (x + 0.02, -d / 2 + 0.03, 1.15), r=0.018, sides=5, tile="planks")
        k.box((x + 0.02, -d / 2 + 0.04, 1.12), (0.12, 0.02, 0.06), tile="iron", bevel=0.005)
    k.grindstone(0.6, d / 2 + 0.35)
    k.crate((-0.65, d / 2 + 0.3, 0.0), s=0.34, yaw=15)
    k.log_pile(-w / 2 - 0.3, 0.1, 0.0, length=1.0, rows=2, r=0.07, along_x=False)
    k.barrel((w / 2 + 0.25, -0.2, 0.0))


def stone_site(k):
    """The quarry: a broken outcrop with cut faces, blocks stacked to dry, rubble, a hand cart and tools."""
    for pos, size in (((-0.4, -0.45, 0.35), (1.2, 0.95, 1.0)), ((0.4, -0.6, 0.25), (0.85, 0.75, 0.7)), ((-0.85, 0.15, 0.18), (0.6, 0.55, 0.5)),
                      ((0.05, -0.95, 0.3), (0.9, 0.5, 0.8))):
        k.rock(pos, size, subdiv=2, rough=0.16, faceted=True)
    # a wedge split off with the cut face up
    k.box((0.35, 0.05, 0.12), (0.55, 0.42, 0.24), (0, 0, 18), tile="stone", bevel=0.03, jitter=0.02)
    k.cut_blocks(0.55, 0.45, n=5, s=0.25)
    k.stone_pile(-0.3, 0.45, n=8, size=0.14, spread=0.22)
    k.cart(-0.85, 0.9, yaw=20, load="stone")
    k.crate((0.0, 0.95, 0.0), s=0.3, yaw=20)
    # wooden lifting frame
    for x in (-0.15, 0.55):
        k.log((x, -0.15, 0.0), (0.2, -0.15, 1.7), r=0.04, sides=6)
    k.log((0.2, -0.15, 1.68), (0.2, 0.25, 1.62), r=0.035, sides=6)
    k.log((0.2, 0.25, 1.6), (0.2, 0.26, 0.6), r=0.008, sides=4, tile="straw")
    for i in range(2):
        x = 0.95 + i * 0.12
        k.log((x, 0.6, 0.0), (x + 0.05, 0.7, 0.9), r=0.02, sides=5, tile="planks")
        k.box((x + 0.05, 0.7, 0.88), (0.18, 0.03, 0.05), tile="iron", bevel=0.005)


def hunters_lodge(k):
    """The hunters' lodge: a little log cabin under shingles, pelts stretched on frames, a rack of spears."""
    w, d, wall_h = 1.7, 1.35, 1.1
    k.stone_footing(-w / 2, -d / 2 - 0.3, w / 2, d / 2 - 0.3, h=0.18, size=0.26, gaps=[(0.2, d / 2 - 0.3, 0.3)])
    k.log_wall((-w / 2, d / 2 - 0.3), (w / 2, d / 2 - 0.3), 0.12, wall_h, r=0.08, skip=[(w / 2 + 0.2 - 0.3, w / 2 + 0.2 + 0.3, 0.12 + 0.95)])
    k.log_wall((w / 2, -d / 2 - 0.3), (w / 2, d / 2 - 0.3), 0.2, wall_h - 0.08, r=0.08)
    k.log_wall((-w / 2, -d / 2 - 0.3), (w / 2, -d / 2 - 0.3), 0.12, wall_h, r=0.08)
    k.log_wall((-w / 2, -d / 2 - 0.3), (-w / 2, d / 2 - 0.3), 0.2, wall_h - 0.08, r=0.08)
    k.door(0.2, d / 2 - 0.24, 0.12, w=0.5, h=0.92, open_angle=-30)
    k.gable_roof(0, -0.3, 0.12 + wall_h, w, d, 0.85, kind="shingle", overhang=0.24)
    for e in (-1, 1):
        k.plank_wall((e * (w / 2 + 0.03), -d / 2 - 0.3), (e * (w / 2 + 0.03), d / 2 - 0.3), 0.12 + wall_h + 0.02, 0.02, gable_rise=0.8, tile="planks_old")
    # pelts on frames
    for x, yaw in ((-0.55, 10), (0.35, -15)):
        m = k.matrix((x, 0.75, 0), (0, 0, yaw))
        P = lambda a, b, c, m=m: tuple(m @ Vector((a, b, c)))
        k.log(P(-0.3, 0, 0), P(-0.28, 0, 1.0), r=0.03, sides=5)
        k.log(P(0.3, 0, 0), P(0.28, 0, 1.0), r=0.03, sides=5)
        k.log(P(-0.36, 0, 0.95), P(0.36, 0, 0.95), r=0.025, sides=5)
        k.log(P(-0.36, 0, 0.25), P(0.36, 0, 0.25), r=0.025, sides=5)
        k.quad_strip([P(-0.22, 0.01, 0.32), P(0.0, 0.01, 0.27), P(0.22, 0.01, 0.32)], [P(-0.24, 0.01, 0.88), P(0.0, 0.01, 0.92), P(0.24, 0.01, 0.88)], "hide")
    # spears leaning on a rail
    k.log((w / 2 + 0.15, -0.9, 0.7), (w / 2 + 0.15, -0.1, 0.7), r=0.025, sides=5)
    for i in range(4):
        y = -0.8 + i * 0.18
        k.log((w / 2 + 0.4, y, 0.0), (w / 2 + 0.1, y + 0.05, 1.45), r=0.016, sides=4, tile="planks")
        k.box((w / 2 + 0.08, y + 0.05, 1.5), (0.03, 0.03, 0.12), (0, -12, 0), tile="iron", bevel=0.004)
    k.firewood(-w / 2 - 0.22, -0.3, 0.0, w=0.24, h=0.45, depth=0.9)
    k.basket((0.8, 0.45, 0.0), full="hide")


def smokehouse(k):
    """The smokehouse: a tall tight plank shed with a stone chimney, fish hanging on racks outside."""
    w, d, wall_h = 1.4, 1.3, 1.45
    k.stone_footing(-w / 2, -d / 2, w / 2, d / 2, h=0.2, size=0.24, gaps=[(0.0, d / 2, 0.28)])
    house_walls(k, w, d, wall_h, "planks", door_x=0.0, door_w=0.5, door_h=1.0, windows=(), z0=0.15)
    k.gable_roof(0, 0, 0.15 + wall_h, w, d, 0.8, kind="shingle", overhang=0.2, tile="shingle")
    for e in (-1, 1):
        k.plank_wall((e * (w / 2 + 0.01), -d / 2), (e * (w / 2 + 0.01), d / 2), 0.15 + wall_h + 0.02, 0.02, gable_rise=0.76, tile="planks_old")
    # stone chimney at the back corner
    cx, cy = -w / 2 + 0.25, -d / 2 - 0.12
    for i in range(10):
        z = 0.12 + i * 0.26
        k.box((cx, cy, z), (0.44 - i * 0.012, 0.44 - i * 0.012, 0.25), (0, 0, k.rng.uniform(-4, 4)), tile="stone", bevel=0.04, jitter=0.015)
    k.box((cx, cy, 0.12 + 10 * 0.26 - 0.08), (0.3, 0.3, 0.06), tile="iron", bevel=0)
    # fish racks
    for x in (w / 2 + 0.4,):
        m = k.matrix((x, 0.15, 0), (0, 0, 90))
        P = lambda a, b, c, m=m: tuple(m @ Vector((a, b, c)))
        k.log(P(-0.45, 0, 0), P(-0.45, 0, 1.15), r=0.035, sides=6)
        k.log(P(0.45, 0, 0), P(0.45, 0, 1.15), r=0.035, sides=6)
        for z in (1.1, 0.75):
            k.log(P(-0.5, 0, z), P(0.5, 0, z), r=0.02, sides=5)
            for i in range(6):
                fx = -0.35 + i * 0.14
                k.lathe(P(fx, 0, z - 0.3), [(0.005, 0), (0.035, 0.07), (0.04, 0.16), (0.02, 0.26), (0.005, 0.29)], "iron", sides=6,
                        sub=(0.1, 0.1, 0.3, 0.3))
    k.barrel((-w / 2 - 0.25, 0.3, 0.0))
    k.firewood(0.3, d / 2 + 0.35, 0.0, w=0.6, h=0.4, depth=0.22)


def sawpit(k):
    """The saw pit (Foundation's sawmill): a log on trestles over a pit, a lean-to over the sawyers, planks stacked."""
    # pit and trestles
    k.box((0.0, 0.0, -0.02), (1.7, 0.55, 0.06), tile="iron", bevel=0.0)
    for x in (-0.6, 0.6):
        k.log((x, -0.4, 0.62), (x, 0.4, 0.62), r=0.09)
        for sy in (-1, 1):
            k.log((x - 0.12, sy * 0.33, 0.0), (x, sy * 0.33, 0.62), r=0.045, sides=6)
            k.log((x + 0.12, sy * 0.33, 0.0), (x, sy * 0.33, 0.62), r=0.045, sides=6)
    k.log((-1.05, 0.0, 0.78), (1.05, 0.02, 0.78), r=0.13)
    # the big two-man saw standing in the cut
    k.box((0.25, 0.0, 0.85), (0.02, 0.06, 1.2), tile="iron", bevel=0.0)
    k.log((0.15, 0.0, 1.45), (0.35, 0.0, 1.45), r=0.02, sides=5, tile="planks")
    # lean-to shelter behind
    w = 2.0
    for x in (-w / 2, w / 2):
        k.log((x, -0.9, 0.0), (x, -0.9, 1.25), r=0.06, sides=7)
        k.log((x, -0.25, 0.0), (x, -0.25, 1.7), r=0.06, sides=7)
    k.lean_to(-w / 2, w / 2, -0.95, -0.2, 1.25, 1.75, overhang=0.2)
    # stacked planks with spacers
    for i in range(5):
        z = 0.06 + i * 0.07
        k.box((0.0, 0.85, z), (1.5, 0.45, 0.04), tile="planks", bevel=0.006, grain=0)
        if i < 4:
            for x in (-0.6, 0.0, 0.6):
                k.box((x, 0.85, z + 0.035), (0.04, 0.48, 0.03), tile="planks_old", bevel=0.0, grain=1)
    k.sawhorse(0.95, 0.6, yaw=90, L=0.5, h=0.4)
    k.stump(-1.0, 0.65, r=0.13, h=0.26)


def lookout_tower(k):
    """The lookout: four log legs braced in X, a plank platform with a railing, a small shingle roof, a ladder."""
    s, H = 0.62, 3.0
    for x in (-s, s):
        for y in (-s, s):
            k.box((x * 1.08, y * 1.08, 0.1), (0.28, 0.28, 0.2), tile="stone", bevel=0.03, jitter=0.01)
            k.log((x * 1.08, y * 1.08, 0.15), (x * 0.92, y * 0.92, H + 1.1), r=0.075, taper=0.85)
    for lvl in (0.9, 2.0):
        for (ax, ay), (bx, by) in (((-s, -s), (s, -s)), ((s, -s), (s, s)), ((s, s), (-s, s)), ((-s, s), (-s, -s))):
            sc0 = 1.08 - (lvl / (H + 1.1)) * 0.16
            sc1 = 1.08 - ((lvl + 0.9) / (H + 1.1)) * 0.16
            k.log((ax * sc0, ay * sc0, lvl), (bx * sc1, by * sc1, lvl + 0.9), r=0.035, sides=6)
            k.log((bx * sc0, by * sc0, lvl), (ax * sc1, ay * sc1, lvl + 0.9), r=0.035, sides=6)
    # platform
    for i in range(8):
        y = -s - 0.2 + (i + 0.5) * (2 * s + 0.4) / 8
        k.box((0, y, H), (2 * s + 0.45, (2 * s + 0.4) / 8 * 0.94, 0.06), tile="planks_old", grain=0)
    for x in (-s, s):
        k.beam((x * 0.95, -s - 0.25, H - 0.08), (x * 0.95, s + 0.25, H - 0.08), w=0.1, tile="planks")
    # railing
    for (ax, ay), (bx, by) in (((-s - 0.15, -s - 0.15), (s + 0.15, -s - 0.15)), ((s + 0.15, -s - 0.15), (s + 0.15, s + 0.15)),
                               ((s + 0.15, s + 0.15), (-s - 0.15, s + 0.15)), ((-s - 0.15, s + 0.15), (-s - 0.15, -s - 0.15))):
        k.log((ax, ay, H + 0.55), (bx, by, H + 0.55), r=0.03, sides=6)
        k.log((ax, ay, H + 0.28), (bx, by, H + 0.28), r=0.025, sides=6)
    k.gable_roof(0, 0, H + 1.1, 2 * s * 0.92, 2 * s * 0.92, 0.6, kind="shingle", overhang=0.28)
    k.ladder((0.25, s + 0.75, 0.0), (0.25, s + 0.1, H + 0.05), w=0.34)
    k.barrel((-s - 0.3, s + 0.3, 0.0))


def weavers_hut(k):
    """The weaver's: a small daub cottage under thatch, a loom with ochre cloth outside, cloth drying on a line."""
    w, d, wall_h = 1.7, 1.4, 1.15
    k.stone_footing(-w / 2, -d / 2 - 0.25, w / 2, d / 2 - 0.25, h=0.2, size=0.24, gaps=[(0.3, d / 2 - 0.25, 0.3)])
    sub = Kit(seed=k.rng.randrange(1 << 30))
    house_walls(sub, w, d, wall_h, "plaster", door_x=0.3, door_w=0.5, door_h=0.98, windows=((-0.4, 0.55),), z0=0.17)
    sub.gable_roof(0, 0, 0.17 + wall_h, w, d, 0.9, kind="thatch", overhang=0.24, thatch_thick=0.22, cross_ends=False)
    for e in (-1, 1):
        sub.plank_wall((e * (w / 2 + 0.01), -d / 2), (e * (w / 2 + 0.01), d / 2), 0.17 + wall_h + 0.02, 0.02, gable_rise=0.84, tile="planks_old")
    k.merge(sub, (0, -0.25, 0))
    # loom
    lx, ly = -0.45, 0.85
    for x in (-0.38, 0.38):
        k.beam((lx + x, ly, 0.0), (lx + x, ly, 1.15), w=0.06, tile="planks")
        k.beam((lx + x, ly - 0.35, 0.0), (lx + x, ly, 0.75), w=0.05, tile="planks")
    for z in (1.1, 0.35):
        k.log((lx - 0.45, ly, z), (lx + 0.45, ly, z), r=0.035, sides=6)
    k.quad_strip([(lx - 0.3, ly + 0.01, 0.38), (lx + 0.3, ly + 0.01, 0.38)], [(lx - 0.3, ly + 0.01, 1.07), (lx + 0.3, ly + 0.01, 1.07)], "cloth_ochre")
    k.bench(lx, ly + 0.45, L=0.6)
    # drying line
    for x in (0.4, 1.05):
        k.log((x, 0.95, 0.0), (x, 0.95, 1.2), r=0.03, sides=6)
    k.log((0.35, 0.95, 1.15), (1.1, 0.95, 1.15), r=0.008, sides=4)
    k.quad_strip([(0.45, 0.96, 0.75), (0.7, 0.96, 0.78)], [(0.45, 0.96, 1.15), (0.7, 0.96, 1.15)], "cloth_red")
    k.quad_strip([(0.75, 0.96, 0.8), (1.0, 0.96, 0.77)], [(0.75, 0.96, 1.15), (1.0, 0.96, 1.15)], "plaster")
    k.basket((1.0, 0.55, 0.0), full="cloth_ochre")


def pottery_kiln(k):
    """The potter's: a domed kiln of stones and clay, a shed with shelves of pots, a wheel and clay heaps."""
    # kiln dome
    k.lathe((0.45, -0.25, 0.0), [(0.62, 0), (0.64, 0.2), (0.58, 0.55), (0.42, 0.85), (0.2, 1.02), (0.16, 1.15)], "hide", sides=14,
            sub=(0.1, 0.1, 0.6, 0.6))
    for i in range(14):
        ang = i / 14 * math.tau
        if abs(ang - math.pi / 2) < 0.4:
            continue
        k.rock((0.45 + math.cos(ang) * 0.64, -0.25 + math.sin(ang) * 0.64, 0.08), (0.24, 0.2, 0.18), rough=0.1)
    k.box((0.45, 0.38, 0.25), (0.36, 0.06, 0.4), tile="iron", bevel=0.0)
    k.firewood(0.45, 0.65, 0.0, w=0.5, h=0.25, depth=0.2)
    # lean-to shed with shelves
    for x in (-1.0, -0.15):
        k.log((x, -0.85, 0.0), (x, -0.85, 1.2), r=0.05, sides=7)
        k.log((x, -0.2, 0.0), (x, -0.2, 1.55), r=0.05, sides=7)
    k.lean_to(-1.0, -0.15, -0.9, -0.15, 1.2, 1.6, overhang=0.18, tile="shingle_red")
    for z in (0.35, 0.75):
        k.box((-0.58, -0.75, z), (0.8, 0.25, 0.04), tile="planks", grain=0)
        for i in range(4):
            k.pot((-0.88 + i * 0.2, -0.75, z + 0.02), h=0.2 + (i % 2) * 0.05, r=0.07)
    for i in range(4):
        k.pot((-0.8 + i * 0.22, 0.25 + (i % 2) * 0.12, 0.0), h=0.3, r=0.12)
    k.rock((-0.6, 0.75, 0.05), (0.45, 0.35, 0.2), tile="hide", rough=0.2)


def copper_smelter(k):
    """The smelter: a tall stone furnace with a clay crucible, hide bellows on a frame, ore heaps and charcoal."""
    for i in range(7):
        z = i * 0.2
        rr = 0.48 - i * 0.04
        n = 9 - i // 2
        for j in range(n):
            ang = (j + (i % 2) * 0.5) / n * math.tau
            if i < 2 and abs(ang - math.pi / 2) < 0.35:
                continue
            k.box((math.cos(ang) * rr, math.sin(ang) * rr - 0.2, z + 0.1), (0.24, 0.2, 0.19), (0, 0, math.degrees(ang) + 90), tile="stone", bevel=0.035, jitter=0.012)
    k.lathe((0, -0.2, 0.0), [(0.4, 0.0), (0.38, 0.6), (0.25, 1.3)], "iron", sides=10, cap_top=True, sub=(0.1, 0.1, 0.3, 0.3))
    # bellows
    k.box((-0.75, 0.15, 0.22), (0.4, 0.3, 0.14), (0, 0, 25), tile="hide", bevel=0.05)
    k.box((-0.75, 0.15, 0.33), (0.42, 0.32, 0.04), (0, 0, 25), tile="planks", bevel=0.01)
    k.log((-0.55, 0.06, 0.25), (-0.3, -0.05, 0.25), r=0.03, sides=6)
    # ore heaps and a cart
    k.stone_pile(0.75, 0.5, n=6, size=0.14, spread=0.2)
    k.stone_pile(-0.7, -0.75, n=5, size=0.12, spread=0.18)
    k.basket((0.75, -0.6, 0.0), full="iron")
    k.crate((-0.85, 0.75, 0.0), s=0.3, yaw=-10)


def bronze_workshop(k):
    """The bronze smith: an open shed under shingles, a stone forge with a hood, an anvil, a quench tub and tools."""
    w, d, h = 1.9, 1.4, 1.4
    for x in (-w / 2, w / 2):
        for y in (-d / 2, d / 2):
            k.box((x, y, 0.1), (0.24, 0.24, 0.2), tile="stone", bevel=0.03, jitter=0.01)
            k.log((x, y, 0.18), (x, y, h + 0.04), r=0.07, sides=8)
    for y in (-d / 2, d / 2):
        k.log((-w / 2 - 0.14, y, h), (w / 2 + 0.14, y, h), r=0.065)
        for sx in (-1, 1):
            k.log((sx * w / 2, y, h - 0.38), (sx * (w / 2 - 0.38), y, h - 0.02), r=0.035, sides=6)
    k.plank_wall((w / 2, -d / 2 - 0.03), (-w / 2, -d / 2 - 0.03), 0.08, h - 0.08, tile="planks_old")
    k.gable_roof(0, 0, h + 0.06, w, d, 0.9, kind="shingle", overhang=0.24)
    for e in (-1, 1):
        k.plank_wall((e * (w / 2 + 0.02), -d / 2), (e * (w / 2 + 0.02), d / 2), h + 0.08, 0.02, gable_rise=0.86, tile="planks")
    # forge: a stone hearth with a hood and chimney through the roof
    fx, fy = -0.45, -0.3
    k.box((fx, fy, 0.3), (0.7, 0.55, 0.6), tile="stone", bevel=0.04, jitter=0.015)
    k.box((fx, fy, 0.61), (0.5, 0.38, 0.04), tile="iron", bevel=0.0)
    k.lathe((fx, fy, 1.05), [(0.36, 0.0), (0.2, 0.35), (0.13, 0.45), (0.13, 1.4)], "stone", sides=8, cap_top=True, sub=(0.1, 0.1, 0.6, 0.6))
    k.anvil(0.3, 0.05)
    k.barrel((0.75, -0.35, 0.0), h=0.36, r=0.17, lid=False)
    k.crate((0.6, d / 2 + 0.35, 0.0), s=0.32, yaw=20)
    k.stone_pile(-0.6, d / 2 + 0.35, n=5, size=0.12, spread=0.15)


BUILDINGS = {
    "shelter": shelter,
    "woven_shelter": woven_shelter,
    "daub_house": daub_house,
    "longhouse": longhouse,
    "elders_lodge": elders_lodge,
    "healers_hut": healers_hut,
    "wood_camp": wood_camp,
    "sawpit": sawpit,
    "stone_site": stone_site,
    "storage_pit": storage_pit,
    "granary": granary,
    "foraging_camp": foraging_camp,
    "toolmaker": toolmaker,
    "hunters_lodge": hunters_lodge,
    "smokehouse": smokehouse,
    "lookout_tower": lookout_tower,
    "weavers_hut": weavers_hut,
    "pottery_kiln": pottery_kiln,
    "copper_smelter": copper_smelter,
    "bronze_workshop": bronze_workshop,
}


# MARK: - export

def export(obj, path):
    for o in bpy.context.scene.objects:
        o.select_set(o == obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, object_types={"MESH"},
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, path_mode="STRIP", embed_textures=False)


def material():
    m = bpy.data.materials.get("Atlas")
    if m:
        return m
    m = bpy.data.materials.new("Atlas")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(TEX, "bw_atlas_albedo.png"))
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nrm = nt.nodes.new("ShaderNodeTexImage")
    nrm.image = bpy.data.images.load(os.path.join(TEX, "bw_atlas_normal.png"))
    nrm.image.colorspace_settings.name = "Non-Color"
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.85
    return m


def render_preview(obj, path):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 900, 700
    world = bpy.data.worlds.new("W") if not scene.world else scene.world
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.68, 0.85, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9
    if "Sun" not in bpy.data.objects:
        sun = bpy.data.lights.new("Sun", "SUN")
        sun.energy = 3.2
        sun.angle = math.radians(8)
        so = bpy.data.objects.new("Sun", sun)
        scene.collection.objects.link(so)
        so.rotation_euler = (math.radians(50), 0, math.radians(-35))
        gm = bpy.data.materials.new("Ground")
        gm.use_nodes = True
        gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.32, 0.42, 0.14, 1)
        bpy.ops.mesh.primitive_plane_add(size=14)
        g = bpy.context.active_object
        g.name = "Ground"
        g.data.materials.append(gm)
        cam = bpy.data.cameras.new("Cam")
        cam.lens = 50
        co = bpy.data.objects.new("Cam", cam)
        scene.collection.objects.link(co)
        scene.camera = co
    co = scene.camera
    dims = obj.dimensions
    r = max(dims.x, dims.y, dims.z) * 1.9 + 1.5
    target = Vector((0, 0, dims.z * 0.38))
    yaw, pitch = math.radians(28), math.radians(32)
    co.location = target + Vector((math.sin(yaw) * math.cos(pitch), math.cos(yaw) * math.cos(pitch), math.sin(pitch))) * r
    co.rotation_euler = (target - co.location).to_track_quat("-Z", "Y").to_euler()
    for o in scene.objects:
        if o.type == "MESH" and o.name not in ("Ground", obj.name):
            o.hide_render = True
    obj.hide_render = False
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
    info_path = os.path.join(OUT, "info.json")
    info = json.load(open(info_path)) if os.path.exists(info_path) else {}
    mat = material()
    for i, (mid, fn) in enumerate(BUILDINGS.items()):
        if ONLY and mid not in ONLY.split(","):
            continue
        k = Kit(seed=1000 + i * 17)
        fn(k)
        obj = k.build("bw_" + mid)
        obj.data.materials.append(mat)
        xs = [v.co.x for v in obj.data.vertices]
        ys = [v.co.y for v in obj.data.vertices]
        zs = [v.co.z for v in obj.data.vertices]
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        export(obj, os.path.join(OUT, "bw_" + mid + ".fbx"))
        info["bw_" + mid] = {"triangles": tris, "size": [round(max(xs) - min(xs), 3), round(max(zs) - min(zs), 3), round(max(ys) - min(ys), 3)]}
        print(f"[bw] {mid}: {tris} triangles")
        if PREVIEW:
            render_preview(obj, os.path.join(PREVIEW, mid + ".png"))
        obj.hide_render = True
    json.dump(info, open(info_path, "w"), indent=1)


main()
