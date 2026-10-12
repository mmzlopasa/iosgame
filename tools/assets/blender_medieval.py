"""Blender (4.2): assembles complete medieval buildings from the CC0 Quaternius "Medieval Village
MegaKit" modules (2 m wall grid, 3 m storeys) and exports each one as a glTF (separate
textures) for the model pipeline (build_medieval.py), plus an optional preview render.

Buildings: cottage, house, townhouse, church (nave and bell tower), market hall, watermill
(with a wooden water wheel) and smithy (with a lean-to forge).

blender -b --factory-startup -P blender_medieval.py -- <kit glTF dir> <out dir> [--preview] [--only a,b]
"""
import math
import os
import re
import sys

import bpy
from mathutils import Euler, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
KIT, OUT = argv[0], argv[1]
PREVIEW = "--preview" in argv
ONLY = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None

cache = {}


def load(name):
    """Imports a kit module once and returns its objects (kept hidden as a library)."""
    if name in cache:
        return cache[name]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(KIT, name + ".gltf"))
    objs = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    for o in objs:
        o.parent = None
        o.hide_render = True
        o.hide_set(True)
    cache[name] = objs
    return objs


def place(name, loc, rot_z=0.0, col=None, scale=1.0):
    out = []
    for src in load(name):
        o = src.copy()
        o.data = src.data
        o.hide_render = False
        (col or bpy.context.scene.collection).objects.link(o)
        o.matrix_world = src.matrix_world.copy()
        o.rotation_mode = "XYZ"  # glTF imports use quaternions; rotate around Z explicitly
        o.rotation_euler = Euler((0, 0, rot_z))
        o.scale = (scale, scale, scale)
        o.location = Vector(loc) + Euler((0, 0, rot_z)).to_matrix() @ (src.matrix_world.translation * scale)
        out.append(o)
    return out


def wall_module(style, kind, front):
    if style == "UnevenBrick":
        if kind == "door" and front:
            return "Wall_UnevenBrick_Door_Round"
        if kind == "thin":
            return "Wall_UnevenBrick_Window_Thin_Round"
        return "Wall_UnevenBrick_Window_Wide_Round" if kind not in ("door", "plain") else "Wall_UnevenBrick_Straight"
    if kind == "door" and front:
        return "Wall_Plaster_Door_Round"
    if kind == "window":
        return "Wall_Plaster_Window_Wide_Round"
    if kind == "thin":
        return "Wall_Plaster_Window_Thin_Round"
    if kind == "grid":
        return "Wall_Plaster_WoodGrid"
    return "Wall_Plaster_Straight"


def wall(style, kind, front, loc, rot, col):
    module = wall_module(style, kind, front)
    place(module, loc, rot, col)
    if "Window_Wide" in module:
        place("Window_Wide_Round1", loc, rot, col)
    elif "Window_Thin" in module:
        place("Window_Thin_Round1", loc, rot, col)


def block(col, w, d, floors, origin=(0.0, 0.0), roof=None, gable=None, chimney=False, open_front=False, door=True):
    """A rectangular block of w×d wall modules with storeys, floors, corners and an optional roof.
    floors: list of (style, kind) per storey; the front faces −y."""
    ox, oy = origin
    W, D = w * 2.0, d * 2.0
    h = 3.0
    for f, (style, kind) in enumerate(floors):
        z = f * h
        for i in range(w):
            x = ox - W / 2 + 1 + 2 * i
            front_kind = kind
            if f == 0 and door:
                front_kind = "door" if i == w // 2 else ("window" if kind == "door" else kind)
            if not (open_front and f == 0):
                wall(style, front_kind, True, (x, oy - D / 2, z), 0.0, col)
            wall(style, "window" if kind == "door" else kind, False, (x, oy + D / 2, z), math.pi, col)
        for j in range(d):
            y = oy - D / 2 + 1 + 2 * j
            side = "window" if kind == "door" and j % 2 == 0 else ("plain" if kind == "door" else kind)
            wall(style, side, False, (ox - W / 2, y, z), -math.pi / 2, col)
            wall(style, side, False, (ox + W / 2, y, z), math.pi / 2, col)
        for cx in (ox - W / 2, ox + W / 2):
            for cy in (oy - D / 2, oy + D / 2):
                place("Corner_Exterior_Wood", (cx, cy, z), 0.0, col)
        for i in range(w):
            for j in range(d):
                place("Floor_WoodDark", (ox - W / 2 + 1 + 2 * i, oy - D / 2 + 1 + 2 * j, z + 0.01), 0.0, col)
    top = len(floors) * h
    if roof:
        place(roof, (ox, oy, top), 0.0, col)
        if gable:
            place(gable, (ox, oy - D / 2, top), 0.0, col)
            place(gable, (ox, oy + D / 2, top), math.pi, col)
    if chimney:
        place("Prop_Chimney", (ox + W / 2 - 0.6, oy + D / 2 - 0.9, top - 0.2), 0.0, col)
    return top


