"""Shared Blender (4.2) helpers for the loading-screen key art (blender_loading_art.py).

Everything is built from the game's own pieces so the pictures show what the player gets:
- the Ultimate Nature packs (private repo mmzlopasa/founders-assets, checked out next to this repo or
  pointed to by FOUNDERS_ASSETS) with the colours the game's NatureKitSetup gives them;
- the game's models listed in unity/Assets/Art/Resources/art_manifest.json (the detailed bw_ buildings
  with their painted atlas, the medieval houses, the Kenney props, the animals);
- the villagers from people.json with the game's painted proportions (tools/assets/person_style.py).
"""
import glob
import json
import math
import os
import random
import re
import sys

import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
ASSETS = os.path.join(ROOT, "unity", "Assets")
MANIFEST = json.load(open(os.path.join(ASSETS, "Art", "Resources", "art_manifest.json")))
PACKS = os.environ.get("FOUNDERS_ASSETS", os.path.join(ROOT, "..", "founders-assets"))
UNP = os.path.join(PACKS, "Nature", "Innerverse Games", "(UNP) Ultimate Nature PRO")
sys.path.insert(0, os.path.join(ROOT, "tools", "assets"))


def lin(c):
    c = np.asarray(c, dtype=np.float64)
    return tuple(np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4))


# MARK: - Scene


def reset(width, height, samples):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = 0.02
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.cycles.transparent_max_bounces = 16
    scene.cycles.diffuse_bounces = 3
    scene.cycles.glossy_bounces = 2
    scene.cycles.volume_bounces = 1
    scene.cycles.caustics_reflective = scene.cycles.caustics_refractive = False
    scene.cycles.blur_glossy = 1.0
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_depth = "16"
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.render.threads_mode = "AUTO"
    return scene


def sky(sun_elevation, sun_azimuth, strength=1.0, sun_strength=4.0, warm=(1.0, 0.86, 0.66), air=1.2, dust=2.0,
        sun_angle=2.0):
    """Nishita sky for the ambient light and a separate warm sun lamp (crisper shadows than the sky's own disc)."""
    scene = bpy.context.scene
    world = bpy.data.worlds.new("sky")
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes["Background"]
    st = nt.nodes.new("ShaderNodeTexSky")
    st.sky_type = "NISHITA"
    st.sun_disc = False
    st.sun_elevation = math.radians(sun_elevation)
    st.sun_rotation = math.radians(sun_azimuth)
    st.air_density = air
    st.dust_density = dust
    st.altitude = 400
    nt.links.new(st.outputs[0], bg.inputs[0])
    bg.inputs[1].default_value = strength
    scene.world = world
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = sun_strength
    sun.data.color = warm
    sun.data.angle = math.radians(sun_angle)
    # Blender sun points down -Z; elevation above the horizon, azimuth clockwise from +Y like the sky texture.
    sun.rotation_euler = Euler((math.radians(90 - sun_elevation), 0, math.radians(180 + sun_azimuth)), "XYZ")
    scene.collection.objects.link(sun)
    return sun


def camera(location, target, lens=35, dof=None, fstop=4.0, shift=(0, 0)):
    scene = bpy.context.scene
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.location = Vector(location)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    cam.data.clip_end = 3000
    cam.data.shift_x, cam.data.shift_y = shift
    if dof is not None:
        cam.data.dof.use_dof = True
        cam.data.dof.focus_distance = dof
        cam.data.dof.aperture_fstop = fstop
    return cam


def mist(start=20.0, depth=600.0, falloff="QUADRATIC"):
    """Aerial haze is added after the render from the mist pass (render() saves it next to the picture):
    a world volume would cost much more and dim the sun."""
    w = bpy.context.scene.world
    w.mist_settings.start = start
    w.mist_settings.depth = depth
    w.mist_settings.falloff = falloff
    bpy.context.view_layer.use_pass_mist = True


# MARK: - Materials


def image(path, non_color=False, closest=False):
    im = bpy.data.images.load(path, check_existing=True)
    if non_color:
        im.colorspace_settings.name = "Non-Color"
    im["closest"] = closest
    return im


def principled(name, color=(0.5, 0.5, 0.5), roughness=0.85):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1) if len(color) == 3 else color
    b.inputs["Roughness"].default_value = roughness
    b.inputs["Specular IOR Level"].default_value = 0.25
    return m


