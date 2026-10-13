"""Blender (4.2): review sheet of the finished villagers as the game shows them.

Imports every Person_*.fbx listed in people.json with its baked texture set and the game's
per-person dye (Main garment, Hair) and skin tone, exactly as Villager.Spawn computes them
(albedo x tint, tint = colour / neutral grey or skin tone / painted skin), and renders each
person from the front (Idle) and the side (Walk), plus a row of dye variants and a close-up of a
woodcutter's swing. Writes one PNG sheet.

blender -b --factory-startup -P blender_people_preview.py -- <people.json> <out.png> [--size 300]
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
MANIFEST, OUT = argv[0], argv[1]
SIZE = int(argv[argv.index("--size") + 1]) if "--size" in argv else 300
ASSETS = os.path.join(os.path.dirname(os.path.abspath(MANIFEST)), "..", "..")  # unity/Assets
people = [p for p in json.load(open(MANIFEST))["people"] if "textures" in p]

# The game's palettes (Behaviours.cs Villager): sRGB.
SKIN_TONES = [(0.87, 0.68, 0.56), (0.80, 0.60, 0.47), (0.70, 0.50, 0.37), (0.56, 0.39, 0.28), (0.42, 0.29, 0.21)]
DYES = [(0.55, 0.47, 0.36), (0.42, 0.45, 0.30), (0.33, 0.38, 0.47), (0.52, 0.30, 0.22), (0.62, 0.55, 0.40),
        (0.36, 0.30, 0.24), (0.47, 0.40, 0.48), (0.30, 0.36, 0.33)]
HAIR = [(0.09, 0.065, 0.05), (0.18, 0.12, 0.07), (0.30, 0.20, 0.11), (0.42, 0.30, 0.18), (0.55, 0.36, 0.20)]


def lin(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 48
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = SIZE, int(SIZE * 1.4)
scene.view_settings.view_transform = "AgX"
scene.view_settings.look = "AgX - Medium High Contrast"
world = bpy.data.worlds.new("w")
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.62, 0.70, 0.80, 1)
bg.inputs[1].default_value = 0.8
scene.world = world
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 3.5
sun.data.angle = math.radians(4)
sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
scene.collection.objects.link(sun)
bpy.ops.mesh.primitive_plane_add(size=12)
ground = bpy.context.object
gm = bpy.data.materials.new("ground")
gm.use_nodes = True
gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.16, 0.19, 0.10, 1)
ground.data.materials.append(gm)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 70


def texture(path, non_color):
    im = bpy.data.images.load(os.path.join(ASSETS, path), check_existing=True)
    if non_color:
        im.colorspace_settings.name = "Non-Color"
    return im


def person_material(p, zone, tint_lin):
    m = bpy.data.materials.new(f"{p['model']}_{zone}")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    t = p["textures"]
    alb = nt.nodes.new("ShaderNodeTexImage")
    alb.image = texture(t["albedo"], False)
    mask = nt.nodes.new("ShaderNodeTexImage")
    mask.image = texture(t["mask"], True)
    nrm = nt.nodes.new("ShaderNodeTexImage")
    nrm.image = texture(t["normal"], True)
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(mask.outputs["Color"], sep.inputs[0])
    # Albedo x tint x occlusion (URP applies occlusion to the indirect light only; close enough here).
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs[0].default_value = 1
    nt.links.new(alb.outputs["Color"], mul.inputs[6])
    mul.inputs[7].default_value = (*tint_lin, 1)
    ao = nt.nodes.new("ShaderNodeMix")
    ao.data_type = "RGBA"
    ao.blend_type = "MULTIPLY"
    ao.inputs[0].default_value = 0.6
    nt.links.new(mul.outputs[2], ao.inputs[6])
    nt.links.new(sep.outputs["Green"], ao.inputs[7])
    nt.links.new(ao.outputs[2], bsdf.inputs["Base Color"])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1
    nt.links.new(mask.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    if zone == "Skin":
        bsdf.inputs["Subsurface Weight"].default_value = 0.15
        bsdf.inputs["Subsurface Radius"].default_value = (0.02, 0.008, 0.005)
    return m


def tints(p, skin, main, hair):
    neutral = p["neutral"]
    return {"Skin": lin(skin) / np.maximum(1e-3, np.asarray(p["skin"])),
            "Main": lin(main) / neutral if p.get("dye", True) else np.ones(3), "Hair": lin(hair) / neutral, "Rest": np.ones(3)}


def load(p, skin, main, hair):
    before, acts = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ASSETS, "Art", "Resources", p["model"] + ".fbx"))
    objs = [o for o in bpy.data.objects if o not in before]
    rig = next(o for o in objs if o.type == "ARMATURE")
    rig["clips"] = [a.name for a in bpy.data.actions if a not in acts]
    t = tints(p, skin, main, hair)
    for o in objs:
        if o.type != "MESH":
            continue
        if o.name.startswith("Prop_") or o.name.startswith("Body_LOD1"):
            o.hide_render = True
            continue
        for slot in o.material_slots:
            zone = slot.material.name.split(".")[0]
            slot.material = person_material(p, zone, t.get(zone, np.ones(3)))
    return rig, objs


def unload(rig, objs):
    for n in list(rig["clips"]):
        bpy.data.actions.remove(bpy.data.actions[n])
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)


def pose(rig, clip, t):
    act = next(bpy.data.actions[n] for n in rig["clips"] if n.split("|")[-1] == clip)
    rig.animation_data.action = act
    f0, f1 = act.frame_range
    scene.frame_set(int(f0 + (f1 - f0) * t))


def shoot(target, offset, path, lens=70):
    cam.data.lens = lens
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def read_png(path):
    im = bpy.data.images.load(path)
    w, h = im.size
    a = np.empty(w * h * 4, dtype=np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return a.reshape(h, w, 4)


tmp = os.path.join(os.path.dirname(os.path.abspath(OUT)), "people_preview_tmp")
os.makedirs(tmp, exist_ok=True)
rows = []
body_target = Vector((0, 0, 0.92))
for i, p in enumerate(people):
    rig, objs = load(p, SKIN_TONES[i % len(SKIN_TONES)], p["main"], p["hair"] if p["age"] == "elder" else HAIR[i % len(HAIR)])
    row = []
    for j, (clip, t, off) in enumerate((("Idle", 0.3, Vector((0.0, -4.2, 0.35))), ("Walk", 0.3, Vector((4.2, 0.4, 0.45))),
                                        ("Chop", 0.25, Vector((2.6, -3.2, 0.6))))):
        pose(rig, clip, t)
        if clip == "Chop":
            for o in objs:
                if o.name.startswith("Prop_Axe"):
                    o.hide_render = False
        path = os.path.join(tmp, f"{i}_{j}.png")
        shoot(body_target, off, path)
        row.append(path)
    # Face close-up.
    pose(rig, "Idle", 0.3)
    head = rig.matrix_world @ rig.pose.bones["Head"].head
    path = os.path.join(tmp, f"{i}_face.png")
    shoot(head + Vector((0, 0, 0.08)), Vector((0.35, -1.6, 0.05)), path, lens=135)
    row.append(path)
    rows.append(row)
    unload(rig, objs)
# Dye variants of one man and one woman.
variants = []
for k, p in enumerate([q for q in people if q["age"] == "adult"][:1] + [q for q in people if q["gender"] == "female"][:1]):
    for v in range(4 if k == 0 else 4):
        n = k * 4 + v
        rig, objs = load(p, SKIN_TONES[(n * 2) % len(SKIN_TONES)], DYES[n % len(DYES)], HAIR[(n * 3) % len(HAIR)])
        pose(rig, "Idle", 0.5)
        path = os.path.join(tmp, f"var_{n}.png")
        shoot(body_target, Vector((1.2, -4.0, 0.35)), path)
        variants.append(path)
        unload(rig, objs)
rows.append(variants[:4])
rows.append(variants[4:])

w, h = scene.render.resolution_x, scene.render.resolution_y
cols = max(len(r) for r in rows)
sheet = np.ones((h * len(rows), w * cols, 4), dtype=np.float32) * 0.15
for r, row in enumerate(rows):
    for c, path in enumerate(row):
        sheet[(len(rows) - 1 - r) * h:(len(rows) - r) * h, c * w:(c + 1) * w] = read_png(path)
img = bpy.data.images.new("sheet", w * cols, h * len(rows), alpha=True)
img.pixels.foreach_set(sheet.ravel())
img.filepath_raw = OUT
img.file_format = "PNG"
img.save()
print("PREVIEW_OK", OUT)