def kit_material(base):
    for m in bpy.data.materials:
        if re.sub(r"\.\d+$", "", m.name) == base:
            return m
    return None


def wheel(col, center, radius=1.5, width=0.5, paddles=12):
    """A wooden water wheel turning around the x axis."""
    wood = kit_material("MI_WoodTrim")
    parts = []
    for side in (-1, 1):
        bpy.ops.mesh.primitive_torus_add(major_radius=radius, minor_radius=0.07, major_segments=24, minor_segments=6,
                                         location=(center[0] + side * width / 2, center[1], center[2]),
                                         rotation=(0, math.pi / 2, 0))
        parts.append(bpy.context.object)
    for k in range(paddles):
        a = k / paddles * math.tau
        y, z = math.cos(a) * radius * 0.92, math.sin(a) * radius * 0.92
        bpy.ops.mesh.primitive_cube_add(size=1, location=(center[0], center[1] + y, center[2] + z),
                                        rotation=(a, 0, 0))
        p = bpy.context.object
        p.scale = (width + 0.1, 0.06, 0.45)
        parts.append(p)
        if k % 3 == 0:
            bpy.ops.mesh.primitive_cylinder_add(radius=0.05, depth=radius * 2, location=center, rotation=(a, 0, 0))
            parts.append(bpy.context.object)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.12, depth=width + 0.8, location=center, rotation=(0, math.pi / 2, 0))
    parts.append(bpy.context.object)
    for p in parts:
        p.data.materials.clear()
        p.data.materials.append(wood)
        for c in list(p.users_collection):
            c.objects.unlink(p)
        col.objects.link(p)


def cube(col, loc, size, material):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.object
    o.scale = size
    o.data.materials.append(kit_material(material))
    for c in list(o.users_collection):
        c.objects.unlink(o)
    col.objects.link(o)
    return o


# MARK: - Buildings

def cottage(col):
    block(col, 2, 2, [("UnevenBrick", "door")], roof="Roof_RoundTiles_4x4", gable="Roof_Front_Brick4", chimney=True)


def house(col):
    block(col, 2, 2, [("UnevenBrick", "door"), ("Plaster", "window")], roof="Roof_RoundTiles_4x4",
          gable="Roof_Front_Brick4", chimney=True)


def townhouse(col):
    block(col, 2, 3, [("UnevenBrick", "door"), ("Plaster", "window"), ("Plaster", "grid")], roof="Roof_RoundTiles_4x6",
          gable="Roof_Front_Brick4", chimney=True)


def church(col):
    # Nave 6×8 m with tall windows; a square bell tower in front with the main door.
    block(col, 3, 4, [("UnevenBrick", "thin"), ("UnevenBrick", "thin")], origin=(0, 2.0), roof="Roof_RoundTiles_6x8",
          gable="Roof_Front_Brick6", door=False)
    block(col, 2, 2, [("UnevenBrick", "door"), ("UnevenBrick", "thin"), ("UnevenBrick", "plain"), ("UnevenBrick", "thin")],
          origin=(0, -4.0))
    place("Roof_Tower_RoundTiles", (0, -4.0, 12.0), 0.0, col, scale=0.78)


def market(col):
    # An open market hall: tiled roof on timber posts, stalls of crates and a wagon.
    for x in (-3.0, 0.0, 3.0):
        for y in (-3.0, 3.0):
            place("Corner_Exterior_Wood", (x, y, 0.0), 0.0, col)
    for y in (-1.0, 1.0):
        for x in (-3.0, 3.0):
            place("Corner_Exterior_Wood", (x, y, 0.0), 0.0, col)
    place("Roof_RoundTiles_6x6", (0, 0, 3.0), 0.0, col)
    for i in range(3):
        for j in range(3):
            place("Floor_UnevenBrick", (-2 + 2 * i, -2 + 2 * j, 0.02), 0.0, col)
    for x, y, r in ((-1.6, -1.2, 0.2), (-1.0, 1.4, 0.0), (1.5, 0.6, 0.6), (0.4, -1.6, 1.1)):
        place("Prop_Crate", (x, y, 0.0), r, col, scale=0.8)
    place("Prop_Crate", (-1.6, -1.2, 0.85), 0.5, col, scale=0.6)
    place("Prop_Wagon", (2.2, -2.4, 0.0), math.pi / 2, col, scale=0.8)


