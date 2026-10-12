"""Blender (4.2) script: turns a CC0 Quaternius mannequin (Universal Animation Library 2, male
or female, Unreal rig) into a prehistoric villager for Unity.

- merges the animations of both libraries (UAL1 and UAL2 share the same Unreal skeleton and
  rest pose, so their actions apply directly): walking, talking and sitting, plus the work of
  the valley (chopping, planting, harvesting, watering, carrying, throwing)
- decimates the body for mobile
- paints three material zones on the same mesh: Skin, Hide (tunic or dress, leggings, foot
  wraps) and Hair, and inflates the hide and hair zones so they read as layers over the body
- adds hand tools as separate meshes parented to the right hand (Prop_Axe, Prop_Pickaxe,
  Prop_Hammer, Prop_Spear); the game shows the one the current job needs
- exports a skinned FBX (Y-up, metres) with every kept action as a take

Usage: blender -b --factory-startup -P blender_people.py -- <base.glb> <ual2.glb> <ual1.fbx> <out.fbx> [--female]
"""
import math
import re
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
base_path, ual2_path, ual1_path, dst = argv[:4]
FEMALE = "--female" in argv

# Source action (name ends with) -> clip name in the game.
KEEP = {
    # UAL1
    "Idle_Loop": "Idle",
    "Walk_Loop": "Walk",
    "Fixing_Kneeling": "Work",
    "PickUp_Table": "Gather",
    "Idle_Talking_Loop": "Talk",
    "Sitting_Idle_Loop": "Sit",
    "Push_Loop": "Push",
    "Jog_Fwd_Loop": "Jog",
    "Idle_Torch_Loop": "Torch",
    # UAL2
    "TreeChopping_Loop": "Chop",
    "Farm_PlantSeed": "Plant",
    "Farm_Harvest": "Harvest",
    "Farm_Watering": "Water",
    "Walk_Carry_Loop": "Carry",
    "OverhandThrow": "Throw",
    "Idle_Lantern_Loop": "Lantern",
    "Consume": "Eat",
    "Idle_FoldArms_Loop": "Watch",
}


def clip_for(action_name):
    # glTF actions are named "<clip>_<armature>[.001]", FBX takes "Rig|Rig|<clip>".
    n = re.sub(r"_Armature(\.\d+)?$", "", action_name).split("|")[-1]
    return KEEP.get(n)


bpy.ops.wm.read_factory_settings(use_empty=True)


def import_any(path):
    before = set(bpy.data.objects)
    if path.lower().endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


base_objs = import_any(base_path)
rig = next(o for o in base_objs if o.type == "ARMATURE")
body = max((o for o in base_objs if o.type == "MESH"), key=lambda o: len(o.data.vertices))
for o in base_objs:
    if o.type == "MESH" and o != body:
        bpy.data.objects.remove(o, do_unlink=True)

# Animation sources: keep their actions, drop their objects.
for path in (ual2_path, ual1_path):
    if path == base_path:
        continue
    for o in import_any(path):
        bpy.data.objects.remove(o, do_unlink=True)

taken = set()
for a in list(bpy.data.actions):
    clip = clip_for(a.name)
    if clip and clip not in taken:
        taken.add(clip)
        a.name = clip
        a.use_fake_user = True
        # FBX-imported actions have no owner type; the FBX exporter skips those silently.
        a.id_root = "OBJECT"
    else:
        bpy.data.actions.remove(a)
# The FBX exporter drops a whole action if any curve points at a bone this skeleton lacks
# (UAL1 has a few extra bones): remove those curves.
for a in bpy.data.actions:
    for fc in list(a.fcurves):
        try:
            rig.path_resolve(fc.data_path)
        except ValueError:
            a.fcurves.remove(fc)
missing = set(KEEP.values()) - taken
if missing:
    print("PEOPLE_WARN missing clips", sorted(missing))

# Weld split vertices first (UV seams), so the decimation and the clothing layers stay closed.
bm = bmesh.new()
bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
bm.to_mesh(body.data)
bm.free()

