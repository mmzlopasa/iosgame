"""Blender (4.2): renders the app icon "The Long Rise".

A small floating island at dusk: a glowing campfire in front, and behind it the settlement rising
from left to right in the game's eras - a hide tent, a thatched hut, a timber house and a tall stone
tower with a golden banner piercing the teal sky. Sparks drift up from the fire towards the tower.
Everything is modelled procedurally here (bold flat-shaded low poly that still reads at 60 px), plus
a few CC0 Quaternius "Stylized Nature MegaKit" pines.

The PNG is opaque RGB (the App Store rejects icons with an alpha channel). Without an explicit
output path it is written to both places the project reads it from:
  unity/Assets/Art/UI/AppIcon/AppIcon.png              (Unity player settings, Editor/UiSetup.cs)
  App/Assets.xcassets/AppIcon.appiconset/AppIcon.png   (the XcodeGen app target)

blender -b --factory-startup -P tools/assets/blender_icon.py -- [<stylized nature glTF dir>] [<out.png>] [size] [samples]
  (glTF dir default: tools/assets/.cache/packs/stylized_nature/glTF; ~1-2 min on 4 cores)
"""
import math
import os
import random
import shutil
import sys

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SN = argv[0] if len(argv) > 0 else os.path.join(ROOT, "tools", "assets", ".cache", "packs", "stylized_nature", "glTF")
OUT = argv[1] if len(argv) > 1 and argv[1] != "-" else None
SIZE = int(argv[2]) if len(argv) > 2 else 1024
SAMPLES = int(argv[3]) if len(argv) > 3 else 160
TARGETS = [os.path.join(ROOT, "unity", "Assets", "Art", "UI", "AppIcon", "AppIcon.png"),
           os.path.join(ROOT, "App", "Assets.xcassets", "AppIcon.appiconset", "AppIcon.png")]

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
rnd = random.Random(11)


