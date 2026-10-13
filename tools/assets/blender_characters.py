"""Blender (4.2): assembles one villager from the CC0 Quaternius "Universal Base Characters" and
"Modular Character Outfits - Fantasy" packs and gives it every work animation of the game.

- head, eyes and eyebrows come from a base character (realistic proportions), the clothes from
  the outfit pack (peasant shirt and trousers, ranger jerkin and hood, boots), the hair from the
  base pack's hairstyles. Every pack uses the same Unreal-style skeleton, so the parts are simply
  re-parented to one armature.
- the packs' painted PBR textures (skin and face, cloth weave, folds, stitching, leather) are kept:
  the full-detail parts are baked onto the game mesh as one texture set per person (albedo,
  tangent-space normal map, mask with ambient occlusion and smoothness). The garment zones are
  still found by colour (k-means over per-face texture samples, cleaned with a neighbour vote):
  the main garment ("Main") and the hair ("Hair") are baked as neutral grey detail so the game can
  dye them per person; trousers, boots, hood and bodice are baked in the period colours of the
  spec; skin ("Skin") keeps the painted skin and the game shifts its tone per person.
- women can get a long skirt (a skinned cone over the trousers, following pelvis and thighs, with
  a woven wool surface); elders get a slight stoop (applied to the rest pose, so every clip keeps it)
- every mesh is welded, smooth shaded and decimated to fit the triangle budget (about 7.4k), and a
  second, lighter mesh (Body_LOD1, same skeleton and textures) is exported for distant villagers
- the animations of Villager.fbx (Universal Animation Library 1 + 2, same skeleton) are
  retargeted bone by bone: each bone takes the source bone's world rotation change from rest, the
  pelvis follows the source pelvis scaled to the leg length. Extra clips can come from other files
  on the same skeleton (the sick lie down: the first moment of UAL2 "LayToIdle", held as "Lie").
- hand tools (Prop_Axe, Prop_Pickaxe, Prop_Hammer, Prop_Spear) are parented to hand_r exactly as
  in Villager.fbx (blender_people.py)

blender -b --factory-startup -P blender_characters.py -- <Villager.fbx> <spec.json> <out.fbx>
spec.json: {"base": gltf, "parts": [[gltf, object name prefix], ...], "hair": [gltf, ...],
            "skirt": bool, "elder": bool, "hood": bool, "colors": {"Main": [r,g,b] (sRGB), ...},
            "tris": target triangle count, "lod_tris": LOD1 triangle count, "dye": bool (default true),
            "textures": output path prefix (writes <prefix>_albedo/_normal/_mask.npy),
            "tex_size": texture size, "extra_anims": [[file, source action, clip, end fraction], ...]}
"""
import json
import math
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, SPEC, OUT = argv[0], argv[1], argv[2]
spec = json.load(open(SPEC))
TARGET_TRIS = spec.get("tris", 7400)
LOD_TRIS = spec.get("lod_tris", 2400)
TEX = spec.get("textures")
TEX_SIZE = spec.get("tex_size", 1024)
NEUTRAL = 0.5  # linear albedo of the dyed zones' grey detail (the game multiplies it by colour / NEUTRAL)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def import_any(path):
    before, acts = set(bpy.data.objects), set(bpy.data.actions)
    if path.lower().endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before], [a for a in bpy.data.actions if a not in acts]


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def luminance(lin):
    lin = np.asarray(lin, dtype=np.float64)
    return lin[..., 0] * 0.2126 + lin[..., 1] * 0.7152 + lin[..., 2] * 0.0722


# MARK: - Animation sources

src_objs, src_actions = import_any(SRC)
src = next(o for o in src_objs if o.type == "ARMATURE")
for o in src_objs:
    if o.type == "MESH":
        o.hide_render = o.hide_viewport = True
if src.animation_data is None:
    src.animation_data_create()

# Extra clips from other files on the same skeleton: (armature, action, clip name).
extra_sources = []
for path, action_name, clip, end in spec.get("extra_anims", []):
    objs, acts = import_any(path)
    arm = next(o for o in objs if o.type == "ARMATURE")
    for o in objs:
        if o.type == "MESH":
            o.hide_render = o.hide_viewport = True
    act = next((a for a in acts if a.name.split("|")[-1].split("_Armature")[0] == action_name), None)
    for a in acts:
        if a != act:
            bpy.data.actions.remove(a)
    if act is None:
        print("CHARACTER_WARN missing extra clip", action_name)
        continue
    if arm.animation_data is None:
        arm.animation_data_create()
    f0, f1 = act.frame_range
    act["clip_end"] = f0 + (f1 - f0) * end
    extra_sources.append((arm, act, clip, objs))

# MARK: - Parts

parts = []  # (object, kind) kind: head, eyes, brows, cloth, hair
rig = None


def adopt(o, src_rig=None):
    """Moves a skinned mesh onto `rig`. All packs share the skeleton; a part made for another
    body (hair "rigged to the head bone" sits at the head height of its model) is moved by the
    offset between its own Head bone and ours."""
    if src_rig is not None and src_rig != rig:
        d = (rig.matrix_world @ rig.data.bones["Head"].head_local) - (src_rig.matrix_world @ src_rig.data.bones["Head"].head_local)
        if o.type == "MESH" and "Head" in [g.name for g in o.vertex_groups] and d.length > 1e-4 and o.get("kind") == "hair":
            o.data.transform(o.matrix_world.inverted() @ Matrix.Translation(d) @ o.matrix_world)
    mw = o.matrix_world.copy()
    o.parent = rig
    o.matrix_world = mw
    for m in o.modifiers:
        if m.type == "ARMATURE":
            m.object = rig


# The clothes are made for the "Regular" bodies: their skeleton (narrower shoulders and hips than
# the free "Superhero" body) becomes the character's rig. Head and neck bones are identical in both.
by_file = {}
for path, prefix in spec["parts"]:
    by_file.setdefault(path, []).append(prefix)
for path, prefixes in by_file.items():
    objs, _ = import_any(path)
    arm = next(o for o in objs if o.type == "ARMATURE")
    if rig is None:
        rig = arm
        rig.name = rig.data.name = "Armature"
    for o in objs:
        if o.type == "MESH" and o.name.split(".")[0] in prefixes:
            adopt(o, arm)
            parts.append((o, "cloth"))
        elif o != rig:
            o["drop"] = True

