"""Blender (4.2): path-traced pictures of the buildings for the build menu and building cards.

Each job is one model: an OBJ exported from Unity by FoundersEditor.ExportBuildingModels (assembled
exactly as the game builds it), or - for the local fallback - a kit FBX straight from
unity/Assets/Art/Resources/Models. Materials are rebuilt as Principled BSDFs from the job's material
table (albedo x base colour, OpenGL normal map, URP mask: R metallic, G occlusion, A smoothness, plus
a little roughness noise), lit by the game's Poly Haven sky HDRI and a warm sun aligned with the
HDRI's own sun, and rendered with Cycles (CPU, adaptive sampling, OpenImageDenoise, AgX) on a
transparent film over an invisible shadow-catcher ground. The camera is the game's three-quarter
view (35 deg elevation, 45 deg yaw, street side towards the viewer), auto-framed to fill ~88%.

Per job it writes <raw>/<file>.png (the building, straight RGBA) and <raw>/<file>_shadow.png
(black, alpha = ground shadow + contact occlusion). render_buildings.py adds the kit outline,
composites the shadow and downsamples to the 256 px pictures the game loads.

blender -b --factory-startup -P tools/art/blender_buildings.py -- <jobs.json> <raw dir>
        [--samples N] [--res PX] [--only key,key]
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
HDRI = os.path.join(ROOT, "unity", "Assets", "Art", "Sky", "Sky.hdr")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def opt(flag, default, cast=str):
    if flag in argv:
        i = argv.index(flag)
        v = cast(argv[i + 1])
        del argv[i:i + 2]
        return v
    return default


SAMPLES = opt("--samples", 192, int)
RES = opt("--res", 512, int)
ONLY = [k for k in opt("--only", "").split(",") if k]
JOBS, RAW = argv[0], argv[1]

# Look (tuned on the preview sheet).
CAM_ELEVATION = 35.0     # degrees above the ground
CAM_YAW = 45.0           # from the street side (-y in Blender), towards the viewer's left
CAM_FOV = 22.0           # degrees; a long-ish lens like the game's thumbnail camera
FILL = 0.88              # the model spans this much of the frame
SUN_ELEVATION = 58.0     # high enough for short, readable shadows
SUN_AZIMUTH_FROM_CAM = -55.0  # key from the camera's left, a little from the front
SUN_STRENGTH = 3.2
SUN_ANGLE = 6.0          # soft penumbra (degrees)
SUN_COLOR = (1.0, 0.88, 0.74)
SKY_STRENGTH = 1.0
EXPOSURE = 0.15
LOOK = "AgX - Medium High Contrast"


# ---------------------------------------------------------------------------------------- scene

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def setup_render():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    cy = sc.cycles
    cy.device = "CPU"
    cy.samples = SAMPLES
    cy.use_adaptive_sampling = True
    cy.adaptive_threshold = 0.015
    cy.adaptive_min_samples = 32
    cy.use_denoising = True
    cy.denoiser = "OPENIMAGEDENOISE"
    cy.denoising_input_passes = "RGB_ALBEDO_NORMAL"
    cy.denoising_prefilter = "ACCURATE"
    cy.max_bounces = 8
    cy.diffuse_bounces = 4
    cy.glossy_bounces = 3
    cy.transmission_bounces = 6
    cy.transparent_max_bounces = 16
    cy.caustics_reflective = False
    cy.caustics_refractive = False
    cy.sample_clamp_indirect = 8.0
    cy.blur_glossy = 1.0
    sc.render.film_transparent = True
    sc.render.resolution_x = RES
    sc.render.resolution_y = RES
    sc.render.resolution_percentage = 100
    sc.render.filter_size = 1.2
    sc.render.threads_mode = "AUTO"
    sc.view_settings.view_transform = "AgX"
    try:
        sc.view_settings.look = LOOK
    except TypeError:
        pass
    sc.view_settings.exposure = EXPOSURE
    sc.display_settings.display_device = "sRGB"
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.image_settings.color_depth = "8"
    bpy.context.view_layer.cycles.use_pass_shadow_catcher = True


_SUN = {}


def hdri_sun():
    """Direction (azimuth, elevation in radians, in the HDRI's own frame) of the brightest spot of the sky."""
    if "dir" in _SUN:
        return _SUN["dir"]
    img = bpy.data.images.load(HDRI, check_existing=True)
    import numpy as np
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    lum = (px.reshape(h, w, 4)[..., :3] @ np.array([0.2126, 0.7152, 0.0722], np.float32))
    lum[: h // 2] = 0  # only the upper half holds the sun
    y, x = np.unravel_index(int(np.argmax(lum)), lum.shape)
    best = float(lum[y, x])
    u, v = (x + 0.5) / w, (y + 0.5) / h
    # Blender's equirectangular lookup: u = 0.5 - atan2(y, x) / 2pi, v = 0.5 + elevation / pi (row 0 at the bottom).
    az = -(u - 0.5) * 2 * math.pi
    el = (v - 0.5) * math.pi
    _SUN["dir"] = (az, el)
    print(f"[hdri] sun at u={u:.3f} v={v:.3f} -> az {math.degrees(az):.1f} el {math.degrees(el):.1f} (lum {best:.1f})")
    return _SUN["dir"]


def world(sun_az):
    """Sky HDRI turned so its sun sits at sun_az (radians, Blender world azimuth)."""
    w = bpy.data.worlds.new("Sky")
    bpy.context.scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    env = nt.nodes.new("ShaderNodeTexEnvironment")
    env.image = bpy.data.images.load(HDRI, check_existing=True)
    mapping = nt.nodes.new("ShaderNodeMapping")
    coord = nt.nodes.new("ShaderNodeTexCoord")
    az, _ = hdri_sun()
    nt.links.new(coord.outputs["Generated"], mapping.inputs["Vector"])
    nt.links.new(mapping.outputs["Vector"], env.inputs["Vector"])
    nt.links.new(env.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = SKY_STRENGTH
    nt.links.new(bg.outputs["Background"], out.inputs["Surface"])
    # Mapping rotates the lookup vector: a world direction d samples the HDRI at R(d), so the HDRI's sun
    # (az) appears at az - rotation. Invert the sign accordingly.
    mapping.inputs["Rotation"].default_value = (0, 0, az - sun_az)


def sun(direction):
    ld = bpy.data.lights.new("Sun", "SUN")
    ld.energy = SUN_STRENGTH
    ld.color = SUN_COLOR
    ld.angle = math.radians(SUN_ANGLE)
    o = bpy.data.objects.new("Sun", ld)
    bpy.context.scene.collection.objects.link(o)
    o.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    return o


def compositor(raw, name):
    """Building (Combined, without the catcher's shadows) and the shadow as black + alpha."""
    sc = bpy.context.scene
    sc.use_nodes = True
    nt = sc.node_tree
    nt.nodes.clear()
    rl = nt.nodes.new("CompositorNodeRLayers")
    comp = nt.nodes.new("CompositorNodeComposite")
    nt.links.new(rl.outputs["Image"], comp.inputs["Image"])
    bw = nt.nodes.new("CompositorNodeRGBToBW")
    nt.links.new(rl.outputs["Shadow Catcher"], bw.inputs["Image"])
    inv = nt.nodes.new("CompositorNodeMath")
    inv.operation = "SUBTRACT"
    inv.use_clamp = True
    inv.inputs[0].default_value = 1.0
    nt.links.new(bw.outputs[0], inv.inputs[1])
    # A black image the size of the render (a constant colour would be a single value, which the
    # File Output node skips).
    black = nt.nodes.new("CompositorNodeMixRGB")
    black.blend_type = "MULTIPLY"
    black.inputs[0].default_value = 1.0
    black.inputs[2].default_value = (0, 0, 0, 1)
    nt.links.new(rl.outputs["Image"], black.inputs[1])
    sa = nt.nodes.new("CompositorNodeSetAlpha")
    sa.mode = "REPLACE_ALPHA"
    nt.links.new(black.outputs[0], sa.inputs["Image"])
    nt.links.new(inv.outputs[0], sa.inputs["Alpha"])
    fo = nt.nodes.new("CompositorNodeOutputFile")
    fo.base_path = raw
    fo.format.file_format = "PNG"
    fo.format.color_mode = "RGBA"
    fo.file_slots.clear()
    fo.file_slots.new(name + "_shadow_")
    nt.links.new(sa.outputs[0], fo.inputs[0])


# ---------------------------------------------------------------------------------------- materials

def _img(path, non_color=False):
    if not path or not os.path.exists(path):
        return None
    im = bpy.data.images.load(path, check_existing=True)
    if non_color:
        im.colorspace_settings.name = "Non-Color"
    return im


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def build_material(name, spec):
    """Principled BSDF from a material table entry (see render_buildings.py for the fields)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    L = nt.links.new
    color = spec.get("color", [1, 1, 1, 1])
    tint = tuple(srgb_to_linear(c) for c in color[:3])
    smooth = float(spec.get("smoothness", 0.3))
    metal = float(spec.get("metallic", 0.0))
    lname = name.lower()

    if "glass" in lname and not spec.get("albedo"):
        # Window panes (untextured in the kit): dark, slightly blue, glossy glass.
        bsdf.inputs["Base Color"].default_value = (0.035, 0.045, 0.055, 1)
        bsdf.inputs["Roughness"].default_value = 0.08
        bsdf.inputs["Specular IOR Level"].default_value = 0.6
        return m

    uv = nt.nodes.new("ShaderNodeTexCoord")
    albedo = _img(spec.get("albedo"))
    alpha_out = None
    if albedo:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = albedo
        tex.interpolation = "Cubic"
        L(uv.outputs["UV"], tex.inputs["Vector"])
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs["Factor"].default_value = 1.0
        L(tex.outputs["Color"], mul.inputs["A"])
        mul.inputs["B"].default_value = (*tint, 1)
        base = mul.outputs["Result"]
        alpha_out = tex.outputs["Alpha"]
    else:
        base = None
        bsdf.inputs["Base Color"].default_value = (*tint, 1)

    # Roughness: from the mask's smoothness when there is one, with a little large-scale variation.
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 3.5
    noise.inputs["Detail"].default_value = 4.0
    obj_coord = uv.outputs["Object"]
    L(obj_coord, noise.inputs["Vector"])
    var = nt.nodes.new("ShaderNodeMapRange")
    var.inputs["From Min"].default_value = 0.3
    var.inputs["From Max"].default_value = 0.7
    var.inputs["To Min"].default_value = -0.08
    var.inputs["To Max"].default_value = 0.08
    L(noise.outputs["Fac"], var.inputs["Value"])
    rough_add = nt.nodes.new("ShaderNodeMath")
    rough_add.operation = "ADD"
    rough_add.use_clamp = True
    L(var.outputs["Result"], rough_add.inputs[1])

    mask = _img(spec.get("mask"), non_color=True)
    if mask:
        mt = nt.nodes.new("ShaderNodeTexImage")
        mt.image = mask
        mask.alpha_mode = "CHANNEL_PACKED"
        L(uv.outputs["UV"], mt.inputs["Vector"])
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        L(mt.outputs["Color"], sep.inputs["Color"])
        # roughness = 1 - smoothness_map * smoothness ; URP smoothness is a bit "glossier" than Blender's
        # roughness, so it is softened towards matte (photo-scanned surfaces read better slightly rough).
        sm = nt.nodes.new("ShaderNodeMath")
        sm.operation = "MULTIPLY"
        sm.inputs[1].default_value = smooth * 0.85
        L(mt.outputs["Alpha"], sm.inputs[0])
        r = nt.nodes.new("ShaderNodeMath")
        r.operation = "SUBTRACT"
        r.inputs[0].default_value = 1.0
        L(sm.outputs[0], r.inputs[1])
        # Scanned metal reads as white plastic when it mirrors the sky: keep it a little rough.
        floor = nt.nodes.new("ShaderNodeMath")
        floor.operation = "MAXIMUM"
        floor.inputs[1].default_value = 0.32
        L(r.outputs[0], floor.inputs[0])
        L(floor.outputs[0], rough_add.inputs[0])
        L(sep.outputs["Red"], bsdf.inputs["Metallic"])
        # Baked occlusion darkens crevices a little (path tracing does the rest).
        if base is not None:
            ao = nt.nodes.new("ShaderNodeMix")
            ao.data_type = "RGBA"
            ao.blend_type = "MULTIPLY"
            ao.inputs["Factor"].default_value = 0.6
            L(base, ao.inputs["A"])
            L(sep.outputs["Green"], ao.inputs["B"])
            base = ao.outputs["Result"]
    else:
        rough_add.inputs[0].default_value = min(1.0, max(0.05, 1.0 - smooth))
        bsdf.inputs["Metallic"].default_value = metal
    L(rough_add.outputs[0], bsdf.inputs["Roughness"])

    if base is not None:
        L(base, bsdf.inputs["Base Color"])

    normal = _img(spec.get("normal"), non_color=True)
    if normal:
        nt_ = nt.nodes.new("ShaderNodeTexImage")
        nt_.image = normal
        L(uv.outputs["UV"], nt_.inputs["Vector"])
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = float(spec.get("bumpScale", 1.0))
        L(nt_.outputs["Color"], nm.inputs["Color"])
        L(nm.outputs["Normal"], bsdf.inputs["Normal"])

    if spec.get("alphaClip") and alpha_out is not None:
        gt = nt.nodes.new("ShaderNodeMath")
        gt.operation = "GREATER_THAN"
        gt.inputs[1].default_value = float(spec.get("cutoff", 0.5))
        L(alpha_out, gt.inputs[0])
        L(gt.outputs[0], bsdf.inputs["Alpha"])
    elif color[3] < 0.999:
        bsdf.inputs["Alpha"].default_value = color[3]

    em = spec.get("emission") or [0, 0, 0]
    if max(em) > 0.001:
        bsdf.inputs["Emission Color"].default_value = (*em[:3], 1)
        bsdf.inputs["Emission Strength"].default_value = 1.0

    # Plant cards and leaves: a touch of translucency so they glow when back-lit.
    if spec.get("alphaClip"):
        bsdf.inputs["Subsurface Weight"].default_value = 0.0
        bsdf.inputs["Transmission Weight"].default_value = 0.0
        bsdf.inputs["Specular IOR Level"].default_value = 0.3
    return m


def base_name(n):
    """'MI_Brick.001' -> 'MI_Brick'."""
    if len(n) > 4 and n[-4] == "." and n[-3:].isdigit():
        return n[:-4]
    return n


# ---------------------------------------------------------------------------------------- effects

def _flame_material():
    m = bpy.data.materials.get("FlameMat")
    if m:
        return m
    m = bpy.data.materials.new("FlameMat")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    L = nt.links.new
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.5
    # Height in the tongue (generated z: 0 bottom .. 1 tip) for a yellow root and a red tip.
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    L(tc.outputs["Generated"], sep.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    els = ramp.color_ramp.elements
    els[0].position, els[0].color = 0.0, (1.0, 0.3, 0.02, 1)
    els[1].position, els[1].color = 1.0, (0.7, 0.05, 0.0, 1)
    L(sep.outputs["Z"], ramp.inputs["Fac"])
    core = nt.nodes.new("ShaderNodeMix")
    core.data_type = "RGBA"
    L(lw.outputs["Facing"], core.inputs["Factor"])
    core.inputs["A"].default_value = (1.0, 0.5, 0.08, 1)
    L(ramp.outputs["Color"], core.inputs["B"])
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Strength"].default_value = 1.5
    L(core.outputs["Result"], emit.inputs["Color"])
    # Soft edges and a fading tip.
    op = nt.nodes.new("ShaderNodeMath")
    op.operation = "MULTIPLY"
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    L(lw.outputs["Facing"], inv.inputs[1])
    tip = nt.nodes.new("ShaderNodeMapRange")
    tip.inputs["From Min"].default_value = 1.0
    tip.inputs["From Max"].default_value = 0.55
    L(sep.outputs["Z"], tip.inputs["Value"])
    L(inv.outputs[0], op.inputs[0])
    L(tip.outputs["Result"], op.inputs[1])
    dense = nt.nodes.new("ShaderNodeMath")  # a dense core, only the rim and the tip fade out
    dense.operation = "MULTIPLY"
    dense.use_clamp = True
    dense.inputs[1].default_value = 1.8
    L(op.outputs[0], dense.inputs[0])
    op = dense
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    L(op.outputs[0], mix.inputs["Fac"])
    L(tr.outputs[0], mix.inputs[1])
    L(emit.outputs[0], mix.inputs[2])
    L(mix.outputs[0], out.inputs["Surface"])
    return m


def flame(pos, scale):
    """A small path-traced fire the size of Effects.Fire at this scale: a few glowing tongues with soft,
    transparent edges, and a warm point light that lights the logs and stones around it."""
    pos = Vector(pos)
    tongues = [((0, 0), 1.0, 0.17), ((0.07, 0.04), 0.72, 0.12), ((-0.06, 0.05), 0.62, 0.11), ((0.01, -0.07), 0.55, 0.1)]
    for i, ((dx, dy), h, r) in enumerate(tongues):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=1.0)
        o = bpy.context.active_object
        o.name = f"Flame{i}"
        for v in o.data.vertices:
            t = (v.co.z + 1) / 2  # 0 bottom .. 1 top
            k = math.sin(math.pi * min(1.0, t * 1.15) ** 0.6) * (1 - t) ** 0.35 if t < 1 else 0
            v.co.x *= k
            v.co.y *= k
            v.co.z = t
        for p in o.data.polygons:
            p.use_smooth = True
        o.location = pos + Vector((dx, dy, 0)) * scale
        o.scale = (r * scale, r * scale, 0.62 * h * scale)
        o.rotation_euler = (math.radians(dy * 60), math.radians(-dx * 60), 0)
        o.data.materials.append(_flame_material())
        o.visible_shadow = False
        o["flame"] = True
    ld = bpy.data.lights.new("FireLight", "POINT")
    ld.energy = 30 * scale
    ld.color = (1.0, 0.5, 0.18)
    ld.shadow_soft_size = 0.12 * scale
    lo = bpy.data.objects.new("FireLight", ld)
    bpy.context.scene.collection.objects.link(lo)
    lo.location = pos + Vector((0, 0, 0.25 * scale))


# ---------------------------------------------------------------------------------------- import

def obj_to_blender(p):
    """Exporter coordinates (Y up, right-handed, street side -z) -> Blender (Z up, street side +y)."""
    return Vector((p[0], -p[2], p[1]))


def import_job(job):
    before = set(bpy.data.objects)
    src = job["source"]
    if job["format"] == "obj":
        bpy.ops.wm.obj_import(filepath=src, forward_axis="NEGATIVE_Z", up_axis="Y")
    else:
        bpy.ops.import_scene.fbx(filepath=src)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    for o in new:
        if o.type not in ("MESH", "EMPTY"):
            bpy.data.objects.remove(o, do_unlink=True)
    # Game placement (FBX fallback): yaw about the vertical and a uniform scale, around the origin.
    t = job.get("transform") or {}
    if t:
        root = Matrix.Rotation(math.radians(t.get("yaw", 0.0)), 4, "Z") @ Matrix.Scale(t.get("scale", 1.0), 4)
        for o in new:
            if o.parent is None and o.name in bpy.data.objects:
                o.matrix_world = root @ o.matrix_world
    specs = job.get("materials", {})
    built = {}
    for o in meshes:
        for slot in o.material_slots:
            if slot.material is None:
                continue
            key = base_name(slot.material.name)
            spec = specs.get(key) or specs.get(slot.material.name)
            if spec is None:
                print(f"[warn] {job['key']}: no material spec for {slot.material.name}")
                spec = {"color": [0.5, 0.45, 0.4, 1]}
            if key not in built:
                built[key] = build_material(key + "_R", spec)
            slot.material = built[key]
    return meshes


def world_points(meshes, limit=300000):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    total = sum(len(o.data.vertices) for o in meshes)
    step = max(1, total // limit)
    for o in meshes:
        ev = o.evaluated_get(dg)
        mw = ev.matrix_world
        vs = ev.data.vertices
        for i in range(0, len(vs), step):
            pts.append(mw @ vs[i].co)
    return pts


# ---------------------------------------------------------------------------------------- camera

def make_camera():
    cd = bpy.data.cameras.new("Cam")
    cd.type = "PERSP"
    cd.sensor_fit = "AUTO"
    cd.angle = math.radians(CAM_FOV)
    cd.clip_start = 0.05
    cd.clip_end = 1000
    cam = bpy.data.objects.new("Cam", cd)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    return cam


def cam_dir():
    """Unit vector from the subject towards the camera: street side (+y), from the viewer's left... as in game."""
    el, yaw = math.radians(CAM_ELEVATION), math.radians(CAM_YAW)
    # Street side is +y; the game camera sits at the front-left of the building (Unity -x,-z -> Blender +x,+y).
    return Vector((math.sin(yaw) * math.cos(el), math.cos(yaw) * math.cos(el), math.sin(el))).normalized()


def frame(cam, pts):
    from bpy_extras.object_utils import world_to_camera_view
    sc = bpy.context.scene
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    center = (lo + hi) / 2
    radius = max(0.3, (hi - lo).length / 2)
    d = cam_dir()
    dist = radius / math.sin(cam.data.angle / 2)
    cam.data.shift_x = cam.data.shift_y = 0.0
    for _ in range(6):
        cam.location = center + d * dist
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        bpy.context.view_layer.update()
        xs, ys = [], []
        for p in pts:
            v = world_to_camera_view(sc, cam, p)
            xs.append(v.x)
            ys.append(v.y)
        x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
        span = max(x1 - x0, y1 - y0)
        cam.data.shift_x += ((x0 + x1) / 2 - 0.5)
        cam.data.shift_y += ((y0 + y1) / 2 - 0.5)
        k = span / FILL
        dist *= k
        if abs(k - 1) < 0.005 and abs((x0 + x1) / 2 - 0.5) < 0.003 and abs((y0 + y1) / 2 - 0.5) < 0.003:
            break
    return center, radius


# ---------------------------------------------------------------------------------------- render

def render(job):
    reset()
    setup_render()
    meshes = import_job(job)
    if not meshes:
        print(f"[skip] {job['key']}: no meshes")
        return
    pts = world_points(meshes)
    for e in job.get("effects", []):
        if e.get("kind") in ("Flames", "TorchFlame"):
            size = float(e.get("size", 0.6))
            # The particles start at most 0.6 x scale (Effects.Fire) or 0.9 x scale (Effects.Torch) wide.
            flame(obj_to_blender(e["position"]), size / 0.6 if e["kind"] == "Flames" else size / 0.9 * 0.6)
    cam = make_camera()
    center, radius = frame(cam, pts)

    # Sun from the camera's left-front, and the sky turned so its own sun agrees.
    d = cam_dir()
    cam_az = math.atan2(d.y, d.x)
    sun_az = cam_az + math.radians(SUN_AZIMUTH_FROM_CAM)
    el = math.radians(SUN_ELEVATION)
    sun_vec = Vector((math.cos(el) * math.cos(sun_az), math.cos(el) * math.sin(sun_az), math.sin(el)))
    sun(sun_vec)
    world(sun_az)

    # Invisible ground that only catches shadows and contact occlusion.
    # The model's origin is the ground; parts below it (bridge piles) stay visible through the catcher.
    ground_z = max(0.0, min(p.z for p in pts))
    bpy.ops.mesh.primitive_plane_add(size=radius * 40, location=(center.x, center.y, ground_z))
    g = bpy.context.active_object
    g.name = "ShadowCatcher"
    g.is_shadow_catcher = True
    gm = bpy.data.materials.new("Ground")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.32, 0.28, 0.22, 1)
    gm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    g.data.materials.append(gm)

    name = job["file"]
    os.makedirs(RAW, exist_ok=True)
    compositor(RAW, name)
    sc = bpy.context.scene
    sc.frame_set(1)
    sc.render.filepath = os.path.join(RAW, name + ".png")
    bpy.ops.render.render(write_still=True)
    # The file output node appends the frame number.
    produced = os.path.join(RAW, f"{name}_shadow_0001.png")
    if os.path.exists(produced):
        os.replace(produced, os.path.join(RAW, f"{name}_shadow.png"))
    print(f"[done] {job['key']} -> {name}.png")


def main():
    with open(JOBS) as f:
        jobs = json.load(f)
    for job in jobs:
        if ONLY and job["key"] not in ONLY and job["file"] not in ONLY:
            continue
        try:
            render(job)
        except Exception as e:  # keep going: one broken model must not stop the batch
            import traceback
            traceback.print_exc()
            print(f"[fail] {job['key']}: {e}")


main()
