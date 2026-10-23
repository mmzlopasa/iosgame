"""Shared Blender (4.2) helpers for the rendered UI art: scene reset, the house camera and lights,
the "painted" material, and chunky modelling primitives (bevelled boxes, lathes, extruded 2D
shapes, tubes, faceted rocks, Changa One text).

The look (after the casual survival-strategy UI kit the game follows): three-quarter view, warm
soft key from the upper left, a cool rim from behind on the right, soft AO, saturated colours with a
hand-painted light-to-shadow ramp baked into every material, glossy specular. The thick dark
outline and drop shadow are added afterwards from the alpha (post_icons.py), so they are identical
on every icon.

Imported by the tools/art/blender_*.py scripts (they put this folder on sys.path).
"""
import math
import os
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
TEX = os.path.join(ROOT, "unity", "Assets", "Art", "Textures")
MODELS = os.path.join(ROOT, "unity", "Assets", "Art", "Resources", "Models")
FONT = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Resources", "UI", "Fonts", "ChangaOne-Regular.ttf")


def lin(c):
    """'#RRGGBB' (sRGB) or an (r, g, b) tuple in 0..1 sRGB -> linear RGB."""
    if isinstance(c, str):
        c = c.lstrip("#")
        c = tuple(int(c[i:i + 2], 16) / 255 for i in (0, 2, 4))
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def mul(a, k):
    return tuple(x * k for x in a)


INK = lin("#2B1A0E")

# The painted look of the reference sheet (chunky hand-painted props: soft matte paint with visible
# brush texture, a teal rim light on the far and lower edges, small soft highlights instead of gloss).
# Every paint() material goes through these, so the whole icon set changes together.
STYLE = {
    "spec": 0.45,        # gloss multiplier (painted highlights, not lacquer)
    "rough": 0.22,       # added roughness
    "rim": 1.55,         # rim multiplier
    "rim_col": "#7DE3F0",  # teal rim, like the reference's metal and wood edges
    "bounce": 0.42,      # teal bounce light on the lower left edges
    "brush": 0.16,       # painted brush texture (value variation)
    "edge": 0.75,        # silhouette darkening multiplier
    "light": 0.6,        # how far lit tones go towards cream (the reference keeps colour in the light)
    "shade": 0.82,       # deeper, richer shadow tones
    "metal_tint": "#7F98A6",  # metals read as painted blue-grey iron, not chrome
}

# ---------------------------------------------------------------- scene / camera / lights
STATE = {"L": Vector((-0.5, -0.4, 0.75)).normalized(), "cam": None}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.images, bpy.data.lights,
                bpy.data.cameras, bpy.data.node_groups):
        for x in list(blk):
            blk.remove(x)


def setup_render(size=512, samples=48, transparent=True):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_adaptive_sampling = True
    sc.cycles.adaptive_threshold = 0.02
    sc.cycles.use_denoising = True
    sc.cycles.denoiser = "OPENIMAGEDENOISE"
    sc.cycles.max_bounces = 6
    sc.cycles.diffuse_bounces = 3
    sc.cycles.glossy_bounces = 3
    sc.cycles.transmission_bounces = 6
    sc.cycles.transparent_max_bounces = 8
    sc.cycles.blur_glossy = 1.0
    sc.cycles.caustics_reflective = False
    sc.cycles.caustics_refractive = False
    sc.cycles.filter_width = 1.2
    sc.render.film_transparent = transparent
    sc.render.resolution_x = size
    sc.render.resolution_y = size
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA" if transparent else "RGB"
    sc.render.image_settings.color_depth = "8"
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.view_settings.exposure = 0.0
    sc.view_settings.gamma = 1.0
    try:
        sc.render.threads_mode = "AUTO"
    except Exception:
        pass


def world(top=(0.62, 0.58, 0.55), bottom=(0.30, 0.24, 0.26), strength=1.0):
    """Soft sky dome: warm above, darker warm bounce below (ambient that keeps shadows coloured)."""
    w = bpy.data.worlds.new("W")
    bpy.context.scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = -1
    mr.inputs["From Max"].default_value = 1
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*lin(bottom), 1)
    ramp.color_ramp.elements[1].color = (*lin(top), 1)
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Strength"].default_value = strength
    out = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    nt.links.new(bg.outputs[0], out.inputs[0])
    return w


def cam_basis(az, el):
    """Camera forward/right/up for azimuth az (deg, 0 = looking along +Y from -Y) and elevation el."""
    a, e = math.radians(az), math.radians(el)
    back = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))  # towards camera
    fwd = -back
    right = fwd.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(fwd).normalized()
    return fwd, right, up