def mill(col):
    block(col, 2, 2, [("UnevenBrick", "door"), ("Plaster", "window")], roof="Roof_RoundTiles_4x4",
          gable="Roof_Front_Brick4")
    wheel(col, (2.6, 0.0, 1.5))


def smithy(col):
    # Stone workshop with two chimneys and an open forge under a lean-to roof in front.
    block(col, 2, 2, [("UnevenBrick", "door")], origin=(0, 1.0), roof="Roof_RoundTiles_4x4", gable="Roof_Front_Brick4",
          chimney=True)
    place("Prop_Chimney", (-1.4, 2.1, 2.8), 0.0, col)
    for x in (-2.0, 2.0):
        place("Corner_Exterior_Wood", (x, -2.6, -0.8), 0.0, col)
    for x in (-1.0, 1.0):
        place("Roof_Wooden_2x1", (x, -1.0, 2.2), 0.0, col)
    cube(col, (-0.6, -1.8, 0.45), (1.0, 0.8, 0.9), "MI_RockTrim")       # forge hearth
    cube(col, (0.9, -1.9, 0.35), (0.35, 0.6, 0.7), "MI_RockTrim")       # anvil block
    cube(col, (0.9, -1.9, 0.75), (0.25, 0.75, 0.12), "MI_MetalOrnaments")
    place("Prop_Crate", (1.7, -2.3, 0.0), 0.3, col, scale=0.7)


def barn(col):
    # A wide timber barn at the back of a fenced paddock.
    block(col, 2, 3, [("Plaster", "grid")], origin=(-1.0, 1.0), roof="Roof_RoundTiles_4x6", gable="Roof_Front_Brick4",
          open_front=True, door=False)
    place("Wall_Arch", (-1.0, -2.0, 0), 0.0, col)
    for x in (-2.8, -1.4, 0.0, 1.4, 2.8):
        place("Prop_WoodenFence_Single", (x, -2.9, 0), 0.0, col)
    for y in (-2.2, -0.8):
        place("Prop_WoodenFence_Single", (-3.0, y, 0), math.pi / 2, col)
        place("Prop_WoodenFence_Single", (3.0, y, 0), math.pi / 2, col)
    place("Prop_Crate", (2.2, 0.4, 0), 0.4, col, scale=0.7)
    place("Prop_Wagon", (-2.1, 0.3, 0), 0.2, col, scale=0.6)


BUILDINGS = {
    "medieval_cottage": cottage,
    "medieval_house": house,
    "medieval_townhouse": townhouse,
    "medieval_church": church,
    "medieval_market": market,
    "medieval_mill": mill,
    "medieval_smithy": smithy,
    "medieval_barn": barn,
}


def dedupe_materials(col):
    """Kit modules each import their own copy of the materials; share one per kind."""
    for o in col.objects:
        for slot in o.material_slots:
            if slot.material:
                base = kit_material(re.sub(r"\.\d+$", "", slot.material.name))
                if base:
                    slot.material = base


def export(col, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in col.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = col.objects[0]
    path = os.path.join(OUT, name, name + ".gltf")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLTF_SEPARATE", use_selection=True,
                              export_image_format="AUTO")
    return path


def preview(cols):
    scene = bpy.context.scene
    x = 0.0
    for col in cols:
        for o in col.objects:
            o.location.x += x
        x += 14.0
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = max(20.0, 14.0 * len(cols) * 0.75)
    target = Vector(((len(cols) - 1) * 7.0, 0, 4.0))
    cam.location = target + Vector((20, -26, 22))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.0
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(35))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.5, 0.58, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    scene.world = world
    bpy.ops.mesh.primitive_plane_add(size=200, location=(target.x, 0, 0))
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.render.resolution_x = 2400
    scene.render.resolution_y = 700
    scene.render.filepath = os.path.join(OUT, "medieval_preview.png")
    bpy.ops.render.render(write_still=True)


bpy.ops.wm.read_factory_settings(use_empty=True)
cols = []
for name, build in BUILDINGS.items():
    if ONLY and name not in ONLY:
        continue
    col = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(col)
    build(col)
    dedupe_materials(col)
    cols.append(col)
    export(col, name)
    tris = sum(len(p.vertices) - 2 for o in col.objects for p in o.data.polygons)
    print("BUILDING", name, tris, "tris", sorted({s.material.name for o in col.objects for s in o.material_slots if s.material}))
if PREVIEW:
    preview(cols)
