"""The villagers' chunky painted-figure proportions, for Blender previews.

Same rules and numbers as the game's import step (unity/Assets/Editor/PersonStyle.cs): the skin
moves around the joints it is bound to (head, hands and feet scaled about their joint; arms, legs,
neck and body thickened across the bone), so the skeleton and the clips stay as they are.
"""
from mathutils import Vector

HEAD, HAND, FOOT = 1.35, 1.25, 1.12
ARM, LEG, TORSO, NECK = 1.25, 1.2, 1.1, 1.1
CHILD = {"pelvis": "spine_01", "spine_01": "spine_02", "spine_02": "spine_03", "spine_03": "neck_01", "neck_01": "Head",
         "upperarm_l": "lowerarm_l", "lowerarm_l": "hand_l", "upperarm_r": "lowerarm_r", "lowerarm_r": "hand_r",
         "thigh_l": "calf_l", "calf_l": "foot_l", "thigh_r": "calf_r", "calf_r": "foot_r"}


def rule(name):
    if name == "Head":
        return True, HEAD
    if name.startswith("hand_"):
        return True, HAND
    if name.startswith(("foot_", "ball_")):
        return True, FOOT
    if name.startswith(("upperarm", "lowerarm")):
        return False, ARM
    if name.startswith(("thigh", "calf")):
        return False, LEG
    if name.startswith(("spine", "pelvis")):
        return False, TORSO
    if name.startswith("neck"):
        return False, NECK
    return None


def stylize(rig, objs):
    """Reshapes the rest pose of the skinned meshes in objs (bound to rig), in place."""
    joint = {b.name: rig.matrix_world @ b.head_local for b in rig.data.bones}
    for o in objs:
        if o.type != "MESH" or o.name.startswith("Prop_"):
            continue
        inv = o.matrix_world.inverted()
        groups = {g.index: g.name for g in o.vertex_groups}
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            move = Vector()
            for g in v.groups:
                name = groups.get(g.group)
                r = rule(name) if name in joint else None
                if r is None or g.weight <= 0:
                    continue
                rel = p - joint[name]
                if not r[0]:
                    a = (joint[CHILD[name]] - joint[name]).normalized() if name in CHILD else Vector((0, 0, 1))
                    rel = rel - a * rel.dot(a)
                move += g.weight * (r[1] - 1) * rel
            v.co = inv @ (p + move)


# Soft, rounded figures: the low-poly game mesh is relaxed (Taubin smoothing, which rounds without
# shrinking) and shaded with one smooth normal per point, so no hard rims or blocky hair are left.
SMOOTH_PASSES = 10
SMOOTH_ZONE = {"Hair": 0.5, "Skin": 0.8, "Main": 0.7, "Rest": 0.7}
# Hair and clothes lie on the skin: they may sink at most this much (x body height), so they never
# go under it; the skin itself is free.
SINK = {"Hair": 0.002, "Main": 0.003, "Rest": 0.003}
LAMBDA, MU = 0.5, -0.53


def soften(objs):
    import numpy as np
    for o in objs:
        if o.type != "MESH" or o.name.startswith("Prop_"):
            continue
        me = o.data
        n = len(me.vertices)
        co = np.empty(n * 3, np.float64)
        me.vertices.foreach_get("co", co)
        co = co.reshape(n, 3)
        # Points that share a position are one point (the game mesh is split at UV seams).
        tol = max(1e-9, np.ptp(co, axis=0).max() * 1e-5)
        _, weld, = np.unique(np.round(co / tol).astype(np.int64), axis=0, return_inverse=True)[:2]
        weld = weld.ravel()
        m = weld.max() + 1
        strength = np.zeros(m)
        sink = np.full(m, np.inf)
        edges = set()
        names = [mat.name.split(".")[0] if mat else "" for mat in me.materials]
        for p in me.polygons:
            s = SMOOTH_ZONE.get(names[p.material_index] if p.material_index < len(names) else "", 0.6)
            vs = [weld[v] for v in p.vertices]
            for i, a in enumerate(vs):
                strength[a] = max(strength[a], s)
                sink[a] = min(sink[a], SINK.get(names[p.material_index] if p.material_index < len(names) else "", np.inf))
                b = vs[(i + 1) % len(vs)]
                if a != b:
                    edges.add((min(a, b), max(a, b)))
        e = np.array(sorted(edges))
        pos = np.zeros((m, 3))
        np.add.at(pos, weld, co)
        cnt = np.bincount(weld, minlength=m)[:, None]
        pos /= cnt
        deg = np.bincount(e.ravel(), minlength=m)[:, None].astype(np.float64)
        for _ in range(SMOOTH_PASSES):
            for f in (LAMBDA, MU):
                avg = np.zeros((m, 3))
                np.add.at(avg, e[:, 0], pos[e[:, 1]])
                np.add.at(avg, e[:, 1], pos[e[:, 0]])
                avg = np.where(deg > 0, avg / np.maximum(deg, 1), pos)
                pos += (f * strength)[:, None] * (avg - pos)
        # Original smooth normal per point: the limit on sinking is measured along it.
        n0 = np.zeros((m, 3))
        ln = np.empty(n * 3)
        me.vertices.foreach_get("normal", ln)
        np.add.at(n0, weld, ln.reshape(n, 3))
        n0 /= np.maximum(np.linalg.norm(n0, axis=1, keepdims=True), 1e-12)
        orig = np.zeros((m, 3))
        np.add.at(orig, weld, co)
        orig /= cnt
        d = pos - orig
        depth = -(d * n0).sum(1)
        limit = sink * np.ptp(co, axis=0).max()
        over = np.maximum(0, depth - limit)
        pos = pos + n0 * over[:, None]
        me.vertices.foreach_set("co", pos[weld].ravel())
        # One smooth normal per point.
        me.update()
        fn = np.empty(len(me.polygons) * 3)
        me.polygons.foreach_get("normal", fn)
        fa = np.empty(len(me.polygons))
        me.polygons.foreach_get("area", fa)
        vn = np.zeros((m, 3))
        for p, nrm, a in zip(me.polygons, fn.reshape(-1, 3), fa):
            for v in p.vertices:
                vn[weld[v]] += nrm * a
        vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)
        me.normals_split_custom_set_from_vertices(vn[weld].tolist())


SUBDIVIDE = ("Hair", "Skin")


def subdivide(objs):
    """Splits every triangle of the hair and skin in four (neighbours split to match), for rounder heads and hands.

    The game does the same with its own red-green split (PersonStyle.cs); Blender's subdivide is close enough for a preview.
    """
    import bmesh
    for o in objs:
        if o.type != "MESH" or o.name.startswith("Prop_") or "_LOD" in o.name:
            continue
        names = [mat.name.split(".")[0] if mat else "" for mat in o.data.materials]
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        edges = {e for f in bm.faces if names[f.material_index] in SUBDIVIDE for e in f.edges}
        bmesh.ops.subdivide_edges(bm, edges=list(edges), cuts=1, use_grid_fill=True)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(o.data)
        bm.free()