def begin(az=-18.0, el=18.0, key=1.0, rim=1.0, fill=1.0, world_strength=1.0, rim_color=(0.62, 0.82, 1.0),
          key_color=(1.0, 0.9, 0.76), size=512, samples=48, transparent=True):
    """Fresh scene with the house camera and three-point light. Returns nothing; build, then finish()."""
    reset()
    STATE["grad"] = []
    setup_render(size, samples, transparent)
    world(strength=0.0)
    fwd, right, up = cam_basis(az, el)
    cd = bpy.data.cameras.new("Cam")
    cd.type = "ORTHO"
    cd.clip_start = 0.01
    cd.clip_end = 200
    cam = bpy.data.objects.new("Cam", cd)
    bpy.context.scene.collection.objects.link(cam)
    cam.matrix_world = Matrix((
        (right.x, up.x, -fwd.x, -fwd.x * 30),
        (right.y, up.y, -fwd.y, -fwd.y * 30),
        (right.z, up.z, -fwd.z, -fwd.z * 30),
        (0, 0, 0, 1)))
    bpy.context.scene.camera = cam
    STATE["cam"] = cam
    STATE["basis"] = (fwd, right, up)
    # Painted-light direction (screen upper-left, a bit towards the viewer), used by the materials.
    L = (-right * 0.55 + up * 0.72 - fwd * 0.42).normalized()
    STATE["L"] = L

    def area(name, d, energy, color, size_):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy = energy
        ld.color = color
        ld.size = size_
        o = bpy.data.objects.new(name, ld)
        bpy.context.scene.collection.objects.link(o)
        o.location = d * 12
        o.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        return o

    # Diffuse is painted in the materials; these lights only make the glossy glints.
    spec_d = (-right * 0.5 + up * 0.85 - fwd * 0.18).normalized()
    area("Key", spec_d, 900 * key, key_color, 2.5)
    rim_d = (right * 0.85 + up * 0.4 + fwd * 0.5).normalized()
    area("Rim", rim_d, 500 * rim, rim_color, 2.0)


def ink_lines(thickness=4.0, crease=125.0, color=None):
    """Freestyle inner contours in the outline ink (silhouettes inside the shape, creases, part borders)."""
    sc = bpy.context.scene
    sc.render.use_freestyle = True
    sc.render.line_thickness_mode = "ABSOLUTE"
    sc.render.line_thickness = 1.0
    vl = bpy.context.view_layer
    fs = vl.freestyle_settings
    fs.crease_angle = math.radians(crease)
    fs.use_culling = False
    for ls in list(fs.linesets):
        fs.linesets.remove(ls)
    ls = fs.linesets.new("Ink")
    ls.select_by_visibility = True
    ls.visibility = "VISIBLE"
    ls.select_by_edge_types = True
    ls.select_silhouette = True
    ls.select_border = True
    ls.select_crease = crease > 0
    ls.select_contour = True
    ls.select_external_contour = False
    ls.select_material_boundary = False
    lst = ls.linestyle
    lst.color = color or INK
    lst.thickness = thickness
    lst.thickness_position = "INSIDE"
    lst.alpha = 0.92
    lst.caps = "ROUND"
    lst.use_chaining = True
    lst.chaining = "PLAIN"
    return ls


def mesh_points(objs=None):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in (objs or bpy.context.scene.objects):
        if o.type not in ("MESH", "CURVE", "FONT", "SURFACE") or o.hide_render:
            continue
        oe = o.evaluated_get(dg)
        try:
            me = oe.to_mesh()
        except RuntimeError:
            continue
        mw = oe.matrix_world
        pts.extend(mw @ v.co for v in me.vertices)
        oe.to_mesh_clear()
    return pts


def _index_pass(out, objs=None):
    """Writes <out>_id.png: each object (or ink group) as its own grey level, for the inner ink lines."""
    sc = bpy.context.scene
    vl = bpy.context.view_layer
    vl.use_pass_object_index = True
    groups = {}
    k = 0
    for o in sc.objects:
        if o.type in ("MESH", "CURVE", "FONT"):
            g = o.get("ink_group")
            if g is None:
                k += 1
                o.pass_index = k
            else:
                if g not in groups:
                    k += 1
                    groups[g] = k
                o.pass_index = groups[g]
    sc.use_nodes = True
    nt = sc.node_tree
    nt.nodes.clear()
    rl = nt.nodes.new("CompositorNodeRLayers")
    comp = nt.nodes.new("CompositorNodeComposite")
    nt.links.new(rl.outputs["Image"], comp.inputs["Image"])
    div = nt.nodes.new("CompositorNodeMath")
    div.operation = "DIVIDE"
    div.inputs[1].default_value = 40.0
    nt.links.new(rl.outputs["IndexOB"], div.inputs[0])
    fo = nt.nodes.new("CompositorNodeOutputFile")
    fo.base_path = os.path.dirname(out)
    fo.format.file_format = "PNG"
    fo.format.color_mode = "BW"
    fo.format.color_depth = "8"
    fo.file_slots[0].path = os.path.basename(out)[:-4] + "_id"
    nt.links.new(div.outputs[0], fo.inputs[0])
    sc.view_settings.view_transform = "Standard"


