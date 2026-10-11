"""Blender: convert a .glb (embedded textures) to .gltf with separate texture files.
blender -b --factory-startup -P blender_glb2gltf.py -- in.glb out.gltf"""
import sys
import bpy

src, dst = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLTF_SEPARATE", export_image_format="AUTO")