# ---------------------------------------------------------------- helpers
def mat(name, color, rough=0.7, emission=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = rough
    if emission:
        p.inputs["Emission Color"].default_value = (*emission, 1)
        p.inputs["Emission Strength"].default_value = strength
    return m


def glow(name, color, strength):
    """Pure emission (flames, windows, sparks)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = (*color, 1)
    e.inputs["Strength"].default_value = strength
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs[0])
    return m


def assign(o, m):
    o.data.materials.clear()
    o.data.materials.append(m)
    return o


def cyl(r, h, loc, m, verts=10, r2=None, rot=(0, 0, 0)):
    """Cylinder/cone standing on loc (base at loc.z)."""
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=h)
    o = bpy.context.object
    o.location = Vector(loc) + Vector((0, 0, h / 2))
    o.rotation_euler = rot
    return assign(o, m)


def box(size, loc, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.object
    o.scale = size
    o.location = loc
    o.rotation_euler = rot
    return assign(o, m)


def rock(r, loc, m, squash=0.6, seed=0):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=r)
    o = bpy.context.object
    rr = random.Random(seed)
    for v in o.data.vertices:
        v.co *= 1 + rr.uniform(-0.18, 0.18)
    o.scale = (1, 1, squash)
    o.location = loc
    o.rotation_euler = (0, 0, rr.uniform(0, 6.28))
    return assign(o, m)


def gable(w, d, h, loc, m, overhang=0.12):
    """A pitched roof (triangular prism, ridge along X) sitting on loc.z."""
    bm = bmesh.new()
    x, y = w / 2 + overhang, d / 2 + overhang
    t = 0.07
    pts = [(-x, -y, 0), (x, -y, 0), (x, 0, h), (-x, 0, h), (-x, y, 0), (x, y, 0)]
    vs = [bm.verts.new(p) for p in pts]
    vs2 = [bm.verts.new((p[0], p[1], p[2] - t)) for p in pts]
    bm.faces.new([vs[0], vs[1], vs[2], vs[3]])
    bm.faces.new([vs[3], vs[2], vs[5], vs[4]])
    bm.faces.new([vs2[3], vs2[2], vs2[1], vs2[0]])
    bm.faces.new([vs2[4], vs2[5], vs2[2], vs2[3]])
    for a, b in ((0, 1), (1, 2), (2, 5), (5, 4), (4, 3), (3, 0)):
        bm.faces.new([vs[a], vs2[a], vs2[b], vs[b]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Roof")
    bm.to_mesh(me)
    o = bpy.data.objects.new("Roof", me)
    scene.collection.objects.link(o)
    o.location = loc
    return assign(o, m)


def gable_end(w, d, h, loc, m):
    """The triangular wall under a gable roof (both ends)."""
    bm = bmesh.new()
    for sx in (-1, 1):
        a = bm.verts.new((sx * w / 2, -d / 2, 0))
        b = bm.verts.new((sx * w / 2, d / 2, 0))
        c = bm.verts.new((sx * w / 2, 0, h))
        bm.faces.new([a, b, c])
    me = bpy.data.meshes.new("Gable")
    bm.to_mesh(me)
    o = bpy.data.objects.new("Gable", me)
    scene.collection.objects.link(o)
    o.location = loc
    return assign(o, m)


def flame(r, h, loc, m, twist=0.45, verts=6, rings=6):
    """A stylized faceted flame tongue: a twisted teardrop standing on loc (origin at mid height)."""
    bm = bmesh.new()
    prev = None
    for i in range(rings):
        t = i / rings
        rad = r * (math.sin(math.pi * min(1.0, 0.25 + t * 1.1)) ** 0.8) * (1 - t) ** 0.35 if i else r * 0.55
        ring = [bm.verts.new((rad * math.cos(2 * math.pi * k / verts + twist * i),
                              rad * math.sin(2 * math.pi * k / verts + twist * i), t * h - h / 2))
                for k in range(verts)]
        if prev:
            for k in range(verts):
                bm.faces.new([prev[k], prev[(k + 1) % verts], ring[(k + 1) % verts], ring[k]])
        else:
            bm.faces.new(list(reversed(ring)))
        prev = ring
    tip = bm.verts.new((0.0, 0.0, h / 2))
    for k in range(verts):
        bm.faces.new([prev[k], prev[(k + 1) % verts], tip])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Flame")
    bm.to_mesh(me)
    o = bpy.data.objects.new("Flame", me)
    scene.collection.objects.link(o)
    o.location = Vector(loc) + Vector((0, 0, h / 2))
    return assign(o, m)


def bush(r, loc, seed):
    """A round low-poly bush: three overlapping faceted balls."""
    rr = random.Random(seed)
    for k, (dx, dy, s) in enumerate(((0, 0, 1.0), (0.55, 0.15, 0.75), (-0.5, 0.2, 0.7))):
        rock(r * s, (loc[0] + dx * r, loc[1] + dy * r, loc[2] + r * s * 0.6), LEAF if k else LEAF_LT,
             squash=0.85, seed=seed * 10 + k)


def kit(name, loc, height, rot=0.0):
    """Imports a Stylized Nature model and scales it to a height."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SN, name + ".gltf"))
    new = [o for o in bpy.data.objects if o not in before]
    roots = [o for o in new if o.parent is None]
    bpy.context.view_layer.update()
    zs = [(o.matrix_world @ Vector(c)).z for o in new if o.type == "MESH" for c in o.bound_box]
    s = height / max(max(zs) - min(zs), 1e-3)
    for o in roots:
        o.rotation_mode = "XYZ"
        o.rotation_euler = (0, 0, rot)
        o.scale = (s, s, s)
        o.location = loc
    return new


# ---------------------------------------------------------------- palette (linear)
GRASS = mat("Grass", (0.16, 0.52, 0.07), 0.85)
GRASS_TOP = mat("GrassTop", (0.22, 0.58, 0.08), 0.9)
EARTH = mat("Earth", (0.36, 0.14, 0.045), 0.9)
ROCK_A = mat("RockA", (0.30, 0.19, 0.14), 0.9)
ROCK_B = mat("RockB", (0.19, 0.12, 0.11), 0.9)
STONE = mat("Stone", (0.40, 0.35, 0.30), 0.8)
STONE_DK = mat("StoneDark", (0.33, 0.30, 0.28), 0.8)
HIDE = mat("Hide", (0.72, 0.36, 0.12), 0.75)
HIDE_DK = mat("HideDark", (0.30, 0.12, 0.04), 0.8)
WOOD = mat("Wood", (0.22, 0.10, 0.04), 0.8)
WATTLE = mat("Wattle", (0.42, 0.22, 0.09), 0.85)
THATCH = mat("Thatch", (0.85, 0.55, 0.14), 0.8)
PLASTER = mat("Plaster", (0.86, 0.72, 0.50), 0.8)
TILE = mat("Tile", (0.62, 0.10, 0.035), 0.6)
ROOF_TOWER = mat("TowerRoof", (0.55, 0.05, 0.03), 0.5)
BANNER = mat("Banner", (1.0, 0.62, 0.05), 0.5, emission=(1.0, 0.55, 0.05), strength=0.6)
GOLD = mat("Gold", (1.0, 0.7, 0.2), 0.3)
WINDOW = glow("Window", (1.0, 0.55, 0.15), 2.2)
DOOR = mat("Door", (0.08, 0.035, 0.015), 0.8)
FLAME_RED = glow("FlameRed", (1.0, 0.12, 0.01), 2.6)
FLAME_OUT = glow("FlameOuter", (1.0, 0.34, 0.03), 3.2)
FLAME_IN = glow("FlameInner", (1.0, 0.82, 0.35), 5.0)
SPARK = glow("Spark", (1.0, 0.45, 0.08), 3.0)
LEAF = mat("Leaf", (0.06, 0.28, 0.05), 0.85)
LEAF_LT = mat("LeafLight", (0.10, 0.38, 0.06), 0.85)

# ---------------------------------------------------------------- the floating island
SEG = 22
XS, YS = 1.1, 0.85  # the island is wider than deep
PROFILE = [(0.0, 3.0), (-0.20, 3.12), (-0.34, 2.95), (-0.75, 2.75), (-1.15, 2.30), (-1.65, 1.75),
           (-2.15, 1.15), (-2.65, 0.60), (-3.05, 0.22)]


def build_island():
    bm = bmesh.new()
    rings = []
    for i, (z, r) in enumerate(PROFILE):
        ring = []
        for k in range(SEG):
            a = 2 * math.pi * (k + (0.5 if i % 2 else 0)) / SEG
            j = 0.03 if i < 2 else 0.09
            rr = r * (1 + rnd.uniform(-j, j))
            zz = z + (rnd.uniform(-0.07, 0.07) if i > 2 else 0)
            ring.append(bm.verts.new((rr * math.cos(a) * XS, rr * math.sin(a) * YS, zz)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        for k in range(SEG):
            k1 = (k + 1) % SEG
            if i % 2 == 0:  # b is offset half a step forward
                bm.faces.new([a[k], a[k1], b[k]])
                bm.faces.new([a[k1], b[k1], b[k]])
            else:
                bm.faces.new([a[k], b[k1], b[k]])
                bm.faces.new([a[k], a[k1], b[k1]])
    tip = bm.verts.new((0.1, 0.05, -3.55))
    last = rings[-1]
    for k in range(SEG):
        bm.faces.new([last[k], last[(k + 1) % SEG], tip])
    # Gently domed top.
    inner = [bm.verts.new((1.6 * XS * math.cos(2 * math.pi * k / SEG), 1.6 * math.sin(2 * math.pi * k / SEG) * YS,
                           0.08 + rnd.uniform(-0.02, 0.03))) for k in range(SEG)]
    top = rings[0]
    for k in range(SEG):
        k1 = (k + 1) % SEG
        bm.faces.new([top[k1], top[k], inner[k], inner[k1]])
    centre = bm.verts.new((0, 0, 0.12))
    for k in range(SEG):
        bm.faces.new([inner[(k + 1) % SEG], inner[k], centre])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Island")
    bm.to_mesh(me)
    o = bpy.data.objects.new("Island", me)
    scene.collection.objects.link(o)
    for m in (GRASS_TOP, GRASS, EARTH, ROCK_A, ROCK_B):
        me.materials.append(m)
    for p in me.polygons:
        z = p.center.z
        if z > 0.0:
            p.material_index = 0
        elif z > -0.30:
            p.material_index = 1
        elif z > -0.85:
            p.material_index = 2
        else:
            band = int((-z - 0.85) / 0.55)
            p.material_index = 3 + (band + (1 if rnd.random() < 0.2 else 0)) % 2
    return o


build_island()

# Little floating rocks beside the island sell the "floating".
for loc, r, s in (((-3.7, -0.4, -1.6), 0.38, 1), ((3.65, -0.2, -2.3), 0.28, 2), ((-2.6, -1.4, -2.6), 0.15, 3)):
    rock(r, loc, ROCK_A, squash=1.1, seed=s)
    cyl(r * 0.95, 0.09, (loc[0], loc[1], loc[2] + r * 0.95), GRASS, verts=7)

# ---------------------------------------------------------------- the settlement, rising left to right
# 1. Hide tent
TX, TY = -2.05, 0.35
cyl(0.5, 0.95, (TX, TY, 0.04), HIDE, verts=6, r2=0.0)
for a in (0.3, 2.4, 4.4):
    cyl(0.025, 0.45, (TX, TY, 0.85), WOOD, verts=5, rot=(0.35 * math.cos(a), 0.35 * math.sin(a), 0))
box((0.02, 0.2, 0.45), (TX + 0.0, TY - 0.36, 0.25), HIDE_DK, rot=(0.5, 0, 0))
box((0.22, 0.02, 0.4), (TX, TY - 0.38, 0.22), HIDE_DK, rot=(0.4, 0, 0))

# 2. Thatched round hut
HX, HY = -0.95, 0.85
cyl(0.46, 0.55, (HX, HY, 0.0), WATTLE, verts=9)
cyl(0.70, 0.75, (HX, HY, 0.5), THATCH, verts=9, r2=0.02)
box((0.2, 0.06, 0.34), (HX, HY - 0.45, 0.2), DOOR)
cyl(0.025, 0.25, (HX, HY, 1.2), WOOD, verts=5)

# 3. Two-storey timber house
BX, BY, BW, BD, BH = 0.25, 0.95, 1.05, 0.85, 1.15
box((BW, BD, BH), (BX, BY, BH / 2), PLASTER)
gable_end(BW, BD, 0.62, (BX, BY, BH), PLASTER)
gable(BW, BD, 0.62, (BX, BY, BH), TILE)
fy = BY - BD / 2 - 0.012
for x in (-BW / 2 + 0.03, BW / 2 - 0.03, 0.0):
    box((0.07, 0.03, BH), (BX + x, fy, BH / 2), WOOD)
for z in (0.04, 0.55, BH - 0.04):
    box((BW + 0.02, 0.03, 0.07), (BX, fy, z), WOOD)
for x in (-0.26, 0.26):
    box((0.2, 0.025, 0.24), (BX + x, fy - 0.01, 0.84), WINDOW)
box((0.2, 0.025, 0.24), (BX - 0.26, fy - 0.01, 0.28), WINDOW)
box((0.2, 0.03, 0.38), (BX + 0.26, fy - 0.005, 0.2), DOOR)
box((0.16, 0.16, 0.45), (BX + 0.3, BY + 0.15, BH + 0.45), STONE_DK)

# 4. The stone tower, piercing the sky
WX, WY = 1.75, 0.55
TOWER_H = 3.1
cyl(0.62, 0.25, (WX, WY, -0.05), STONE_DK, verts=10)
cyl(0.55, TOWER_H, (WX, WY, 0.0), STONE, verts=10, r2=0.47)
cyl(0.53, 0.12, (WX, WY, 1.7), STONE_DK, verts=10)
cyl(0.64, 0.38, (WX, WY, TOWER_H), STONE, verts=10)
for k in range(10):
    a = 2 * math.pi * (k + 0.5) / 10
    box((0.17, 0.17, 0.2), (WX + 0.58 * math.cos(a), WY + 0.58 * math.sin(a), TOWER_H + 0.47), STONE,
        rot=(0, 0, a))
cyl(0.5, 1.45, (WX, WY, TOWER_H + 0.3), ROOF_TOWER, verts=10, r2=0.0)
fz = WY - 0.5
for z, h in ((0.45, 0.38), (1.15, 0.34), (2.35, 0.38), (2.95, 0.3)):
    r = 0.55 - (0.08 * (z + h / 2) / TOWER_H)
    box((0.15, 0.04, h), (WX, WY - r - 0.005, z + h / 2), WINDOW)
box((0.24, 0.04, 0.42), (WX - 0.0, WY - 0.555, 0.21), DOOR)
# Flagpole and a long swallow-tailed pennant streaming left
PZ = TOWER_H + 0.3 + 1.40
cyl(0.03, 1.05, (WX, WY, PZ), WOOD, verts=6)
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.06, location=(WX, WY, PZ + 1.08), segments=8, ring_count=5)
assign(bpy.context.object, GOLD)


def pennant():
    bm = bmesh.new()
    L, H0, n = 1.5, 0.48, 14
    top, bot = [], []
    for i in range(n + 1):
        t = i / n
        x = -L * t
        h = H0 * (1 - 0.55 * t)
        wave = 0.13 * math.sin(t * 5.5 + 0.4) * t
        dy = 0.12 * math.sin(t * 4.0) * t
        zc = PZ + 0.95 - 0.12 * t + wave
        top.append(bm.verts.new((WX + x, WY + dy, zc)))
        bz = zc - h + (0.18 * H0 if i == n else 0)
        bot.append(bm.verts.new((WX + x, WY + dy, bz)))
    mid = bm.verts.new((WX - L * 0.86, WY + 0.12 * math.sin(4.0 * 0.86) * 0.86,
                        PZ + 0.95 - 0.1 + 0.13 * math.sin(0.86 * 5.5 + 0.4) * 0.86 - H0 * 0.27))
    for i in range(n - 1):
        bm.faces.new([top[i], top[i + 1], bot[i + 1], bot[i]])
    bm.faces.new([top[n - 1], top[n], mid])
    bm.faces.new([mid, bot[n], bot[n - 1]])
    bm.faces.new([top[n - 1], mid, bot[n - 1]])
    me = bpy.data.meshes.new("Pennant")
    bm.to_mesh(me)
    o = bpy.data.objects.new("Pennant", me)
    scene.collection.objects.link(o)
    sol = o.modifiers.new("Solid", "SOLIDIFY")
    sol.thickness = 0.03
    return assign(o, BANNER)


pennant()

# Greenery: pines framing the back, bushes and rocks on the rim
if os.path.isdir(SN):
    for name, loc, h, r in (("Pine_2", (-1.45, 1.75, 0.0), 1.9, 0.4), ("Pine_4", (-2.55, 1.2, 0.0), 1.35, 1.1),
                            ("Pine_1", (2.6, 1.35, 0.0), 1.5, 2.0), ("Pine_2", (0.95, 1.95, 0.0), 1.3, 2.6),
                            ("Grass_Common_Tall", (1.2, -1.2, 0.0), 0.3, 0.0),
                            ("Grass_Common_Tall", (-1.5, -1.6, 0.0), 0.28, 1.0)):
        kit(name, loc, h, r)
bush(0.32, (-2.75, -0.7, 0.0), 1)
bush(0.36, (2.75, -0.55, 0.0), 2)
bush(0.25, (-0.2, 1.6, 0.05), 3)
for loc, r, s in (((2.15, -1.45, 0.05), 0.2, 4), ((-2.4, -1.05, 0.05), 0.16, 5), ((0.9, 0.0, 0.08), 0.12, 6)):
    rock(r, loc, STONE_DK, seed=s)

# ---------------------------------------------------------------- the campfire (the hero, in front)
FX, FY = -0.35, -1.7
FIRE_SCALE = 1.35
_before_fire = set(bpy.data.objects)
for k in range(9):
    a = 2 * math.pi * k / 9
    rock(0.14, (FX + 0.42 * math.cos(a), FY + 0.42 * math.sin(a), 0.1), STONE_DK if k % 2 else STONE,
         squash=0.7, seed=20 + k)
for k in range(4):
    a = 2 * math.pi * k / 4 + 0.4
    o = cyl(0.06, 0.75, (0, 0, 0), WOOD, verts=6)
    o.location = (FX + 0.18 * math.cos(a), FY + 0.18 * math.sin(a), 0.32)
    o.rotation_euler = (0.75 * math.sin(a), -0.75 * math.cos(a), 0)
# Flame tongues: red outside, orange, a yellow-white heart; side tongues lean outwards.
for (a, off, r, h, lean, m) in ((0, 0.0, 0.30, 1.2, 0.0, FLAME_OUT), (0.3, 0.17, 0.17, 0.75, 0.35, FLAME_RED),
                                (2.0, 0.17, 0.16, 0.85, 0.3, FLAME_RED), (3.6, 0.16, 0.17, 0.7, 0.35, FLAME_OUT),
                                (5.0, 0.15, 0.16, 0.8, 0.3, FLAME_RED), (1.2, 0.1, 0.15, 0.9, 0.15, FLAME_OUT),
                                (4.3, 0.04, 0.17, 0.78, 0.05, FLAME_IN)):
    o = flame(r * 1.1, h, (FX + off * math.cos(a), FY - 0.05 + off * math.sin(a), 0.1), m, twist=0.3 + 0.1 * (a % 2))
    o.rotation_euler = (-lean * math.sin(a), lean * math.cos(a), rnd.uniform(0, 3))
    o.location.z -= h / 2 * (1 - math.cos(lean))
for o in set(bpy.data.objects) - _before_fire:  # scale the whole campfire about its centre
    o.location = Vector((FX, FY, 0)) + (o.location - Vector((FX, FY, 0))) * FIRE_SCALE
    o.scale = o.scale * FIRE_SCALE
for i in range(36):
    t = rnd.random() ** 0.8
    z = 1.4 + t * 2.9
    x = FX + rnd.gauss(0, 0.12 + 0.35 * t) + 0.55 * t
    y = FY + rnd.gauss(0, 0.15)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.055 * (1 - 0.6 * t) * rnd.uniform(0.6, 1.2),
                                          location=(x, y, z))
    assign(bpy.context.object, SPARK)
fire = bpy.data.objects.new("FireLight", bpy.data.lights.new("FireLight", "POINT"))
fire.data.energy = 240
fire.data.color = (1.0, 0.45, 0.12)
fire.data.shadow_soft_size = 0.5
fire.location = (FX, FY - 0.2, 1.25)
scene.collection.objects.link(fire)
for loc, e in (((WX, WY - 1.1, 1.6), 8),):  # window spill
    L = bpy.data.objects.new("Spill", bpy.data.lights.new("Spill", "POINT"))
    L.data.energy = e
    L.data.color = (1.0, 0.55, 0.2)
    L.location = loc
    scene.collection.objects.link(L)

# ---------------------------------------------------------------- the dusk sky (an emissive backdrop)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 50
target = Vector((0.05, 0.0, 1.35))
elev = math.radians(15)
dist = 14.9
cam.location = target + Vector((0, -math.cos(elev) * dist, math.sin(elev) * dist))
cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()

SKY_Y = 28.0
bpy.ops.mesh.primitive_plane_add(size=1)
sky = bpy.context.object
sky.name = "Sky"
d = (SKY_Y - cam.location.y)
half = d * 18 / 50 * 1.15
sky.scale = (half * 2, half * 2, 1)
sky.rotation_euler = (math.radians(90) - elev, 0, 0)
sky.location = cam.location + (target - cam.location).normalized() * d
sky.visible_shadow = False
sky.visible_diffuse = False
sky.visible_glossy = False
m = bpy.data.materials.new("Sky")
m.use_nodes = True
nt = m.node_tree
nt.nodes.clear()
N = nt.nodes.new
tc = N("ShaderNodeTexCoord")
sep = N("ShaderNodeSeparateXYZ")
nt.links.new(tc.outputs["UV"], sep.inputs[0])
ramp = N("ShaderNodeValToRGB")
nt.links.new(sep.outputs["Y"], ramp.inputs[0])
stops = [(0.0, (0.035, 0.012, 0.06)), (0.24, (0.30, 0.06, 0.12)), (0.42, (1.0, 0.36, 0.07)),
         (0.52, (0.85, 0.30, 0.10)), (0.66, (0.06, 0.20, 0.22)), (0.82, (0.012, 0.075, 0.10)),
         (1.0, (0.004, 0.025, 0.045))]
cr = ramp.color_ramp
cr.interpolation = "EASE"
cr.elements[0].position, cr.elements[0].color = stops[0][0], (*stops[0][1], 1)
cr.elements[1].position, cr.elements[1].color = stops[-1][0], (*stops[-1][1], 1)
for p, c in stops[1:-1]:
    e = cr.elements.new(p)
    e.color = (*c, 1)
# The setting sun's halo, low behind the settlement.
dsun = N("ShaderNodeVectorMath")
dsun.operation = "DISTANCE"
nt.links.new(tc.outputs["UV"], dsun.inputs[0])
dsun.inputs[1].default_value = (0.58, 0.47, 0)
halo = N("ShaderNodeMapRange")
halo.inputs["From Min"].default_value = 0.0
halo.inputs["From Max"].default_value = 0.30
halo.inputs["To Min"].default_value = 1.0
halo.inputs["To Max"].default_value = 0.0
nt.links.new(dsun.outputs["Value"], halo.inputs["Value"])
hpow = N("ShaderNodeMath")
hpow.operation = "POWER"
hpow.inputs[1].default_value = 2.2
nt.links.new(halo.outputs["Result"], hpow.inputs[0])
hcol = N("ShaderNodeMix")
hcol.data_type = "RGBA"
hcol.blend_type = "ADD"
nt.links.new(hpow.outputs[0], hcol.inputs["Factor"])
nt.links.new(ramp.outputs["Color"], hcol.inputs["A"])
hcol.inputs["B"].default_value = (1.0, 0.55, 0.18, 1)
# Stars: sparse Voronoi points in the upper sky.
vor = N("ShaderNodeTexVoronoi")
vor.inputs["Scale"].default_value = 38
vor.inputs["Randomness"].default_value = 1.0
nt.links.new(tc.outputs["UV"], vor.inputs["Vector"])
star = N("ShaderNodeMapRange")
star.inputs["From Min"].default_value = 0.05
star.inputs["From Max"].default_value = 0.0
nt.links.new(vor.outputs["Distance"], star.inputs["Value"])
sel = N("ShaderNodeMath")  # only some cells carry a star
sel.operation = "GREATER_THAN"
sel.inputs[1].default_value = 0.6
nt.links.new(vor.outputs["Color"], sel.inputs[0])
mask = N("ShaderNodeMapRange")
mask.inputs["From Min"].default_value = 0.70
mask.inputs["From Max"].default_value = 0.88
nt.links.new(sep.outputs["Y"], mask.inputs["Value"])
s1 = N("ShaderNodeMath")
s1.operation = "MULTIPLY"
nt.links.new(star.outputs["Result"], s1.inputs[0])
nt.links.new(sel.outputs[0], s1.inputs[1])
s2 = N("ShaderNodeMath")
s2.operation = "MULTIPLY"
nt.links.new(s1.outputs[0], s2.inputs[0])
nt.links.new(mask.outputs["Result"], s2.inputs[1])
s3 = N("ShaderNodeMath")
s3.operation = "MULTIPLY"
s3.inputs[1].default_value = 2.5
nt.links.new(s2.outputs[0], s3.inputs[0])
add = N("ShaderNodeMix")
add.data_type = "RGBA"
add.blend_type = "ADD"
nt.links.new(s3.outputs[0], add.inputs["Factor"])
nt.links.new(hcol.outputs["Result"], add.inputs["A"])
add.inputs["B"].default_value = (0.9, 0.95, 1.0, 1)
em = N("ShaderNodeEmission")
nt.links.new(add.outputs["Result"], em.inputs["Color"])
em.inputs["Strength"].default_value = 1.0
out = N("ShaderNodeOutputMaterial")
nt.links.new(em.outputs[0], out.inputs[0])
sky.data.materials.append(m)

# ---------------------------------------------------------------- lights
world = bpy.data.worlds.new("Dusk")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.10, 0.14, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
scene.world = world
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))  # low warm sun from behind right: rim light
sun.data.energy = 4.0
sun.data.color = (1.0, 0.5, 0.2)
sun.data.angle = math.radians(3)
sun.rotation_euler = (math.radians(78), 0, math.radians(150))
scene.collection.objects.link(sun)
sky_fill = bpy.data.objects.new("SkyFill", bpy.data.lights.new("SkyFill", "SUN"))  # cool teal from above left
sky_fill.data.energy = 1.3
sky_fill.data.color = (0.45, 0.75, 1.0)
sky_fill.rotation_euler = (math.radians(35), 0, math.radians(-35))
scene.collection.objects.link(sky_fill)
front = bpy.data.objects.new("Front", bpy.data.lights.new("Front", "AREA"))  # soft warm front fill
front.data.energy = 350
front.data.size = 8
front.data.color = (1.0, 0.7, 0.5)
front.location = (2.0, -10.0, 1.0)
front.rotation_euler = (target - front.location).to_track_quat("-Z", "Y").to_euler()
scene.collection.objects.link(front)

# ---------------------------------------------------------------- render
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = SAMPLES
scene.cycles.use_denoising = True
scene.cycles.max_bounces = 6
scene.render.resolution_x = scene.render.resolution_y = SIZE
scene.render.film_transparent = False
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGB"  # no alpha channel for the App Store
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"
# A soft bloom around the fire, the sparks and the lit windows.
scene.use_nodes = True
ct = scene.node_tree
rl = ct.nodes.get("Render Layers") or ct.nodes.new("CompositorNodeRLayers")
comp = ct.nodes.get("Composite") or ct.nodes.new("CompositorNodeComposite")
glare = ct.nodes.new("CompositorNodeGlare")
glare.glare_type = "FOG_GLOW"
glare.quality = "HIGH"
glare.threshold = 1.0
glare.size = 8
glare.mix = -0.35
ct.links.new(rl.outputs["Image"], glare.inputs["Image"])
ct.links.new(glare.outputs["Image"], comp.inputs["Image"])
dest = OUT or TARGETS[0]
scene.render.filepath = dest
bpy.ops.render.render(write_still=True)
if OUT is None:
    for t in TARGETS[1:]:
        shutil.copyfile(dest, t)
print("ICON", dest if OUT else ", ".join(TARGETS))