def finish(out, fill=0.84, shift=(0.0, 0.0), objs=None, square=True, ids=True, zmin=None):
    """Fits the orthographic camera to everything visible (fill = share of the frame) and renders."""
    cam = STATE["cam"]
    fwd, right, up = STATE["basis"]
    pts = mesh_points(objs)
    if zmin is not None:
        pts = [p for p in pts if p.z >= zmin]
    xs = [p.dot(right) for p in pts]
    ys = [p.dot(up) for p in pts]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    span = max(w, h) / fill
    cam.data.ortho_scale = span
    if zmin is not None:  # busts: the crop line sits on the bottom edge of the frame
        cy = min(ys) + span / 2
    centre = right * cx + up * cy
    cam.location = centre - fwd * 30
    for pd, gmr in STATE.get("grad", []):
        pd.inputs[1].default_value = (up.x, up.y, up.z)
        gmr.inputs["From Min"].default_value = min(ys)
        gmr.inputs["From Max"].default_value = max(ys)
    cam.data.shift_x = shift[0]
    cam.data.shift_y = shift[1]
    sc = bpy.context.scene
    sc.render.filepath = out
    if ids:
        _index_pass(out, objs)
    bpy.ops.render.render(write_still=True)
    if ids:
        import glob as _g
        for f in _g.glob(out[:-4] + "_id*.png"):
            os.replace(f, out[:-4] + "_id.png")


# ---------------------------------------------------------------- materials
def _n(nt, kind, **inputs):
    n = nt.nodes.new(kind)
    for k, v in inputs.items():
        n.inputs[k].default_value = v
    return n


def _pattern(nt, pattern, col):
    kind, scale, pcol, amount = pattern[:4]
    sharp = pattern[4] if len(pattern) > 4 else 0.5
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    if kind == "rings":
        t = nt.nodes.new("ShaderNodeTexWave")
        t.wave_type = "RINGS"
        t.rings_direction = "Z"
        t.inputs["Scale"].default_value = scale
        t.inputs["Distortion"].default_value = 1.5
        t.inputs["Detail Scale"].default_value = 1.0
        src = t.outputs["Fac"]
    elif kind == "bands":
        t = nt.nodes.new("ShaderNodeTexWave")
        t.wave_type = "BANDS"
        t.bands_direction = "Z"
        t.inputs["Scale"].default_value = scale
        t.inputs["Distortion"].default_value = 4
        src = t.outputs["Fac"]
    else:
        t = nt.nodes.new("ShaderNodeTexNoise")
        t.inputs["Scale"].default_value = scale
        t.inputs["Detail"].default_value = 3
        src = t.outputs["Fac"]
        if kind == "streaks":
            mp.inputs["Scale"].default_value = (1.0, 1.0, 0.1)
    nt.links.new(mp.outputs["Vector"], t.inputs["Vector"])
    pr = nt.nodes.new("ShaderNodeValToRGB")
    pr.color_ramp.elements[0].position = 0.5 - sharp * 0.25
    pr.color_ramp.elements[1].position = 0.5 + sharp * 0.25
    nt.links.new(src, pr.inputs["Fac"])
    mx = nt.nodes.new("ShaderNodeMix")
    mx.data_type = "RGBA"
    mx.blend_type = "MIX"
    mx.inputs["B"].default_value = (*lin(pcol), 1)
    fac = nt.nodes.new("ShaderNodeMath")
    fac.operation = "MULTIPLY"
    fac.inputs[1].default_value = amount
    nt.links.new(pr.outputs["Color"], fac.inputs[0])
    nt.links.new(fac.outputs[0], mx.inputs["Factor"])
    nt.links.new(col, mx.inputs["A"])
    return mx.outputs["Result"]


def _dot_const(nt, vec_out, v):
    d = nt.nodes.new("ShaderNodeVectorMath")
    d.operation = "DOT_PRODUCT"
    d.inputs[1].default_value = (v.x, v.y, v.z)
    nt.links.new(vec_out, d.inputs[0])
    return d.outputs["Value"]