# Decimate to about 6.5k triangles (plenty at strategy-camera distance).
bpy.context.view_layer.objects.active = body
body.select_set(True)
tris0 = sum(len(p.vertices) - 2 for p in body.data.polygons)
dec = body.modifiers.new("Decimate", "DECIMATE")
dec.ratio = min(1.0, 6500 / max(1, tris0))
while body.modifiers[0].name != "Decimate":
    bpy.ops.object.modifier_move_up(modifier="Decimate")
bpy.ops.object.modifier_apply(modifier="Decimate")


def make_mat(name, color):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m


# Materials: Skin (0), Hide (1), Hair (2).
body.data.materials.clear()
for name, color in (("Skin", (0.55, 0.36, 0.26)), ("Hide", (0.36, 0.24, 0.15)), ("Hair", (0.06, 0.04, 0.03))):
    body.data.materials.append(make_mat(name, color))

groups = {g.index: g.name for g in body.vertex_groups}


def weights(v):
    w = {}
    for g in v.groups:
        w[groups[g.group]] = w.get(groups[g.group], 0) + g.weight
    return w


def total(w, *names):
    return sum(v for k, v in w.items() if any(k.startswith(n) for n in names))


mesh = body.data
mw = body.matrix_world
verts = mesh.vertices


def centroid(*prefixes):
    pts = [mw @ v.co for v in verts if total(weights(v), *prefixes) > 0.6]
    return sum(pts, Vector()) / max(1, len(pts))


head_c = centroid("Head")
foot_c = centroid("foot_")
toe_c = centroid("ball_")
forward = toe_c - foot_c
forward.z = 0
forward.normalize()
knee_z = centroid("calf_").z + 0.17
shoulder_c = (centroid("clavicle_l") + centroid("clavicle_r")) * 0.5
# A dress reaches below the knee; a tunic stops above it.
hem_z = knee_z - (0.22 if FEMALE else 0.0)

zone = {}
for v in verts:
    w = weights(v)
    p = mw @ v.co
    torso = total(w, "pelvis", "spine_", "clavicle_")
    thigh = total(w, "thigh_")
    upper = total(w, "upperarm_")
    head = total(w, "Head", "neck_")
    lowleg = total(w, "foot_", "ball_")
    z = "skin"
    if torso > 0.35 or (thigh > 0.3 and p.z > hem_z) or (upper > 0.5 and (p - shoulder_c).length < 0.36):
        z = "hide"
    if FEMALE and total(w, "calf_") > 0.3 and p.z > hem_z:
        z = "hide"
    if lowleg > 0.4 or (total(w, "calf_") > 0.5 and p.z < 0.2):
        z = "hide"  # foot wraps
    if total(w, "Head") > 0.6:
        rel = p - head_c
        back = -rel.dot(forward)
        if rel.z > -0.02 and (back > -0.035 or rel.z > 0.06):
            z = "hair"
        elif back > 0.03 and rel.z > (-0.2 if FEMALE else -0.12):
            z = "hair"
    zone[v.index] = z

for poly in mesh.polygons:
    counts = {"skin": 0, "hide": 0, "hair": 0}
    for i in poly.vertices:
        counts[zone[i]] += 1
    poly.material_index = {"skin": 0, "hide": 1, "hair": 2}[max(counts, key=counts.get)]

# Inflate hide and hair so they sit over the body (interior vertices move, borders blend).
bm = bmesh.new()
bm.from_mesh(mesh)
bm.verts.ensure_lookup_table()
bm.normal_update()
mat_of_face = {f.index: f.material_index for f in bm.faces}
for v in bm.verts:
    mats = {mat_of_face[f.index] for f in v.link_faces}
    if not mats or mats == {0}:
        continue
    interior = 0 not in mats
    amount = 0.0
    if 2 in mats:
        amount = (0.02 if FEMALE else 0.016) if interior else 0.006
    elif 1 in mats:
        amount = 0.014 if interior else 0.005
        w = weights(verts[v.index])
        # Flare the hide over the thighs (and the calves for a dress), like a skirt.
        if interior and total(w, "thigh_") > 0.3:
            amount += 0.022 if FEMALE else 0.012
        if interior and FEMALE and total(w, "calf_") > 0.3:
            amount += 0.03
    v.co += v.normal * amount
