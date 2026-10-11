"""Blender (4.2) script: turns the CC0 Quaternius Universal Animation Library mannequin into a
prehistoric villager for Unity.

- decimates the body for mobile
- paints three material zones on the same mesh: Skin, Hide (tunic, leggings, foot wraps)
  and Hair, and inflates the hide and hair zones so they read as layers over the body
- keeps a small set of animations (idle, walk, work, gather, talk, sit, carry)
- exports a skinned FBX (Y-up, metres) with every kept action as a take

Usage: blender -b --factory-startup -P blender_people.py -- <ual.glb> <out.fbx>
"""
import sys

import bmesh
import bpy
from mathutils import Vector

src, dst = sys.argv[sys.argv.index("--") + 1:][:2]

KEEP = {
    "Idle_Loop_Rig": "Idle",
    "Walk_Loop_Rig": "Walk",
    "Fixing_Kneeling_Rig": "Work",
    "PickUp_Table_Rig": "Gather",
    "Idle_Talking_Loop_Rig": "Talk",
    "Sitting_Idle_Loop_Rig": "Sit",
    "Push_Loop_Rig": "Carry",
    "Jog_Fwd_Loop_Rig": "Jog",
}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

for o in list(bpy.data.objects):
    if o.type == "MESH" and o.name != "Mannequin":
        bpy.data.objects.remove(o, do_unlink=True)
body = bpy.data.objects["Mannequin"]
rig = bpy.data.objects["Rig"]

# Actions: keep and rename the ones the game uses.
for a in list(bpy.data.actions):
    if a.name in KEEP:
        a.name = KEEP[a.name]
        a.use_fake_user = True
    else:
        bpy.data.actions.remove(a)

# Decimate (the body is ~13.7k triangles; ~6k is plenty at strategy-camera distance).
bpy.context.view_layer.objects.active = body
body.select_set(True)
dec = body.modifiers.new("Decimate", "DECIMATE")
dec.ratio = 0.45
# The decimate modifier must run before the armature modifier.
while body.modifiers[0].name != "Decimate":
    bpy.ops.object.modifier_move_up(modifier="Decimate")
bpy.ops.object.modifier_apply(modifier="Decimate")

# Materials: Skin (0), Hide (1), Hair (2).
def make_mat(name, color):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m

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

# Reference points from the rest pose.
def centroid(prefix):
    pts = [mw @ v.co for v in verts if total(weights(v), prefix) > 0.6]
    return sum(pts, Vector()) / max(1, len(pts))

head_c = centroid("DEF-head")
foot_c = centroid("DEF-foot")
toe_c = centroid("DEF-toe")
forward = (toe_c - foot_c)
forward.z = 0
forward.normalize()
knee_z = centroid("DEF-shin").z + 0.17
shoulder_c = (centroid("DEF-shoulder.L") + centroid("DEF-shoulder.R")) * 0.5

zone = {}
for v in verts:
    w = weights(v)
    p = mw @ v.co
    torso = total(w, "DEF-hips", "DEF-spine", "DEF-shoulder")
    thigh = total(w, "DEF-thigh")
    upper = total(w, "DEF-upper_arm")
    head = total(w, "DEF-head")
    lowleg = total(w, "DEF-foot", "DEF-toe")
    z = "skin"
    if torso > 0.35 or (thigh > 0.3 and p.z > knee_z) or (upper > 0.5 and (p - shoulder_c).length < 0.36):
        z = "hide"
    if lowleg > 0.4 or (total(w, "DEF-shin") > 0.5 and p.z < 0.2):
        z = "hide"  # foot wraps
    if head > 0.6:
        rel = p - head_c
        back = -rel.dot(forward)
        if rel.z > -0.02 and (back > -0.035 or rel.z > 0.06):
            z = "hair"
        elif back > 0.03 and rel.z > -0.12:
            z = "hair"
    zone[v.index] = z

# Faces take the zone of the majority of their vertices.
for poly in mesh.polygons:
    counts = {"skin": 0, "hide": 0, "hair": 0}
    for i in poly.vertices:
        counts[zone[i]] += 1
    best = max(counts, key=counts.get)
    poly.material_index = {"skin": 0, "hide": 1, "hair": 2}[best]

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
        amount = 0.016 if interior else 0.006
    elif 1 in mats:
        amount = 0.014 if interior else 0.005
        # Flare the tunic a little over the thighs, like a hide skirt.
        w = weights(verts[v.index])
        if total(w, "DEF-thigh") > 0.3 and interior:
            amount += 0.012
    v.co += v.normal * amount
bm.to_mesh(mesh)
bm.free()
mesh.update()

# Export: armature + mesh, every action baked as its own take.
bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = rig
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
print(f"PEOPLE_OK tris={tris} actions={[a.name for a in bpy.data.actions]}")
