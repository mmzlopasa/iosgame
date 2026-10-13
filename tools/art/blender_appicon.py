"""Blender (4.2): renders the app icon "The Founder's Hearth".

A dramatic dusk close-up: the founder (a CC0 Quaternius villager) stands by a roaring campfire in the
foreground, warm firelight on one side and a cool moonlight rim on the other; behind them a stone
tower with lit windows and a golden banner rises against a huge pale moon and a teal-to-amber sky.
Big simple shapes and strong warm/cool contrast so it still reads at 60 px.

The PNG is opaque RGB (the App Store rejects icons with alpha). Without an explicit output path it
is written to both places the project reads it from:
  unity/Assets/Art/UI/AppIcon/AppIcon.png              (Unity player settings, Editor/UiSetup.cs)
  App/Assets.xcassets/AppIcon.appiconset/AppIcon.png   (the XcodeGen app target)

blender -b --factory-startup -P tools/art/blender_appicon.py -- [<out.png>|-] [size] [samples]
"""
import math
import os
import random
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import iconlib as L  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if len(argv) > 0 and argv[0] != "-" else None
SIZE = int(argv[1]) if len(argv) > 1 else 1024
SAMPLES = int(argv[2]) if len(argv) > 2 else 192
TARGETS = [os.path.join(L.ROOT, "unity", "Assets", "Art", "UI", "AppIcon", "AppIcon.png"),
           os.path.join(L.ROOT, "App", "Assets.xcassets", "AppIcon.appiconset", "AppIcon.png")]

L.reset()
scene = bpy.context.scene
rnd = random.Random(4)


