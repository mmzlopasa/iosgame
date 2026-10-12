"""Blender (4.2): assembles one villager from the CC0 Quaternius "Universal Base Characters" and
"Modular Character Outfits - Fantasy" packs and gives it every work animation of the game.

- head, eyes and eyebrows come from a base character (realistic proportions), the clothes from
  the outfit pack (peasant shirt and trousers, ranger jerkin and hood, boots), the hair from the
  base pack's hairstyles. Every pack uses the same Unreal-style skeleton, so the parts are simply
  re-parented to one armature.
- textures are not used by the game: each texture is turned into a few flat-colour material
  zones (k-means over per-face texture samples, cleaned with a neighbour vote). The main garment
  is called "Main" (the game tints it per person); skin "Skin", hair/beard/eyebrows "Hair".
- women can get a long skirt (a skinned cone over the trousers, following pelvis and thighs);
  elders get a slight stoop (applied to the rest pose, so every clip keeps it)
- every mesh is welded, smooth shaded and decimated to fit about 7k triangles in all
- the animations of Villager.fbx (Universal Animation Library 1 + 2, same skeleton) are
  retargeted bone by bone: each bone takes the source bone's world rotation change from rest, the
  pelvis follows the source pelvis scaled to the leg length
- hand tools (Prop_Axe, Prop_Pickaxe, Prop_Hammer, Prop_Spear) are parented to hand_r exactly as
  in Villager.fbx (blender_people.py)

blender -b --factory-startup -P blender_characters.py -- <Villager.fbx> <spec.json> <out.fbx> [--preview <png prefix>]
spec.json: {"base": gltf, "parts": [[gltf, object name prefix], ...], "hair": [gltf, ...],
            "skirt": bool, "elder": bool, "hood": bool, "colors": {"Main": [r,g,b] (sRGB), ...},
            "tris": target triangle count}
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
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
spec = json.load(open(SPEC))
TARGET_TRIS = spec.get("tris", 7000)

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


# MARK: - Animation source

src_objs, src_actions = import_any(SRC)
src = next(o for o in src_objs if o.type == "ARMATURE")
for o in src_objs:
    if o.type == "MESH":
        o.hide_render = o.hide_viewport = True
if src.animation_data is None:
    src.animation_data_create()

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
for a in [a for a in bpy.data.actions if a not in src_actions]:
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
        if im.size[0] > 1024:
            im.scale(1024, 1024)
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
MAX_ZONES = 5  # cloth materials per person (draw calls)
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
materials = {"Skin": make_mat("Skin", colors.get("Skin", (0.80, 0.60, 0.47))),
             "Hair": make_mat("Hair", colors.get("Hair", (0.78, 0.77, 0.74) if spec.get("elder") else (0.22, 0.14, 0.08)))}
for c, n in names.items():
    materials[n] = make_mat(n, colors.get(n, np.clip(centers[c], 0, 1)))

# Eyes: dark iris and pupil, white sclera.
eye_rec = [r for r in records if (r[1] == "eyes").any()]
if eye_rec:
    ex = np.concatenate([c[k == "eyes"] for _, k, c, _ in eye_rec])
    lum = ex.mean(1)
    eye_split = (np.percentile(lum, 30) + np.percentile(lum, 85)) / 2
    materials["Eyes"] = make_mat("Eyes", (0.16, 0.10, 0.07))
    materials["EyeWhite"] = make_mat("EyeWhite", (0.86, 0.84, 0.80))

# Assign the final materials per face.
for o, kinds, cols, _ in records:
    me = o.data
    want = []
    for i, p in enumerate(me.polygons):
        k = kinds[i]
        if k == "skin":
            want.append("Skin")
        elif k == "hair":
            want.append("Hair")
        elif k == "eyes":
            want.append("Eyes" if cols[i].mean() < eye_split else "EyeWhite")
        else:
            want.append(names[cloth_labels[o.name][i]])
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


weight = {"head": 1.5, "eyes": 0.35, "brows": 0.35, "hair": 0.8, "cloth": 1.0}
counts = {o.name: tri_count(o) for o, _ in parts}
print("PARTS", [(o.name, k, counts[o.name], tuple(round(d, 2) for d in o.dimensions)) for o, k in parts])
budget = TARGET_TRIS - (900 if spec.get("skirt") else 0)
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


def add_skirt():
    """A long skirt from the waist to the shins: rings sized to the legs inside, flaring at the hem,
    skinned to the pelvis at the top and blending into the thighs below."""
    pelvis = bone_world("pelvis").translation
    calf_z = bone_world("calf_l").translation.z
    z_top = pelvis.z + 0.08
    z_hem = 0.2 if spec.get("elder") else max(0.24, calf_z - 0.24)
    rings, segs = 9, 28
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
    # Ellipse fit per ring: smooth the outline (no dents between the legs).
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
            verts.append((sx * rad, cy + sy * rad, z))
    for r in range(rings - 1):
        for s in range(segs):
            a, b = r * segs + s, r * segs + (s + 1) % segs
            faces.append((a, b, b + segs, a + segs))
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
    sm.materials.append(materials.setdefault("Main", make_mat("Main", colors.get("Main", (0.45, 0.30, 0.2)))))
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
    bpy.ops.object.select_all(action="DESELECT")
    so.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()

if spec.get("skirt"):
    add_skirt()

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

# MARK: - Retarget the clips of Villager.fbx

rig.animation_data_create()


def leg_s(n):
    return src.matrix_world @ src.data.bones[n].matrix_local


src_leg = (leg_s("thigh_l").translation - leg_s("foot_l").translation).length
tgt_leg = (bone_world("thigh_l").translation - bone_world("foot_l").translation).length
leg_scale = tgt_leg / src_leg
src_pelvis_rest = leg_s("pelvis").translation
tgt_pelvis_rest = bone_world("pelvis").translation
D, order = {}, []


def setup_retarget():
    """Per bone: D = Ss^-1 St (rest world rotations), so target world rotation = source world rotation · D."""
    D.clear()
    order.clear()
    for b in rig.data.bones:
        if b.name in src.data.bones:
            Ss = (src.matrix_world @ src.data.bones[b.name].matrix_local).to_3x3().normalized()
            St = (rig.matrix_world @ b.matrix_local).to_3x3().normalized()
            D[b.name] = Ss.inverted() @ St

    def visit(b):
        order.append(b.name)
        for c in b.children:
            visit(c)

    for b in rig.data.bones:
        if b.parent is None:
            visit(b)


def retarget_frame():
    world, basis = {}, {}
    for name in order:
        bone = rig.data.bones[name]
        parent_world = world[bone.parent.name] if bone.parent else rig.matrix_world
        rest_local = (bone.parent.matrix_local.inverted() if bone.parent else Matrix()) @ bone.matrix_local
        default_world = parent_world @ rest_local
        if name not in D:
            world[name] = default_world
            basis[name] = Matrix()
            continue
        Ts = (src.matrix_world @ src.pose.bones[name].matrix).to_3x3().normalized()
        rot = (Ts @ D[name]).to_4x4()
        if name == "pelvis":
            sp = (src.matrix_world @ src.pose.bones["pelvis"].matrix).translation
            loc = tgt_pelvis_rest + (sp - src_pelvis_rest) * leg_scale
        else:
            loc = default_world.translation
        w = Matrix.Translation(loc) @ rot
        world[name] = w
        basis[name] = rest_local.inverted() @ parent_world.inverted() @ w
    return basis


# Fingers: every bone costs nine animation curves per clip in the FBX, so the 30 finger bones
# would double the file. Instead the right hand keeps the grip of the chopping clip and the left
# hand the relaxed curl of the idle clip, baked into the mesh, and the finger bones go.
FINGERS = ("index_", "middle_", "ring_", "pinky_", "thumb_")
setup_retarget()
grip = {}
for clip, side in (("Chop", "_r"), ("Idle", "_l")):
    act = next(a for a in src_actions if a.name.split("|")[-1] == clip)
    src.animation_data.action = act
    scene.frame_set(int(sum(act.frame_range) / 2))
    grip.update({n: m for n, m in retarget_frame().items() if n.startswith(FINGERS) and n.endswith(side)})
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
setup_retarget()

new_actions = []
for act in src_actions:
    clip = act.name.split("|")[-1]
    src.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    out = bpy.data.actions.new(clip)
    out.id_root = "OBJECT"
    out.use_fake_user = True
    rig.animation_data.action = out
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        for name, m in retarget_frame().items():
            if name not in D:
                continue
            pb = rig.pose.bones[name]
            pb.rotation_mode = "QUATERNION"
            loc, rot, _ = m.decompose()
            pb.rotation_quaternion = rot
            pb.keyframe_insert("rotation_quaternion", frame=f, group=name)
            if name == "pelvis":
                pb.location = loc
                pb.keyframe_insert("location", frame=f, group=name)
    new_actions.append(out)

for o in src_objs:
    bpy.data.objects.remove(o, do_unlink=True)
for a in src_actions:
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

# MARK: - Export
# Only flat colours are used in the game: drop UVs and colour attributes.
for o in [body] + props:
    while o.data.uv_layers:
        o.data.uv_layers.remove(o.data.uv_layers[0])
    while o.data.color_attributes:
        o.data.color_attributes.remove(o.data.color_attributes[0])

bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
body.select_set(True)
for p in props:
    p.select_set(True)
bpy.context.view_layer.objects.active = rig
rig.animation_data.action = bpy.data.actions.get("Idle")
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=spec.get("simplify", 1.0),
    mesh_smooth_type="FACE", use_armature_deform_only=True,
)
mat_names = [m.name for m in body.data.materials]
print("CHARACTER_OK", json.dumps({"out": OUT, "tris": tri_count(body), "materials": mat_names,
                                  "clips": sorted(a.name for a in new_actions),
                                  "height": round(body.dimensions.z, 3)}))

# MARK: - Preview: Walk and Chop (two frames each, axe in hand), Throw with the spear, front Idle

if PREVIEW:
    shots = [("Idle", None, 0.3, "front"), ("Walk", None, 0.1, "side"), ("Walk", None, 0.6, "side"),
             ("Chop", "Prop_Axe", 0.2, "side"), ("Chop", "Prop_Axe", 0.55, "side"), ("Throw", "Prop_Spear", 0.4, "side"),
             ("Sit", None, 0.5, "side"), ("Work", "Prop_Hammer", 0.3, "side")]
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 60
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.2
    sun.rotation_euler = (math.radians(45), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.6, 0.65, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    scene.world = world
    bpy.ops.mesh.primitive_plane_add(size=6)
    ground = bpy.context.object
    ground.data.materials.append(make_mat("Ground", (0.42, 0.45, 0.33)))
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 360, 480
    target = Vector((0, 0, 0.9))
    for i, (clip, tool, t, view) in enumerate(shots):
        act = bpy.data.actions.get(clip)
        if act is None:
            continue
        for p in props:
            p.hide_render = p.name != tool
        rig.animation_data.action = act
        f0, f1 = act.frame_range
        scene.frame_set(int(f0 + (f1 - f0) * t))
        off = Vector((0.6, -3.6, 0.9)) if view == "front" else Vector((2.6, -2.6, 1.0))
        cam.location = target + off
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{PREVIEW}_{i}_{clip}.png"
        bpy.ops.render.render(write_still=True)
