"""Blender (4.2): renders the app icon: a cheerful survivor holding a burning torch, standing in a
stylized jungle at night (the game's own villager, CC0 Quaternius mannequin and plants).

blender -b --factory-startup -P blender_icon.py -- <Villager.fbx> <stylized nature glTF dir> <out.png> [size]
"""
import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
VILLAGER, SN, OUT = argv[0], argv[1], argv[2]
SIZE = int(argv[3]) if len(argv) > 3 else 1024

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def mat(name, color, emission=None, strength=0.0, rough=0.6):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = rough
    if emission:
        p.inputs["Emission Color"].default_value = (*emission, 1)
        p.inputs["Emission Strength"].default_value = strength
    return m


# The villager in the "holding a torch" pose.
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=VILLAGER)
objs = [o for o in bpy.data.objects if o not in before]
rig = next(o for o in objs if o.type == "ARMATURE")
body = max((o for o in objs if o.type == "MESH" and not o.name.startswith("Prop_")), key=lambda o: len(o.data.vertices))
for o in objs:
    if o.type == "MESH" and o.name.startswith("Prop_"):
        bpy.data.objects.remove(o, do_unlink=True)
act = next((a for a in bpy.data.actions if a.name.endswith("Torch")), None)
rig.animation_data_create()
rig.animation_data.action = act
scene.frame_set(int(act.frame_range[0] + (act.frame_range[1] - act.frame_range[0]) * 0.3) if act else 1)

colors = {"Skin": (0.96, 0.74, 0.58), "Hide": (0.78, 0.16, 0.1), "Hair": (0.08, 0.05, 0.035)}
for slot in body.material_slots:
    base = slot.material.name.split(".")[0] if slot.material else "Skin"
    slot.material = mat("Icon" + base, colors.get(base, (0.8, 0.6, 0.4)), rough=0.55)
body.data.polygons.foreach_set("use_smooth", [True] * len(body.data.polygons))


def bone_world(name):
    pb = rig.pose.bones[name]
    return rig.matrix_world @ pb.matrix


def attach(o, bone):
    """The icon is a single still frame: parts simply stay where they are placed."""
    return o


# Where the posed head is and which way the villager faces (from the evaluated mesh).
deps = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(deps)
me = ev.to_mesh()
groups = {g.index: g.name for g in body.vertex_groups}
head_pts = [ev.matrix_world @ v.co for v in me.vertices
            if sum(g.weight for g in v.groups if groups.get(g.group) == "Head") > 0.6]
hc = sum(head_pts, Vector()) / len(head_pts)
hr = max((p - hc).length for p in head_pts) * 0.82
foot = (bone_world("foot_l").translation + bone_world("foot_r").translation) / 2
ball = (bone_world("ball_l").translation + bone_world("ball_r").translation) / 2
fwd = ball - foot
fwd.z = 0
fwd.normalize()
side_axis = fwd.cross(Vector((0, 0, 1))).normalized()
ev.to_mesh_clear()

# Cartoon face: big eyes with pupils, eyebrows and a smile.
white, black = mat("EyeWhite", (1, 1, 1), rough=0.3), mat("Pupil", (0.03, 0.03, 0.03), rough=0.2)
for side in (-1, 1):
    eye = hc + fwd * hr * 0.9 - Vector((0, 0, hr * 0.02)) + side_axis * side * hr * 0.36
    bpy.ops.mesh.primitive_uv_sphere_add(radius=hr * 0.24, location=eye, segments=16, ring_count=8)
    bpy.context.object.data.materials.append(white)
    attach(bpy.context.object, "Head")
    bpy.ops.mesh.primitive_uv_sphere_add(radius=hr * 0.13, location=eye + fwd * hr * 0.17, segments=12, ring_count=6)
    bpy.context.object.data.materials.append(black)
    attach(bpy.context.object, "Head")
    bpy.ops.mesh.primitive_cube_add(size=1, location=eye + Vector((0, 0, hr * 0.33)) + fwd * hr * 0.08)
    brow = bpy.context.object
    brow.scale = (hr * 0.34, hr * 0.08, hr * 0.08)
    brow.rotation_euler = Euler((0, side * 0.25, math.atan2(fwd.y, fwd.x) + math.pi / 2))
    brow.data.materials.append(mat("Hair2", colors["Hair"]))
    attach(brow, "Head")
bpy.ops.mesh.primitive_torus_add(major_radius=hr * 0.26, minor_radius=hr * 0.05,
                                 location=hc + fwd * hr * 0.9 - Vector((0, 0, hr * 0.32)))
