"""Blender (4.2) script: render a (film-quality, very heavy) tree into billboard textures that
an iPhone can draw cheaply. Produces <out>_side.png, <out>_side2.png (two side views, 90° apart)
and <out>_top.png (seen from above), all with transparent background.

blender -b --factory-startup -P blender_impostor.py -- in.gltf OUT_PREFIX info.json [preview]
With "preview" it renders one 3/4 view to OUT_PREFIX_preview.png instead (used to check models).
"""
import json
import math
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_prefix, info_path = argv[0], argv[1], argv[2]
preview = len(argv) > 3 and argv[3] == "preview"
only_node = argv[3] if len(argv) > 3 and argv[3] != "preview" else None

bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith(".fbx"):
    bpy.ops.import_scene.fbx(filepath=src)
else:
    bpy.ops.import_scene.gltf(filepath=src)
scene = bpy.context.scene

if only_node:
    # Keep one tree of a multi-tree asset: the node and everything parented under it.
    keep = bpy.data.objects.get(only_node)
    keep_set = {keep} | set(keep.children_recursive) if keep else set()
    for o in list(scene.objects):
        if keep and o not in keep_set:
            bpy.data.objects.remove(o, do_unlink=True)

meshes = [o for o in scene.objects if o.type == "MESH"]
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
center = (lo + hi) / 2
width = max(hi.x - lo.x, hi.y - lo.y)
height = hi.z - lo.z

# Render settings: Cycles on CPU, transparent background, denoised.
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 48
scene.cycles.use_denoising = True
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"

# Soft daylight: sky colour + sun, matching the in-game sun direction roughly.
world = bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.62, 0.72, 0.85, 1.0)
bg.inputs[1].default_value = 0.9
sun_data = bpy.data.lights.new("Sun", "SUN")
sun_data.energy = 3.2
sun_data.angle = math.radians(8)
sun = bpy.data.objects.new("Sun", sun_data)
sun.rotation_euler = (math.radians(42), 0, math.radians(30))
scene.collection.objects.link(sun)

cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam


def render(path, direction, ortho, res_x, res_y):
    cam_data.type = "ORTHO" if ortho else "PERSP"
    if ortho:
        cam_data.ortho_scale = ortho
    else:
        cam_data.lens = 50
    dist = max(width, height) * 3
    cam.location = center + direction.normalized() * dist
    look = (center - cam.location).normalized()
    cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
    cam_data.clip_end = dist * 4
    scene.render.resolution_x = res_x
    scene.render.resolution_y = res_y
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


if preview:
    scene.render.film_transparent = False
    render(out_prefix + "_preview.png", Vector((1.2, -1.6, 1.0)), None, 640, 480)
else:
    side_scale = max(width, height) * 1.02
    res = 512
    aspect_h = int(res * 2) if height > width * 1.3 else res
    # Side views: frame width x height exactly so the quad size maps 1:1.
    scene.render.pixel_aspect_x = 1
    cam_data.sensor_fit = "AUTO"
    render(out_prefix + "_side.png", Vector((0, -1, 0)), side_scale, int(aspect_h * width / max(width, height)) if height > width else res, aspect_h)
    render(out_prefix + "_side2.png", Vector((1, 0, 0)), side_scale, int(aspect_h * width / max(width, height)) if height > width else res, aspect_h)
    render(out_prefix + "_top.png", Vector((0, 0, 1)), width * 1.02, res, res)

with open(info_path, "w") as f:
    json.dump({"width": round(width, 3), "height": round(height, 3)}, f)
