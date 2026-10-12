"""Blender (4.2): turns a CC0 Quaternius "Ultimate Animated Character Pack" character into a
villager for the game, with all of the game's work animations.

The game's animations live on the Unreal-style skeleton of Villager.fbx (Universal Animation
Library 1 + 2). They are retargeted onto the character's own skeleton: every mapped bone copies
the world rotation of its source bone (with the rest poses aligned bone by bone), the hips follow
the source pelvis scaled to the character's leg length, and the feet (free bones in this rig)
are placed at the ends of the shins. Hand tools (axe, pickaxe, hammer, spear) are added to the
right fist. The result is a skinned FBX with every clip as a take.

blender -b --factory-startup -P blender_characters.py -- <Villager.fbx> <character.fbx> <out.fbx> [--preview <png prefix>]
"""
import math
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, CHAR, OUT = argv[0], argv[1], argv[2]
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None

MAP = {  # Unreal-style source bone -> character bone
    "pelvis": "Body", "spine_01": "Hips", "spine_02": "Abdomen", "spine_03": "Torso", "neck_01": "Neck", "Head": "Head",
    "clavicle_l": "Shoulder.L", "upperarm_l": "UpperArm.L", "lowerarm_l": "LowerArm.L", "hand_l": "Fist.L",
    "clavicle_r": "Shoulder.R", "upperarm_r": "UpperArm.R", "lowerarm_r": "LowerArm.R", "hand_r": "Fist.R",
    "thigh_l": "UpperLeg.L", "calf_l": "LowerLeg.L", "foot_l": "Foot.L",
    "thigh_r": "UpperLeg.R", "calf_r": "LowerLeg.R", "foot_r": "Foot.R",
}
FEET = {"Foot.L": "LowerLeg.L", "Foot.R": "LowerLeg.R"}

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def import_fbx(path):
    before = set(bpy.data.objects)
    acts = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before], [a for a in bpy.data.actions if a not in acts]


src_objs, src_actions = import_fbx(SRC)
src = next(o for o in src_objs if o.type == "ARMATURE")
for o in src_objs:
    if o.type == "MESH":
        o.hide_render = True
char_objs, char_actions = import_fbx(CHAR)
tgt = next(o for o in char_objs if o.type == "ARMATURE")
meshes = [o for o in char_objs if o.type == "MESH"]
for a in char_actions:
    bpy.data.actions.remove(a)
# Modern headwear (top hats, hard hats) does not belong in the valley: remove those faces.
import bmesh
for o in meshes:
    hats = {i for i, m in enumerate(o.data.materials) if m and m.name.split(".")[0] in ("Hat", "Helmet")}
    if hats:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in hats], context="FACES")
        bm.to_mesh(o.data)
        bm.free()

# The FBX importer reads the pack's materials as fully transparent: make them opaque.
for o in meshes:
    for m in o.data.materials:
        if m and m.node_tree and m.node_tree.nodes.get("Principled BSDF"):
            m.node_tree.nodes["Principled BSDF"].inputs["Alpha"].default_value = 1.0
            if m.name.startswith("Skin"):  # previews only: the game paints skin, hair and clothes itself
                m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.8, 0.55, 0.4, 1)
            m.blend_method = "OPAQUE"
if tgt.animation_data:
    tgt.animation_data.action = None
src.animation_data_create()


def rest_world(arm, name):
    return arm.matrix_world @ arm.data.bones[name].matrix_local


# Per bone: K = Ss^-1 · Align(St), with Align turning the target rest so its bone axis matches
# the source rest axis. Then target world rotation = source world rotation · K.
K = {}
for s_name, t_name in MAP.items():
    Ss = rest_world(src, s_name).to_3x3().normalized()
    St = rest_world(tgt, t_name).to_3x3().normalized()
    ys, yt = Ss.col[1].normalized(), St.col[1].normalized()
    if t_name in FEET:  # feet: match the toe direction instead (both rigs point the foot forward)
        ys = (rest_world(src, "ball_" + s_name[-1]).translation - rest_world(src, s_name).translation).normalized()
    align = yt.rotation_difference(ys).to_matrix()
    K[t_name] = Ss.inverted() @ (align @ St)

src_leg = (rest_world(src, "thigh_l").translation - rest_world(src, "foot_l").translation).length
tgt_leg = (rest_world(tgt, "UpperLeg.L").translation - rest_world(tgt, "Foot.L").translation).length
scale = tgt_leg / src_leg
src_pelvis_rest = rest_world(src, "pelvis").translation
tgt_body_rest = rest_world(tgt, "Body").translation
inv_tgt = tgt.matrix_world.inverted()

# Parents come before their children; the free foot bones go last (they follow the shins).
order = [b.name for b in tgt.data.bones if b.name not in FEET and not (b.parent and b.parent.name in FEET)]
order += [b.name for b in tgt.data.bones if b.name not in order]


