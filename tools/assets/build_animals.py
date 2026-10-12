#!/usr/bin/env python3
"""Builds the farm animals (unity/Assets/Art/Resources/Models/Animal_<Name>.fbx) from the CC0
Quaternius "Farm Animals" pack (OpenGameArt): cow, sheep, pig, horse and llama, scaled to real
size, with their animations (Idle, Walk, Run, Jump…) as takes for Unity's legacy Animation.

Usage: python3 tools/assets/build_animals.py   (needs Blender 4.2 on PATH)
"""
import os
import subprocess
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CACHE = os.path.join(HERE, ".cache", "packs", "farm_animals")
URL = "https://opengameart.org/sites/default/files/Farm%20Animals%20by%20%40Quaternius.zip"
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "Resources", "Models")
# Name: body length in metres.
ANIMALS = {"Cow": 2.2, "Sheep": 1.25, "Pig": 1.2, "Horse": 2.3, "Llama": 1.6}

SCRIPT = r'''
import bpy, sys
a = sys.argv[sys.argv.index("--") + 1:]
src, dst, length = a[0], a[1], float(a[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
size = max(max(o.dimensions.x, o.dimensions.y, o.dimensions.z) for o in meshes)
k = length / size
for o in bpy.data.objects:
    if o.parent is None:
        o.scale = [s * k for s in o.scale]
for act in bpy.data.actions:
    act.name = act.name.split("|")[-1]
    act.id_root = "OBJECT"
    act.use_fake_user = True
bpy.ops.object.select_all(action="SELECT")
bpy.context.view_layer.objects.active = rig
rig.animation_data_create()
rig.animation_data.action = bpy.data.actions.get("Idle") or bpy.data.actions[0]
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=True,
    bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.5, mesh_smooth_type="FACE", use_armature_deform_only=True)
print("ANIMAL", dst, round(k, 4), [x.name for x in bpy.data.actions])
'''


def main():
    os.makedirs(CACHE, exist_ok=True)
    zpath = os.path.join(CACHE, "farm.zip")
    if not os.path.exists(zpath):
        urllib.request.urlretrieve(URL, zpath)
    with zipfile.ZipFile(zpath) as z:
        z.extractall(CACHE)
    src_dir = os.path.join(CACHE, "Farm Animals by @Quaternius", "FBX")
    for name, length in ANIMALS.items():
        dst = os.path.join(OUT, f"Animal_{name}.fbx")
        subprocess.run(["blender", "-b", "--factory-startup", "--python-expr", SCRIPT, "--",
                        os.path.join(src_dir, name + ".fbx"), dst, str(length)], check=True)


if __name__ == "__main__":
    main()