base_objs, _ = import_any(spec["base"])
neck_z = (rig.matrix_world @ rig.data.bones["neck_01"].head_local).z
for o in base_objs:
    if o.type != "MESH":
        o["drop"] = True
        continue
    n = o.name.lower()
    if n.startswith("superhero") or n.startswith("regular"):
        # Keep only the head and neck: the clothes cover the rest.
        mw = o.matrix_world
        bm = bmesh.new()
        bm.from_mesh(o.data)
        drop = [v for v in bm.verts if (mw @ v.co).z < neck_z - 0.035 or abs((mw @ v.co).x) > 0.13]
        bmesh.ops.delete(bm, geom=drop, context="VERTS")
        bm.to_mesh(o.data)
        bm.free()
        parts.append((o, "head"))
    elif n.startswith("eyes"):
        parts.append((o, "eyes"))
    elif n.startswith("eyebrows"):
        parts.append((o, "brows"))
    else:
        o["drop"] = True
        continue
    adopt(o)

for path in spec.get("hair", []):
    objs, _ = import_any(path)
    arm = next(o for o in objs if o.type == "ARMATURE")
    for o in objs:
        if o.type == "MESH" and o.name.lower().startswith(("hair", "eyebrows")):
            o["kind"] = "hair"
            adopt(o, arm)
            parts.append((o, "hair"))
        else:
            o["drop"] = True
for o in [o for o in bpy.data.objects if o.get("drop")]:
    bpy.data.objects.remove(o, do_unlink=True)
keep_actions = set(src_actions) | {a for _, a, _, _ in extra_sources}
for a in [a for a in bpy.data.actions if a not in keep_actions]:
    bpy.data.actions.remove(a)

# Trousers under a skirt would only poke through it (the boots reach above the hem).
if spec.get("skirt"):
    for o, kind in [p for p in parts if "_Legs" in p[0].name]:
        parts.remove((o, kind))
        bpy.data.objects.remove(o, do_unlink=True)

# MARK: - Texture colours per face


def base_image(mat):
    if not mat or not mat.node_tree:
        return None
    imgs = [n.image for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image]
    for im in imgs:
        low = im.name.lower()
        if "normal" not in low and "orm" not in low and "rough" not in low:
            return im
    return None


pixel_cache = {}


def pixels(im):
    if im.name not in pixel_cache:
        w, h = im.size
        px = np.empty(w * h * 4, dtype=np.float32)
        im.pixels.foreach_get(px)
        pixel_cache[im.name] = px.reshape(h, w, 4)[:, :, :3]
    return pixel_cache[im.name]


def face_colors(o):
    """Average texture colour (sRGB) of each face, sampled at its centre and near its corners."""
    me = o.data
    uv = me.uv_layers.active.data if me.uv_layers.active else None
    cols = np.zeros((len(me.polygons), 3))
    for p in me.polygons:
        mat = me.materials[p.material_index] if me.materials else None
        im = base_image(mat)
        if im is None or uv is None:
            bc = mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value if mat and mat.node_tree and mat.node_tree.nodes.get("Principled BSDF") else (0.5, 0.5, 0.5, 1)
            cols[p.index] = np.clip(np.array(bc[:3]), 0, 1) ** (1 / 2.2)
            continue
        px = pixels(im)
        h, w = px.shape[:2]
        uvs = [uv[i].uv for i in p.loop_indices]
        c = sum((Vector(u) for u in uvs), Vector((0, 0))) / len(uvs)
        pts = [c] + [c.lerp(Vector(u), 0.6) for u in uvs]
        acc = np.zeros(3)
        for q in pts:
            x = int((q.x % 1.0) * w) % w
            y = int((q.y % 1.0) * h) % h
            acc += px[y, x]
        cols[p.index] = acc / len(pts)
    return cols


def kind_of_material(mat, kind):
    n = (mat.name if mat else "").lower()
    if kind in ("head",) or "regular_" in n or "superhero" in n:
        return "skin"
    if "hair" in n or kind in ("hair", "brows"):
        return "hair"
    if "eye" in n or kind == "eyes":
        return "eyes"
    return "cloth"


# Weld UV seams (so decimation and smooth shading see closed surfaces) and gather face data.
records = []  # (object, face kinds array, colours array, areas array)
for o, kind in parts:
    # One UV map per part, under one name, so joining keeps every part's islands on the same map.
    uvs = o.data.uv_layers
    if uvs.active is not None:
        keep_uv = uvs.active.name
        for name in [l.name for l in uvs if l.name != keep_uv]:
            uvs.remove(uvs[name])
        uvs[keep_uv].name = "UVMap"
    else:
        uvs.new(name="UVMap")
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
    bm.to_mesh(o.data)
    bm.free()
    me = o.data
    if me.has_custom_normals:
        bpy.context.view_layer.objects.active = o
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    kinds = np.array([kind_of_material(me.materials[p.material_index] if me.materials else None, kind) for p in me.polygons])
    records.append((o, kinds, face_colors(o), np.array([p.area for p in me.polygons])))

# MARK: - Clothing zones (k-means over face colours)


def kmeans(x, wts, k, iters=30):
    rng = np.random.default_rng(1)
    # k-means++ seeding
    centers = [x[rng.choice(len(x), p=wts / wts.sum())]]
    for _ in range(k - 1):
        d = np.min([((x - c) ** 2).sum(1) for c in centers], axis=0) * wts
        centers.append(x[rng.choice(len(x), p=d / d.sum())] if d.sum() > 0 else x[0])
    centers = np.array(centers)
    for _ in range(iters):
        lab = np.argmin(((x[:, None, :] - centers[None]) ** 2).sum(2), axis=1)
        for j in range(k):
            m = lab == j
            if m.any():
                centers[j] = (x[m] * wts[m, None]).sum(0) / wts[m].sum()
    return centers, lab


cloth_x = np.concatenate([c[k == "cloth"] for _, k, c, _ in records])
cloth_w = np.concatenate([a[k == "cloth"] for _, k, _, a in records])
K = 7
MAX_ZONES = 5
centers, _ = kmeans(cloth_x, cloth_w + 1e-9, K)
# Merge near-identical zones.
merged = list(range(K))
for i in range(K):
    for j in range(i):
        if merged[j] == j and np.linalg.norm(centers[i] - centers[j]) < 0.12:
            merged[i] = j
            break


