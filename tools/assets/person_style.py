"""The villagers' chunky painted-figure proportions, for Blender previews.

Same rules and numbers as the game's import step (unity/Assets/Editor/PersonStyle.cs): the skin
moves around the joints it is bound to (head, hands and feet scaled about their joint; arms, legs,
neck and body thickened across the bone), so the skeleton and the clips stay as they are.
"""
from mathutils import Vector

HEAD, HAND, FOOT = 1.55, 1.4, 1.18
ARM, LEG, TORSO, NECK = 1.4, 1.3, 1.15, 1.15
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