def paint(name, color, shade=0.5, light=0.28, hue_dark="#3E1A66", light_col="#FFF3C4", spec=0.35, rough=0.28,
          metal=False, rim=0.55, rim_col="#D6ECFF", ao=0.55, grad=(0.74, 1.10), edge=0.22, bands=0.55,
          pattern=None, tex=None, tex_scale=1.0, tex_mix=0.6, emit=None, emit_strength=0.0, bump=None,
          alpha=1.0, concave=False, **_ignored):
    """The house toon material. Colour = a painted light ramp on N.L (shadow tone hue-shifted towards
    purple, light tone towards warm cream; bands sets how stepped it is) x a screen-space top-to-bottom
    gradient x soft AO, plus a cool rim on the far side, emitted as is (so it is exactly the painted
    colour), with a glossy layer on top for real highlights from the key light."""
    base = lin(color) if isinstance(color, str) else color
    spec *= STYLE["spec"]
    light *= STYLE["light"]
    shade *= STYLE["shade"]
    if metal and base[2] >= base[0] * 0.9:  # silver/steel (not gold or copper): painted blue-grey iron
        base = mix(base, lin(STYLE["metal_tint"]), 0.6)
        light_col = "#DDF6FF"
    rough = min(1.0, rough + STYLE["rough"])
    edge *= STYLE["edge"]
    if rim > 0:
        rim = rim * STYLE["rim"]
        rim_col = STYLE["rim_col"]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    normal = geo.outputs["Normal"]
    bump_node = None
    if bump:
        kind, scale, strength = bump
        tc = nt.nodes.new("ShaderNodeTexCoord")
        if kind == "voronoi":
            t = nt.nodes.new("ShaderNodeTexVoronoi")
            t.inputs["Scale"].default_value = scale
            src = t.outputs["Distance"]
        else:
            t = nt.nodes.new("ShaderNodeTexNoise")
            t.inputs["Scale"].default_value = scale
            t.inputs["Detail"].default_value = 4
            src = t.outputs["Fac"]
        nt.links.new(tc.outputs["Object"], t.inputs["Vector"])
        bump_node = nt.nodes.new("ShaderNodeBump")
        bump_node.inputs["Strength"].default_value = strength
        nt.links.new(src, bump_node.inputs["Height"])
        normal = bump_node.outputs["Normal"]
    Lv = STATE["L"]
    if concave:  # light from the other side: reads as a dent (craters, keyholes)
        fwd_, right_, up_ = STATE["basis"]
        Lv = (right_ * 0.55 - up_ * 0.72 - fwd_ * 0.42).normalized()
    ndl = _dot_const(nt, normal, Lv)
    mr = _n(nt, "ShaderNodeMapRange", **{"From Min": -1.0, "From Max": 1.0})
    nt.links.new(ndl, mr.inputs["Value"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.interpolation = "EASE"
    dark = mix(mul(base, shade), mul(lin(hue_dark), shade * 1.2), 0.32)
    lite = mix(base, lin(light_col), light)
    w = (1 - bands) * 0.12 + 0.02
    stops = [(0.0, dark), (0.40 - w, dark), (0.40 + w, base), (0.70 - w, base), (0.70 + w, lite), (1.0, lite)]
    cr.elements[0].position, cr.elements[0].color = stops[0][0], (*stops[0][1], 1)
    cr.elements[1].position, cr.elements[1].color = stops[-1][0], (*stops[-1][1], 1)
    for pos, c in stops[1:-1]:
        e = cr.elements.new(pos)
        e.color = (*c, 1)
    nt.links.new(mr.outputs["Result"], ramp.inputs["Fac"])
    col = ramp.outputs["Color"]
    if pattern:
        col = _pattern(nt, pattern, col)
    if tex:
        img = bpy.data.images.load(tex if os.path.isabs(tex) else os.path.join(TEX, tex), check_existing=True)
        ti = nt.nodes.new("ShaderNodeTexImage")
        ti.image = img
        ti.interpolation = "Cubic"
        tc = nt.nodes.new("ShaderNodeTexCoord")
        mp = nt.nodes.new("ShaderNodeMapping")
        mp.inputs["Scale"].default_value = (tex_scale, tex_scale, tex_scale)
        nt.links.new(tc.outputs["UV"], mp.inputs["Vector"])
        nt.links.new(mp.outputs["Vector"], ti.inputs["Vector"])
        mx = nt.nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.blend_type = "OVERLAY"
        mx.inputs["Factor"].default_value = tex_mix
        nt.links.new(col, mx.inputs["A"])
        nt.links.new(ti.outputs["Color"], mx.inputs["B"])
        col = mx.outputs["Result"]
    if STYLE["brush"] > 0:  # painted brush texture: stretched noise strokes lighten/darken the paint
        btc = nt.nodes.new("ShaderNodeTexCoord")
        bmp = nt.nodes.new("ShaderNodeMapping")
        bmp.inputs["Scale"].default_value = (1.0, 1.0, 3.2)
        bmp.inputs["Rotation"].default_value = (0.4, 0.3, 0.7)
        nt.links.new(btc.outputs["Object"], bmp.inputs["Vector"])
        bn = nt.nodes.new("ShaderNodeTexNoise")
        bn.inputs["Scale"].default_value = 3.5
        bn.inputs["Detail"].default_value = 6
        bn.inputs["Roughness"].default_value = 0.62
        nt.links.new(bmp.outputs["Vector"], bn.inputs["Vector"])
        bmr = nt.nodes.new("ShaderNodeMapRange")
        bmr.inputs["From Min"].default_value = 0.3
        bmr.inputs["From Max"].default_value = 0.7
        bmr.inputs["To Min"].default_value = 1 - STYLE["brush"]
        bmr.inputs["To Max"].default_value = 1 + STYLE["brush"]
        nt.links.new(bn.outputs["Fac"], bmr.inputs["Value"])
        bsc = nt.nodes.new("ShaderNodeVectorMath")
        bsc.operation = "SCALE"
        nt.links.new(col, bsc.inputs[0])
        nt.links.new(bmr.outputs["Result"], bsc.inputs["Scale"])
        col = bsc.outputs["Vector"]
    # screen-space vertical gradient (finish() sets the range once the frame is known)
    pos_dot = nt.nodes.new("ShaderNodeVectorMath")
    pos_dot.operation = "DOT_PRODUCT"
    nt.links.new(geo.outputs["Position"], pos_dot.inputs[0])
    gmr = nt.nodes.new("ShaderNodeMapRange")
    gmr.inputs["To Min"].default_value = grad[0]
    gmr.inputs["To Max"].default_value = grad[1]
    nt.links.new(pos_dot.outputs["Value"], gmr.inputs["Value"])
    STATE.setdefault("grad", []).append((pos_dot, gmr))
    gs = nt.nodes.new("ShaderNodeVectorMath")
    gs.operation = "SCALE"
    nt.links.new(col, gs.inputs[0])
    nt.links.new(gmr.outputs["Result"], gs.inputs["Scale"])
    col = gs.outputs["Vector"]
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.5
    if bump_node:
        nt.links.new(normal, lw.inputs["Normal"])
    if edge > 0:  # darker silhouette edges (rounded painted form)
        pw = nt.nodes.new("ShaderNodeMath")
        pw.operation = "POWER"
        pw.inputs[1].default_value = 2.5
        nt.links.new(lw.outputs["Facing"], pw.inputs[0])
        ef = nt.nodes.new("ShaderNodeMath")
        ef.operation = "MULTIPLY"
        ef.inputs[1].default_value = edge
        nt.links.new(pw.outputs[0], ef.inputs[0])
        mx = nt.nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.inputs["B"].default_value = (*dark, 1)
        nt.links.new(ef.outputs[0], mx.inputs["Factor"])
        nt.links.new(col, mx.inputs["A"])
        col = mx.outputs["Result"]
    if ao > 0:
        aon = nt.nodes.new("ShaderNodeAmbientOcclusion")
        aon.inputs["Distance"].default_value = STATE.get("ao_dist", 0.35)
        aon.samples = 8
        aof = nt.nodes.new("ShaderNodeMapRange")
        aof.inputs["To Min"].default_value = ao
        aof.inputs["To Max"].default_value = 0.0
        nt.links.new(aon.outputs["AO"], aof.inputs["Value"])
        mx = nt.nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.blend_type = "MULTIPLY"
        mx.inputs["B"].default_value = (*mul(mix(base, lin(hue_dark), 0.6), 0.55), 1)
        nt.links.new(aof.outputs["Result"], mx.inputs["Factor"])
        nt.links.new(col, mx.inputs["A"])
        col = mx.outputs["Result"]
    if rim > 0:  # cool rim on the far (right) side
        fwd, right, up = STATE["basis"]
        R = (right * 0.9 + up * 0.25 + fwd * 0.35).normalized()
        rd = _dot_const(nt, normal, R)
        rclamp = nt.nodes.new("ShaderNodeMapRange")
        rclamp.inputs["From Min"].default_value = 0.15
        rclamp.inputs["From Max"].default_value = 0.75
        nt.links.new(rd, rclamp.inputs["Value"])
        fr = nt.nodes.new("ShaderNodeMath")
        fr.operation = "POWER"
        fr.inputs[1].default_value = 2.6
        nt.links.new(lw.outputs["Facing"], fr.inputs[0])
        rm = nt.nodes.new("ShaderNodeMath")
        rm.operation = "MULTIPLY"
        nt.links.new(fr.outputs[0], rm.inputs[0])
        nt.links.new(rclamp.outputs["Result"], rm.inputs[1])
        rs = nt.nodes.new("ShaderNodeMath")
        rs.operation = "MULTIPLY"
        rs.inputs[1].default_value = rim
        nt.links.new(rm.outputs[0], rs.inputs[0])
        mx = nt.nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.blend_type = "SCREEN"
        mx.inputs["B"].default_value = (*mix(lin(rim_col), lite, 0.25), 1)
        nt.links.new(rs.outputs[0], mx.inputs["Factor"])
        nt.links.new(col, mx.inputs["A"])
        col = mx.outputs["Result"]
    if rim > 0 and STYLE["bounce"] > 0:  # teal bounce on the lower edges (the reference's under-glow)
        fwd, right, up = STATE["basis"]
        B = (-right * 0.35 - up * 0.85 + fwd * 0.25).normalized()
        bd = _dot_const(nt, normal, B)
        bcl = nt.nodes.new("ShaderNodeMapRange")
        bcl.inputs["From Min"].default_value = 0.1
        bcl.inputs["From Max"].default_value = 0.8
        nt.links.new(bd, bcl.inputs["Value"])
        bf = nt.nodes.new("ShaderNodeMath")
        bf.operation = "POWER"
        bf.inputs[1].default_value = 2.2
        nt.links.new(lw.outputs["Facing"], bf.inputs[0])
        bm_ = nt.nodes.new("ShaderNodeMath")
        bm_.operation = "MULTIPLY"
        nt.links.new(bf.outputs[0], bm_.inputs[0])
        nt.links.new(bcl.outputs["Result"], bm_.inputs[1])
        bs_ = nt.nodes.new("ShaderNodeMath")
        bs_.operation = "MULTIPLY"
        bs_.inputs[1].default_value = STYLE["bounce"] * min(1.0, rim)
        nt.links.new(bm_.outputs[0], bs_.inputs[0])
        mx = nt.nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.blend_type = "SCREEN"
        mx.inputs["B"].default_value = (*lin(STYLE["rim_col"]), 1)
        nt.links.new(bs_.outputs[0], mx.inputs["Factor"])
        nt.links.new(col, mx.inputs["A"])
        col = mx.outputs["Result"]
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(col, em.inputs["Color"])
    em.inputs["Strength"].default_value = 1.0
    shader = em.outputs[0]
    if emit is not None and emit_strength > 0:
        e2 = nt.nodes.new("ShaderNodeEmission")
        e2.inputs["Color"].default_value = (*(lin(emit) if isinstance(emit, str) else emit), 1)
        e2.inputs["Strength"].default_value = emit_strength
        add = nt.nodes.new("ShaderNodeAddShader")
        nt.links.new(shader, add.inputs[0])
        nt.links.new(e2.outputs[0], add.inputs[1])
        shader = add.outputs[0]
    if spec > 0:
        gl = nt.nodes.new("ShaderNodeBsdfGlossy")
        gl.distribution = "GGX"
        gl.inputs["Roughness"].default_value = rough
        gcol = mix((1, 1, 1), lite, 0.6) if metal else (1, 1, 1)
        gl.inputs["Color"].default_value = (*mul(gcol, spec), 1)
        if bump_node:
            nt.links.new(normal, gl.inputs["Normal"])
        # Fresnel-free: a clear highlight on every material, weaker on the shadow side
        add = nt.nodes.new("ShaderNodeAddShader")
        nt.links.new(shader, add.inputs[0])
        nt.links.new(gl.outputs[0], add.inputs[1])
        shader = add.outputs[0]
    if alpha < 1:
        tr = nt.nodes.new("ShaderNodeBsdfTransparent")
        ms = nt.nodes.new("ShaderNodeMixShader")
        ms.inputs["Fac"].default_value = alpha
        nt.links.new(tr.outputs[0], ms.inputs[1])
        nt.links.new(shader, ms.inputs[2])
        shader = ms.outputs[0]
        m.blend_method = "BLEND"
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(shader, out.inputs[0])
    return m


def glow(name, color, strength=4.0, core=None):
    """Pure emission (flame, lamp light). With core, a facing-ratio blend from core (centre) to colour (edge)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Strength"].default_value = strength
    if core:
        lw = nt.nodes.new("ShaderNodeLayerWeight")
        lw.inputs["Blend"].default_value = 0.45
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].color = (*lin(core), 1)
        ramp.color_ramp.elements[1].color = (*lin(color), 1)
        ramp.color_ramp.elements[0].position = 0.25
        ramp.color_ramp.elements[1].position = 0.85
        nt.links.new(lw.outputs["Facing"], ramp.inputs["Fac"])
        nt.links.new(ramp.outputs["Color"], e.inputs["Color"])
    else:
        e.inputs["Color"].default_value = (*lin(color), 1)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs[0])
    return m


def flame_mat(name="Flame", outer="#FF5A1A", mid="#FFB020", core="#FFF4B0", strength=3.2):
    """Gradient flame: hot core, orange edge, by height (object Z) and facing."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.5
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].color = (*lin(core), 1)
    cr.elements[0].position = 0.15
    cr.elements[1].color = (*lin(outer), 1)
    cr.elements[1].position = 0.8
    e2 = cr.elements.new(0.45)
    e2.color = (*lin(mid), 1)
    nt.links.new(lw.outputs["Facing"], ramp.inputs["Fac"])
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Strength"].default_value = strength
    nt.links.new(ramp.outputs["Color"], e.inputs["Color"])
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs[0])
    return m