bm.to_mesh(mesh)
bm.free()
mesh.update()

# MARK: - Tools in the right hand
# Hand bone space: +Y runs along the fingers, +Z points from the little finger to the index
# finger (the thumb side), so a handle held in the fist runs along Z with the head on +Z.
wood = make_mat("Wood", (0.45, 0.32, 0.2))
stone = make_mat("Stone", (0.42, 0.42, 0.4))


def part(kind, size, loc, mat, rot=None):
    if kind == "cyl":
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=size[0], depth=size[1], location=loc)
    else:
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        bpy.context.object.scale = size
    o = bpy.context.object
    if rot:
        o.rotation_euler = rot
    o.data.materials.append(mat)
    return o


def prop(name, parts):
    bpy.ops.object.select_all(action="DESELECT")
    objs = [part(*p) for p in parts]
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    o = bpy.context.object
    o.name = name
    o.data.name = name
    # Grip: in the palm, a little toward the palm side of the hand.
    o.parent = rig
    o.parent_type = "BONE"
    o.parent_bone = "hand_r"
    o.matrix_parent_inverse = Matrix.Identity(4)
    o.location = (-0.02, 0.025, 0.0)  # relative to the bone tail
    return o


props = [
    prop("Prop_Axe", [("cyl", (0.016, 0.62), (0, 0, 0.21), wood),
                      ("cube", (0.03, 0.15, 0.08), (0, 0.06, 0.47), stone)]),
    prop("Prop_Pickaxe", [("cyl", (0.016, 0.64), (0, 0, 0.22), wood),
                          ("cube", (0.035, 0.36, 0.05), (0, 0.0, 0.5), stone)]),
    prop("Prop_Hammer", [("cyl", (0.015, 0.36), (0, 0, 0.1), wood),
                         ("cube", (0.07, 0.12, 0.07), (0, 0.02, 0.28), stone)]),
    prop("Prop_Spear", [("cyl", (0.013, 1.7), (0, 0, 0.35), wood),
                        ("cube", (0.012, 0.035, 0.12), (0, 0, 1.25), stone)]),
]

# Export: armature, body and tools, every action baked as its own take.
bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
body.select_set(True)
for p in props:
    p.select_set(True)
bpy.context.view_layer.objects.active = rig
if rig.animation_data is None:
    rig.animation_data_create()
rig.animation_data.action = bpy.data.actions.get("Idle")
bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=True,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    object_types={"ARMATURE", "MESH"},
    add_leaf_bones=False,
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.5,
    mesh_smooth_type="FACE",
    use_armature_deform_only=True,
)
tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
zones = [sum(1 for p in mesh.polygons if p.material_index == i) for i in range(3)]
print(f"PEOPLE_OK tris={tris} zones={zones} female={FEMALE} actions={sorted(a.name for a in bpy.data.actions)}")

# Optional preview sheet: --preview <png> renders each work clip with its tool.
if "--preview" in argv:
    out = argv[argv.index("--preview") + 1]
    scene = bpy.context.scene
    shots = [("Chop", "Prop_Axe"), ("Chop", "Prop_Pickaxe"), ("Work", "Prop_Hammer"), ("Plant", None),
             ("Harvest", None), ("Throw", "Prop_Spear"), ("Carry", None), ("Water", None)]
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.location = (2.3, -2.3, 1.5)
    cam.rotation_euler = (math.radians(78), 0, math.radians(45))
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    scene.collection.objects.link(sun)
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.6, 1)
    scene.world = world
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 12
    scene.render.resolution_x, scene.render.resolution_y = 320, 420
    import os
    paths = []
    for i, (clip, tool) in enumerate(shots):
        for p in props:
            p.hide_render = p.name != tool
        act = bpy.data.actions.get(clip)
        if act is None:
            continue
        rig.animation_data.action = act
        f0, f1 = act.frame_range
        for k, t in enumerate((0.25, 0.6)):
            scene.frame_set(int(f0 + (f1 - f0) * t))
            path = f"{out}_{i}_{k}.png"
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            paths.append(path)
    print("PREVIEW", " ".join(paths))
