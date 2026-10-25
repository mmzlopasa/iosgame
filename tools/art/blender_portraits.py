"""Blender (4.2): the founder portraits (face_m1 … face_f3) as painted head-and-shoulders pictures.

Each portrait is one of the game's own villagers (people.json: the Person_*.fbx model with its baked
painted texture, the game's chunky proportions from tools/assets/person_style.py and a dye and hair
colour), seen three-quarters from the front at eye height under a warm key light with a cool rim,
rendered with a transparent background at 768 px. tools/art/post_portraits.py paints the backdrop
and writes the 256 px icons.

blender -b --factory-startup -P blender_portraits.py -- <out dir> [face_m1 ...] [--samples N]
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "assets"))
from person_style import soften, stylize, subdivide  # noqa: E402

ASSETS = os.path.join(ROOT, "unity", "Assets")
MANIFEST = os.path.join(ASSETS, "Art", "Resources", "people.json")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SAMPLES = int(argv[argv.index("--samples") + 1]) if "--samples" in argv else 96
if "--samples" in argv:
    i = argv.index("--samples")
    argv = argv[:i] + argv[i + 2:]
OUT = argv[0] if argv else os.path.join(HERE, ".cache", "portraits")
ONLY = argv[1:]
SIZE = 768

# Portrait id -> (model, skin sRGB, dye sRGB or None for the model's own clothes, hair sRGB, yaw of the head)
PORTRAITS = {
    "face_m1": ("Person_Elder_Male", (0.80, 0.60, 0.47), (0.55, 0.36, 0.22), (0.80, 0.79, 0.76), -12),   # Elder
    "face_f1": ("Person_Elder_Female", (0.87, 0.68, 0.56), (0.30, 0.48, 0.44), (0.74, 0.73, 0.71), -6),  # Keeper
    "face_m2": ("Person_Hunter_Male", (0.70, 0.50, 0.37), None, (0.18, 0.12, 0.07), -14),                 # Hunter
    "face_f2": ("Person_Gatherer_Female", (0.80, 0.60, 0.47), (0.46, 0.55, 0.30), (0.42, 0.22, 0.12), -10),  # Healer
    "face_m3": ("Person_Woodcutter_Male", (0.87, 0.68, 0.56), (0.66, 0.31, 0.22), (0.30, 0.20, 0.11), -12),  # Builder
    "face_f3": ("Person_Weaver_Female", (0.56, 0.39, 0.28), (0.32, 0.45, 0.62), (0.09, 0.065, 0.05), -8),  # Weaver
}


def lin(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def setup():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = SIZE
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.63, 0.78, 1)
    bg.inputs[1].default_value = 0.3
    scene.world = world

    def area(name, loc, energy, size, color):
        light = bpy.data.lights.new(name, "AREA")
        light.energy = energy
        light.size = size
        light.color = color
        o = bpy.data.objects.new(name, light)
        o.location = loc
        o.rotation_euler = (Vector((0, 0, 1.55)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(o)

    # Warm key from the front left and above, a cool rim from behind right, a soft fill from below.
    area("key", (-1.3, -1.9, 2.4), 120, 1.6, (1.0, 0.86, 0.68))
    area("rim", (1.4, 1.3, 2.0), 140, 1.0, (0.62, 0.80, 1.0))
    area("fill", (0.8, -2.0, 1.1), 22, 2.5, (0.85, 0.9, 1.0))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 105
    return scene, cam


def texture(path, non_color):
    im = bpy.data.images.load(os.path.join(ASSETS, path), check_existing=True)
    if non_color:
        im.colorspace_settings.name = "Non-Color"
    return im


def material(p, zone, tint):
    """The game's painted villager material: albedo x tint, matte, a hint of the normal map; soft skin."""
    m = bpy.data.materials.new(f"{p['model']}_{zone}")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    t = p["textures"]
    alb = nt.nodes.new("ShaderNodeTexImage")
    alb.image = texture(t["albedo"], False)
    nrm = nt.nodes.new("ShaderNodeTexImage")
    nrm.image = texture(t["normal"], True)
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs[0].default_value = 1
    nt.links.new(alb.outputs["Color"], mul.inputs[6])
    mul.inputs[7].default_value = (*tint, 1)
    nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.75
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nm.inputs["Strength"].default_value = 0.25
    nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    if zone == "Skin":
        bsdf.inputs["Subsurface Weight"].default_value = 0.2
        bsdf.inputs["Subsurface Radius"].default_value = (0.03, 0.012, 0.008)
        bsdf.inputs["Roughness"].default_value = 0.55
    return m


def load(p, skin, dye, hair):
    before, acts = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ASSETS, "Art", "Resources", p["model"] + ".fbx"))
    objs = [o for o in bpy.data.objects if o not in before]
    rig = next(o for o in objs if o.type == "ARMATURE")
    clips = [a for a in bpy.data.actions if a not in acts]
    stylize(rig, objs)
    subdivide(objs)
    soften(objs)
    neutral = p["neutral"]
    tints = {"Skin": lin(skin) / np.maximum(1e-3, np.asarray(p["skin"])),
             "Main": lin(dye) / neutral if dye is not None and p.get("dye", True) else np.ones(3),
             "Hair": lin(hair) / neutral, "Rest": np.ones(3)}
    for o in objs:
        if o.type != "MESH":
            continue
        if o.name.startswith("Prop_") or o.name.startswith("Body_LOD1"):
            o.hide_render = True
            continue
        for slot in o.material_slots:
            zone = slot.material.name.split(".")[0]
            slot.material = material(p, zone, tuple(np.clip(tints.get(zone, np.ones(3)), 0, 4)))
    idle = next((a for a in clips if a.name.split("|")[-1] == "Idle"), None)
    if idle is not None:
        rig.animation_data.action = idle
        f0, f1 = idle.frame_range
        # The head and neck are ours: level, chin up a touch, so the eyes meet the viewer.
        for fc in [f for f in idle.fcurves if any(f.data_path == f'pose.bones["{b}"].{k}' for b in ("Head", "neck_01")
                                                   for k in ("rotation_quaternion", "rotation_euler"))]:
            idle.fcurves.remove(fc)
        bpy.context.scene.frame_set(int(f0 + (f1 - f0) * 0.3))
        for b, pitch in (("neck_01", -8), ("Head", -10)):
            if b in rig.pose.bones:
                rig.pose.bones[b].rotation_mode = "XYZ"
                rig.pose.bones[b].rotation_euler = (math.radians(pitch), 0, 0)
        bpy.context.view_layer.update()
    return rig, objs


def main():
    os.makedirs(OUT, exist_ok=True)
    people = {q["model"].split("/")[-1]: q for q in json.load(open(MANIFEST))["people"] if "textures" in q}
    for name, (model, skin, dye, hair, yaw) in PORTRAITS.items():
        if ONLY and name not in ONLY:
            continue
        scene, cam = setup()
        rig, objs = load(people[model], skin, dye, hair)
        bpy.context.view_layer.update()
        head = rig.matrix_world @ rig.pose.bones["Head"].head
        neck = rig.matrix_world @ rig.pose.bones["neck_01"].head
        # Head and shoulders: aim a little below the eyes; the camera three-quarters from the front at eye height.
        target = Vector((head.x, head.y, head.z + 0.02))
        dist = 2.0
        a = math.radians(yaw)
        cam.location = target + Vector((math.sin(-a) * dist, -math.cos(a) * dist, 0.12))
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(OUT, name + ".png")
        bpy.ops.render.render(write_still=True)
        print("PORTRAIT", name, flush=True)


main()
