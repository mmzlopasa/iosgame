"""Blender (4.2): renders the frames of the loading-screen video "The Long Rise".

A time-lapse on a floating island (the app icon's island, grown up): a lone campfire burns at night,
hide tents pop up around it, dawn breaks, the tents give way to thatched huts and timber houses, then
to stone-and-plaster houses from the CC0 Quaternius "Medieval Village MegaKit" while fields appear,
and finally the church tower rises - all under a sky that goes from starry night through a pink-orange
dawn to golden day. Trees sway, sparks drift up from the fire, clouds float by below the island.

The camera swings slowly around the island with a period of LOOP frames, and the frames after LOOP
repeat the first ones (night, campfire only), so the encoder can cross-fade the end into the start
and the clip loops seamlessly (see build_loading_video.py, which also builds the medieval houses).

blender -b --factory-startup -P tools/video/blender_rise.py -- <out dir> [--medieval DIR] [--nature DIR]
        [--res 1024] [--samples 16] [--frames A-B] [--stills 30,120,200]
  --medieval  glTF houses from tools/assets/blender_medieval.py (default tools/video/.cache/medieval)
  --nature    Stylized Nature MegaKit glTF dir (default tools/assets/.cache/packs/stylized_nature/glTF)
  --res       output width; the height keeps the 16:9 aspect (default 1024). The picture is framed for
              landscape phones and iPads: the island stays inside the centre 1920x886 (2.17:1) band and the
              centre 1440x1080 (4:3) of a 1920x1080 frame, so "cover" scaling never crops it
  --frames    frame range to render as f_0000.png ... (default 0-263)
  --stills    render only these frames (for checking), as still_<n>.png
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Euler, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def opt(name, default):
    return argv[argv.index(name) + 1] if name in argv else default


OUT = argv[0] if argv and not argv[0].startswith("--") else os.path.join(ROOT, "tools", "video", ".cache", "frames")
MED = opt("--medieval", os.path.join(ROOT, "tools", "video", ".cache", "medieval"))
SN = opt("--nature", os.path.join(ROOT, "tools", "assets", ".cache", "packs", "stylized_nature", "glTF"))
RES = int(opt("--res", "1024"))
SAMPLES = int(opt("--samples", "16"))
FRAMES = [int(x) for x in opt("--frames", "0-263").split("-")]
STILLS = [int(x) for x in opt("--stills", "").split(",") if x]

FPS = 24
LOOP = 240          # the camera swing period; frames LOOP.. repeat frames 0..
W_ASPECT = 16 / 9   # landscape

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
rnd = random.Random(5)


# ------------------------------------------------------------------ helpers
def mat(name, color, rough=0.7):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = rough
    return m


def glow(name, color, strength):
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


def rock(r, loc, m, squash=0.6, seed=0, subdiv=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=r)
    o = bpy.context.object
    rr = random.Random(seed)
    for v in o.data.vertices:
        v.co *= 1 + rr.uniform(-0.18, 0.18)
    o.scale = (1, 1, squash)
    o.location = loc
    o.rotation_euler = (0, 0, rr.uniform(0, 6.28))
    return assign(o, m)


def mesh_obj(name, bm, m):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    return assign(o, m)


def gable(w, d, h, loc, m, overhang=0.12):
    bm = bmesh.new()
    x, y, t = w / 2 + overhang, d / 2 + overhang, 0.07
    pts = [(-x, -y, 0), (x, -y, 0), (x, 0, h), (-x, 0, h), (-x, y, 0), (x, y, 0)]
    vs = [bm.verts.new(p) for p in pts]
    vs2 = [bm.verts.new((p[0], p[1], p[2] - t)) for p in pts]
    bm.faces.new([vs[0], vs[1], vs[2], vs[3]])
    bm.faces.new([vs[3], vs[2], vs[5], vs[4]])
    bm.faces.new([vs2[3], vs2[2], vs2[1], vs2[0]])
    bm.faces.new([vs2[4], vs2[5], vs2[2], vs2[3]])
    for a, b in ((0, 1), (1, 2), (2, 5), (5, 4), (4, 3), (3, 0)):
        bm.faces.new([vs[a], vs2[a], vs2[b], vs[b]])
    o = mesh_obj("Roof", bm, m)
    o.location = loc
    return o


def gable_end(w, d, h, loc, m):
    bm = bmesh.new()
    for sx in (-1, 1):
        a = bm.verts.new((sx * w / 2, -d / 2, 0))
        b = bm.verts.new((sx * w / 2, d / 2, 0))
        c = bm.verts.new((sx * w / 2, 0, h))
        bm.faces.new([a, b, c])
    o = mesh_obj("Gable", bm, m)
    o.location = loc
    return o


def flame(r, h, loc, m, twist=0.4, verts=6, rings=6):
    """A faceted twisted teardrop flame (origin at mid height)."""
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
    o = mesh_obj("Flame", bm, m)
    o.location = Vector(loc) + Vector((0, 0, h / 2))
    return o


def group(objs, name, loc, rot_z):
    """Parents objects (modelled around the origin) to an empty placed at loc."""
    e = bpy.data.objects.new(name, None)
    scene.collection.objects.link(e)
    for o in objs:
        o.parent = e
    e.location = loc
    e.rotation_euler = (0, 0, rot_z)
    return e


def made(fn):
    """Runs a builder and returns the objects it created."""
    before = set(bpy.data.objects)
    fn()
    return [o for o in bpy.data.objects if o not in before and o.parent is None]


def key(obj, path, frame, value, interp=None, easing=None):
    setattr(obj, path, value)
    obj.keyframe_insert(path, frame=frame)
    if interp:
        fc = obj.animation_data.action.fcurves
        for c in fc:
            if c.data_path == path:
                for kp in c.keyframe_points:
                    if abs(kp.co.x - frame) < 0.01:
                        kp.interpolation = interp
                        if easing:
                            kp.easing = easing


def key_input(socket, frame, value):
    socket.default_value = value
    socket.keyframe_insert("default_value", frame=frame)


# ------------------------------------------------------------------ palette (linear)
GRASS = mat("Grass", (0.16, 0.50, 0.07), 0.85)
GRASS_TOP = mat("GrassTop", (0.22, 0.56, 0.08), 0.9)
EARTH = mat("Earth", (0.36, 0.14, 0.045), 0.9)
ROCK_A = mat("RockA", (0.30, 0.19, 0.14), 0.9)
ROCK_B = mat("RockB", (0.19, 0.12, 0.11), 0.9)
STONE = mat("Stone", (0.40, 0.35, 0.30), 0.8)
STONE_DK = mat("StoneDark", (0.30, 0.27, 0.25), 0.8)
HIDE = mat("Hide", (0.72, 0.36, 0.12), 0.75)
HIDE_LT = mat("HideLight", (0.80, 0.55, 0.30), 0.75)
HIDE_DK = mat("HideDark", (0.30, 0.12, 0.04), 0.8)
WOOD = mat("Wood", (0.22, 0.10, 0.04), 0.8)
WATTLE = mat("Wattle", (0.42, 0.22, 0.09), 0.85)
THATCH = mat("Thatch", (0.85, 0.55, 0.14), 0.8)
PLASTER = mat("Plaster", (0.86, 0.72, 0.50), 0.8)
TILE = mat("Tile", (0.62, 0.10, 0.035), 0.6)
DOOR = mat("Door", (0.08, 0.035, 0.015), 0.8)
DIRT = mat("Dirt", (0.40, 0.24, 0.10), 0.95)
CROP_A = mat("CropA", (0.75, 0.55, 0.08), 0.8)
CROP_B = mat("CropB", (0.30, 0.55, 0.06), 0.8)
CLOUD = mat("Cloud", (0.95, 0.90, 0.90), 1.0)
LEAF = mat("Leaf", (0.06, 0.28, 0.05), 0.85)
LEAF_LT = mat("LeafLight", (0.10, 0.38, 0.06), 0.85)
WINDOW = glow("Window", (1.0, 0.55, 0.15), 3.0)
FLAME_RED = glow("FlameRed", (1.0, 0.12, 0.01), 3.0)
FLAME_OUT = glow("FlameOuter", (1.0, 0.34, 0.03), 3.6)
FLAME_IN = glow("FlameInner", (1.0, 0.82, 0.35), 5.5)
SPARK = glow("Spark", (1.0, 0.45, 0.08), 4.5)

# ------------------------------------------------------------------ the island
R = 5.2
SEG = 34
XS, YS = 1.0, 0.92
PROFILE = [(0.0, 1.0), (-0.07, 1.035), (-0.11, 0.985), (-0.26, 0.93), (-0.45, 0.80), (-0.66, 0.62),
           (-0.86, 0.42), (-1.04, 0.22), (-1.18, 0.08)]
DEPTH = 5.0


def build_island():
    bm = bmesh.new()
    rings = []
    for i, (z, r) in enumerate(PROFILE):
        ring = []
        for k in range(SEG):
            a = 2 * math.pi * (k + (0.5 if i % 2 else 0)) / SEG
            j = 0.02 if i < 2 else 0.08
            rr = R * r * (1 + rnd.uniform(-j, j))
            zz = z * DEPTH + (rnd.uniform(-0.15, 0.15) if i > 2 else 0)
            ring.append(bm.verts.new((rr * math.cos(a) * XS, rr * math.sin(a) * YS, zz)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        for k in range(SEG):
            k1 = (k + 1) % SEG
            if i % 2 == 0:
                bm.faces.new([a[k], a[k1], b[k]])
                bm.faces.new([a[k1], b[k1], b[k]])
            else:
                bm.faces.new([a[k], b[k1], b[k]])
                bm.faces.new([a[k], a[k1], b[k1]])
    tip = bm.verts.new((0.2, 0.1, -DEPTH * 1.32))
    for k in range(SEG):
        bm.faces.new([rings[-1][k], rings[-1][(k + 1) % SEG], tip])
    inner = [bm.verts.new((R * 0.55 * math.cos(2 * math.pi * k / SEG) * XS, R * 0.55 * math.sin(2 * math.pi * k / SEG) * YS,
                           0.06 + rnd.uniform(-0.02, 0.03))) for k in range(SEG)]
    for k in range(SEG):
        k1 = (k + 1) % SEG
        bm.faces.new([rings[0][k1], rings[0][k], inner[k], inner[k1]])
    centre = bm.verts.new((0, 0, 0.08))
    for k in range(SEG):
        bm.faces.new([inner[(k + 1) % SEG], inner[k], centre])
    o = mesh_obj("Island", bm, GRASS_TOP)
    me = o.data
    for m in (GRASS, EARTH, ROCK_A, ROCK_B):
        me.materials.append(m)
    for p in me.polygons:
        z = p.center.z
        if z > 0.0:
            p.material_index = 0
        elif z > -0.4:
            p.material_index = 1
        elif z > -1.2:
            p.material_index = 2
        else:
            band = int((-z - 1.2) / 0.8)
            p.material_index = 3 + (band + (1 if rnd.random() < 0.2 else 0)) % 2
    return o


build_island()
for loc, r, s in (((-6.3, 1.0, -2.2), 0.55, 1), ((6.0, 0.4, -3.4), 0.42, 2), ((-4.3, -1.8, -5.0), 0.3, 3),
                  ((4.6, -2.0, -0.9), 0.25, 4)):
    rock(r, loc, ROCK_A, squash=1.1, seed=s)
    cyl(r * 0.95, 0.12, (loc[0], loc[1], loc[2] + r * 0.95), GRASS, verts=7)

# ------------------------------------------------------------------ nature from the Stylized Nature MegaKit
kit_cache = {}


def nature(name, loc, height, rot=0.0):
    """Places a copy of a Stylized Nature model scaled to a height (imported once)."""
    if not os.path.isdir(SN):
        return []
    if name not in kit_cache:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=os.path.join(SN, name + ".gltf"))
        objs = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
        bpy.context.view_layer.update()
        zs = [(o.matrix_world @ Vector(c)).z for o in objs for c in o.bound_box]
        for o in objs:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
            o.hide_render = True
            o.hide_set(True)
        kit_cache[name] = (objs, max(zs) - min(zs))
    objs, h = kit_cache[name]
    s = height / max(h, 1e-3)
    out = []
    for src in objs:
        o = src.copy()
        o.hide_render = False
        scene.collection.objects.link(o)
        o.rotation_mode = "XYZ"
        o.rotation_euler = (0, 0, rot)
        o.scale = Vector(src.scale) * s
        o.location = Vector(loc) + Euler((0, 0, rot)).to_matrix() @ (src.location * s)
        out.append(o)
    return out


def sway(objs, amp, phase, harmonics=3):
    """Trees lean gently in the wind, periodic over LOOP frames."""
    for o in objs:
        base = o.rotation_euler.copy()
        for f in range(0, FRAMES[1] + 13, 12):
            a = amp * math.sin(2 * math.pi * harmonics * f / LOOP + phase)
            key(o, "rotation_euler", f, Euler((base.x + a, base.y + a * 0.5, base.z)))


def keep_clear(x, y):
    """The front of the island (towards the camera) and the building plots stay free of trees."""
    return (y < -0.8 and abs(x) < 3.6) or (abs(x) < 1.5 and y > 1.6)


for i in range(26):
    a = rnd.uniform(0, 2 * math.pi)
    rr = R * rnd.uniform(0.80, 0.94)
    x, y = rr * math.cos(a) * XS, rr * math.sin(a) * YS
    if keep_clear(x, y) or (y < -2.0 and rnd.random() < 0.7):
        continue
    name = rnd.choice(["Pine_1", "Pine_2", "Pine_4", "CommonTree_1", "CommonTree_2", "CommonTree_3"])
    h = rnd.uniform(1.5, 2.4) if name.startswith("Pine") else rnd.uniform(1.3, 1.9)
    sway(nature(name, (x, y, 0.0), h, rnd.uniform(0, 6.28)), 0.035, rnd.uniform(0, 6.28))
for i in range(40):
    a = rnd.uniform(0, 2 * math.pi)
    rr = R * math.sqrt(rnd.uniform(0.1, 0.9))
    x, y = rr * math.cos(a) * XS, rr * math.sin(a) * YS
    if math.hypot(x, y) < 1.0:
        continue
    name = rnd.choice(["Grass_Common_Short", "Grass_Common_Tall", "Flower_3_Group", "Flower_4_Group", "Grass_Wispy_Short"])
    nature(name, (x, y, 0.02), rnd.uniform(0.18, 0.3), rnd.uniform(0, 6.28))
for i, (x, y) in enumerate(((-4.3, -0.9), (4.2, -0.6), (-3.6, 2.5), (3.9, 2.0), (1.0, -3.7))):
    rock(rnd.uniform(0.18, 0.3), (x, y, 0.05), STONE_DK, seed=60 + i)

# ------------------------------------------------------------------ the campfire
FX, FY = 0.0, -0.25
for k in range(9):
    a = 2 * math.pi * k / 9
    rock(0.14, (FX + 0.42 * math.cos(a), FY + 0.42 * math.sin(a), 0.1), STONE_DK if k % 2 else STONE, squash=0.7,
         seed=20 + k)
for k in range(4):
    a = 2 * math.pi * k / 4 + 0.4
    o = cyl(0.06, 0.75, (0, 0, 0), WOOD, verts=6)
    o.location = (FX + 0.18 * math.cos(a), FY + 0.18 * math.sin(a), 0.32)
    o.rotation_euler = (0.75 * math.sin(a), -0.75 * math.cos(a), 0)
flames = []
for (a, off, r, h, lean, m) in ((0, 0.0, 0.30, 1.15, 0.0, FLAME_OUT), (0.3, 0.17, 0.17, 0.75, 0.35, FLAME_RED),
                                (2.0, 0.17, 0.16, 0.85, 0.3, FLAME_RED), (3.6, 0.16, 0.17, 0.7, 0.35, FLAME_OUT),
                                (5.0, 0.15, 0.16, 0.8, 0.3, FLAME_RED), (1.2, 0.1, 0.15, 0.9, 0.15, FLAME_OUT),
                                (4.3, 0.04, 0.17, 0.78, 0.05, FLAME_IN)):
    o = flame(r * 1.1, h, (FX + off * math.cos(a), FY - 0.05 + off * math.sin(a), 0.1), m, twist=0.3 + 0.1 * (a % 2))
    o.rotation_euler = (-lean * math.sin(a), lean * math.cos(a), rnd.uniform(0, 3))
    o.location.z -= h / 2 * (1 - math.cos(lean))
    flames.append(o)
# Flicker: every flame tongue stretches and squashes on its own rhythm (periodic over LOOP).
for i, o in enumerate(flames):
    p = rnd.uniform(0, 6.28)
    for f in range(0, FRAMES[1] + 4, 3):
        s = 1 + 0.16 * math.sin(2 * math.pi * 18 * f / LOOP + p) + 0.08 * math.sin(2 * math.pi * 31 * f / LOOP + p * 2)
        key(o, "scale", f, Vector((1 / math.sqrt(s), 1 / math.sqrt(s), s)))
fire = bpy.data.objects.new("FireLight", bpy.data.lights.new("FireLight", "POINT"))
fire.data.color = (1.0, 0.45, 0.12)
fire.data.shadow_soft_size = 0.5
fire.location = (FX, FY - 0.2, 1.2)
scene.collection.objects.link(fire)

# Sparks rising from the fire (a particle system instancing tiny glowing balls).
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1.0, location=(0, 0, -3000))
spark = assign(bpy.context.object, SPARK)
spark.hide_render = False
bpy.ops.mesh.primitive_circle_add(vertices=12, radius=0.25, fill_type="NGON", location=(FX, FY, 0.9))
emitter = bpy.context.object  # (hiding the emitter would hide its particles too)
ps = emitter.modifiers.new("Sparks", "PARTICLE_SYSTEM").particle_system.settings
ps.count = 150
ps.frame_start = -60
ps.frame_end = FRAMES[1] + 1
ps.lifetime = 50
ps.lifetime_random = 0.5
ps.emit_from = "FACE"
ps.normal_factor = 1.1
ps.factor_random = 0.7
ps.object_align_factor = (0.2, 0.0, 0.0)
ps.effector_weights.gravity = 0.0
ps.brownian_factor = 0.7
ps.render_type = "OBJECT"
ps.instance_object = spark
ps.particle_size = 0.05
ps.size_random = 0.6
emitter.show_instancer_for_render = False

# ------------------------------------------------------------------ the buildings
TENT_COLORS = [HIDE, HIDE_LT]


def tent(variant=0):
    cyl(0.5, 0.95, (0, 0, 0.0), TENT_COLORS[variant % 2], verts=6, r2=0.0)
    for a in (0.3, 2.4, 4.4):
        cyl(0.025, 0.45, (0, 0, 0.82), WOOD, verts=5, rot=(0.35 * math.cos(a), 0.35 * math.sin(a), 0))
    box((0.22, 0.02, 0.4), (0, -0.38, 0.2), HIDE_DK, rot=(0.4, 0, 0))


def hut():
    cyl(0.5, 0.55, (0, 0, 0.0), WATTLE, verts=9)
    cyl(0.74, 0.78, (0, 0, 0.5), THATCH, verts=9, r2=0.02)
    box((0.2, 0.06, 0.34), (0, -0.49, 0.17), DOOR)
    cyl(0.025, 0.25, (0, 0, 1.22), WOOD, verts=5)


def timber():
    bw, bd, bh = 1.05, 0.85, 1.0
    box((bw, bd, bh), (0, 0, bh / 2), PLASTER)
    gable_end(bw, bd, 0.6, (0, 0, bh), PLASTER)
    gable(bw, bd, 0.6, (0, 0, bh), THATCH)
    fy = -bd / 2 - 0.012
    for x in (-bw / 2 + 0.03, bw / 2 - 0.03, 0.0):
        box((0.07, 0.03, bh), (x, fy, bh / 2), WOOD)
    for z in (0.04, 0.5, bh - 0.04):
        box((bw + 0.02, 0.03, 0.07), (0, fy, z), WOOD)
    box((0.2, 0.025, 0.22), (-0.26, fy - 0.01, 0.75), WINDOW)
    box((0.2, 0.025, 0.22), (0.26, fy - 0.01, 0.75), WINDOW)
    box((0.2, 0.03, 0.36), (0.26, fy - 0.005, 0.18), DOOR)
    box((0.2, 0.025, 0.22), (-0.26, fy - 0.01, 0.25), WINDOW)


KIT_SCALE = 0.25
med_cache = {}


def medieval(name, extra=1.0):
    """A house assembled by blender_medieval.py (front faces -Y), scaled to the island."""
    path = os.path.join(MED, name, name + ".gltf")
    if name not in med_cache:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        objs = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
        bpy.context.view_layer.update()
        for o in objs:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
            o.hide_render = True
            o.hide_set(True)
        pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
        cx = (min(p.x for p in pts) + max(p.x for p in pts)) / 2
        cy = (min(p.y for p in pts) + max(p.y for p in pts)) / 2
        med_cache[name] = (objs, Vector((cx, cy, min(p.z for p in pts))))
    objs, c = med_cache[name]
    for src in objs:
        o = src.copy()
        o.hide_render = False
        scene.collection.objects.link(o)
        o.matrix_world = src.matrix_world.copy()
        o.location = (src.location - c) * KIT_SCALE * extra
        o.scale = Vector(src.scale) * KIT_SCALE * extra


def field(w, d, crop):
    box((w, d, 0.06), (0, 0, 0.03), DIRT)
    n = int(d / 0.22)
    for i in range(n):
        box((w * 0.92, 0.1, 0.12), (0, -d / 2 + 0.11 + i * 0.22, 0.1), crop)
    for x in (-w / 2, w / 2):
        for y in (-d / 2, 0, d / 2):
            box((0.05, 0.05, 0.25), (x, y, 0.12), WOOD)
    box((0.02, d, 0.03), (-w / 2, 0, 0.2), WOOD)
    box((0.02, d, 0.03), (w / 2, 0, 0.2), WOOD)


def appear(e, frame, grow=10, rise=False):
    """Pops a building in with a little overshoot (or raises it from the ground)."""
    tiny = Vector((0.001, 0.001, 0.001))
    key(e, "scale", 0, tiny)
    if rise:  # a flat footprint first, then the walls grow up out of the ground
        key(e, "scale", frame - 1, tiny, "CONSTANT")
        tiny = Vector((1, 1, 0.001))
    key(e, "scale", frame, tiny, "BACK", "EASE_OUT")
    key(e, "scale", frame + grow, Vector((1, 1, 1)))


def vanish(e, frame):
    key(e, "scale", frame, Vector((1, 1, 1)), "BACK", "EASE_IN")
    key(e, "scale", frame + 7, Vector((0.001, 0.001, 0.001)))


def facing(x, y):
    """Rotation that turns a building's front (-Y) towards the campfire; the ones in front of the fire
    turn their fronts outwards to the camera instead."""
    dx, dy = (FX - x, FY - y) if y > FY else (x, y - 3.0)
    return math.atan2(dy, dx) + math.pi / 2


# (position, [(builder, appear frame), ...]); each stage replaces the previous one.
SLOTS = [
    ((-1.55, 0.75), [(lambda: tent(0), 40), (hut, 94), (lambda: medieval("medieval_cottage"), 160)]),
    ((1.55, 0.8), [(lambda: tent(1), 46), (hut, 100), (lambda: medieval("medieval_house"), 165)]),
    ((-1.75, -1.35), [(lambda: tent(1), 52), (timber, 118), (lambda: medieval("medieval_smithy"), 170)]),
    ((1.8, -1.3), [(lambda: tent(0), 58), (timber, 124), (lambda: medieval("medieval_market"), 175)]),
    ((-3.3, -0.1), [(hut, 110), (lambda: medieval("medieval_townhouse"), 180)]),
    ((3.35, 0.0), [(timber, 132), (lambda: medieval("medieval_barn"), 184)]),
    ((-2.75, 2.3), [(timber, 138), (lambda: medieval("medieval_house"), 188)]),
    ((2.8, 2.25), [(hut, 142), (lambda: medieval("medieval_cottage"), 192)]),
]
for (x, y), stages in SLOTS:
    prev = None
    for builder, f in stages:
        e = group(made(builder), "B", (x, y, 0.02), facing(x, y))
        appear(e, f)
        if prev is not None:
            vanish(prev, f - 4)
        prev = e
church = group(made(lambda: medieval("medieval_church", 1.3)), "Church", (0.0, 2.75, 0.02), 0.0)
appear(church, 198, grow=22, rise=True)
for (x, y, w, d, crop, f) in ((-2.4, -3.1, 1.5, 1.0, CROP_A, 150), (2.4, -3.05, 1.5, 1.0, CROP_B, 154),
                              (0.0, -3.45, 1.2, 0.8, CROP_A, 158)):
    e = group(made(lambda: field(w, d, crop)), "Field", (x, y, 0.02), facing(x, y))
    appear(e, f, grow=8)

# Clouds drifting by below and behind the island.
for i, (x, y, z, s) in enumerate(((-5.5, 4.0, -3.5, 1.2), (4.5, 5.5, -1.8, 1.0), (-1.0, 3.0, -6.8, 1.4),
                                  (6.5, 1.0, -5.5, 0.9), (-7.0, 0.0, 1.8, 0.8),
                                  (-11.5, 3.0, -1.5, 1.4), (11.0, 4.0, 0.8, 1.3), (-9.5, 7.0, 4.5, 1.1),
                                  (9.5, 2.0, -6.5, 1.2), (12.5, 8.0, 5.0, 1.0))):
    parts = []
    for k in range(5):
        parts.append(rock(0.55 * s * rnd.uniform(0.7, 1.2), ((k - 2) * 0.55 * s, rnd.uniform(-0.2, 0.2), rnd.uniform(-0.1, 0.25)),
                          CLOUD, squash=0.6, seed=80 + i * 7 + k, subdiv=2))
    e = group(parts, "Cloud", (x, y, z), 0)
    key(e, "location", 0, Vector((x - 0.6, y, z)), "LINEAR")
    key(e, "location", FRAMES[1] + 1, Vector((x + 1.2, y, z)), "LINEAR")

# ------------------------------------------------------------------ time of day
D0, D1 = 62, 122      # night -> dawn
G0, G1 = 132, 200     # dawn -> golden day


def ramp(nt, stops):
    r = nt.nodes.new("ShaderNodeValToRGB")
    cr = r.color_ramp
    cr.interpolation = "EASE"
    cr.elements[0].position, cr.elements[0].color = stops[0][0], (*stops[0][1], 1)
    cr.elements[1].position, cr.elements[1].color = stops[-1][0], (*stops[-1][1], 1)
    for p, c in stops[1:-1]:
        cr.elements.new(p).color = (*c, 1)
    return r


cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.sensor_fit = "HORIZONTAL"
cam.data.lens = 45
cam.data.clip_end = 400
pivot = bpy.data.objects.new("Pivot", None)
scene.collection.objects.link(pivot)
TARGET = Vector((0, 0.4, -0.9))
pivot.location = TARGET
ELEV, DIST = math.radians(17), 35.0
cam.parent = pivot
cam.location = Vector((0, -math.cos(ELEV) * DIST, math.sin(ELEV) * DIST))
cam.rotation_euler = (-cam.location).to_track_quat("-Z", "Y").to_euler()
for f in range(0, FRAMES[1] + 7, 6):
    key(pivot, "rotation_euler", f, Euler((0, 0, math.radians(-12) + math.radians(20) * math.sin(2 * math.pi * f / LOOP))))

# The sky: an emissive card behind everything, carried by the camera.
SKY_D = 120.0
half_w = SKY_D * 18 / cam.data.lens * 1.1
half_h = half_w / W_ASPECT
bpy.ops.mesh.primitive_plane_add(size=1)
sky = bpy.context.object
sky.parent = cam
sky.location = (0, 0, -SKY_D)
sky.scale = (half_w * 2, half_h * 2, 1)
sky.visible_shadow = sky.visible_diffuse = sky.visible_glossy = False
m = bpy.data.materials.new("Sky")
m.use_nodes = True
nt = m.node_tree
nt.nodes.clear()
N = nt.nodes.new
tc = N("ShaderNodeTexCoord")
sep = N("ShaderNodeSeparateXYZ")
nt.links.new(tc.outputs["UV"], sep.inputs[0])
night = ramp(nt, [(0.0, (0.006, 0.01, 0.03)), (0.38, (0.035, 0.06, 0.13)), (0.6, (0.014, 0.03, 0.075)),
                  (1.0, (0.003, 0.006, 0.022))])
dawn = ramp(nt, [(0.0, (0.10, 0.03, 0.10)), (0.30, (0.55, 0.12, 0.14)), (0.42, (1.0, 0.38, 0.10)),
                 (0.52, (0.85, 0.32, 0.12)), (0.68, (0.06, 0.18, 0.22)), (1.0, (0.008, 0.04, 0.08))])
day = ramp(nt, [(0.0, (0.40, 0.30, 0.32)), (0.34, (1.0, 0.62, 0.28)), (0.48, (0.95, 0.70, 0.40)),
                (0.66, (0.30, 0.50, 0.72)), (1.0, (0.06, 0.20, 0.50))])
for r in (night, dawn, day):
    nt.links.new(sep.outputs["Y"], r.inputs[0])
mix1 = N("ShaderNodeMix")
mix1.data_type = "RGBA"
nt.links.new(night.outputs[0], mix1.inputs["A"])
nt.links.new(dawn.outputs[0], mix1.inputs["B"])
mix2 = N("ShaderNodeMix")
mix2.data_type = "RGBA"
nt.links.new(mix1.outputs["Result"], mix2.inputs["A"])
nt.links.new(day.outputs[0], mix2.inputs["B"])
for f, v in ((0, 0.0), (D0, 0.0), (D1, 1.0)):
    key_input(mix1.inputs["Factor"], f, v)
for f, v in ((0, 0.0), (G0, 0.0), (G1, 1.0)):
    key_input(mix2.inputs["Factor"], f, v)
vor = N("ShaderNodeTexVoronoi")
vor.inputs["Scale"].default_value = 60
uvs = N("ShaderNodeVectorMath")  # square star cells on the wide card
uvs.operation = "MULTIPLY"
uvs.inputs[1].default_value = (W_ASPECT, 1, 1)
nt.links.new(tc.outputs["UV"], uvs.inputs[0])
nt.links.new(uvs.outputs[0], vor.inputs["Vector"])
star = N("ShaderNodeMapRange")
star.inputs["From Min"].default_value = 0.06
star.inputs["From Max"].default_value = 0.0
nt.links.new(vor.outputs["Distance"], star.inputs["Value"])
sel = N("ShaderNodeMath")
sel.operation = "GREATER_THAN"
sel.inputs[1].default_value = 0.55
nt.links.new(vor.outputs["Color"], sel.inputs[0])
mask = N("ShaderNodeMapRange")
mask.inputs["From Min"].default_value = 0.42
mask.inputs["From Max"].default_value = 0.65
nt.links.new(sep.outputs["Y"], mask.inputs["Value"])
mul = N("ShaderNodeMath")
mul.operation = "MULTIPLY"
nt.links.new(star.outputs["Result"], mul.inputs[0])
nt.links.new(sel.outputs[0], mul.inputs[1])
mul2 = N("ShaderNodeMath")
mul2.operation = "MULTIPLY"
nt.links.new(mul.outputs[0], mul2.inputs[0])
nt.links.new(mask.outputs["Result"], mul2.inputs[1])
starfade = N("ShaderNodeMath")
starfade.operation = "MULTIPLY"
nt.links.new(mul2.outputs[0], starfade.inputs[0])
for f, v in ((0, 1.6), (D0, 1.6), (D1 - 10, 0.0)):
    key_input(starfade.inputs[1], f, v)
addst = N("ShaderNodeMix")
addst.data_type = "RGBA"
addst.blend_type = "ADD"
nt.links.new(starfade.outputs[0], addst.inputs["Factor"])
nt.links.new(mix2.outputs["Result"], addst.inputs["A"])
addst.inputs["B"].default_value = (0.85, 0.92, 1.0, 1)
em = N("ShaderNodeEmission")
nt.links.new(addst.outputs["Result"], em.inputs["Color"])
outn = N("ShaderNodeOutputMaterial")
nt.links.new(em.outputs[0], outn.inputs[0])
sky.data.materials.append(m)

world = bpy.data.worlds.new("Sky")
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
scene.world = world
for f, c, s in ((0, (0.04, 0.07, 0.16), 1.0), (D0, (0.04, 0.07, 0.16), 1.0), (D1, (0.30, 0.18, 0.18), 0.8),
                (G0, (0.30, 0.18, 0.18), 0.8), (G1, (0.45, 0.48, 0.55), 1.0)):
    key_input(bg.inputs[0], f, (*c, 1))
    key_input(bg.inputs[1], f, s)

moon = bpy.data.objects.new("Moon", bpy.data.lights.new("Moon", "SUN"))
moon.data.color = (0.5, 0.65, 1.0)
moon.rotation_euler = (math.radians(50), 0, math.radians(-40))
scene.collection.objects.link(moon)
for f, e in ((0, 2.2), (D0, 2.2), (D1, 0.0)):
    key(moon.data, "energy", f, e)
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.angle = math.radians(4)
scene.collection.objects.link(sun)


def sun_dir(elev, az):
    s = Vector((math.cos(elev) * math.sin(az), -math.cos(elev) * math.cos(az), math.sin(elev)))
    return (-s).to_track_quat("-Z", "Y").to_euler()


for f, elev, az, e, c in ((0, -5, 70, 0.0, (1.0, 0.4, 0.15)), (D0, -5, 70, 0.0, (1.0, 0.4, 0.15)),
                          (D1, 8, 62, 2.6, (1.0, 0.45, 0.2)), (G1, 38, 40, 4.2, (1.0, 0.85, 0.65)),
                          (FRAMES[1] + 1, 42, 36, 4.2, (1.0, 0.86, 0.68))):
    key(sun, "rotation_euler", f, sun_dir(math.radians(elev), math.radians(az)))
    key(sun.data, "energy", f, e)
    key(sun.data, "color", f, Vector(c))
for f in range(0, FRAMES[1] + 4, 3):
    base = 120 if f < D0 else (120 - 70 * min(1, (f - D0) / (G1 - D0)))
    fl = 1 + 0.12 * math.sin(2 * math.pi * 22 * f / LOOP) + 0.07 * math.sin(2 * math.pi * 37 * f / LOOP + 1)
    key(fire.data, "energy", f, base * fl)
we = WINDOW.node_tree.nodes["Emission"].inputs["Strength"]
for f, v in ((0, 3.0), (D1, 3.0), (G1, 0.6)):
    key_input(we, f, v)

# Frames LOOP.. must match frames 0.. for the cross-fade: everything except the camera and the clouds
# is either periodic over LOOP or static by then, and the encoder blends the two.

# ------------------------------------------------------------------ render
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = SAMPLES
scene.cycles.use_denoising = True
scene.cycles.denoiser = "OPENIMAGEDENOISE"
scene.cycles.max_bounces = 4
scene.cycles.caustics_reflective = scene.cycles.caustics_refractive = False
scene.cycles.blur_glossy = 1.0
scene.render.use_persistent_data = True
scene.render.resolution_x = RES
scene.render.resolution_y = int(round(RES / W_ASPECT / 2)) * 2
scene.render.fps = FPS
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGB"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"
scene.use_nodes = True
ct = scene.node_tree
rl = ct.nodes.get("Render Layers") or ct.nodes.new("CompositorNodeRLayers")
comp = ct.nodes.get("Composite") or ct.nodes.new("CompositorNodeComposite")
glare = ct.nodes.new("CompositorNodeGlare")
glare.glare_type = "FOG_GLOW"
glare.quality = "MEDIUM"
glare.threshold = 1.0
glare.size = 7
glare.mix = -0.4
ct.links.new(rl.outputs["Image"], glare.inputs["Image"])
ct.links.new(glare.outputs["Image"], comp.inputs["Image"])

os.makedirs(OUT, exist_ok=True)
if STILLS:
    for f in STILLS:
        scene.frame_set(f)
        scene.render.filepath = os.path.join(OUT, f"still_{f:04d}.png")
        bpy.ops.render.render(write_still=True)
        print("STILL", scene.render.filepath, flush=True)
else:
    scene.frame_start, scene.frame_end = FRAMES
    scene.render.filepath = os.path.join(OUT, "f_####")
    scene.render.use_overwrite = False  # resumable
    bpy.ops.render.render(animation=True)
print("RISE DONE", OUT)