def textured(name, albedo, normal=None, tint=None, roughness=0.88, alpha_clip=False, closest=False,
             crown=None, normal_strength=0.5):
    """A matte painted material: albedo (x tint), optional normal map, leaf cut-out, and for tree crowns a
    gradient from the dark to the light colour up the crown (what the pack's vegetation shader does)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = roughness
    b.inputs["Specular IOR Level"].default_value = 0.2
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image(albedo)
    if closest:
        tex.interpolation = "Closest"
    color = tex.outputs["Color"]
    if crown is not None:
        dark, light = crown
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        tc = nt.nodes.new("ShaderNodeTexCoord")
        nt.links.new(tc.outputs["Object"], sep.inputs[0])
        rng = nt.nodes.new("ShaderNodeMapRange")
        rng.inputs["From Min"].default_value = crown_low(name)
        rng.inputs["From Max"].default_value = crown_high(name)
        nt.links.new(sep.outputs["Z"], rng.inputs["Value"])
        # Facing the sky is lighter too (sunlit tops of the clumps).
        dot = nt.nodes.new("ShaderNodeVectorMath")
        dot.operation = "DOT_PRODUCT"
        dot.inputs[1].default_value = (0, 0, 1)
        nt.links.new(geo.outputs["Normal"], dot.inputs[0])
        add = nt.nodes.new("ShaderNodeMath")
        add.operation = "MULTIPLY_ADD"
        add.inputs[1].default_value = 0.25
        add.use_clamp = True
        nt.links.new(dot.outputs["Value"], add.inputs[0])
        nt.links.new(rng.outputs["Result"], add.inputs[2])
        ramp = nt.nodes.new("ShaderNodeMix")
        ramp.data_type = "RGBA"
        nt.links.new(add.outputs[0], ramp.inputs[0])
        ramp.inputs[6].default_value = (*dark, 1)
        ramp.inputs[7].default_value = (*light, 1)
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs[0].default_value = 1
        nt.links.new(color, mul.inputs[6])
        nt.links.new(ramp.outputs[2], mul.inputs[7])
        color = mul.outputs[2]
        # Each tree a little different: hue towards yellow or blue-green, lighter or darker.
        info = nt.nodes.new("ShaderNodeObjectInfo")
        hue = nt.nodes.new("ShaderNodeMapRange")
        hue.inputs["To Min"].default_value = 0.465
        hue.inputs["To Max"].default_value = 0.53
        nt.links.new(info.outputs["Random"], hue.inputs["Value"])
        val = nt.nodes.new("ShaderNodeMapRange")
        val.inputs["To Min"].default_value = 0.82
        val.inputs["To Max"].default_value = 1.18
        sh = nt.nodes.new("ShaderNodeMath")
        sh.operation = "FRACT"
        mulr = nt.nodes.new("ShaderNodeMath")
        mulr.operation = "MULTIPLY"
        mulr.inputs[1].default_value = 7.31
        nt.links.new(info.outputs["Random"], mulr.inputs[0])
        nt.links.new(mulr.outputs[0], sh.inputs[0])
        nt.links.new(sh.outputs[0], val.inputs["Value"])
        hsv = nt.nodes.new("ShaderNodeHueSaturation")
        nt.links.new(hue.outputs["Result"], hsv.inputs["Hue"])
        hsv.inputs["Saturation"].default_value = 0.92
        nt.links.new(val.outputs["Result"], hsv.inputs["Value"])
        nt.links.new(color, hsv.inputs["Color"])
        color = hsv.outputs["Color"]
        b.inputs["Subsurface Weight"].default_value = 0.0
        # Light through the leaves.
        b.inputs["Transmission Weight"].default_value = 0.0
    elif tint is not None:
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs[0].default_value = 1
        nt.links.new(color, mul.inputs[6])
        mul.inputs[7].default_value = (*tint, 1)
        color = mul.outputs[2]
    nt.links.new(color, b.inputs["Base Color"])
    if alpha_clip:
        # Hard cut-out like the game (alpha test), not soft blending.
        gt = nt.nodes.new("ShaderNodeMath")
        gt.operation = "GREATER_THAN"
        gt.inputs[1].default_value = 0.45
        nt.links.new(tex.outputs["Alpha"], gt.inputs[0])
        nt.links.new(gt.outputs[0], b.inputs["Alpha"])
    if normal and os.path.exists(normal):
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = normal_strength
        ntex = nt.nodes.new("ShaderNodeTexImage")
        ntex.image = image(normal, True)
        nt.links.new(ntex.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m


_crown = {}


def crown_low(name):
    return _crown.get(name, (1.5, 9.0))[0]


def crown_high(name):
    return _crown.get(name, (1.5, 9.0))[1]


# MARK: - Ultimate Nature


def _guid_index(root):
    idx = {}
    for meta in glob.glob(os.path.join(root, "**", "*.meta"), recursive=True):
        with open(meta, errors="ignore") as f:
            m = re.search(r"guid: ([0-9a-f]{32})", f.read())
        if m:
            idx[m.group(1)] = meta[:-5]
    return idx


_guids = None
_mats = None


def _unity_mat(name):
    """Texture paths and colours of a pack material (parsed from its .mat file)."""
    global _guids, _mats
    if _guids is None:
        _guids = _guid_index(UNP)
        _mats = {os.path.splitext(os.path.basename(p))[0]: p for p in glob.glob(os.path.join(UNP, "Materials", "**", "*.mat"), recursive=True)}
    path = _mats.get(name)
    if path is None:
        return {}
    text = open(path, errors="ignore").read()
    out = {"colors": {}, "tex": {}}
    for m in re.finditer(r"- (_\w+):\s*\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})", text):
        if m.group(2) in _guids:
            out["tex"][m.group(1)] = _guids[m.group(2)]
    for m in re.finditer(r"- (_\w+): \{r: ([\d.e-]+), g: ([\d.e-]+), b: ([\d.e-]+), a: ([\d.e-]+)\}", text):
        out["colors"][m.group(1)] = tuple(float(m.group(i)) for i in (2, 3, 4))
    out["shader"] = "vegetation" if "30a203ae9844b254f8e9f4952c49bef0" in text else ""
    return out


def _first(d, *keys):
    for k in keys:
        if k in d:
            return d[k]
    return None


_unp_cache = {}
ROCK_LARGE_DARKEN = 0.55
CUTOUT = ("leaves", "leaf", "branch", "grass", "fern", "clover", "card", "decal", "petal", "flower", "wheat", "reeds")


def unp_material(name):
    if name in _unp_cache:
        return _unp_cache[name]
    info = _unity_mat(name)
    low = name.lower()
    tex = _first(info.get("tex", {}), "_MainTexture", "_MainTex", "_Texture", "_BaseMap", "_Albedo")
    nrm = _first(info.get("tex", {}), "_BumpMap", "_NormalMap", "_Normal")
    colors = info.get("colors", {})
    # Unity stores material colours in gamma space.
    main = lin(_first(colors, "_MainColor", "_Color", "_BaseColor") or (1, 1, 1))
    second = lin(colors["_SecondColor"]) if "_SecondColor" in colors else None
    cut = any(k in low for k in CUTOUT)
    if tex is None:
        if any(k in low for k in ("rock", "cliff", "stone", "boulder")):
            main = lin((0.50, 0.47, 0.42))  # the packs' untextured stones read as white plaster
        m = principled(name, main)
    elif second is not None and any(k in low for k in ("lea", "branch", "grass", "fern", "reeds", "clover")):
        # Foundation's warm yellow-green, a little more saturated than the pack (the game's NatureKitSetup).
        dark = np.array(main) * np.array((1.02, 1.0, 0.72))
        light = np.array(second) * 1.12 * np.array((1.1, 1.06, 0.7))
        m = textured(name, tex, nrm, alpha_clip=cut, crown=(tuple(dark), tuple(np.minimum(light, 1))))
    else:
        tint = main
        if any(k in low for k in ("rock", "cliff", "mountain")):
            tint = (main[0] * 0.56, main[1] * 0.53, main[2] * 0.48)
            if "large" in low:
                tint = tuple(c * ROCK_LARGE_DARKEN for c in tint)  # pale texture: reads white in full sun
        m = textured(name, tex, nrm, tint=tint, alpha_clip=cut, closest="palette" in low, normal_strength=0.8)
    if "rock" in low and "large" in low:
        m["darkened"] = True
    _unp_cache[name] = m
    return m


_unp_models = {}


def unp(name, lod=0):
    """A pack model (e.g. 'Tree_Broadleaf_03') as a hidden collection to instance; LOD0 by default."""
    key = (name, lod)
    if key in _unp_models and _unp_models[key].name in bpy.data.collections:
        return _unp_models[key]
    cname = f"unp_{name}" + (f"_lod{lod}" if lod else "")
    if cname in bpy.data.collections:
        _unp_models[key] = bpy.data.collections[cname]
        return _unp_models[key]
    files = [f for f in glob.glob(os.path.join(UNP, "Meshes", "**", "*.fbx"), recursive=True)
             if os.path.basename(f).endswith(" " + name + ".fbx") or os.path.basename(f).endswith("_" + name + ".fbx")
             or os.path.basename(f) == name + ".fbx"]
    files = [f for f in files if os.path.basename(f).replace("Innerverse (UNP) Ultimate Nature ", "").split(" ")[-1] == name + ".fbx"]
    if not files:
        raise FileNotFoundError(name)
    col = _import_fbx(files[0], cname, keep=lambda o: ("_LOD" not in o.name) or o.name.split(".")[0].endswith(f"_LOD{lod}"))
    for o in col.objects:
        for slot in o.material_slots:
            if slot.material:
                base = slot.material.name.split(".")[0]
                slot.material = unp_material(base)
    # Crown heights for the leaf gradient.
    zs = [(o.matrix_world @ Vector(c)).z for o in col.objects for c in o.bound_box]
    if zs:
        for o in col.objects:
            for slot in o.material_slots:
                if slot.material and slot.material.name not in _crown:
                    pass
    _unp_models[key] = col
    return col


def _import_fbx(path, colname, keep=lambda o: True):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    col = bpy.data.collections.new(colname)
    bpy.context.scene.collection.children.link(col)
    for o in new:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        if o.type == "MESH" and keep(o):
            col.objects.link(o)
        elif o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
        elif o.type in ("ARMATURE", "EMPTY"):
            col.objects.link(o)
    # Bake the import transform into the meshes so the collection's origin is the model's foot.
    bpy.context.view_layer.update()
    for o in list(col.objects):
        if o.type == "MESH":
            mw = o.matrix_world.copy()
            o.parent = None
            o.data.transform(mw)
            o.matrix_world = Matrix.Identity(4)
    for o in list(col.objects):
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.layer_collection.children[col.name].exclude = True
    return col


_placed = None


def place(col, location, rot_z=0.0, scale=1.0, tilt=(0.0, 0.0)):
    """An instance of a model collection at a spot (degrees, uniform scale)."""
    global _placed
    if _placed is None or _placed.name not in bpy.data.collections:
        if "placed" in bpy.data.collections:
            _placed = bpy.data.collections["placed"]
        else:
            _placed = bpy.data.collections.new("placed")
            bpy.context.scene.collection.children.link(_placed)
    e = bpy.data.objects.new(col.name, None)
    e.instance_type = "COLLECTION"
    e.instance_collection = col
    e.location = Vector(location)
    e.rotation_euler = Euler((math.radians(tilt[0]), math.radians(tilt[1]), math.radians(rot_z)))
    e.scale = (scale, scale, scale) if not isinstance(scale, (tuple, list)) else scale
    _placed.objects.link(e)
    return e


# MARK: - The game's models


_game = {}


def darken_large_rocks(materials):
    """Applies ROCK_LARGE_DARKEN to big-boulder materials saved before it existed (a cached world)."""
    for m in materials:
        if not m.name.lower().startswith("rock_large") or m.get("darkened") or not m.use_nodes:
            continue
        for n in m.node_tree.nodes:
            if n.type == "MIX" and n.blend_type == "MULTIPLY":
                c = n.inputs[7].default_value
                n.inputs[7].default_value = (c[0] * ROCK_LARGE_DARKEN, c[1] * ROCK_LARGE_DARKEN, c[2] * ROCK_LARGE_DARKEN, 1)
        m["darkened"] = True


def forget():
    """Drops the cached datablocks (after a .blend is reopened they point at freed data)."""
    for d in (_game, _crown, _unp_cache, _unp_models, _exported):
        d.clear()


def game_material(model, mname, spec):
    a = os.path.join(ASSETS, spec["albedo"])
    n = os.path.join(ASSETS, spec["normal"]) if spec.get("normal") else None
    return textured(f"{model}_{mname}", a, n, roughness=0.9, alpha_clip=spec.get("alphaClip", False), normal_strength=0.6)


def game(model, tint=None):
    """A model from art_manifest.json (Models/<name>.fbx) with the game's textures."""
    if model in _game and _game[model].name in bpy.data.collections:
        return _game[model]
    if "game_" + model in bpy.data.collections:
        _game[model] = bpy.data.collections["game_" + model]
        return _game[model]
    entry = MANIFEST["models"][model]
    path = os.path.join(ASSETS, "Art", "Resources", entry["resource"] + ".fbx")
    col = _import_fbx(path, "game_" + model)
    mats = {k: game_material(model, k, v) for k, v in entry.get("materials", {}).items() if "albedo" in v}
    order = entry.get("materialOrder", [])
    for o in col.objects:
        for i, slot in enumerate(o.material_slots):
            base = slot.material.name.split(".")[0] if slot.material else (order[i] if i < len(order) else None)
            if base in mats:
                slot.material = mats[base]
            elif base and "glass" in base.lower():
                slot.material = principled(base, (0.95, 0.72, 0.35), 0.3)
    _game[model] = col
    return col