def label(cols):
    lab = np.argmin(((cols[:, None, :] - centers[None]) ** 2).sum(2), axis=1)
    return np.array([merged[l] for l in lab])


def smooth_labels(o, lab, mask, rounds=6):
    """Neighbour vote over faces sharing an edge (removes speckles from texture detail)."""
    me = o.data
    edge_faces = {}
    for p in me.polygons:
        for ek in p.edge_keys:
            edge_faces.setdefault(ek, []).append(p.index)
    nbrs = [[] for _ in me.polygons]
    for fs in edge_faces.values():
        for a in fs:
            nbrs[a].extend(b for b in fs if b != a)
    for _ in range(rounds):
        new = lab.copy()
        for i in range(len(lab)):
            if not mask[i] or not nbrs[i]:
                continue
            votes = {}
            for b in nbrs[i] + [i, i]:
                if mask[b]:
                    votes[lab[b]] = votes.get(lab[b], 0) + 1
            new[i] = max(votes, key=votes.get)
        lab = new
    return lab


cloth_labels = {}
area_by = {}  # (cluster, part name) -> area
for o, kinds, cols, areas in records:
    mask = kinds == "cloth"
    if not mask.any():
        continue
    lab = smooth_labels(o, label(cols), mask)
    # Painted wear and highlights: small zones of this part (default under 15%) join its main zone.
    ar = {c: areas[mask & (lab == c)].sum() for c in set(lab[mask])}
    main_c = max(ar, key=ar.get)
    for c, a in ar.items():
        if a < spec.get("fold", 0.15) * sum(ar.values()):
            lab[mask & (lab == c)] = main_c
    cloth_labels[o.name] = lab
    part = o.name.split(".")[0].split("_", 2)[-1].split("_")[0]  # Body, Arms, Legs, Feet, Head
    for i in np.nonzero(mask)[0]:
        area_by[(lab[i], part)] = area_by.get((lab[i], part), 0) + areas[i]


def top_cluster(part, taken):
    cands = [(a, c) for (c, p), a in area_by.items() if p == part and c not in taken]
    return max(cands)[1] if cands else None


names = {}
role_order = [("Main" if not spec.get("skirt") else "Top", "Body"), ("Trousers", "Legs"), ("Boots", "Feet"), ("Hood", "Head")]
for role, part in role_order:
    c = top_cluster(part, names)
    if c is not None:
        names[c] = role
totals = {}
for (c, _), a in area_by.items():
    totals[c] = totals.get(c, 0) + a
n_extra = 0
for c in sorted(totals, key=lambda c: -totals[c]):
    if c not in names and len(names) < MAX_ZONES:
        n_extra += 1
        names[c] = f"Cloth{n_extra}"
# Small leftover zones join the nearest kept colour.
for c in totals:
    if c not in names:
        near = min(names, key=lambda k: np.linalg.norm(centers[k] - centers[c]))
        for lab in cloth_labels.values():
            lab[lab == c] = near


def make_mat(name, srgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    lin = srgb_to_linear(srgb)
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*lin, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.85
    return m


colors = spec.get("colors", {})
DEFAULT_HAIR = (0.78, 0.77, 0.74) if spec.get("elder") else (0.22, 0.14, 0.08)

# Face roles: Skin, Hair (with brows), Eyes, or a garment zone (Main, Top, Trousers, Boots, Hood, ClothN).
roles = {}
for o, kinds, cols, _ in records:
    want = []
    for i in range(len(kinds)):
        k = kinds[i]
        want.append("Skin" if k == "skin" else "Hair" if k == "hair" else "Eyes" if k == "eyes" else names[cloth_labels[o.name][i]])
    roles[o.name] = want

# Mean linear luminance of each role's painted texture (to normalise the dyed and recoloured zones).
role_lum, role_area = {}, {}
for o, kinds, cols, areas in records:
    lum = luminance(srgb_to_linear(np.clip(cols, 0, 1)))
    for i, r in enumerate(roles[o.name]):
        role_lum[r] = role_lum.get(r, 0.0) + lum[i] * areas[i]
        role_area[r] = role_area.get(r, 0.0) + areas[i]
role_mean = {r: max(0.02, role_lum[r] / max(1e-9, role_area[r])) for r in role_lum}
skin_cols = np.concatenate([srgb_to_linear(np.clip(c[k == "skin"], 0, 1)) for _, k, c, _ in records if (k == "skin").any()])
skin_area = np.concatenate([a[k == "skin"] for _, k, _, a in records if (k == "skin").any()])
SKIN_MEAN = (skin_cols * skin_area[:, None]).sum(0) / max(1e-9, skin_area.sum()) if len(skin_cols) else np.array([0.5, 0.35, 0.25])


DYE = spec.get("dye", True)


def dyed(role):
    """Zones the game colours per person: baked as neutral grey detail. Without "dye" (the ranger
    leathers: straps and plates cross the garment, so per-face zones would show their jagged edges)
    the clothes keep the pack's own colours and only the hair is dyed."""
    return role == "Hair" or (DYE and role == "Main")


def low_zone(role):
    """Submesh (material) of a role on the game mesh."""
    return role if role in ("Skin", "Hair", "Main") else "Rest"


# MARK: - Full-detail copies for baking (original textures, zone colours applied)

variants = {}


def principled(mat):
    return next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)


def variant(mat, role):
    """The part's own material, with its base colour turned into grey detail (dyed zones) or
    into the spec colour with the texture's light and shade (recoloured zones)."""
    colour = colors.get(role) if DYE and not dyed(role) and role in colors else None
    mode = "neutral" if dyed(role) else "recolour" if colour is not None else "as"
    key = (mat.name if mat else "-", mode, role)
    if key in variants:
        return variants[key]
    m = mat.copy() if mat else bpy.data.materials.new("Plain")
    m.use_nodes = True
    m.name = f"bake_{role}_{mode}"
    nt = m.node_tree
    bsdf = principled(m)
    if mode == "as" and role.startswith("Cloth") and bsdf is not None and bsdf.inputs["Base Color"].is_linked:
        # Undyed cloth: no bleached whites in the valley, linen and wool are greige.
        inp = bsdf.inputs["Base Color"]
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs[0].default_value = 1.0
        nt.links.new(inp.links[0].from_socket, mix.inputs[6])
        mix.inputs[7].default_value = (0.72, 0.66, 0.54, 1)
        nt.links.new(mix.outputs[2], inp)
    if mode != "as" and bsdf is not None:
        inp = bsdf.inputs["Base Color"]
        bw = nt.nodes.new("ShaderNodeRGBToBW")
        if inp.is_linked:
            nt.links.new(inp.links[0].from_socket, bw.inputs[0])
        else:
            bw.inputs[0].default_value = inp.default_value
        mul = nt.nodes.new("ShaderNodeMath")
        mul.operation = "MULTIPLY"
        mul.inputs[1].default_value = (NEUTRAL if mode == "neutral" else 1.0) / role_mean.get(role, 0.3)
        nt.links.new(bw.outputs[0], mul.inputs[0])
        out = mul.outputs[0]
        if mode == "recolour":
            mix = nt.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.inputs[0].default_value = 1.0
            nt.links.new(mul.outputs[0], mix.inputs[6])
            mix.inputs[7].default_value = (*srgb_to_linear(colour), 1)
            out = mix.outputs[2]
        nt.links.new(out, inp)
    variants[key] = m
    return m