def retarget_frame():
    """Poses the target from the source's current (evaluated) pose and returns basis matrices."""
    world = {}
    basis = {}
    for name in order:
        bone = tgt.data.bones[name]
        parent_world = world[bone.parent.name] if bone.parent else tgt.matrix_world
        rest_local = (bone.parent.matrix_local.inverted() if bone.parent else Matrix()) @ bone.matrix_local
        default_world = parent_world @ rest_local
        s_name = next((s for s, t in MAP.items() if t == name), None)
        if s_name is None:
            world[name] = default_world
            basis[name] = Matrix()
            continue
        Ts = (src.matrix_world @ src.pose.bones[s_name].matrix).to_3x3().normalized()
        rot = (Ts @ K[name]).to_4x4()
        if name == "Body":
            sp = (src.matrix_world @ src.pose.bones["pelvis"].matrix).translation
            loc = tgt_body_rest + (sp - src_pelvis_rest) * scale
        elif name in FEET:
            shin = world[FEET[name]]
            loc = shin @ Vector((0, tgt.data.bones[FEET[name]].length, 0))
        else:
            loc = default_world.translation
        w = Matrix.Translation(loc) @ rot
        world[name] = w
        basis[name] = rest_local.inverted() @ parent_world.inverted() @ w
    return basis


new_actions = []
for act in src_actions:
    clip = act.name.split("|")[-1]
    src.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    out = bpy.data.actions.new(clip)
    out.id_root = "OBJECT"
    out.use_fake_user = True
    tgt.animation_data_create()
    tgt.animation_data.action = out
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        basis = retarget_frame()
        for name, m in basis.items():
            pb = tgt.pose.bones[name]
            pb.rotation_mode = "QUATERNION"
            loc, rot, _ = m.decompose()
            pb.rotation_quaternion = rot
            pb.location = loc
            pb.keyframe_insert("rotation_quaternion", frame=f, group=name)
            if name == "Body" or name in FEET:
                pb.keyframe_insert("location", frame=f, group=name)
    new_actions.append(out)
    print("RETARGET", clip, f0, f1)

# The source rig and its people are no longer needed.
for o in src_objs:
    bpy.data.objects.remove(o, do_unlink=True)
for a in src_actions:
    bpy.data.actions.remove(a)

# MARK: - Tools in the right fist (the fist bone runs along the forearm; the handle crosses it).


def mat(name, color):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m


wood, stone = mat("Wood", (0.45, 0.32, 0.2)), mat("Stone", (0.42, 0.42, 0.4))
fist_len = tgt.data.bones["Fist.R"].length
k = max(o.dimensions.z for o in meshes) / 1.8  # the rig is larger than life: tools scale with it


def part(kind, size, loc, m):
    if kind == "cyl":
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=size[0] * k, depth=size[1] * k, location=[c * k for c in loc])
    else:
        bpy.ops.mesh.primitive_cube_add(size=1, location=[c * k for c in loc])
        bpy.context.object.scale = [c * k for c in size]
    o = bpy.context.object
    o.data.materials.append(m)
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
    o.name = o.data.name = name
    # Handle along the fist's Z axis, centred in the fist.
    o.parent = tgt
    o.parent_type = "BONE"
    o.parent_bone = "Fist.R"
    o.matrix_parent_inverse = Matrix()
    o.location = (0, -fist_len * 0.5, 0)
    return o


props = [
    prop("Prop_Axe", [("cyl", (0.016, 0.62), (0, 0, 0.21), wood), ("cube", (0.03, 0.15, 0.08), (0, 0.06, 0.47), stone)]),
    prop("Prop_Pickaxe", [("cyl", (0.016, 0.64), (0, 0, 0.22), wood), ("cube", (0.035, 0.36, 0.05), (0, 0, 0.5), stone)]),
    prop("Prop_Hammer", [("cyl", (0.015, 0.36), (0, 0, 0.1), wood), ("cube", (0.07, 0.12, 0.07), (0, 0.02, 0.28), stone)]),
    prop("Prop_Spear", [("cyl", (0.013, 1.7), (0, 0, 0.35), wood), ("cube", (0.012, 0.035, 0.12), (0, 0, 1.25), stone)]),
]

# MARK: - Export

bpy.ops.object.select_all(action="DESELECT")
tgt.select_set(True)
for o in meshes + props:
    o.select_set(True)
bpy.context.view_layer.objects.active = tgt
tgt.animation_data.action = bpy.data.actions.get("Idle")
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=1.0, mesh_smooth_type="FACE",
    use_armature_deform_only=False,
)
print("CHARACTER_OK", OUT, sorted(a.name for a in new_actions), [m.name for o in meshes for m in o.data.materials])

if PREVIEW:
    shots = [("Walk", None), ("Chop", "Prop_Axe"), ("Plant", None), ("Throw", "Prop_Spear"), ("Talk", None), ("Sit", None)]
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    h = max(o.dimensions.z for o in meshes)
    target = Vector((0, 0, h * 0.5))
    cam.location = target + Vector((h * 1.4, -h * 1.9, h * 0.5))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.6, 1)
    scene.world = world
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 12
    scene.render.resolution_x, scene.render.resolution_y = 300, 400
    for i, (clip, tool) in enumerate(shots):
        act = bpy.data.actions.get(clip)
        if act is None:
            continue
        for p in props:
            p.hide_render = p.name != tool
        tgt.animation_data.action = act
        scene.frame_set(int(act.frame_range[0] + (act.frame_range[1] - act.frame_range[0]) * 0.4))
        scene.render.filepath = f"{PREVIEW}_{i}.png"
        bpy.ops.render.render(write_still=True)
