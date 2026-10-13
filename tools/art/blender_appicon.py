"""Blender (4.2): renders the app icon, a carved stone and gold emblem.

Full-bleed: a gold frame with turquoise inlay tiles around a carved stone wall; at the top a gold sun
crest behind the founder's hearth, a brazier with a big glowing amber flame; across the middle the
title "FOUNDERS" in chunky gold letters with a dark outline on a stone banner, a red ribbon "THE LONG
RISE" under it, and leaves tucked around the banner. Warm, rich light. All modelled here (stone
textures from the CC0 Medieval Village MegaKit, Changa One under the OFL).

The PNG is opaque RGB (the App Store rejects icons with alpha). Without an explicit output path it is
written to both places the project reads it from:
  unity/Assets/Art/UI/AppIcon/AppIcon.png              (Unity player settings, Editor/UiSetup.cs)
  App/Assets.xcassets/AppIcon.appiconset/AppIcon.png   (the XcodeGen app target)

blender -b --factory-startup -P tools/art/blender_appicon.py -- [<out.png>|-] [size] [samples]
(the earlier scene icon is tools/art/blender_appicon_scene.py)
"""
import math
import os
import random
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import iconlib as L  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if len(argv) > 0 and argv[0] != "-" else None
SIZE = int(argv[1]) if len(argv) > 1 else 1024
SAMPLES = int(argv[2]) if len(argv) > 2 else 128
TARGETS = [os.path.join(L.ROOT, "unity", "Assets", "Art", "UI", "AppIcon", "AppIcon.png"),
           os.path.join(L.ROOT, "App", "Assets.xcassets", "AppIcon.appiconset", "AppIcon.png")]

L.reset()
scene = bpy.context.scene
rnd = random.Random(9)