high = []
for o, _ in parts:
    hi = o.copy()
    hi.data = o.data.copy()
    hi.name = o.name + "_high"
    scene.collection.objects.link(hi)
    me = hi.data
    src_mats = list(me.materials)
    want = roles[o.name]
    mats, idx = [], []
    for i, p in enumerate(me.polygons):
        m = variant(src_mats[p.material_index] if src_mats else None, want[i])
        if m not in mats:
            mats.append(m)
        idx.append(mats.index(m))
    me.materials.clear()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", idx)
    for p in me.polygons:
        p.use_smooth = True
    me.update()
    # One copy per game zone: each zone of the game mesh is baked only from the same zone's
    # detail (the buzzed hair lies a few millimetres over the scalp, the beard over the chin).
    zones_here = sorted({low_zone(r) for r in want})
    for z in zones_here:
        zc = hi if z == zones_here[-1] else hi.copy()
        if zc is not hi:
            zc.data = hi.data.copy()
            scene.collection.objects.link(zc)
        zc.name = f"{o.name}_high_{z}"
        zc["zone"] = z
        bm = bmesh.new()
        bm.from_mesh(zc.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if low_zone(want[f.index]) != z], context="FACES")
        bm.to_mesh(zc.data)
        bm.free()
        high.append(zc)

# MARK: - Game materials (one submesh per zone; all share the person's texture set)

materials = {"Skin": make_mat("Skin", (0.80, 0.60, 0.47)),
             "Hair": make_mat("Hair", colors.get("Hair", DEFAULT_HAIR)),
             "Main": make_mat("Main", colors.get("Main", (0.45, 0.30, 0.2))),
             "Rest": make_mat("Rest", (0.5, 0.45, 0.4))}
for o, kinds, cols, _ in records:
    me = o.data
    want = [low_zone(r) for r in roles[o.name]]
    me.materials.clear()
    slots = sorted(set(want))
    for s in slots:
        me.materials.append(materials[s])
    idx = {s: i for i, s in enumerate(slots)}
    me.polygons.foreach_set("material_index", [idx[w] for w in want])
    me.update()

# MARK: - Decimation to the triangle budget


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


weight = {"head": 1.6, "eyes": 0.35, "brows": 0.35, "hair": 0.8, "cloth": 1.0}
counts = {o.name: tri_count(o) for o, _ in parts}
print("PARTS", [(o.name, k, counts[o.name], tuple(round(d, 2) for d in o.dimensions)) for o, k in parts])
budget = TARGET_TRIS - (1100 if spec.get("skirt") else 0)
wsum = sum(counts[o.name] * weight[k] for o, k in parts)
scale = budget / max(1, wsum)
for o, kind in parts:
    ratio = min(1.0, scale * weight[kind])
    if kind in ("eyes", "brows"):
        ratio = min(ratio, 0.35)
    if ratio < 0.999:
        bpy.context.view_layer.objects.active = o
        d = o.modifiers.new("Decimate", "DECIMATE")
        d.ratio = ratio
        while o.modifiers[0].name != "Decimate":
            bpy.ops.object.modifier_move_up(modifier="Decimate")
        bpy.ops.object.modifier_apply(modifier="Decimate")

# Join everything into one skinned mesh (one renderer, one submesh per material).
bpy.ops.object.select_all(action="DESELECT")
body = parts[0][0]
for o, _ in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
body.name = body.data.name = "Body"
for m in [m for m in body.modifiers if m.type == "ARMATURE"][1:]:
    body.modifiers.remove(m)
body.modifiers[0].object = rig
for p in body.data.polygons:
    p.use_smooth = True
# Bone weights: at most 4 per vertex (what mobile skinning uses), normalised.
bpy.ops.object.vertex_group_limit_total(limit=4)
bpy.ops.object.vertex_group_normalize_all(lock_active=False)
bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=0.01)

mesh = body.data
mw = body.matrix_world


def bone_world(name):
    return rig.matrix_world @ rig.data.bones[name].matrix_local


# MARK: - Skirt