smile = bpy.context.object
smile.rotation_euler = (math.pi / 2, 0, math.atan2(fwd.y, fwd.x) + math.pi / 2)
bpy.ops.object.transform_apply(rotation=True)
for v in smile.data.vertices:
    v.select = v.co.z > -hr * 0.05
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.delete(type="VERT")
bpy.ops.object.mode_set(mode="OBJECT")
smile.data.materials.append(mat("Mouth", (0.35, 0.08, 0.06)))
attach(smile, "Head")

# The torch in the raised hand: a wooden stick with a bright flame.
hand_name = max(("hand_l", "hand_r"), key=lambda n: bone_world(n).translation.z)
hpos = bone_world(hand_name).translation
torch_top = hpos + Vector((0, 0, 0.42))
bpy.ops.mesh.primitive_cylinder_add(radius=0.025, depth=0.6, location=hpos + Vector((0, 0, 0.12)))
bpy.context.object.data.materials.append(mat("Stick", (0.36, 0.22, 0.12)))
flame_mat = mat("Flame", (1.0, 0.6, 0.15), emission=(1.0, 0.5, 0.08), strength=9.0)
for r, dz in ((0.075, 0.0), (0.06, 0.08), (0.04, 0.16), (0.022, 0.23)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=torch_top + Vector((0, 0, dz)), segments=16, ring_count=8)
    f = bpy.context.object
    f.scale = (1, 1, 1.4)
    f.data.materials.append(flame_mat)
fire = bpy.data.objects.new("FireLight", bpy.data.lights.new("FireLight", "POINT"))
fire.data.energy = 160
fire.data.color = (1.0, 0.55, 0.2)
fire.data.shadow_soft_size = 0.25
fire.location = torch_top + fwd * 0.25
scene.collection.objects.link(fire)

# The jungle: stylized ferns, plants and trees around and behind.


def place(name, loc, scale, rot=0.0):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SN, name + ".gltf"))
    for o in bpy.data.objects:
        if o not in before and o.parent is None:
            o.location = loc
            o.rotation_mode = "XYZ"
            o.rotation_euler = (0, 0, rot)
            o.scale = (scale, scale, scale)


for name, loc, s, r in (
    ("CommonTree_2", (-2.6, 3.5, 0), 0.9, 0.3), ("Pine_2", (2.4, 4.5, 0), 0.9, 1.0), ("CommonTree_4", (0.4, 6.5, 0), 1.0, 2.0),
    ("Plant_1_Big", (-0.9, 0.2, 0), 0.9, 0.5), ("Plant_7_Big", (0.9, 0.4, 0), 1.0, 2.0), ("Fern_1", (-1.1, -0.6, 0), 0.6, 1.2),
    ("Fern_1", (1.2, -0.4, 0), 0.55, 4.0), ("Bush_Common", (-1.8, 1.4, 0), 0.9, 0.0), ("Bush_Common", (1.9, 1.6, 0), 1.0, 2.5),
    ("Plant_1", (0.3, -0.9, 0), 0.6, 1.0), ("Flower_3_Group", (-0.5, -1.0, 0), 0.35, 0.0),
):
    place(name, loc, s, r)
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
ground = bpy.context.object
ground.data.materials.append(mat("Ground", (0.12, 0.2, 0.1), rough=0.9))

# Night: deep teal sky, a cool moon from behind, warm torchlight on the face.
world = bpy.data.worlds.new("Night")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.03, 0.09, 0.12, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
scene.world = world
moon = bpy.data.objects.new("Moon", bpy.data.lights.new("Moon", "SUN"))
moon.data.energy = 1.6
moon.data.color = (0.55, 0.75, 1.0)
moon.rotation_euler = (math.radians(60), 0, math.radians(160))
scene.collection.objects.link(moon)
fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "AREA"))
fill.data.energy = 60
fill.data.size = 2
fill.data.color = (1.0, 0.75, 0.55)
fill.location = (0.6, -2.2, 1.6)
fill.rotation_euler = (math.radians(70), 0, math.radians(15))
scene.collection.objects.link(fill)

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 50
target = Vector((0, 0, 1.32))
cam.location = target + fwd * 2.5 + side_axis * 0.45 + Vector((0, 0, 0.1))
cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()

scene.render.engine = "CYCLES"
scene.cycles.samples = 96
scene.cycles.use_denoising = True
scene.render.resolution_x = scene.render.resolution_y = SIZE
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"
scene.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("ICON", OUT)