def pm(name, color, rough=0.6, spec=0.3, emit=None, strength=0.0, tex=None, tex_scale=1.0, tex_mix=0.5, metal=0.0):
    """Physical (Principled) material with an optional box-projected texture for detail."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    base = L.lin(color) if isinstance(color, str) else color
    p.inputs["Base Color"].default_value = (*base, 1)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Specular IOR Level"].default_value = spec
    p.inputs["Metallic"].default_value = metal
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
    if emit:
        p.inputs["Emission Color"].default_value = (*L.lin(emit), 1)
        p.inputs["Emission Strength"].default_value = strength
    return m


def emission(name, color, strength):
    return L.glow(name, color, strength)


MOON = (-2.4, 25.5, 9.8, 2.3)  # x, (card), z, radius

# ---------------------------------------------------------------- sky (a big emissive backdrop card)
def sky():
    bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 26, 18), rotation=(math.radians(90), 0, 0))
    o = bpy.context.object
    m = bpy.data.materials.new("Sky")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    stops = [(0.275, "#FFC066"), (0.3, "#F5844E"), (0.335, "#B0507A"), (0.39, "#4E4A8C"), (0.5, "#1C2858")]
    cr.elements[0].position, cr.elements[0].color = stops[0][0], (*L.lin(stops[0][1]), 1)
    cr.elements[1].position, cr.elements[1].color = stops[-1][0], (*L.lin(stops[-1][1]), 1)
    for pos, c in stops[1:-1]:
        e = cr.elements.new(pos)
        e.color = (*L.lin(c), 1)
    nt.links.new(sep.outputs["Y"], ramp.inputs["Fac"])
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Strength"].default_value = 1.0
    nt.links.new(ramp.outputs["Color"], em.inputs["Color"])
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs[0])
    o.data.materials.append(m)
    o.visible_shadow = False
    # moon and its halo
    moon = L.cylinder(MOON[3], 0.1, (MOON[0], 25.5, MOON[2]), emission("Moon", "#FFF1CF", 2.2), rot=(90, 0, 0), bevel=0, verts=96)
    moon.visible_shadow = False
    halo = L.cylinder(MOON[3] * 1.8, 0.1, (MOON[0], 25.6, MOON[2]), None, rot=(90, 0, 0), bevel=0, verts=96)
    hm = bpy.data.materials.new("Halo")
    hm.use_nodes = True
    nt = hm.node_tree
    nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord")
    grad = nt.nodes.new("ShaderNodeTexGradient")
    grad.gradient_type = "SPHERICAL"
    mp = nt.nodes.new("ShaderNodeMapping")
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    mp.inputs["Scale"].default_value = (1 / (MOON[3] * 1.8),) * 3
    nt.links.new(mp.outputs["Vector"], grad.inputs["Vector"])
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (*L.lin("#FFD9A0"), 1)
    em.inputs["Strength"].default_value = 0.9
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    pw = nt.nodes.new("ShaderNodeMath")
    pw.operation = "POWER"
    pw.inputs[1].default_value = 2.2
    nt.links.new(grad.outputs["Fac"], pw.inputs[0])
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(pw.outputs[0], mix.inputs["Fac"])
    nt.links.new(tr.outputs[0], mix.inputs[1])
    nt.links.new(em.outputs[0], mix.inputs[2])
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(mix.outputs[0], out.inputs[0])
    halo.data.materials.append(hm)
    halo.visible_shadow = False
    # stars
    star = emission("Star", "#FFF6E0", 4.0)
    for _ in range(40):
        x, z = rnd.uniform(-9, 9), rnd.uniform(4, 14)
        if (x - MOON[0]) ** 2 + (z - MOON[2]) ** 2 < (MOON[3] * 2) ** 2:
            continue
        L.sphere(rnd.uniform(0.04, 0.09), (x, 25.4, z), star, segs=8, rings=4)


# ---------------------------------------------------------------- landscape
def ground():
    grass = pm("Grass", "#2C4A22", rough=0.8, tex="Grass_albedo.jpg", tex_scale=0.4, tex_mix=0.35)
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=60, y_segments=60, size=14)
    for v in bm.verts:
        x, y = v.co.x, v.co.y
        v.co.z = -0.05 * (x * x + (y + 2) ** 2) ** 0.5 + 0.25 * math.sin(x * 0.7) * math.cos(y * 0.5) - 0.2
        if y > 2:
            v.co.z += (y - 2) * 0.12
    o = L.from_bmesh("Ground", bm, grass)
    L.smooth(o)
    # distant hills (dark silhouettes, cooler)
    hill = pm("Hill", "#1E2A3A", rough=0.9)
    for x, y, r, h in ((-9, 18, 7, 2.2), (6, 20, 9, 2.8), (-2, 22, 8, 1.8), (12, 17, 5, 1.6)):
        L.sphere(r, (x, y, -0.5), hill, scale=(1, 0.5, h / r))
    pine = pm("Pine", "#14261E", rough=0.8)
    for i in range(22):
        x = rnd.uniform(-13, 13)
        if -3.5 < x < 3.5:
            continue
        y = rnd.uniform(9, 15)
        h = rnd.uniform(2.0, 3.6)
        L.cylinder(h * 0.28, h, (x, y, h / 2 + 0.2), pine, r2=0.0, verts=7, bevel=0)


def tower():
    stone = pm("TowerStone", "#C9B59A", rough=0.75, tex="medieval__MI_UnevenBrick_albedo.jpg", tex_scale=0.55,
               tex_mix=0.75)
    trim = pm("Trim", "#A8957C", rough=0.8, tex="medieval__MI_RockTrim_albedo.jpg", tex_scale=0.5, tex_mix=0.5)
    roof = pm("Roof", "#C23A2C", rough=0.45, spec=0.4)
    gold = pm("Gold", "#F2B21E", rough=0.3, metal=0.8)
    win = emission("Window", "#FFB54A", 6.0)
    x0, y0 = -1.6, 7.5
    L.cylinder(1.55, 1.0, (x0, y0, 0.4), trim, bevel=0.08, verts=40)
    L.cylinder(1.25, 7.5, (x0, y0, 4.5), stone, r2=1.12, bevel=0.03, verts=40)
    L.cylinder(1.4, 0.35, (x0, y0, 5.3), trim, bevel=0.06, verts=40)
    L.cylinder(1.55, 0.6, (x0, y0, 8.4), trim, bevel=0.06, verts=40)
    for k in range(12):
        a = 2 * math.pi * k / 12
        L.box((0.5, 0.5, 0.55), (x0 + 1.4 * math.cos(a), y0 + 1.4 * math.sin(a), 8.95), trim,
              rot=(0, 0, math.degrees(a)), bevel=0.05)
    L.cylinder(1.25, 1.6, (x0, y0, 9.4), stone, bevel=0.03, verts=40)
    L.cylinder(1.75, 2.8, (x0, y0, 11.6), roof, r2=0.02, bevel=0.03, verts=40)
    L.cylinder(0.05, 1.8, (x0, y0, 13.6), gold, bevel=0)
    flag = [(0, 0), (0.5, 0.1), (1.0, -0.05), (1.5, 0.1), (1.35, -0.25), (1.6, -0.55), (1.0, -0.45), (0.5, -0.6),
            (0.0, -0.5)]
    L.shape2d([flag], depth=0.04, bevel=0.01, m=pm("Banner", "#FFC82E", rough=0.5, emit="#FFB000", strength=0.3),
              loc=(x0 + 0.05, y0, 14.4), rot=(90, 0, -12), smooth_curve=True)
    for z, w, h in ((2.4, 0.42, 0.8), (4.3, 0.36, 0.7), (6.6, 0.36, 0.7), (9.5, 0.5, 0.75)):
        wp = [(-w / 2, 0), (w / 2, 0), (w / 2, h * 0.7), (0, h), (-w / 2, h * 0.7)]
        L.shape2d([wp], depth=0.2, bevel=0.0, m=win, loc=(x0 + 0.25, y0 - 1.15 - (0.0 if z > 9 else 0.04 * z), z),
                  rot=(90, 0, -8))
    door = [(-0.45, 0), (0.45, 0), (0.45, 1.0), (0, 1.4), (-0.45, 1.0)]
    L.shape2d([door], depth=0.2, bevel=0.02, m=pm("Door", "#5A3018", rough=0.6), loc=(x0 + 0.2, y0 - 1.5, 0.75),
              rot=(90, 0, -8))
    # a cottage at its foot (warm windows)
    house = pm("Plaster", "#D9C2A0", rough=0.8, tex="medieval__MI_Plaster_albedo.jpg", tex_scale=0.5, tex_mix=0.4)
    hx, hy = 2.9, 8.6
    L.box((2.4, 1.8, 1.6), (hx, hy, 0.9), house, rot=(0, 0, -18), bevel=0.04)
    g = L.shape2d([[(-1.0, 0), (1.0, 0), (0, 1.05)]], depth=1.75, bevel=0, m=house, loc=(hx, hy, 1.7), rot=(90, 0, 72))
    for s in (-1, 1):
        L.box((2.8, 1.25, 0.14), (hx + s * 0.18, hy + s * 0.55, 2.2), pm("Thatch", "#7A4A2A", rough=0.9,
                                                                         tex="Thatch_albedo.jpg", tex_mix=0.6),
              rot=(s * 46, 0, -18), bevel=0.04)
    L.box((0.42, 0.1, 0.42), (hx - 0.4, hy - 0.95, 1.0), win, rot=(0, 0, -18), bevel=0.02)
    L.box((0.42, 0.1, 0.42), (hx + 0.55, hy - 0.68, 1.0), win, rot=(0, 0, -18), bevel=0.02)


def campfire(x, y):
    rock = pm("Rock", "#8A8A92", rough=0.7, tex="RockGround_albedo.jpg", tex_mix=0.4)
    for k in range(10):
        a = 2 * math.pi * k / 10
        L.hull_rock(0.32, (x + 1.0 * math.cos(a), y + 0.85 * math.sin(a), 0.0), rock, seed=k + 3,
                    scale=(1.2, 1.0, 0.8), n=12, bevel=0.1)
    bark = pm("Bark", "#5A3218", rough=0.8, tex="Bark_albedo.jpg", tex_mix=0.6)
    for ang in (30, -40, 100, 160):
        a = math.radians(ang)
        L.cylinder(0.15, 1.7, (x + 0.15 * math.cos(a), y + 0.15 * math.sin(a), 0.25), bark,
                   rot=(78, 0, ang), bevel=0.04)
    ember = emission("Ember", "#FF6A1A", 8.0)
    for k in range(9):
        L.sphere(rnd.uniform(0.08, 0.16), (x + rnd.uniform(-0.45, 0.45), y + rnd.uniform(-0.35, 0.35), 0.1), ember,
                 segs=10, rings=6)
    fm = L.flame_mat("Fire", outer="#E8280A", mid="#FF7A10", core="#FFD860", strength=2.3)
    # flame tongues: tapered, curling lathes leaning outwards; hot core inside, red licks outside
    for i in range(22):
        a = 2 * math.pi * i / 22 + rnd.uniform(-0.2, 0.2)
        rr = rnd.uniform(0.0, 0.42)
        dx, dy = rr * math.cos(a), rr * math.sin(a) * 0.7
        h = (2.7 - rr * 2.6) * rnd.uniform(0.75, 1.05)
        r = rnd.uniform(0.26, 0.4) * (1.1 - rr)
        prof = [(0.0, 0.0), (r * 0.9, 0.04), (r, 0.22 * h), (r * 0.7, 0.5 * h), (r * 0.28, 0.82 * h), (0.0, h)]
        o = L.lathe(prof, (x + dx, y + dy, 0.25), fm, verts=12, sub=1)
        o.rotation_euler = (-dy * 0.6 + rnd.uniform(-0.1, 0.1), dx * 0.6 + rnd.uniform(-0.1, 0.1), 0)
        o.visible_shadow = False
    core = L.sphere(0.5, (x, y, 0.55), L.glow("Core", "#FFE27A", 5.0), scale=(1.2, 1.0, 1.1))
    core.visible_shadow = False
    spark = emission("Spark", "#FFC860", 12.0)
    for _ in range(26):
        t = rnd.random()
        L.sphere(rnd.uniform(0.025, 0.06), (x + rnd.uniform(-0.8, 0.8) * (0.4 + t), y + rnd.uniform(-0.3, 0.6),
                                            2.2 + t * 4.2), spark, segs=6, rings=4)
    light = bpy.data.lights.new("FireLight", "POINT")
    light.energy = 1700
    light.color = (1.0, 0.52, 0.18)
    light.shadow_soft_size = 0.5
    lo = bpy.data.objects.new("FireLight", light)
    scene.collection.objects.link(lo)
    lo.location = (x, y - 0.1, 1.3)


def grouped(fn, loc, scale, *args):
    """Builds fn(*args) and moves/scales everything it made as one group."""
    before = set(bpy.data.objects)
    fn(*args)
    made = [o for o in bpy.data.objects if o not in before and o.parent is None]
    return L.parent(made, loc=loc, scale=scale)


HERO = 2.15  # the founder's scale (the scene was laid out in "big" units)


def founder(x, y, rot_z):
    """The founder, 3/4 front, holding a torch at shoulder height whose flame lights his face."""
    objs = L.import_fbx(os.path.join(L.MODELS, "Person_Woodcutter_Male.fbx"))
    arm = next(o for o in objs if o.type == "ARMATURE")
    for o in objs:
        if o.name.startswith("Prop_"):
            o.hide_render = True
    torch_pose = next(a for a in bpy.data.actions if a.name.endswith("|Torch"))
    arm.animation_data.action = torch_pose
    for fc in [f for f in torch_pose.fcurves if f.data_path in (
            'pose.bones["Head"].rotation_quaternion', 'pose.bones["neck_01"].rotation_quaternion',
            'pose.bones["Head"].scale')]:
        torch_pose.fcurves.remove(fc)
    scene.frame_set(1)
    for b in ("Head", "neck_01"):  # level head turned to the camera (eyes at the viewer)
        pb = arm.pose.bones[b]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (math.radians(-13), math.radians(-8), 0)
    arm.pose.bones["Head"].scale = (1.15, 1.15, 1.15)
    recolor = {"Main": "#3F8A3A", "Trousers": "#4A3020", "Boots": "#3A2414", "Hair": "#3A2010", "Skin": "#E8A27A"}
    for o in objs:
        if o.type != "MESH":
            continue
        for slot in o.material_slots:
            m = slot.material
            if not m:
                continue
            nm = m.name.split(".")[0]
            p = m.node_tree.nodes.get("Principled BSDF")
            if p:
                if nm in recolor:
                    p.inputs["Base Color"].default_value = (*L.lin(recolor[nm]), 1)
                p.inputs["Roughness"].default_value = 0.55
                if nm == "Skin":
                    p.inputs["Subsurface Weight"].default_value = 0.25
                    p.inputs["Subsurface Radius"].default_value = (0.5, 0.25, 0.15)
                    p.inputs["Subsurface Scale"].default_value = 0.05
    roots = [o for o in objs if o.parent is None]
    L.parent(roots, loc=(x, y, -0.3), rot=(0, 0, rot_z), scale=HERO)
    bpy.context.view_layer.update()
    hand = arm.matrix_world @ arm.pose.bones["hand_l"].head
    # torch: a wrapped stick through the fist, flame above the head height of the hand
    wood = pm("TorchWood", "#5A3418", rough=0.7)
    base = hand + Vector((-0.08, -0.02, -0.3))
    top = hand + Vector((0.05, 0.45, 0.95))
    L.tube([tuple(base), tuple(top)], 0.042, wood, smooth_path=False)
    L.tube([tuple(top + Vector((0, 0, -0.12))), tuple(top + Vector((0, 0, 0.03)))], 0.065,
           pm("Wrap", "#8A6A40", rough=0.9), smooth_path=False)
    fm = L.flame_mat("TorchFire", outer="#E8300A", mid="#FF8A12", core="#FFE070", strength=2.6)
    for i in range(12):
        a = 2 * math.pi * i / 12
        rr = 0.0 if i == 0 else 0.06
        h = 0.5 if i == 0 else rnd.uniform(0.22, 0.4)
        r = 0.1 if i == 0 else 0.055
        prof = [(0.0, 0.0), (r * 0.9, 0.03), (r, 0.22 * h), (r * 0.7, 0.5 * h), (r * 0.28, 0.82 * h), (0.0, h)]
        o = L.lathe(prof, (top.x + rr * math.cos(a), top.y + rr * math.sin(a), top.z - 0.02), fm, verts=12, sub=1)
        o.rotation_euler = (rnd.uniform(-0.15, 0.15), rnd.uniform(-0.25, 0.1), 0)
        o.visible_shadow = False
    core = L.sphere(0.1, tuple(top + Vector((0, 0, 0.1))), L.glow("TorchCore", "#FFE27A", 6.0))
    core.visible_shadow = False
    tl = bpy.data.objects.new("TorchLight", bpy.data.lights.new("TorchLight", "POINT"))
    tl.data.energy = 380
    tl.data.color = (1.0, 0.6, 0.28)
    tl.data.shadow_soft_size = 0.25
    tl.location = top + Vector((0.05, -0.25, 0.35))
    scene.collection.objects.link(tl)
    spark = emission("TorchSpark", "#FFC860", 10.0)
    for _ in range(8):
        t = rnd.random()
        L.sphere(rnd.uniform(0.01, 0.022), tuple(top + Vector((rnd.uniform(-0.4, 0.4) * (0.3 + t), rnd.uniform(-0.2, 0.2),
                                                               0.5 + t * 0.9))), spark, segs=6, rings=4)
    return objs


sky()
ground()
grouped(tower, (-1.3, 9.0, 0.0), 0.62)
grouped(campfire, (-1.2, 2.6, 0.0), 0.75, 0.0, 0.0)
founder(0.55, -2.4, 22)

# ---------------------------------------------------------------- camera and lights
cd = bpy.data.cameras.new("Cam")
cd.lens = 38
cam = bpy.data.objects.new("Cam", cd)
scene.collection.objects.link(cam)
cam.location = (0.95, -4.75, 3.55)
target = Vector((0.9, 0.0, 3.3))
cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.camera = cam

world = bpy.data.worlds.new("Dusk")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (*L.lin("#3A4A7A"), 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.3
scene.world = world
moon_l = bpy.data.objects.new("MoonRim", bpy.data.lights.new("MoonRim", "SUN"))
moon_l.data.energy = 2.2
moon_l.data.color = (0.55, 0.75, 1.0)
moon_l.data.angle = math.radians(4)
moon_l.rotation_euler = (math.radians(-62), 0, math.radians(200))
scene.collection.objects.link(moon_l)
rim = bpy.data.objects.new("Rim", bpy.data.lights.new("Rim", "AREA"))
rim.data.energy = 700
rim.data.size = 2.5
rim.data.color = (0.55, 0.78, 1.0)
rim.location = (4.0, 1.5, 5.0)
rim.rotation_euler = (Vector((0.55, -2.4, 3.2)) - rim.location).to_track_quat("-Z", "Y").to_euler()
scene.collection.objects.link(rim)
warm = bpy.data.objects.new("Glow", bpy.data.lights.new("Glow", "AREA"))
warm.data.energy = 35
warm.data.size = 6
warm.data.color = (1.0, 0.6, 0.35)
warm.location = (0, -9, 3)
warm.rotation_euler = (target - warm.location).to_track_quat("-Z", "Y").to_euler()
scene.collection.objects.link(warm)

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
glare.threshold = 1.2
glare.size = 8
glare.mix = -0.3
ell = ct.nodes.new("CompositorNodeEllipseMask")
ell.width, ell.height = 1.25, 1.25
blur = ct.nodes.new("CompositorNodeBlur")
blur.size_x = blur.size_y = int(SIZE * 0.2)
mixv = ct.nodes.new("CompositorNodeMixRGB")
mixv.blend_type = "MULTIPLY"
ct.links.new(rl.outputs["Image"], glare.inputs["Image"])
ct.links.new(ell.outputs["Mask"], blur.inputs["Image"])
mapv = ct.nodes.new("CompositorNodeMapRange")
mapv.inputs["To Min"].default_value = 0.55
ct.links.new(blur.outputs["Image"], mapv.inputs["Value"])
ct.links.new(glare.outputs["Image"], mixv.inputs[1])
ct.links.new(mapv.outputs["Value"], mixv.inputs[2])
ct.links.new(mixv.outputs["Image"], comp.inputs["Image"])
dest = OUT or TARGETS[0]
scene.render.filepath = dest
bpy.ops.render.render(write_still=True)
if OUT is None:
    for t in TARGETS[1:]:
        shutil.copyfile(dest, t)
print("ICON", dest if OUT else ", ".join(TARGETS))
