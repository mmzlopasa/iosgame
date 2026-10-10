"""Blender (4.2) script: import a glTF, reduce it to a triangle budget, put the pivot at the
bottom centre and export an FBX for Unity (Y up, metres).

blender -b --factory-startup -P blender_decimate.py -- in.gltf out.fbx TARGET_TRIS info.json
"""
import json
import sys

import bpy
import bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst, target, info_path = argv[0], argv[1], int(argv[2]), argv[3]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
# Apply parent transforms (glTF imports with an axis-conversion root) and join into one mesh.
for o in bpy.context.scene.objects:
    o.select_set(False)
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
for o in list(bpy.context.scene.objects):
    if o != obj:
        bpy.data.objects.remove(o, do_unlink=True)


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


source_tris = tri_count(obj)
# Scans often have duplicated vertices along seams (every edge "open"), which blocks collapsing.
# Merging them keeps UVs (stored per face corner) and lets the decimator work.
bm = bmesh.new()
bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
bm.to_mesh(obj.data)
bm.free()
# Collapse can stop short on meshes with many UV seams; repeat a few passes until near budget.
for _ in range(6):
    current = tri_count(obj)
    if current <= target * 1.15:
        break
    mod = obj.modifiers.new("Decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = max(0.0005, target / current)
    mod.use_collapse_triangulate = True
    mod.delimit = set()
    bpy.ops.object.modifier_apply(modifier=mod.name)

# Triangulate and clean up.
bm = bmesh.new()
bm.from_mesh(obj.data)
bmesh.ops.triangulate(bm, faces=bm.faces[:])
bm.to_mesh(obj.data)
bm.free()

# Pivot at the bottom centre so the model stands on the ground at its origin.
xs = [v.co.x for v in obj.data.vertices]
ys = [v.co.y for v in obj.data.vertices]
zs = [v.co.z for v in obj.data.vertices]
offset = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
for v in obj.data.vertices:
    v.co -= offset
obj.location = (0, 0, 0)
obj.name = "Model"
obj.data.name = "Model"

for p in obj.data.polygons:
    p.use_smooth = True

bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=False,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=True,
    object_types={"MESH"},
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    path_mode="STRIP",
    embed_textures=False,
)

size = [max(xs) - min(xs), max(zs) - min(zs), max(ys) - min(ys)]  # width, height, depth (Unity axes)
info = {
    "source_triangles": source_tris,
    "triangles": tri_count(obj),
    "size": [round(s, 3) for s in size],
    "materials": [m.name for m in obj.data.materials if m],
}
with open(info_path, "w") as f:
    json.dump(info, f)