# ---------------------------------------------------------------- objects
def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def assign(o, m):
    if m is None:
        return o
    o.data.materials.clear()
    o.data.materials.append(m)
    return o


def smooth(o, angle=None):
    if o.type != "MESH":
        return o
    for p in o.data.polygons:
        p.use_smooth = True
    if angle is not None:
        # mark edges sharper than angle (base mesh; the bevel modifier then rounds them)
        bm = bmesh.new()
        bm.from_mesh(o.data)
        for e in bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(angle):
                e.smooth = False
        bm.to_mesh(o.data)
        bm.free()
    return o


def mod_bevel(o, width, segs=3, limit="ANGLE", angle=40):
    b = o.modifiers.new("Bevel", "BEVEL")
    b.width = width
    b.segments = segs
    b.limit_method = limit
    if limit == "ANGLE":
        b.angle_limit = math.radians(angle)
    b.harden_normals = False
    return b


def mod_subsurf(o, levels=2):
    s = o.modifiers.new("Subsurf", "SUBSURF")
    s.levels = levels
    s.render_levels = levels
    return s


def place(o, loc=(0, 0, 0), rot=(0, 0, 0), scale=None):
    o.location = loc
    o.rotation_euler = tuple(math.radians(r) for r in rot)
    if scale is not None:
        o.scale = scale if isinstance(scale, (tuple, list)) else (scale, scale, scale)
    return o