def pm(name, color, rough=0.5, metal=0.0, spec=0.5, tex=None, tex_scale=1.0, tex_mix=0.5, emit=None, strength=0.0,
       coat=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    base = L.lin(color)
    p.inputs["Base Color"].default_value = (*base, 1)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    p.inputs["Specular IOR Level"].default_value = spec
    p.inputs["Coat Weight"].default_value = coat
    p.inputs["Coat Roughness"].default_value = 0.08
    if tex:
        tc = nt.nodes.new("ShaderNodeTexCoord")
        mp = nt.nodes.new("ShaderNodeMapping")
        mp.inputs["Scale"].default_value = (tex_scale,) * 3
        ti = nt.nodes.new("ShaderNodeTexImage")
        ti.image = bpy.data.images.load(os.path.join(L.TEX, tex), check_existing=True)
        ti.projection = "BOX"
        ti.projection_blend = 0.3
        nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
        nt.links.new(mp.outputs["Vector"], ti.inputs["Vector"])
        mx = nt.nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.blend_type = "OVERLAY"
        mx.inputs["Factor"].default_value = tex_mix
        mx.inputs["A"].default_value = (*base, 1)
        nt.links.new(ti.outputs["Color"], mx.inputs["B"])
        nt.links.new(mx.outputs["Result"], p.inputs["Base Color"])
        bump = nt.nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.35
        nt.links.new(ti.outputs["Color"], bump.inputs["Height"])
        nt.links.new(bump.outputs["Normal"], p.inputs["Normal"])
    if emit:
        p.inputs["Emission Color"].default_value = (*L.lin(emit), 1)
        p.inputs["Emission Strength"].default_value = strength
    return m


GOLD = pm("Gold", "#FFC83A", rough=0.24, metal=1.0)
GOLD_DK = pm("GoldDark", "#D98E22", rough=0.3, metal=1.0)
TEAL = pm("Teal", "#1FB8A8", rough=0.25, coat=0.6)
STONE = pm("Stone", "#7A6450", rough=0.85, tex="medieval__MI_UnevenBrick_albedo.jpg", tex_scale=0.35, tex_mix=0.7)
STONE_LT = pm("StoneLight", "#B59A78", rough=0.8, tex="medieval__MI_RockTrim_albedo.jpg", tex_scale=0.3, tex_mix=0.55)
INK = pm("Ink", "#2B1A0E", rough=0.6, spec=0.2)
RED = pm("Ribbon", "#C23A2A", rough=0.45, coat=0.2)
RED_DK = pm("RibbonDark", "#8A2018", rough=0.5)
CREAM = pm("Cream", "#FFF0D0", rough=0.4)
LEAF = pm("Leaf", "#4FA83A", rough=0.45, coat=0.3)
LEAF_DK = pm("LeafDark", "#2E7A2A", rough=0.5)


def rsquare(half, r, n=10):
    """Rounded square outline (points), centred on the origin."""
    pts = []
    for cx, cy, a0 in ((half - r, half - r, 0), (-(half - r), half - r, 90), (-(half - r), -(half - r), 180),
                       (half - r, -(half - r), 270)):
        for k in range(n + 1):
            a = math.radians(a0 + 90 * k / n)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def ring(outer, inner, r_out, r_in, depth, bevel, m, y=0.0):
    return L.shape2d([rsquare(outer, r_out), rsquare(inner, r_in)], depth=depth, bevel=bevel, m=m, loc=(0, y, 0))


# ---------------------------------------------------------------- frame and wall
wall = L.box((10.4, 0.4, 10.4), (0, 0.5, 0), STONE, bevel=0.0)
ring(5.2, 4.55, 1.3, 0.85, 0.5, 0.12, GOLD, y=-0.2)          # outer gold rim
ring(4.6, 3.95, 0.9, 0.5, 0.3, 0.04, GOLD_DK, y=-0.05)       # inlay bed
ring(4.0, 3.75, 0.55, 0.4, 0.45, 0.08, GOLD, y=-0.15)        # inner gold rim
# turquoise tiles set into the bed, with little gold studs between them
for side in range(4):
    for k in range(7):
        t = -3.3 + k * 1.1
        if side == 0:
            x, z, w, h = t, 4.27, 0.82, 0.42
        elif side == 1:
            x, z, w, h = t, -4.27, 0.82, 0.42
        elif side == 2:
            x, z, w, h = -4.27, t, 0.42, 0.82
        else:
            x, z, w, h = 4.27, t, 0.42, 0.82
        L.box((w, 0.3, h), (x, -0.2, z), TEAL, bevel=0.06)
for x, z in ((4.27, 4.27), (-4.27, 4.27), (4.27, -4.27), (-4.27, -4.27)):
    L.cylinder(0.32, 0.4, (x, -0.25, z), GOLD, rot=(90, 0, 0), bevel=0.08)
    L.cylinder(0.16, 0.42, (x, -0.3, z), TEAL, rot=(90, 0, 0), bevel=0.05)

# ---------------------------------------------------------------- crest: sun fan, brazier and the hearth flame
for k in range(11):
    a = math.radians(180 * k / 10)
    ca, sa = math.cos(a), math.sin(a)
    wedge = [(-0.22, 0.0), (0.22, 0.0), (0.34, 1.35), (0.0, 1.55), (-0.34, 1.35)]
    o = L.shape2d([L.rounded_poly(wedge, 0.08)], depth=0.25, bevel=0.06, m=GOLD if k % 2 == 0 else GOLD_DK,
                  loc=(1.75 * ca, 0.15, 1.35 + 1.75 * sa), rot=(90, -(math.degrees(a) - 90), 0))
    if k % 2 == 0:
        L.box((0.2, 0.2, 0.42), (2.35 * ca, -0.05, 1.35 + 2.35 * sa), TEAL, rot=(0, -(math.degrees(a) - 90), 0),
              bevel=0.05)
L.cylinder(1.6, 0.3, (0, 0.05, 1.35), STONE, rot=(90, 0, 0), bevel=0.08, verts=64)
bowl = L.lathe([(0.0, 0.0), (0.55, 0.0), (0.62, 0.1), (0.5, 0.2), (0.75, 0.35), (1.25, 0.75), (1.3, 0.88),
                (1.15, 0.92), (0.0, 0.92)], (0, -0.6, 0.15), GOLD, verts=48, sub=1)
L.cylinder(1.22, 0.12, (0, -0.6, 1.06), TEAL, bevel=0.04, verts=48)
fm = L.flame_mat("Fire", outer="#F0300A", mid="#FF8A10", core="#FFD860", strength=1.7)
for i in range(13):
    a = 2 * math.pi * i / 13 + rnd.uniform(-0.2, 0.2)
    rr = 0.0 if i == 0 else rnd.uniform(0.25, 0.7)
    h = (2.4 - rr * 1.6) * rnd.uniform(0.85, 1.05)
    r = rnd.uniform(0.32, 0.45) * (1.15 - rr * 0.5)
    prof = [(0.0, 0.0), (r * 0.9, 0.05), (r, 0.22 * h), (r * 0.7, 0.5 * h), (r * 0.28, 0.82 * h), (0.0, h)]
    o = L.lathe(prof, (rr * math.cos(a) * 0.9, -0.6 + rr * math.sin(a) * 0.3, 1.0), fm, verts=14, sub=1)
    o.rotation_euler = (rnd.uniform(-0.1, 0.1), math.cos(a) * rr * 0.5, 0)
    o.visible_shadow = False
core = L.sphere(0.45, (0, -0.7, 1.45), L.glow("Core", "#FFD86A", 2.5), scale=(1.1, 0.6, 1.0))
core.visible_shadow = False
spark = L.glow("Spark", "#FFC860", 8.0)
for _ in range(16):
    L.sphere(rnd.uniform(0.04, 0.08), (rnd.uniform(-1.6, 1.6), -0.9, rnd.uniform(2.8, 3.7)), spark, segs=8, rings=4)

# ---------------------------------------------------------------- banner with the title, ribbon, leaves
banner = [(-4.0, 0.8), (4.0, 0.8), (3.6, 0.0), (4.0, -0.8), (-4.0, -0.8), (-3.6, 0.0)]
L.shape2d([L.rounded_poly(banner, 0.12)], depth=0.5, bevel=0.1, m=STONE_LT, loc=(0, -0.7, -0.75))
L.shape2d([L.rounded_poly([(-4.12, 0.92), (4.12, 0.92), (3.7, 0.0), (4.12, -0.92), (-4.12, -0.92), (-3.7, 0.0)],
                          0.14)], depth=0.3, bevel=0.06, m=GOLD_DK, loc=(0, -0.5, -0.75))


def title(s, size, z, depth, m, outline_m, y=-1.2, spacing=1.0, outline=0.085):
    for mat, off, dy, ext in ((outline_m, outline, 0.12, depth * 0.6), (m, 0.0, 0.0, depth)):
        cd = bpy.data.curves.new("Title", "FONT")
        cd.body = s
        cd.font = bpy.data.fonts.load(L.FONT, check_existing=True)
        cd.size = size
        cd.space_character = spacing
        cd.extrude = ext / 2
        cd.bevel_depth = 0.05 if off == 0 else 0.02
        cd.bevel_resolution = 4
        cd.offset = off
        cd.align_x = "CENTER"
        cd.align_y = "CENTER"
        o = L.link(bpy.data.objects.new("Title", cd))
        L.assign(o, mat)
        L.place(o, (0, y + dy, z), (90, 0, 0))


TITLE_GOLD = pm("TitleGold", "#FFCB3C", rough=0.3, metal=0.55, coat=0.5, emit="#FFB020", strength=0.12)
title("FOUNDERS", 1.66, -0.72, 0.45, TITLE_GOLD, INK, y=-1.25, outline=0.13)
# ribbon "THE LONG RISE"
rib = [(-3.2, 0.48), (3.2, 0.48), (3.2, -0.48), (-3.2, -0.48)]
L.shape2d([L.rounded_poly(rib, 0.06)], depth=0.18, bevel=0.05, m=RED, loc=(0, -1.0, -2.25))
for sx in (-1, 1):
    tail = [(0, 0.42), (sx * 1.0, 0.42), (sx * 0.72, 0.0), (sx * 1.0, -0.42), (0, -0.42)]
    L.shape2d([tail], depth=0.14, bevel=0.04, m=RED_DK, loc=(sx * 3.0, -0.8, -2.45))
title("THE LONG RISE", 0.7, -2.25, 0.14, CREAM, INK, y=-1.2, spacing=1.04, outline=0.07)


def leaf(x, z, ang, s, m):
    pts = [(0, 0), (0.35, 0.18), (0.75, 0.2), (1.1, 0.0), (0.75, -0.2), (0.35, -0.18)]
    L.shape2d([[(px * s, py * s) for px, py in pts]], depth=0.06 * s, bevel=0.04 * s, m=m, loc=(x, -1.0, z),
              rot=(90, -ang, 0), smooth_curve=True)
    L.tube([(x, -1.08, z), (x + 0.8 * s * math.cos(math.radians(ang)), -1.08,
                            z + 0.8 * s * math.sin(math.radians(ang)))], 0.025 * s, LEAF_DK, smooth_path=False)


for sx in (-1, 1):
    for k, (dx, dz, ang, s) in enumerate(((3.55, 0.7, 70, 0.9), (3.85, 0.25, 30, 1.0), (3.9, -0.45, -10, 0.95),
                                          (3.7, -1.1, -45, 0.85), (2.9, -2.9, -70, 0.9), (2.4, -3.2, -110, 0.8),
                                          (1.5, -3.35, -150, 0.75))):
        a = ang if sx > 0 else 180 - ang
        leaf(sx * dx, dz, a, s, LEAF if k % 2 == 0 else LEAF_DK)
# a small gold medallion at the bottom with a turquoise stone
L.cylinder(0.45, 0.3, (0, -0.6, -3.35), GOLD, rot=(90, 0, 0), bevel=0.08, verts=48)
L.cylinder(0.25, 0.32, (0, -0.65, -3.35), TEAL, rot=(90, 0, 0), bevel=0.06, verts=32)

# ---------------------------------------------------------------- camera and light
cd = bpy.data.cameras.new("Cam")
cd.lens = 120
cam = bpy.data.objects.new("Cam", cd)
L.link(cam)
cam.location = (0, -61, 1.2)
target = Vector((0, 0, 0))
cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
cd.sensor_fit = "HORIZONTAL"
cd.sensor_width = 36
# frame exactly the 10.4 wide wall: width = sensor * dist / lens
cd.lens = 36 * 61 / 10.3
scene.camera = cam

w = bpy.data.worlds.new("W")
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (*L.lin("#3A2A20"), 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.5
scene.world = w


def area(name, loc, energy, color, size):
    ld = bpy.data.lights.new(name, "AREA")
    ld.energy = energy
    ld.color = color
    ld.size = size
    o = bpy.data.objects.new(name, ld)
    L.link(o)
    o.location = loc
    o.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()


area("Key", (-6, -10, 9), 2600, (1.0, 0.9, 0.72), 6)
area("Fill", (7, -12, -2), 600, (0.75, 0.85, 1.0), 8)
area("Top", (0, -4, 12), 500, (1.0, 0.9, 0.75), 6)
area("Front", (0, -40, 3), 1500, (1.0, 0.85, 0.65), 26)   # a big soft card the gold faces reflect
fl = bpy.data.objects.new("FlameLight", bpy.data.lights.new("FlameLight", "POINT"))
fl.data.energy = 260
fl.data.color = (1.0, 0.55, 0.2)
fl.data.shadow_soft_size = 0.6
fl.location = (0, -2.0, 2.0)
L.link(fl)

L.setup_render(SIZE, SAMPLES, transparent=False)
scene.render.image_settings.color_mode = "RGB"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"
scene.use_nodes = True
ct = scene.node_tree
ct.nodes.clear()
rl = ct.nodes.new("CompositorNodeRLayers")
comp = ct.nodes.new("CompositorNodeComposite")
glare = ct.nodes.new("CompositorNodeGlare")
glare.glare_type = "FOG_GLOW"
glare.quality = "HIGH"
glare.threshold = 1.1
glare.size = 8
glare.mix = -0.4
ct.links.new(rl.outputs["Image"], glare.inputs["Image"])
ct.links.new(glare.outputs["Image"], comp.inputs["Image"])
dest = OUT or TARGETS[0]
scene.render.filepath = dest
bpy.ops.render.render(write_still=True)
if OUT is None:
    for t in TARGETS[1:]:
        shutil.copyfile(dest, t)
print("ICON", dest if OUT else ", ".join(TARGETS))
