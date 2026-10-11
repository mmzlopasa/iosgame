"""Blender (4.2): assembles complete medieval houses from the CC0 Quaternius "Medieval Village
MegaKit" modules (2 m wall grid, 3 m storeys) and exports each house as a glTF (separate
textures) for the usual model pipeline, plus an optional preview render.

blender -b --factory-startup -P blender_medieval.py -- <kit glTF dir> <out dir> [--preview]
"""
import math
import os
import sys

import bpy
from mathutils import Euler, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
KIT, OUT = argv[0], argv[1]
PREVIEW = "--preview" in argv

# House designs: footprint in 2 m modules, storeys, wall styles per storey, roof module.
HOUSES = {
    "medieval_cottage": dict(w=2, d=2, floors=[("UnevenBrick", "door")], roof="Roof_RoundTiles_4x4", gable="Roof_Front_Brick4", chimney=True),
    "medieval_house": dict(w=2, d=2, floors=[("UnevenBrick", "door"), ("Plaster", "window")], roof="Roof_RoundTiles_4x4", gable="Roof_Front_Brick4", chimney=True),
    "medieval_townhouse": dict(w=2, d=3, floors=[("UnevenBrick", "door"), ("Plaster", "window"), ("Plaster", "grid")], roof="Roof_RoundTiles_4x6", gable="Roof_Front_Brick4", chimney=True),
}

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
    cache[name] = objs
    for o in objs:
        o.hide_render = True
        o.hide_set(True)
    return objs


def place(name, loc, rot_z=0.0, collection=None):
    out = []
    for src in load(name):
        o = src.copy()
        o.data = src.data
        o.hide_render = False
        (collection or bpy.context.scene.collection).objects.link(o)
        o.matrix_world = src.matrix_world.copy()
        o.rotation_mode = "XYZ"  # glTF imports use quaternions; rotate around Z explicitly
        o.rotation_euler = Euler((0, 0, rot_z))
        o.location = Vector(loc) + Euler((0, 0, rot_z)).to_matrix() @ src.matrix_world.translation
        out.append(o)
    return out


def wall_module(style, kind, front):
    if style == "UnevenBrick":
        if kind == "door" and front:
            return "Wall_UnevenBrick_Door_Round"
        return "Wall_UnevenBrick_Window_Wide_Round" if kind != "door" else "Wall_UnevenBrick_Straight"
    if kind == "door" and front:
        return "Wall_Plaster_Door_Round"
    if kind == "window":
        return "Wall_Plaster_Window_Wide_Round"
    if kind == "grid":
        return "Wall_Plaster_WoodGrid"
    return "Wall_Plaster_Straight"


def wall(style, kind, front, loc, rot, col):
    module = wall_module(style, kind, front)
    place(module, loc, rot, col)
    if "Window" in module:
        place("Window_Wide_Round1", loc, rot, col)


def build(name, spec):
    col = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(col)
    W, D = spec["w"] * 2.0, spec["d"] * 2.0
    h = 3.0
    for f, (style, kind) in enumerate(spec["floors"]):
        z = f * h
        # Front (−y) and back (+y) walls, left/right walls.
        for i in range(spec["w"]):
            x = -W / 2 + 1 + 2 * i
            front_kind = kind if (i == spec["w"] // 2 or kind != "door") else "window"
            wall(style, front_kind, True, (x, -D / 2, z), 0.0, col)
            wall(style, "window" if kind != "door" else "plain", False, (x, D / 2, z), math.pi, col)
        for j in range(spec["d"]):
            y = -D / 2 + 1 + 2 * j
            side = "window" if kind != "door" or j % 2 == 0 else "plain"
            wall(style, side, False, (-W / 2, y, z), -math.pi / 2, col)
            wall(style, side, False, (W / 2, y, z), math.pi / 2, col)
        for cx in (-W / 2, W / 2):
            for cy in (-D / 2, D / 2):
                place("Corner_Exterior_Wood", (cx, cy, z), 0.0, col)
        # Floor of this storey
        for i in range(spec["w"]):
            for j in range(spec["d"]):
                place("Floor_WoodDark", (-W / 2 + 1 + 2 * i, -D / 2 + 1 + 2 * j, z + 0.01), 0.0, col)
    top = len(spec["floors"]) * h
    place(spec["roof"], (0, 0, top), 0.0, col)
    place(spec["gable"], (0, -D / 2, top), 0.0, col)
    place(spec["gable"], (0, D / 2, top), math.pi, col)
    if spec.get("chimney"):
        place("Prop_Chimney", (W / 2 - 0.6, D / 2 - 0.9, top - 0.2), 0.0, col)
    return col


def export(col, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in col.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = col.objects[0]
    path = os.path.join(OUT, name, name + ".gltf")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLTF_SEPARATE", use_selection=True, export_image_format="AUTO")
    return path


def preview(cols):
    scene = bpy.context.scene
    for i, col in enumerate(cols):
        for o in col.objects:
            o.location.x += i * 9.0
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 9.0 * len(cols) * 1.05
    target = Vector(((len(cols) - 1) * 4.5, 0, 4.5))
    cam.location = target + Vector((14, -18, 16))
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
    bpy.ops.mesh.primitive_plane_add(size=80, location=(target.x, 0, 0))
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 48
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 800
    scene.render.filepath = os.path.join(OUT, "medieval_preview.png")
    bpy.ops.render.render(write_still=True)


bpy.ops.wm.read_factory_settings(use_empty=True)
cols = []
for name, spec in HOUSES.items():
    col = build(name, spec)
    cols.append(col)
    export(col, name)
    print("HOUSE", name, sum(len(p.vertices) - 2 for o in col.objects for p in o.data.polygons), "tris")
if PREVIEW:
    preview(cols)