def model_size(col):
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for o in col.objects:
        for c in o.bound_box:
            v = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, v))
            hi = Vector(map(max, hi, v))
    return lo, hi


# MARK: - Terrain


def terrain(name, size, res, height_fn, material, origin=(0, 0)):
    """A grid mesh size x size metres, res x res quads, z = height_fn(x, y) (vectorised over numpy arrays)."""
    xs = np.linspace(-size / 2, size / 2, res + 1) + origin[0]
    ys = np.linspace(-size / 2, size / 2, res + 1) + origin[1]
    X, Y = np.meshgrid(xs, ys)
    Z = height_fn(X, Y)
    verts = np.stack([X.ravel(), Y.ravel(), Z.ravel()], 1)
    n = res + 1
    i = np.arange(res)
    a = (i[:, None] * n + i[None, :]).ravel()
    faces = np.stack([a, a + 1, a + n + 1, a + n], 1)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts.tolist(), [], faces.tolist())
    me.update()
    for p in me.polygons:
        p.use_smooth = True
    uv = me.uv_layers.new()
    co = verts[:, :2]
    loops = np.array([l.vertex_index for l in me.loops])
    uv.data.foreach_set("uv", (co[loops] / 4.0).ravel())
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(material)
    return ob, (X, Y, Z)