def wool_material():
    """Woven wool for the skirt's full-detail copy: grey (the skirt is dyed in the game), a fine
    twill weave and soft vertical folds in the bump."""
    m = bpy.data.materials.new("bake_Main_wool")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = principled(m)
    coord = nt.nodes.new("ShaderNodeTexCoord")
    weave = nt.nodes.new("ShaderNodeTexWave")
    weave.wave_type = "BANDS"
    weave.bands_direction = "DIAGONAL"
    weave.inputs["Scale"].default_value = 260.0
    weave.inputs["Distortion"].default_value = 1.5
    folds = nt.nodes.new("ShaderNodeTexWave")
    folds.wave_type = "BANDS"
    folds.bands_direction = "X"
    folds.inputs["Scale"].default_value = 3.5
    folds.inputs["Distortion"].default_value = 4.0
    folds.inputs["Detail"].default_value = 1.0
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 40.0
    for t in (weave, folds, noise):
        nt.links.new(coord.outputs["Object"], t.inputs["Vector"])
    # Albedo: grey around NEUTRAL with mottled fibres and faint weave.
    mix = nt.nodes.new("ShaderNodeMath")
    mix.operation = "MULTIPLY_ADD"
    nt.links.new(noise.outputs["Fac"], mix.inputs[0])
    mix.inputs[1].default_value = 0.22
    mix.inputs[2].default_value = NEUTRAL - 0.11
    wv = nt.nodes.new("ShaderNodeMath")
    wv.operation = "MULTIPLY_ADD"
    nt.links.new(weave.outputs["Fac"], wv.inputs[0])
    wv.inputs[1].default_value = 0.08
    nt.links.new(mix.outputs[0], wv.inputs[2])
    sub = nt.nodes.new("ShaderNodeMath")
    sub.operation = "SUBTRACT"
    nt.links.new(wv.outputs[0], sub.inputs[0])
    sub.inputs[1].default_value = 0.04
    nt.links.new(sub.outputs[0], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.92
    height = nt.nodes.new("ShaderNodeMath")
    height.operation = "MULTIPLY_ADD"
    nt.links.new(folds.outputs["Fac"], height.inputs[0])
    height.inputs[1].default_value = 2.5
    nt.links.new(weave.outputs["Fac"], height.inputs[2])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.35
    bump.inputs["Distance"].default_value = 0.002
    nt.links.new(height.outputs[0], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return m


def add_skirt():
    """A long skirt from the waist to the shins: rings sized to the legs inside, flaring at the hem,
    skinned to the pelvis at the top and blending into the thighs below."""
    pelvis = bone_world("pelvis").translation
    calf_z = bone_world("calf_l").translation.z
    z_top = pelvis.z + 0.08
    z_hem = 0.2 if spec.get("elder") else max(0.24, calf_z - 0.24)
    rings, segs = 11, 32
    co = np.array([mw @ v.co for v in mesh.vertices])
    gidx = {g.index: g.name for g in body.vertex_groups}
    lower = np.array([any(gidx[g.group].startswith(("thigh", "calf", "pelvis", "spine_01")) and g.weight > 0.3 for g in v.groups) for v in mesh.vertices])
    cy = pelvis.y
    radii = []
    for r in range(rings):
        t = r / (rings - 1)
        z = z_top + (z_hem - z_top) * t
        band = co[(np.abs(co[:, 2] - z) < (0.015 if r == 0 else 0.04)) & lower]
        rr = []
        for s in range(segs):
            a = 2 * math.pi * s / segs
            d = Vector((math.sin(a), -math.cos(a)))  # s = 0 points forward (-Y)
            if len(band):
                proj = (band[:, 0] * d.x + (band[:, 1] - cy) * d.y)
                ext = proj.max()
            else:
                ext = 0.15
            rr.append(ext)
        rr = np.array(rr)
        rr = np.maximum(rr, np.roll(rr, 1) * 0.92)
        rr = np.maximum(rr, np.roll(rr, -1) * 0.92)
        if r == 0:
            rr = rr * 0.97  # tucked under the bodice
        else:
            rr = rr + 0.012 + 0.07 * t ** 1.4
        if radii:
            rr = np.maximum(rr, radii[-1] + 0.004)  # keep widening toward the hem
        radii.append(rr)
    radii = np.array(radii)
    # Ellipse fit per ring: smooth the outline (no dents between the legs), soft folds toward the hem.
    verts, faces = [], []
    for r in range(rings):
        t = r / (rings - 1)
        z = z_top + (z_hem - z_top) * t
        rx = max(radii[r][segs // 4], radii[r][3 * segs // 4])
        ry_f, ry_b = radii[r][0], radii[r][segs // 2]
        for s in range(segs):
            a = 2 * math.pi * s / segs
            ry = ry_f if math.cos(a) > 0 else ry_b
            sx, sy = math.sin(a), -math.cos(a)
            rad = max(radii[r][s], 1.0 / math.sqrt((sx / rx) ** 2 + (sy / ry) ** 2))
            rad *= 1.0 + 0.035 * t ** 1.5 * math.sin(a * 8 + 0.6)
            verts.append((sx * rad, cy + sy * rad, z))
    for r in range(rings - 1):
        for s in range(segs):
            a, b = r * segs + s, r * segs + (s + 1) % segs
            faces.append((a, a + segs, b + segs, b))  # outward-facing
    # Inner layer (the inside of the hem is visible when legs swing).
    n = len(verts)
    for (x, y, z) in list(verts):
        d = Vector((x, y - cy, 0))
        k = max(0.0, d.length - 0.006) / max(1e-6, d.length)
        verts.append((x * k, cy + (y - cy) * k, z))
    faces += [tuple(i + n for i in reversed(f)) for f in faces]
    sm = bpy.data.meshes.new("Skirt")
    sm.from_pydata(verts, [], faces)
    sm.update()
    so = bpy.data.objects.new("Skirt", sm)
    scene.collection.objects.link(so)
    so.parent = rig
    # Cylinder UVs (outer and inner layer side by side), on the body's UV map so they join it.
    uv = sm.uv_layers.new(name=mesh.uv_layers.active.name if mesh.uv_layers.active else "UVMap")
    for p in sm.polygons:
        ss = [(vi % n) % segs for vi in p.vertices]
        for li, vi in zip(p.loop_indices, p.vertices):
            s_, r_ = (vi % n) % segs, (vi % n) // segs
            if s_ == 0 and max(ss) == segs - 1:
                s_ = segs
            u = s_ / segs * 2 * math.pi * 0.3
            uv.data[li].uv = (u + (2.2 if vi >= n else 0.0), 1.0 - r_ / (rings - 1) * (z_top - z_hem))
    sm.materials.append(materials["Main"])
    for p in sm.polygons:
        p.use_smooth = True
    gp = so.vertex_groups.new(name="pelvis")
    gl = so.vertex_groups.new(name="thigh_l")
    gr = so.vertex_groups.new(name="thigh_r")
    gs = so.vertex_groups.new(name="spine_01")
    cl, cr = so.vertex_groups.new(name="calf_l"), so.vertex_groups.new(name="calf_r")
    for i, (x, y, z) in enumerate(verts):
        t = (z_top - z) / (z_top - z_hem)
        # The front follows the thighs closely, the back hangs from the pelvis (sitting, kneeling).
        fwd = max(0.0, min(1.0, -(y - cy) / 0.12))
        leg = min(1.0, max(0.0, (t - 0.12) / 0.7)) * (0.45 + 0.45 * fwd)
        side = 0.5 + 0.5 * max(-1.0, min(1.0, x / 0.12))  # 1 = left (+X)
        top = 1.0 - leg
        if t < 0.15:
            gs.add([i], top * (0.15 - t) / 0.15 * 0.5, "REPLACE")
            top *= 1 - (0.15 - t) / 0.15 * 0.5
        gp.add([i], top, "REPLACE")
        if leg > 0:
            # The front of the hem follows the shins, so it drapes down over sitting knees.
            calf = leg * fwd * 0.8 * max(0.0, min(1.0, (t - 0.35) / 0.5))
            thigh = leg - calf
            gl.add([i], thigh * side, "REPLACE")
            gr.add([i], thigh * (1 - side), "REPLACE")
            if calf > 0:
                cl.add([i], calf * side, "REPLACE")
                cr.add([i], calf * (1 - side), "REPLACE")
    mod = so.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    # The full-detail copy for baking carries the woven wool.
    hs = so.copy()
    hs.data = so.data.copy()
    hs.name = "Skirt_high"
    scene.collection.objects.link(hs)
    hs.data.materials.clear()
    hs.data.materials.append(wool_material())
    hs["zone"] = "Main"
    high.append(hs)
    bpy.ops.object.select_all(action="DESELECT")
    so.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()


if spec.get("skirt"):
    add_skirt()

# MARK: - Texture bake (albedo, normal, ambient occlusion, smoothness)


def uv_islands(bm, uv_layer):
    """Face islands of the UV map (faces joined by edges whose UVs match on both sides)."""
    parent = list(range(len(bm.faces)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for e in bm.edges:
        lf = e.link_faces
        if len(lf) != 2:
            continue
        f1, f2 = lf
        def uv_of(f, v):
            return next(l[uv_layer].uv for l in f.loops if l.vert == v)
        if all((uv_of(f1, v) - uv_of(f2, v)).length < 1e-5 for v in e.verts):
            parent[find(f1.index)] = find(f2.index)
    groups = {}
    for f in bm.faces:
        groups.setdefault(find(f.index), []).append(f)
    return list(groups.values())


def atlas_uv():
    """One UV map for the whole person: the artists' own UV islands of every part (and the skirt's
    cylinder), scaled to an even texel density with the face and hair given more, packed together."""
    me = body.data
    keep = me.uv_layers.active.name
    for name in [l.name for l in me.uv_layers if l.name != keep]:
        me.uv_layers.remove(me.uv_layers[name])
    me.uv_layers[keep].name = "Atlas"
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    uvl = bm.loops.layers.uv.active
    head_z = (rig.matrix_world @ rig.data.bones["neck_01"].head_local).z
    zone_of = [me.materials[p.material_index].name for p in me.polygons]
    for isl in uv_islands(bm, uvl):
        zs = [(mw @ f.calc_center_median()).z for f in isl]
        is_head = np.mean(zs) > head_z - 0.03 and any(zone_of[f.index] in ("Skin", "Hair", "Rest") for f in isl)
        k = 2.0 if is_head and any(zone_of[f.index] == "Skin" for f in isl) else 1.4 if is_head else 1.0
        if k == 1.0:
            continue
        loops = [l for f in isl for l in f.loops]
        c = sum((l[uvl].uv for l in loops), Vector((0, 0))) / len(loops)
        for l in loops:
            l[uvl].uv = c + (l[uvl].uv - c) * k
    bm.to_mesh(me)
    bm.free()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake(kind, image, zone=None, **kw):
    """Bakes into `image`: with a zone, only that zone's faces of the game mesh, from that zone's
    full-detail copies; without one, the game mesh alone (ambient occlusion)."""
    dummy = bpy.data.images.get("bake_dummy") or bpy.data.images.new("bake_dummy", 8, 8)
    for m in body.data.materials:
        node = m.node_tree.nodes.get("BakeTarget")
        node.image = image if zone is None or m.name == zone else dummy
        m.node_tree.nodes.active = node
    bpy.ops.object.select_all(action="DESELECT")
    for h in high:
        on = zone is not None and h.get("zone") == zone
        h.hide_render = not on
        h.select_set(on)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    b = scene.render.bake
    b.use_selected_to_active = zone is not None
    b.cage_extrusion = 0.025
    b.max_ray_distance = 0.1
    b.margin = 0
    b.use_clear = True
    bpy.ops.object.bake(type=kind, **kw)


def dilate(a, filled, steps):
    """Grows the baked islands outwards (texture padding, so mipmaps do not bleed the background in)."""
    a, filled = a.copy(), filled.copy()
    for _ in range(steps):
        acc = np.zeros_like(a)
        cnt = np.zeros(filled.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sh_f = np.roll(filled, (dy, dx), axis=(0, 1))
            acc += np.roll(a, (dy, dx), axis=(0, 1)) * sh_f[..., None]
            cnt += sh_f
        grow = (~filled) & (cnt > 0)
        a[grow] = acc[grow] / cnt[grow][:, None]
        filled = filled | grow
    return a


def read(image):
    w, h = image.size
    px = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(px)
    return px.reshape(h, w, 4)


def shrink(a, f):
    h, w, c = a.shape
    return a.reshape(h // f, f, w // f, f, c).mean(axis=(1, 3))


def write_map(path, a):
    """Saves a baked map (rows bottom to top, values as stored) for build_characters.py to write as PNG."""
    np.save(path, np.clip(a, 0, 1).astype(np.float32))


def bake_textures():
    atlas_uv()
    size = TEX_SIZE * 2
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.use_denoising = False
    world = bpy.data.worlds.new("bake")
    scene.world = world
    world.light_settings.distance = 0.12
    imgs = {}
    for name, nc in (("albedo", False), ("normal", True), ("rough", True), ("ao", True)):
        im = bpy.data.images.new("bake_" + name, size, size, alpha=False)
        if nc:
            im.colorspace_settings.name = "Non-Color"
        imgs[name] = im
    for m in body.data.materials:
        n = m.node_tree.nodes.new("ShaderNodeTexImage")
        n.name = "BakeTarget"
    # Which zone every texel belongs to: an emission bake of the game mesh with the zone's index.
    zone_names = [m.name for m in body.data.materials]
    for zi, m in enumerate(body.data.materials):
        bsdf = principled(m)
        bsdf.inputs["Emission Color"].default_value = ((zi + 1) / 8.0, 0, 0, 1)
        bsdf.inputs["Emission Strength"].default_value = 1.0
    idimg = bpy.data.images.new("bake_ids", size, size, alpha=False, float_buffer=True)
    scene.cycles.samples = 1
    for m in body.data.materials:
        node = m.node_tree.nodes["BakeTarget"]
        node.image = idimg
        m.node_tree.nodes.active = node
    bake("EMIT", idimg)
    ids = np.round(read(idimg)[..., 0] * 8.0).astype(int) - 1  # -1: no face
    for m in body.data.materials:
        principled(m).inputs["Emission Strength"].default_value = 0.0
    maps = {}
    scene.cycles.samples = 4
    for kind, name, kw in (("DIFFUSE", "albedo", {"pass_filter": {"COLOR"}}), ("NORMAL", "normal", {"normal_space": "TANGENT"}),
                           ("ROUGHNESS", "rough", {})):
        out = np.zeros((size, size, 4), dtype=np.float32)
        for zi, z in enumerate(zone_names):
            if not any(h.get("zone") == z for h in high):
                continue
            bake(kind, imgs[name], zone=z, **kw)
            sel = ids == zi
            out[sel] = read(imgs[name])[sel]
        maps[name] = dilate(out, ids >= 0, 24)
    scene.cycles.samples = 48
    bake("AO", imgs["ao"])
    maps["ao"] = dilate(read(imgs["ao"]), ids >= 0, 24)
    albedo = shrink(maps["albedo"], 2)
    normal = shrink(maps["normal"], 2)
    rough = shrink(maps["rough"], 4)
    ao = shrink(maps["ao"], 4)
    albedo[..., 3] = 1
    normal[..., 3] = 1
    # Mask as URP Lit reads it: R metallic, G occlusion, B detail mask, A smoothness.
    mask = np.zeros_like(rough)
    mask[..., 1] = 0.35 + 0.65 * ao[..., 0]
    mask[..., 3] = np.clip(1.0 - rough[..., 0], 0.0, 0.75)
    write_map(TEX + "_albedo.npy", albedo[..., :3])
    write_map(TEX + "_normal.npy", normal[..., :3])
    write_map(TEX + "_mask.npy", mask)
    for m in body.data.materials:
        m.node_tree.nodes.remove(m.node_tree.nodes["BakeTarget"])
    for h in high:
        bpy.data.objects.remove(h, do_unlink=True)
    high.clear()


if TEX:
    bake_textures()
else:
    for h in high:
        bpy.data.objects.remove(h, do_unlink=True)
    high.clear()

# MARK: - Rest pose changes: elder stoop, finger grip


def apply_pose_as_rest():
    """Bakes the rig's current pose into the mesh and the rest pose."""
    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.objects.active = body
    keep = next(m for m in body.modifiers if m.type == "ARMATURE").name
    bpy.ops.object.modifier_copy(modifier=keep)
    bpy.ops.object.modifier_apply(modifier=keep)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def stoop():
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for name, deg in (("spine_01", 5), ("spine_02", 6), ("spine_03", 6), ("neck_01", -9), ("Head", -5)):
        pb = rig.pose.bones[name]
        bpy.context.view_layer.update()
        head = pb.head.copy()
        rot = Matrix.Rotation(math.radians(deg), 4, "X")
        pb.matrix = Matrix.Translation(head) @ rot @ Matrix.Translation(-head) @ pb.matrix
    apply_pose_as_rest()


if spec.get("elder"):
    stoop()

# MARK: - Retarget the clips of Villager.fbx (and the extra clips)

rig.animation_data_create()


class Source:
    """One animation source armature and its per-bone rest rotation differences to the rig."""

    def __init__(self, arm):
        self.arm = arm
        self.D = {}
        self.order = []

    def bone(self, n):
        return self.arm.matrix_world @ self.arm.data.bones[n].matrix_local

    def setup(self):
        """Per bone: D = Ss^-1 St (rest world rotations), so target world rotation = source world rotation · D."""
        self.D.clear()
        self.order.clear()
        for b in rig.data.bones:
            if b.name in self.arm.data.bones:
                Ss = self.bone(b.name).to_3x3().normalized()
                St = (rig.matrix_world @ b.matrix_local).to_3x3().normalized()
                self.D[b.name] = Ss.inverted() @ St

        def visit(b):
            self.order.append(b.name)
            for c in b.children:
                visit(c)

        for b in rig.data.bones:
            if b.parent is None:
                visit(b)
        src_leg = (self.bone("thigh_l").translation - self.bone("foot_l").translation).length
        tgt_leg = (bone_world("thigh_l").translation - bone_world("foot_l").translation).length
        self.leg_scale = tgt_leg / src_leg
        self.pelvis_rest = self.bone("pelvis").translation
        self.tgt_pelvis_rest = bone_world("pelvis").translation

    def frame(self):
        world, basis = {}, {}
        for name in self.order:
            bone = rig.data.bones[name]
            parent_world = world[bone.parent.name] if bone.parent else rig.matrix_world
            rest_local = (bone.parent.matrix_local.inverted() if bone.parent else Matrix()) @ bone.matrix_local
            default_world = parent_world @ rest_local
            if name not in self.D:
                world[name] = default_world
                basis[name] = Matrix()
                continue
            Ts = (self.arm.matrix_world @ self.arm.pose.bones[name].matrix).to_3x3().normalized()
            rot = (Ts @ self.D[name]).to_4x4()
            if name == "pelvis":
                sp = (self.arm.matrix_world @ self.arm.pose.bones["pelvis"].matrix).translation
                loc = self.tgt_pelvis_rest + (sp - self.pelvis_rest) * self.leg_scale
            else:
                loc = default_world.translation
            w = Matrix.Translation(loc) @ rot
            world[name] = w
            basis[name] = rest_local.inverted() @ parent_world.inverted() @ w
        return basis


main_src = Source(src)

# Fingers: every bone costs nine animation curves per clip in the FBX, so the 30 finger bones
# would double the file. Instead the right hand keeps the grip of the chopping clip and the left
# hand the relaxed curl of the idle clip, baked into the mesh, and the finger bones go.
FINGERS = ("index_", "middle_", "ring_", "pinky_", "thumb_")
main_src.setup()
grip = {}
for clip, side in (("Chop", "_r"), ("Idle", "_l")):
    act = next(a for a in src_actions if a.name.split("|")[-1] == clip)
    src.animation_data.action = act
    scene.frame_set(int(sum(act.frame_range) / 2))
    grip.update({n: m for n, m in main_src.frame().items() if n.startswith(FINGERS) and n.endswith(side)})
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="POSE")
for n, m in grip.items():
    pb = rig.pose.bones[n]
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = m.decompose()[1]
apply_pose_as_rest()
groups = {g.name: g for g in body.vertex_groups}
for side in ("l", "r"):
    hand = groups[f"hand_{side}"]
    fingers = [g.index for n, g in groups.items() if n.startswith(FINGERS) and n.endswith("_" + side)]
    for v in mesh.vertices:
        w = sum(g.weight for g in v.groups if g.group in fingers)
        if w > 0:
            hand.add([v.index], w, "ADD")
for n, g in list(groups.items()):
    if n.startswith(FINGERS) or "leaf" in n:
        body.vertex_groups.remove(g)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
for eb in [eb for eb in rig.data.edit_bones if eb.name.startswith(FINGERS) or "leaf" in eb.name]:
    rig.data.edit_bones.remove(eb)
bpy.ops.object.mode_set(mode="OBJECT")
main_src.setup()

jobs = [(main_src, act, act.name.split("|")[-1], None) for act in src_actions]
for arm, act, clip, _ in extra_sources:
    s = Source(arm)
    s.setup()
    jobs.append((s, act, clip, act["clip_end"]))

new_actions = []
for source, act, clip, end in jobs:
    source.arm.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    if end is not None:
        f1 = max(f0 + 1, int(end))
    out = bpy.data.actions.new(clip)
    out.id_root = "OBJECT"
    out.use_fake_user = True
    rig.animation_data.action = out
    # A held pose (end given): the first frame, keyed at both ends so the clip loops still.
    frames = range(f0, f1 + 1) if end is None else [f0]
    for f in frames:
        scene.frame_set(f)
        for name, m in source.frame().items():
            if name not in source.D:
                continue
            pb = rig.pose.bones[name]
            pb.rotation_mode = "QUATERNION"
            loc, rot, _ = m.decompose()
            pb.rotation_quaternion = rot
            keys = [f] if end is None else [f0, f1]
            for k in keys:
                pb.keyframe_insert("rotation_quaternion", frame=k, group=name)
                if name == "pelvis":
                    pb.location = loc
                    pb.keyframe_insert("location", frame=k, group=name)
    new_actions.append(out)

for o in src_objs:
    bpy.data.objects.remove(o, do_unlink=True)
for arm, _, _, objs in extra_sources:
    for o in objs:
        if o.name in bpy.data.objects:
            bpy.data.objects.remove(o, do_unlink=True)
for a in list(keep_actions):
    bpy.data.actions.remove(a)

# MARK: - Tools in the right hand (as in blender_people.py)
# Hand bone space: +Y runs along the fingers, +Z points to the thumb side; the handle runs along Z.
wood, stone = make_mat("Wood", (0.45, 0.32, 0.2)), make_mat("Stone", (0.42, 0.42, 0.4))


def part(kind, size, loc, m):
    if kind == "cyl":
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=size[0], depth=size[1], location=loc)
    else:
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        bpy.context.object.scale = size
    o = bpy.context.object
    o.data.materials.append(m)
    return o


def prop(name, pieces):
    bpy.ops.object.select_all(action="DESELECT")
    objs = [part(*p) for p in pieces]
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    o = bpy.context.object
    o.name = o.data.name = name
    o.parent = rig
    o.parent_type = "BONE"
    o.parent_bone = "hand_r"
    o.matrix_parent_inverse = Matrix.Identity(4)
    o.location = (-0.02, 0.025, 0.0)  # relative to the bone tail
    return o


props = [
    prop("Prop_Axe", [("cyl", (0.016, 0.62), (0, 0, 0.21), wood), ("cube", (0.03, 0.15, 0.08), (0, 0.06, 0.47), stone)]),
    prop("Prop_Pickaxe", [("cyl", (0.016, 0.64), (0, 0, 0.22), wood), ("cube", (0.035, 0.36, 0.05), (0, 0.0, 0.5), stone)]),
    prop("Prop_Hammer", [("cyl", (0.015, 0.36), (0, 0, 0.1), wood), ("cube", (0.07, 0.12, 0.07), (0, 0.02, 0.28), stone)]),
    prop("Prop_Spear", [("cyl", (0.013, 1.7), (0, 0, 0.35), wood), ("cube", (0.012, 0.035, 0.12), (0, 0, 1.25), stone)]),
]

# MARK: - LOD1: the same person with about a third of the triangles, for the distant view

lod = None
if TEX and LOD_TRIS:
    lod = body.copy()
    lod.data = body.data.copy()
    lod.name = lod.data.name = "Body_LOD1"
    scene.collection.objects.link(lod)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = lod
    d = lod.modifiers.new("Decimate", "DECIMATE")
    d.ratio = min(1.0, LOD_TRIS / max(1, tri_count(body)))
    d.use_collapse_triangulate = True
    while lod.modifiers[0].name != "Decimate":
        bpy.ops.object.modifier_move_up(modifier="Decimate")
    bpy.ops.object.modifier_apply(modifier="Decimate")

# MARK: - Export
# The person's texture set is mapped by the Atlas UVs; tools take tiled wood and stone surfaces.
for o in props:
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=0.5)
    bpy.ops.object.mode_set(mode="OBJECT")
for o in [body] + ([lod] if lod else []) + props:
    if not TEX and o in (body, lod):
        while o.data.uv_layers:
            o.data.uv_layers.remove(o.data.uv_layers[0])
    while o.data.color_attributes:
        o.data.color_attributes.remove(o.data.color_attributes[0])

bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
body.select_set(True)
if lod:
    lod.select_set(True)
for p in props:
    p.select_set(True)
bpy.context.view_layer.objects.active = rig
rig.animation_data.action = bpy.data.actions.get("Idle")
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=spec.get("simplify", 1.0),
    mesh_smooth_type="FACE", use_armature_deform_only=True, use_tspace=False,
)
mat_names = [m.name for m in body.data.materials]
print("CHARACTER_OK", json.dumps({"out": OUT, "tris": tri_count(body), "lod_tris": tri_count(lod) if lod else 0,
                                  "materials": mat_names, "clips": sorted(a.name for a in new_actions),
                                  "height": round(body.dimensions.z, 3),
                                  "skin": [round(float(c), 4) for c in SKIN_MEAN],
                                  "main": list(colors.get("Main", (0.45, 0.30, 0.2))),
                                  "hair": list(colors.get("Hair", DEFAULT_HAIR)),
                                  "neutral": NEUTRAL, "dye": DYE}))