def from_bmesh(name, bm, m=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = link(bpy.data.objects.new(name, me))
    return assign(o, m)


def box(size, loc=(0, 0, 0), m=None, rot=(0, 0, 0), bevel=0.08, segs=3, sub=0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    o = from_bmesh("Box", bm, m)
    if bevel:
        mod_bevel(o, bevel, segs, "NONE")
    if sub:
        mod_subsurf(o, sub)
    smooth(o)
    return place(o, loc, rot)


def cylinder(r, h, loc=(0, 0, 0), m=None, rot=(0, 0, 0), verts=32, bevel=0.05, segs=3, r2=None, sub=0):
    """Cylinder (or frustum with r2) centred on loc."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r,
                          radius2=r if r2 is None else r2, depth=h)
    o = from_bmesh("Cyl", bm, m)
    if bevel:
        mod_bevel(o, bevel, segs, "ANGLE", 30)
    if sub:
        mod_subsurf(o, sub)
    smooth(o)
    return place(o, loc, rot)


def sphere(r, loc=(0, 0, 0), m=None, scale=(1, 1, 1), rot=(0, 0, 0), segs=32, rings=16):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=r)
    o = from_bmesh("Sphere", bm, m)
    smooth(o)
    return place(o, loc, rot, scale)


def lathe(profile, loc=(0, 0, 0), m=None, rot=(0, 0, 0), verts=40, sub=1, close_top=True, close_bottom=True):
    """Solid of revolution from [(radius, z), ...] bottom to top."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        ring = []
        for i in range(verts):
            a = 2 * math.pi * i / verts
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for a_, b_ in zip(rings, rings[1:]):
        for i in range(verts):
            j = (i + 1) % verts
            bm.faces.new((a_[i], a_[j], b_[j], b_[i]))
    if close_bottom and profile[0][0] > 1e-4:
        bm.faces.new(list(reversed(rings[0])))
    if close_top and profile[-1][0] > 1e-4:
        bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bmesh("Lathe", bm, m)
    if sub:
        mod_subsurf(o, sub)
    smooth(o)
    return place(o, loc, rot)


def shape2d(loops, depth=0.2, bevel=0.06, m=None, loc=(0, 0, 0), rot=(90, 0, 0), smooth_curve=False, res=12,
            bevel_res=4, offset=0.0):
    """Extruded, bevelled 2D shape in the XY plane (then rotated upright, facing -Y by default).
    loops: list of point lists [(x, y), ...]; holes are further loops (curve fill handles even-odd).
    smooth_curve=True uses auto-handle Béziers through the points."""
    cd = bpy.data.curves.new("Shape", "CURVE")
    cd.dimensions = "2D"
    cd.fill_mode = "BOTH"
    cd.extrude = depth / 2
    cd.bevel_depth = bevel
    cd.bevel_resolution = bevel_res
    cd.resolution_u = res
    cd.offset = offset
    for pts in loops:
        if smooth_curve:
            sp = cd.splines.new("BEZIER")
            sp.bezier_points.add(len(pts) - 1)
            for bp, (x, y) in zip(sp.bezier_points, pts):
                bp.co = (x, y, 0)
                bp.handle_left_type = bp.handle_right_type = "AUTO"
        else:
            sp = cd.splines.new("POLY")
            sp.points.add(len(pts) - 1)
            for p, (x, y) in zip(sp.points, pts):
                p.co = (x, y, 0, 1)
        sp.use_cyclic_u = True
    o = link(bpy.data.objects.new("Shape", cd))
    assign(o, m)
    return place(o, loc, rot)


def to_mesh(o):
    """Converts a curve/text object to a mesh (so modifiers can be added)."""
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target="MESH")
    return bpy.context.view_layer.objects.active


def tube(points, radius, m=None, loc=(0, 0, 0), rot=(0, 0, 0), res=8, smooth_path=True, caps=True, radii=None,
         cyclic=False):
    """A round tube along a 3D path (rope, handle, shackle, stem)."""
    cd = bpy.data.curves.new("Tube", "CURVE")
    cd.dimensions = "3D"
    cd.bevel_depth = radius
    cd.bevel_resolution = 4
    cd.resolution_u = res
    cd.use_fill_caps = caps
    if smooth_path:
        sp = cd.splines.new("BEZIER")
        sp.bezier_points.add(len(points) - 1)
        for i, (bp, p) in enumerate(zip(sp.bezier_points, points)):
            bp.co = p
            bp.handle_left_type = bp.handle_right_type = "AUTO"
            if radii:
                bp.radius = radii[i]
    else:
        sp = cd.splines.new("POLY")
        sp.points.add(len(points) - 1)
        for i, (p, c) in enumerate(zip(sp.points, points)):
            p.co = (*c, 1)
            if radii:
                p.radius = radii[i]
    sp.use_cyclic_u = cyclic
    o = link(bpy.data.objects.new("Tube", cd))
    assign(o, m)
    return place(o, loc, rot)


def rock(r, loc=(0, 0, 0), m=None, seed=1, scale=(1, 1, 0.75), rot=(0, 0, 0), jitter=0.22, subdiv=1, bevel=0.04):
    """Faceted chunky rock: jittered icosphere, flat facets with soft bevelled edges."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    for v in bm.verts:
        v.co *= 1 + rnd.uniform(-jitter, jitter)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(14), verts=bm.verts, edges=bm.edges)
    o = from_bmesh("Rock", bm, m)
    if bevel:
        mod_bevel(o, bevel * r, 3, "ANGLE", 20)
    for p in o.data.polygons:
        p.use_smooth = True
    o.data.update()
    smooth(o, 32)
    return place(o, loc, rot, scale)


def hull_rock(r, loc=(0, 0, 0), m=None, seed=1, scale=(1, 1, 0.8), rot=(0, 0, 0), n=18, bevel=0.06, flat_bottom=0.55):
    """Chunky faceted rock: convex hull of random points on an ellipsoid (big flat facets, like the kit)."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for _ in range(n):
        while True:
            v = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1)))
            if 0.3 < v.length <= 1:
                break
        v.normalize()
        v *= r * rnd.uniform(0.86, 1.0)
        v.z = max(v.z, -r * flat_bottom)
        bm.verts.new(v)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(3), verts=bm.verts, edges=bm.edges)
    o = from_bmesh("Rock", bm, m)
    if bevel:
        mod_bevel(o, bevel * r, 4, "ANGLE", 15)
    smooth(o, 28)
    return place(o, loc, rot, scale)