def fbm(x, y, octaves=5, scale=1.0, seed=0):
    rng = np.random.RandomState(seed)
    total = np.zeros_like(x, dtype=np.float64)
    amp, freq = 1.0, scale
    for _ in range(octaves):
        px, py = rng.uniform(0, 1000, 2)
        total += amp * value_noise(x * freq + px, y * freq + py)
        amp *= 0.5
        freq *= 2.03
    return total


def value_noise(x, y):
    xi, yi = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    xf, yf = x - xi, y - yi
    u, v = xf * xf * (3 - 2 * xf), yf * yf * (3 - 2 * yf)

    def h(i, j):
        n = (i * 374761393 + j * 668265263) & 0xFFFFFFFF
        n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
        return (n & 0xFFFF) / 32767.5 - 1.0
    a, b, c, d = h(xi, yi), h(xi + 1, yi), h(xi, yi + 1), h(xi + 1, yi + 1)
    return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v


def ground_material(name="ground", grass=(0.30, 0.42, 0.13), meadow=(0.50, 0.55, 0.20), dirt=(0.42, 0.32, 0.20),
                    rock=(0.42, 0.40, 0.37), slope_rock=0.55, path_attr=None, scale=1.0):
    """Painted ground: two greens in big soft patches, warm dirt where the 'dirt' vertex colour says, rock on
    steep slopes; the game's grass texture adds a little brushy detail."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = 0.95
    b.inputs["Specular IOR Level"].default_value = 0.1
    tc = nt.nodes.new("ShaderNodeTexCoord")
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 0.035 * scale
    noise.inputs["Detail"].default_value = 3
    noise.inputs["Roughness"].default_value = 0.55
    nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.38
    ramp.color_ramp.elements[0].color = (*lin(grass), 1)
    ramp.color_ramp.elements[1].position = 0.66
    ramp.color_ramp.elements[1].color = (*lin(meadow), 1)
    mid = ramp.color_ramp.elements.new(0.52)
    mid.color = (*lin(tuple(min(1, c * 1.12 + d) for c, d in zip(meadow, (0.06, 0.03, -0.02)))), 1)
    dry = ramp.color_ramp.elements.new(0.80)
    dry.color = (*lin((0.70, 0.62, 0.30)), 1)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    col = ramp.outputs["Color"]
    # Fine brushy variation from the game's grass texture.
    gt = nt.nodes.new("ShaderNodeTexImage")
    gt.image = image(os.path.join(ASSETS, "Art", "Textures", "Grass_albedo.jpg"))
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (0.12, 0.12, 0.12)
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    nt.links.new(mp.outputs[0], gt.inputs["Vector"])
    ov = nt.nodes.new("ShaderNodeMix")
    ov.data_type = "RGBA"
    ov.blend_type = "SOFT_LIGHT"
    ov.inputs[0].default_value = 0.45
    nt.links.new(col, ov.inputs[6])
    nt.links.new(gt.outputs["Color"], ov.inputs[7])
    col = ov.outputs[2]
    # Dirt from a vertex colour layer (paths, yards, river banks).
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "paint"
    sepc = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(attr.outputs["Color"], sepc.inputs[0])
    dirt_tex = nt.nodes.new("ShaderNodeTexImage")
    dirt_tex.image = image(os.path.join(ASSETS, "Art", "Textures", "Mud_albedo.jpg"))
    mp2 = nt.nodes.new("ShaderNodeMapping")
    mp2.inputs["Scale"].default_value = (0.25, 0.25, 0.25)
    nt.links.new(tc.outputs["Object"], mp2.inputs["Vector"])
    nt.links.new(mp2.outputs[0], dirt_tex.inputs["Vector"])
    dmix = nt.nodes.new("ShaderNodeMix")
    dmix.data_type = "RGBA"
    dmix.blend_type = "MULTIPLY"
    dmix.inputs[0].default_value = 0.6
    dmix.inputs[6].default_value = (*lin(dirt), 1)
    nt.links.new(dirt_tex.outputs["Color"], dmix.inputs[7])
    dcol = nt.nodes.new("ShaderNodeMix")
    dcol.data_type = "RGBA"
    nt.links.new(sepc.outputs["Red"], dcol.inputs[0])
    nt.links.new(col, dcol.inputs[6])
    nt.links.new(dmix.outputs[2], dcol.inputs[7])
    col = dcol.outputs[2]
    # Sand (green channel), rock (blue channel) and steep slopes.
    sand = nt.nodes.new("ShaderNodeMix")
    sand.data_type = "RGBA"
    nt.links.new(sepc.outputs["Green"], sand.inputs[0])
    nt.links.new(col, sand.inputs[6])
    st = nt.nodes.new("ShaderNodeTexImage")
    st.image = image(os.path.join(ASSETS, "Art", "Textures", "Sand_albedo.jpg"))
    mp4 = nt.nodes.new("ShaderNodeMapping")
    mp4.inputs["Scale"].default_value = (0.3, 0.3, 0.3)
    nt.links.new(tc.outputs["Object"], mp4.inputs["Vector"])
    nt.links.new(mp4.outputs[0], st.inputs["Vector"])
    smul = nt.nodes.new("ShaderNodeMix")
    smul.data_type = "RGBA"
    smul.blend_type = "MULTIPLY"
    smul.inputs[0].default_value = 0.55
    smul.inputs[6].default_value = (*lin((0.86, 0.72, 0.48)), 1)
    nt.links.new(st.outputs["Color"], smul.inputs[7])
    nt.links.new(smul.outputs[2], sand.inputs[7])
    col = sand.outputs[2]
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sepn = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Normal"], sepn.inputs[0])
    sl = nt.nodes.new("ShaderNodeMapRange")
    sl.inputs["From Min"].default_value = slope_rock + 0.25
    sl.inputs["From Max"].default_value = slope_rock
    nt.links.new(sepn.outputs["Z"], sl.inputs["Value"])
    mx = nt.nodes.new("ShaderNodeMath")
    mx.operation = "MAXIMUM"
    nt.links.new(sl.outputs["Result"], mx.inputs[0])
    nt.links.new(sepc.outputs["Blue"], mx.inputs[1])
    rt = nt.nodes.new("ShaderNodeTexImage")
    rt.image = image(os.path.join(ASSETS, "Art", "Textures", "RockGround_albedo.jpg"))
    mp3 = nt.nodes.new("ShaderNodeMapping")
    mp3.inputs["Scale"].default_value = (0.15, 0.15, 0.15)
    nt.links.new(tc.outputs["Object"], mp3.inputs["Vector"])
    nt.links.new(mp3.outputs[0], rt.inputs["Vector"])
    rmul = nt.nodes.new("ShaderNodeMix")
    rmul.data_type = "RGBA"
    rmul.blend_type = "MULTIPLY"
    rmul.inputs[0].default_value = 0.7
    rn = nt.nodes.new("ShaderNodeTexNoise")
    rn.inputs["Scale"].default_value = 0.09 * scale
    rn.inputs["Detail"].default_value = 6
    rn.inputs["Roughness"].default_value = 0.65
    nt.links.new(tc.outputs["Object"], rn.inputs["Vector"])
    rr = nt.nodes.new("ShaderNodeValToRGB")
    rr.color_ramp.elements[0].position = 0.35
    rr.color_ramp.elements[0].color = (*lin(tuple(c * 0.62 for c in rock)), 1)
    rr.color_ramp.elements[1].position = 0.68
    rr.color_ramp.elements[1].color = (*lin(tuple(min(1, c * 1.35) for c in rock)), 1)
    nt.links.new(rn.outputs["Fac"], rr.inputs["Fac"])
    nt.links.new(rr.outputs["Color"], rmul.inputs[6])
    nt.links.new(rt.outputs["Color"], rmul.inputs[7])
    rmix = nt.nodes.new("ShaderNodeMix")
    rmix.data_type = "RGBA"
    nt.links.new(mx.outputs[0], rmix.inputs[0])
    nt.links.new(col, rmix.inputs[6])
    nt.links.new(rmul.outputs[2], rmix.inputs[7])
    nt.links.new(rmix.outputs[2], b.inputs["Base Color"])
    return m


def paint_vertices(ob, fn):
    """Vertex colour 'paint' = fn(x, y) -> (dirt, sand, rock) arrays in 0..1."""
    me = ob.data
    if "paint" not in me.color_attributes:
        me.color_attributes.new("paint", "FLOAT_COLOR", "POINT")
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    d, s, r = fn(co[:, 0], co[:, 1])
    rgba = np.stack([d, s, r, np.ones_like(d)], 1).astype(np.float32)
    me.color_attributes["paint"].data.foreach_set("color", rgba.ravel())


def water(name, size, z, center=(0, 0), color=(0.10, 0.22, 0.24)):
    bpy.ops.mesh.primitive_plane_add(size=1, location=(center[0], center[1], z))
    ob = bpy.context.object
    ob.name = name
    ob.scale = (size[0], size[1], 1)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*lin(color), 1)
    b.inputs["Roughness"].default_value = 0.06
    b.inputs["Specular IOR Level"].default_value = 0.5
    b.inputs["IOR"].default_value = 1.33
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wave = nt.nodes.new("ShaderNodeTexNoise")
    wave.inputs["Scale"].default_value = 0.6
    wave.inputs["Detail"].default_value = 4
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (1, 3.5, 1)
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    nt.links.new(mp.outputs[0], wave.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.08
    nt.links.new(wave.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], b.inputs["Normal"])
    ob.data.materials.append(m)
    return ob


def height_at(XYZ, x, y):
    X, Y, Z = XYZ
    xs, ys = X[0], Y[:, 0]
    i = np.clip(np.searchsorted(xs, x) - 1, 0, len(xs) - 2)
    j = np.clip(np.searchsorted(ys, y) - 1, 0, len(ys) - 2)
    tx = (x - xs[i]) / (xs[i + 1] - xs[i])
    ty = (y - ys[j]) / (ys[j + 1] - ys[j])
    z00, z10, z01, z11 = Z[j, i], Z[j, i + 1], Z[j + 1, i], Z[j + 1, i + 1]
    return (z00 * (1 - tx) + z10 * tx) * (1 - ty) + (z01 * (1 - tx) + z11 * tx) * ty


# MARK: - Villagers


SKIN_TONES = [(0.87, 0.68, 0.56), (0.80, 0.60, 0.47), (0.70, 0.50, 0.37), (0.56, 0.39, 0.28), (0.42, 0.29, 0.21)]
DYES = [(0.66, 0.57, 0.42), (0.46, 0.55, 0.30), (0.32, 0.45, 0.62), (0.66, 0.31, 0.22), (0.76, 0.60, 0.30),
        (0.45, 0.33, 0.24), (0.52, 0.42, 0.58), (0.30, 0.48, 0.44)]
HAIR = [(0.09, 0.065, 0.05), (0.18, 0.12, 0.07), (0.30, 0.20, 0.11), (0.42, 0.30, 0.18), (0.55, 0.36, 0.20)]
_people = None


def people_list():
    global _people
    if _people is None:
        _people = [p for p in json.load(open(os.path.join(ASSETS, "Art", "Textures", "People", "people.json")))["people"]
                   if "textures" in p] if os.path.exists(os.path.join(ASSETS, "Art", "Textures", "People", "people.json")) else \
            [p for p in json.load(open(os.path.join(ASSETS, "Art", "Resources", "people.json")))["people"] if "textures" in p]
    return _people


def _person_material(p, zone, tint):
    m = bpy.data.materials.new(f"{p['model']}_{zone}")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    t = p["textures"]
    alb = nt.nodes.new("ShaderNodeTexImage")
    alb.image = image(os.path.join(ASSETS, t["albedo"]))
    mask = nt.nodes.new("ShaderNodeTexImage")
    mask.image = image(os.path.join(ASSETS, t["mask"]), True)
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(mask.outputs["Color"], sep.inputs[0])
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs[0].default_value = 1
    nt.links.new(alb.outputs["Color"], mul.inputs[6])
    mul.inputs[7].default_value = (*tint, 1)
    ao = nt.nodes.new("ShaderNodeMix")
    ao.data_type = "RGBA"
    ao.blend_type = "MULTIPLY"
    ao.inputs[0].default_value = 0.5
    nt.links.new(mul.outputs[2], ao.inputs[6])
    nt.links.new(sep.outputs["Green"], ao.inputs[7])
    nt.links.new(ao.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    bsdf.inputs["Specular IOR Level"].default_value = 0.2
    if zone == "Skin":
        bsdf.inputs["Subsurface Weight"].default_value = 0.15
        bsdf.inputs["Subsurface Radius"].default_value = (0.02, 0.008, 0.005)
    return m


def villager(index, location, rot_z, clip="Idle", t=0.3, kind=None, prop=None, scale=1.0):
    """One villager posed from its clip (Idle, Walk, Chop, Carry, ...), standing at location facing rot_z."""
    from person_style import soften, stylize, subdivide
    ps = people_list()
    p = next((q for q in ps if kind and kind in q["model"]), ps[index % len(ps)])
    skin = SKIN_TONES[(index * 2) % len(SKIN_TONES)]
    main = DYES[index % len(DYES)]
    hair = p["hair"] if p.get("age") == "elder" else HAIR[(index * 3) % len(HAIR)]
    neutral = np.asarray(p["neutral"])
    tints = {"Skin": np.array(lin(skin)) / np.maximum(1e-3, np.asarray(p["skin"])),
             "Main": np.array(lin(main)) / neutral if p.get("dye", True) else np.ones(3),
             "Hair": np.array(lin(hair)) / neutral, "Rest": np.ones(3)}
    before, acts = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ASSETS, "Art", "Resources", p["model"] + ".fbx"))
    objs = [o for o in bpy.data.objects if o not in before]
    clips = [a for a in bpy.data.actions if a not in acts]
    rig = next(o for o in objs if o.type == "ARMATURE")
    stylize(rig, objs)
    subdivide(objs)
    soften(objs)
    for o in objs:
        if o.type != "MESH":
            continue
        if o.name.startswith("Body_LOD1") or (o.name.startswith("Prop_") and not (prop and prop in o.name)):
            o.hide_render = True
            continue
        for slot in o.material_slots:
            zone = slot.material.name.split(".")[0]
            slot.material = _person_material(p, zone, tuple(np.minimum(tints.get(zone, np.ones(3)), 4)))
    act = next((a for a in clips if a.name.split("|")[-1] == clip), clips[0] if clips else None)
    if act is not None:
        rig.animation_data_create()
        rig.animation_data.action = act
        f0, f1 = act.frame_range
        rig["frame"] = f0 + (f1 - f0) * t
        # Freeze the pose: evaluate at that frame and drop the action so later frame changes do not move it.
        bpy.context.scene.frame_set(int(rig["frame"]))
        for pb in rig.pose.bones:
            pb.matrix_basis = pb.matrix_basis.copy()
        sv = {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy(), pb.rotation_euler.copy(), pb.scale.copy()) for pb in rig.pose.bones}
        rig.animation_data.action = None
        for pb in rig.pose.bones:
            pb.location, pb.rotation_quaternion, pb.rotation_euler, pb.scale = sv[pb.name]
    rig.location = Vector(location)
    rig.rotation_euler = Euler((rig.rotation_euler.x, rig.rotation_euler.y, math.radians(rot_z)))
    rig.scale = rig.scale * scale
    return rig


# MARK: - Scatter


def poisson(rng, w, h, r, k=20, mask=None, limit=100000, origin=(0, 0)):
    """Bridson poisson-disc points in a w x h rectangle centred at origin; mask(x, y) -> bool keeps a point."""
    cell = r / math.sqrt(2)
    gw, gh = int(w / cell) + 1, int(h / cell) + 1
    grid = -np.ones((gh, gw), dtype=np.int64)
    pts = []
    active = []
    x0 = (rng.uniform(0, w), rng.uniform(0, h))
    pts.append(x0)
    active.append(0)
    grid[int(x0[1] / cell), int(x0[0] / cell)] = 0
    while active and len(pts) < limit:
        i = active[rng.randint(len(active))]
        px, py = pts[i]
        found = False
        for _ in range(k):
            a = rng.uniform(0, 2 * math.pi)
            d = rng.uniform(r, 2 * r)
            x, y = px + d * math.cos(a), py + d * math.sin(a)
            if not (0 <= x < w and 0 <= y < h):
                continue
            gx, gy = int(x / cell), int(y / cell)
            ok = True
            for yy in range(max(0, gy - 2), min(gh, gy + 3)):
                for xx in range(max(0, gx - 2), min(gw, gx + 3)):
                    j = grid[yy, xx]
                    if j >= 0 and (pts[j][0] - x) ** 2 + (pts[j][1] - y) ** 2 < r * r:
                        ok = False
                        break
                if not ok:
                    break
            if ok:
                pts.append((x, y))
                active.append(len(pts) - 1)
                grid[gy, gx] = len(pts) - 1
                found = True
                break
        if not found:
            active.remove(i)
    out = [(x - w / 2 + origin[0], y - h / 2 + origin[1]) for x, y in pts]
    return [p for p in out if mask is None or mask(*p)]


def render(path):
    """Renders the camera to path (PNG); with a mist pass also <path>_mist.png (16-bit, 0 near .. 1 far)."""
    scene = bpy.context.scene
    scene.render.filepath = path
    if bpy.context.view_layer.use_pass_mist:
        scene.use_nodes = True
        nt = scene.node_tree
        nt.nodes.clear()
        rl = nt.nodes.new("CompositorNodeRLayers")
        comp = nt.nodes.new("CompositorNodeComposite")
        nt.links.new(rl.outputs["Image"], comp.inputs["Image"])
        fo = nt.nodes.new("CompositorNodeOutputFile")
        fo.base_path = os.path.dirname(os.path.abspath(path))
        fo.format.file_format = "PNG"
        fo.format.color_mode = "BW"
        fo.format.color_depth = "16"
        fo.format.color_management = "OVERRIDE"
        fo.format.view_settings.view_transform = "Standard"
        fo.file_slots[0].path = os.path.splitext(os.path.basename(path))[0] + "_mist_"
        nt.links.new(rl.outputs["Mist"], fo.inputs[0])
    bpy.ops.render.render(write_still=True)
    # File Output appends the frame number: give it a stable name.
    base = os.path.join(os.path.dirname(os.path.abspath(path)), os.path.splitext(os.path.basename(path))[0] + "_mist_")
    for f in os.listdir(os.path.dirname(os.path.abspath(path))):
        full = os.path.join(os.path.dirname(os.path.abspath(path)), f)
        if full.startswith(base) and f.endswith(".png"):
            os.replace(full, base[:-1] + ".png")
    print("RENDERED", path)


# MARK: - Unity OBJ export (FoundersEditor.ExportBuildingModels)


def _export_material(name, spec):
    """Matte version of tools/art/blender_buildings.py build_material: albedo x colour, mask occlusion, normal."""
    color = spec.get("color", [1, 1, 1, 1])
    tint = lin(color[:3])
    lname = name.lower()
    if "glass" in lname and not spec.get("albedo"):
        return principled(name, (0.9, 0.62, 0.3), 0.35)
    if "colormap" in lname:
        # Kit props sample a palette strip by UV points; here they come out salmon, so give them plain wood.
        wood = {"barrel": (0.50, 0.33, 0.20), "bucket": (0.55, 0.38, 0.24), "stone": (0.52, 0.50, 0.46)}
        c = next((v for k, v in wood.items() if k in lname), (0.66, 0.48, 0.30))
        return principled(name, lin(c), 0.85)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Specular IOR Level"].default_value = 0.22
    b.inputs["Roughness"].default_value = 0.86
    base = None
    alpha = None
    if spec.get("albedo") and os.path.exists(spec["albedo"]):
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image(spec["albedo"])
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs[0].default_value = 1
        nt.links.new(tex.outputs["Color"], mul.inputs[6])
        mul.inputs[7].default_value = (*tint, 1)
        base = mul.outputs[2]
        alpha = tex.outputs["Alpha"]
    else:
        b.inputs["Base Color"].default_value = (*tint, 1)
    if spec.get("mask") and os.path.exists(spec["mask"]) and base is not None:
        mt = nt.nodes.new("ShaderNodeTexImage")
        mt.image = image(spec["mask"], True)
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(mt.outputs["Color"], sep.inputs[0])
        ao = nt.nodes.new("ShaderNodeMix")
        ao.data_type = "RGBA"
        ao.blend_type = "MULTIPLY"
        ao.inputs[0].default_value = 0.6
        nt.links.new(base, ao.inputs[6])
        nt.links.new(sep.outputs["Green"], ao.inputs[7])
        base = ao.outputs[2]
    if base is not None:
        nt.links.new(base, b.inputs["Base Color"])
    if spec.get("normal") and os.path.exists(spec["normal"]):
        ntex = nt.nodes.new("ShaderNodeTexImage")
        ntex.image = image(spec["normal"], True)
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = 0.6 * float(spec.get("bumpScale", 1.0))
        nt.links.new(ntex.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    if spec.get("alphaClip") and alpha is not None:
        gt = nt.nodes.new("ShaderNodeMath")
        gt.operation = "GREATER_THAN"
        gt.inputs[1].default_value = float(spec.get("cutoff", 0.5))
        nt.links.new(alpha, gt.inputs[0])
        nt.links.new(gt.outputs[0], b.inputs["Alpha"])
    em = spec.get("emission") or [0, 0, 0]
    if max(em) > 0.001:
        b.inputs["Emission Color"].default_value = (*em[:3], 1)
        b.inputs["Emission Strength"].default_value = 1.0
    return m


_exported = {}


def exported(key, base, entry):
    """A building from the Unity export as a hidden collection (street side towards Blender -y, like the FBX)."""
    if key in _exported and _exported[key].name in bpy.data.collections:
        return _exported[key]
    if "exp_" + key in bpy.data.collections:
        _exported[key] = bpy.data.collections["exp_" + key]
        return _exported[key]
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=os.path.join(base, entry["obj"]), forward_axis="NEGATIVE_Z", up_axis="Y")
    new = [o for o in bpy.data.objects if o not in before]
    col = bpy.data.collections.new("exp_" + key)
    bpy.context.scene.collection.children.link(col)
    specs = {}
    for spec in entry["materials"]:
        s = dict(spec)
        for k in ("albedo", "normal", "mask"):
            if s.get(k):
                s[k] = os.path.join(base, s[k])
        specs[spec["name"]] = s
    built = {}
    for o in new:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        col.objects.link(o)
        for slot in o.material_slots:
            if slot.material is None:
                continue
            n = slot.material.name
            k = n[:-4] if len(n) > 4 and n[-4] == "." and n[-3:].isdigit() else n
            spec = specs.get(k) or specs.get(n) or {"color": [0.5, 0.45, 0.4, 1]}
            if k not in built:
                built[k] = _export_material(f"{key}_{k}", spec)
            slot.material = built[k]
    bpy.context.view_layer.update()
    for o in list(col.objects):
        mw = o.matrix_world.copy()
        o.data.transform(mw)
        o.matrix_world = Matrix.Identity(4)
        # The exporter's street side is OBJ -z, which lands on Blender +y; the FBX models face -y at rot 0
        # after the 180 used for placing: turn the OBJ to match (front towards +y at rot 0).
    col["effects"] = json.dumps(entry.get("effects", []))
    bpy.context.view_layer.layer_collection.children[col.name].exclude = True
    _exported[key] = col
    return col