def text3d(s, size=1.0, depth=0.2, bevel=0.04, m=None, loc=(0, 0, 0), rot=(90, 0, 0), align="CENTER"):
    cd = bpy.data.curves.new("Txt", "FONT")
    cd.body = s
    cd.font = bpy.data.fonts.load(FONT, check_existing=True)
    cd.size = size
    cd.extrude = depth / 2
    cd.bevel_depth = bevel
    cd.bevel_resolution = 4
    cd.align_x = align
    cd.align_y = "CENTER"
    o = link(bpy.data.objects.new("Txt", cd))
    assign(o, m)
    return place(o, loc, rot)


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def circle_pts(r, n=48, cx=0.0, cy=0.0, a0=0.0, a1=360.0, closed=True):
    k = n if closed else n + 1
    span = a1 - a0
    return [(cx + r * math.cos(math.radians(a0 + span * i / n)), cy + r * math.sin(math.radians(a0 + span * i / n)))
            for i in range(k)]


def star_pts(r1, r2, n=5, rot=90.0, cx=0.0, cy=0.0):
    pts = []
    for i in range(n * 2):
        r = r1 if i % 2 == 0 else r2
        a = math.radians(rot + i * 180 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def rounded_poly(pts, radius, steps=6):
    """Polygon with every corner replaced by a circular arc of the given radius (per-corner list allowed)."""
    n = len(pts)
    out = []
    for i in range(n):
        p0, p1, p2 = Vector(pts[i - 1]), Vector(pts[i]), Vector(pts[(i + 1) % n])
        r = radius[i] if isinstance(radius, (list, tuple)) else radius
        if r <= 0:
            out.append(tuple(p1))
            continue
        d1, d2 = (p0 - p1).normalized(), (p2 - p1).normalized()
        ang = math.acos(max(-1.0, min(1.0, d1.dot(d2))))
        t = r / math.tan(ang / 2)
        t = min(t, (p0 - p1).length * 0.49, (p2 - p1).length * 0.49)
        a, b = p1 + d1 * t, p1 + d2 * t
        # quadratic Bezier through the corner approximates the arc well enough at icon scale
        for k in range(steps + 1):
            u = k / steps
            q = a * (1 - u) ** 2 + p1 * 2 * u * (1 - u) + b * u * u
            out.append((q.x, q.y))
    return out


def parent(objs, loc=(0, 0, 0), rot=(0, 0, 0), scale=1.0):
    """Groups objects under an empty so a built assembly can be moved/rotated as one."""
    e = bpy.data.objects.new("Group", None)
    link(e)
    for o in objs:
        o.parent = e
    place(e, loc, rot, scale)
    bpy.context.view_layer.update()
    return e


def tag(objs, group):
    """Objects in one ink group get no inner ink lines between each other."""
    for o in objs:
        o["ink_group"] = group
    return objs
