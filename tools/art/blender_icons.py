"""Blender (4.2): models and renders the UI icons as small 3D still lifes in the kit's style.

Every icon is built procedurally here (plus the CC0 Quaternius Person_* characters for the villager
portraits), framed by the same orthographic three-quarter camera and lit by the same rig
(iconlib.begin). The raw 512 px renders go to <raw dir>; post_icons.py adds the outline and
shadow and downsamples them to the 128 px icons the game loads.

blender -b --factory-startup -P tools/art/blender_icons.py -- <raw dir> [name ...] [--samples N]
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import iconlib as L  # noqa: E402
from iconlib import (box, circle_pts, cylinder, glow, lathe, paint, rock, shape2d, sphere, star_pts,  # noqa: E402
                     text3d, tube)

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SAMPLES = 48
if "--samples" in argv:
    i = argv.index("--samples")
    SAMPLES = int(argv[i + 1])
    del argv[i:i + 2]
RAW = argv[0] if argv else os.path.join(L.ROOT, "tools", "art", ".cache", "raw")
ONLY = argv[1:]

ICONS = {}


def icon(name, **cam):
    def deco(fn):
        ICONS[name] = (fn, cam)
        return fn
    return deco


# ---------------------------------------------------------------- shared materials (made per scene)
PAL = {
    "wood": dict(color="#B85A2E", hue_dark="#4A1440", spec=0.2, rough=0.45,
                 pattern=("streaks", 4.0, "#6A2614", 0.5, 0.75)),
    "wood_dark": dict(color="#8E4422", hue_dark="#3A0C30", spec=0.18, rough=0.5,
                      pattern=("streaks", 4.0, "#4A160C", 0.5, 0.75)),
    "wood_light": dict(color="#F2B66A", light=0.32, hue_dark="#8A3A30", spec=0.2),
    "bark": dict(color="#9C4E22", hue_dark="#3A0C30", spec=0.15, rough=0.6),
    "handle": dict(color="#D08038", hue_dark="#5A1A30", spec=0.35, rough=0.3),
    "steel": dict(color="#A9BCC4", hue_dark="#2A2C66", shade=0.48, light=0.45, light_col="#FFFFFF", spec=0.7,
                  rough=0.18, metal=True),
    "iron": dict(color="#6C7E88", hue_dark="#1A1A40", shade=0.5, light=0.3, spec=0.6, rough=0.22, metal=True),
    "gold": dict(color="#FFC21F", hue_dark="#8A2A0A", shade=0.55, light=0.5, light_col="#FFFBD0", spec=0.85,
                 rough=0.16, metal=True),
    "red": dict(color="#EC3B2E", hue_dark="#5A0A40", spec=0.5, rough=0.2, light_col="#FFD8B0"),
    "green": dict(color="#8FD62C", hue_dark="#1E5A2A", spec=0.55, rough=0.18, light=0.3, light_col="#F4FFB0"),
    "leaf": dict(color="#5EBB2E", hue_dark="#123A3A", spec=0.35, rough=0.3, light_col="#E8FF9A"),
    "white": dict(color="#FBF8F2", hue_dark="#5A5AA0", shade=0.66, light=0.2, spec=0.45, rough=0.2, rim=0.3),
    "stone": dict(color="#B4C2C0", hue_dark="#2C3260", shade=0.48, light=0.4, light_col="#F6FFF8", spec=0.2),
    "cream": dict(color="#F6E2B8", hue_dark="#8A4A60", shade=0.62, light=0.3, spec=0.15, rough=0.5),
    "paper": dict(color="#F3DCAA", hue_dark="#8A4A50", shade=0.6, light=0.35, spec=0.1, rough=0.6),
    "leather": dict(color="#B8743E", hue_dark="#5A1A30", spec=0.2, rough=0.45),
    "blue": dict(color="#3E9BF2", hue_dark="#1A1A7A", spec=0.55, rough=0.2, light_col="#E8F6FF"),
    "skin": dict(color="#F5BE90", hue_dark="#9A3A50", shade=0.62, light=0.3, spec=0.2, rough=0.4),
    "ink": dict(color="#3A2314", hue_dark="#1A0A10", shade=0.7, light=0.12, spec=0.25, rough=0.3, rim=0.2, edge=0,
                ao=0),
    "glow": dict(color="#FFD55A", hue_dark="#FF8A20", shade=0.9, light=0.6, light_col="#FFFFF0", spec=0.2, rim=0,
                 edge=0, ao=0, emit="#FFB830", emit_strength=0.6),
}


def M(kind, **over):
    d = dict(PAL[kind])
    d.update(over)
    return paint(kind, **d)


def P(color, **kw):
    """Quick custom paint."""
    return paint(color, color, **kw)


def glyph(loops, m, depth=0.42, bevel=0.12, loc=(0, 0, 0), rot=(90, 0, 0), smooth_curve=False, res=12):
    return shape2d(loops, depth=depth, bevel=bevel, m=m, loc=loc, rot=rot, smooth_curve=smooth_curve, res=res)


def disc(r, depth, m, loc=(0, 0, 0), bevel=0.1, verts=64, axis_rot=(90, 0, 0)):
    """A coin/token facing the camera (axis along Y)."""
    return cylinder(r, depth, loc, m, rot=axis_rot, verts=verts, bevel=bevel, segs=4)


def arc_pts(r, a0, a1, n=24, cx=0.0, cy=0.0, y=0.0, plane="xz"):
    pts = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        x, z = cx + r * math.cos(a), cy + r * math.sin(a)
        pts.append((x, y, z))
    return pts


# ---------------------------------------------------------------- icons
def crescent(r1=1.0, cx=0.48, cy=0.16, r2=0.84, n=56):
    """Outer circle minus a shifted circle, as one clean outline (outer arc ccw, inner arc back)."""
    def inside2(x, y):
        return (x - cx) ** 2 + (y - cy) ** 2 < r2 * r2
    steps = 1440
    outer = [(r1 * math.cos(2 * math.pi * i / steps), r1 * math.sin(2 * math.pi * i / steps)) for i in range(steps)]
    flags = [not inside2(x, y) for x, y in outer]
    start = next(i for i in range(steps) if flags[i] and not flags[i - 1])
    arc = []
    i = start
    while flags[i % steps]:
        arc.append(outer[i % steps])
        i += 1
    p1, p2 = arc[0], arc[-1]
    a1 = math.atan2(p1[1] - cy, p1[0] - cx)
    a2 = math.atan2(p2[1] - cy, p2[0] - cx)
    # inner arc from p2 back to p1, the way that stays inside the outer circle
    pts = arc[:: max(1, len(arc) // n)] + [p2]
    for d in (1, -1):
        span = (a1 - a2) % (2 * math.pi) if d == 1 else -((a2 - a1) % (2 * math.pi))
        am = a2 + span / 2
        if (cx + r2 * math.cos(am)) ** 2 + (cy + r2 * math.sin(am)) ** 2 < r1 * r1:
            break
    for j in range(1, n):
        a = a2 + span * j / n
        pts.append((cx + r2 * math.cos(a), cy + r2 * math.sin(a)))
    return pts


@icon("stone")
def _stone():
    m = paint("Stone", "#A2B6B2", hue_dark="#1E2A5A", shade=0.45, light=0.42, light_col="#F4FFF8", bands=0.8,
              spec=0.3, rough=0.28, grad=(0.68, 1.12))
    o = L.hull_rock(1.0, (0, 0, 0), m, seed=4, scale=(1.3, 1.1, 1.0), n=34, bevel=0.04, rot=(12, -18, 25))
    o.modifiers["Bevel"].harden_normals = True


@icon("wood")
def _wood():
    rnd = random.Random(3)
    bm = bmesh.new()
    seg, rings = 72, 12
    H = 1.2
    vs = []
    for k in range(rings + 1):
        z = H * k / rings
        flare = 1.0 + 0.2 * max(0, 1 - z / 0.4) ** 2
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            lobe = 0.06 * max(0, math.sin(4 * a + 0.5)) * max(0, 1 - z / 0.5)
            groove = 0.07 * abs(math.sin(6.5 * a + 0.7 * math.sin(3 * a))) ** 0.6
            r = (1.0 - groove + lobe) * flare
            zz = z
            if k == rings:
                zz = z + 0.07 * math.sin(3 * a + 1) + 0.03 * math.sin(7 * a) + rnd.uniform(-0.015, 0.015)
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), zz)))
        vs.append(ring)
    for a_, b_ in zip(vs, vs[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            bm.faces.new((a_[i], a_[j], b_[j], b_[i]))
    bm.faces.new(list(reversed(vs[0])))
    top = bm.faces.new(vs[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    r1 = bmesh.ops.inset_region(bm, faces=[top], thickness=0.1, depth=-0.04)
    # flatten the cut face to a gently tilted plane
    for v in top.verts:
        v.co.z = H + 0.02 + 0.03 * v.co.x
    o = L.from_bmesh("Stump", bm)
    bark = paint("Bark", "#9A4A22", rough=0.65, spec=0.35, hue_dark="#3A0C30", shade=0.34,
                 pattern=("streaks", 5.0, "#5A2410", 0.55, 0.8))
    cut = paint("Cut", "#F7BC6A", rough=0.45, spec=0.35, light=0.25, hue_dark="#8A3A20",
                pattern=("rings", 1.1, "#DE8A44", 0.38, 0.35), grad=(0.85, 1.08))
    o.data.materials.append(bark)
    o.data.materials.append(cut)
    for p in o.data.polygons:
        p.material_index = 1 if p.normal.z > 0.7 and p.center.z > H - 0.1 and p.center.length < 1.6 and \
            Vector((p.center.x, p.center.y)).length < 0.88 else 0
    L.mod_bevel(o, 0.025, 2, "ANGLE", 50)
    L.smooth(o)
    # the kit's split: a pale sliver of heartwood down the front
    shape2d([[(-0.06, 0.0), (0.08, 0.02), (0.03, -0.8), (-0.01, -0.76)]], depth=0.1, bevel=0.025,
            m=paint("Sliver", "#FFD089", rough=0.4, light=0.3), loc=(0.3, -0.93, 1.16), rot=(90, 0, -18))


@icon("moon", el=10, az=-8)
def _moon():
    m = paint("Moon", "#8A9CF4", rough=0.3, spec=0.55, coat=0.35, hue_dark="#24186A", shade=0.42, light=0.25,
              light_col="#EEF2FF")
    shape2d([crescent()], depth=0.5, bevel=0.17, m=m, rot=(90, 0, -18), res=6)
    crater = paint("Crater", "#6474D8", rough=0.4, spec=0.3, hue_dark="#20186A", grad=(0.8, 1.05))
    for (x, y, r) in ((-0.5, 0.42, 0.13), (-0.72, -0.05, 0.11), (-0.42, -0.5, 0.15), (-0.2, 0.78, 0.07),
                      (-0.85, 0.32, 0.06), (-0.05, -0.8, 0.07)):
        sphere(r, (x, -0.38, y), crater, scale=(1, 0.35, 1))


# ---------------------------------------------------------------- glyphs (UI marks)
GLYPH = dict(el=12, az=-12)


@icon("check", **GLYPH)
def _check():
    pts = L.rounded_poly([(-1.0, 0.12), (-0.6, 0.52), (-0.26, 0.16), (0.62, 1.02), (1.02, 0.62), (-0.26, -0.68)], 0.1)
    glyph([pts], M("green"))


@icon("plus", **GLYPH)
def _plus():
    t, s_ = 0.34, 1.0
    pts = [(-t, s_), (t, s_), (t, t), (s_, t), (s_, -t), (t, -t), (t, -s_), (-t, -s_), (-t, -t), (-s_, -t), (-s_, t), (-t, t)]
    glyph([L.rounded_poly(pts, [0.12, 0.12, 0, 0.12, 0.12, 0, 0.12, 0.12, 0, 0.12, 0.12, 0])], M("white"))


@icon("minus", fill=0.8, **GLYPH)
def _minus():
    pts = L.rounded_poly([(-1.0, 0.34), (1.0, 0.34), (1.0, -0.34), (-1.0, -0.34)], 0.12)
    glyph([pts], M("white"))
    # an invisible square keeps the bar the same width as the plus
    o = box((2.0, 0.1, 2.0), (0, 2, 0), None, bevel=0)
    o.hide_render = True


@icon("close", **GLYPH)
def _close():
    t = 0.3
    arm = [(-t, 1.0), (t, 1.0), (t, -1.0), (-t, -1.0)]
    for ang in (45, -45):
        glyph([L.rounded_poly(arm, 0.12)], M("white"), rot=(90, ang, 0))
    L.tag([o for o in bpy.context.scene.objects if o.type == "CURVE"], "x")


def arrow_shape(shaft=0.5, head_w=1.45, head_l=0.95, length=2.0, r=0.1):
    x0, x1 = -length / 2, length / 2
    pts = [(x0, -shaft / 2), (x1 - head_l, -shaft / 2), (x1 - head_l, -head_w / 2), (x1, 0), (x1 - head_l, head_w / 2),
           (x1 - head_l, shaft / 2), (x0, shaft / 2)]
    return L.rounded_poly(pts, [r, 0.03, r, r * 1.2, r, 0.03, r])


@icon("arrow", **GLYPH)
def _arrow():
    glyph([arrow_shape()], M("green"))


@icon("level_up", **GLYPH)
def _level_up():
    glyph([arrow_shape()], M("green"), rot=(90, -90, 0))


@icon("back", el=10, az=-10)
def _back():
    # the kit's chunky back arrow: wide head, thick shaft with a rounded tail, warm tan-gold
    pts = [(1.1, -0.36), (-0.15, -0.36), (-0.15, -0.85), (-1.15, 0.0), (-0.15, 0.85), (-0.15, 0.36), (1.1, 0.36)]
    pts = L.rounded_poly(pts, [0.3, 0.04, 0.14, 0.16, 0.14, 0.04, 0.3])
    glyph([pts], P("#F3BE6E", hue_dark="#7A2A20", shade=0.52, light=0.42, light_col="#FFF4D0", spec=0.4, rough=0.25),
          depth=0.5, bevel=0.14)


@icon("next", **GLYPH)
def _next():
    pts = [(-0.55, 0.95), (0.0, 0.95), (0.62, 0.0), (0.0, -0.95), (-0.55, -0.95), (0.06, 0.0)]
    glyph([L.rounded_poly(pts, [0.1, 0.1, 0.16, 0.1, 0.1, 0.05])], P("#F2A93B", hue_dark="#6A1A20", light=0.38,
                                                                        spec=0.45, rough=0.22))


@icon("alert", **GLYPH)
def _alert():
    pts = L.rounded_poly([(0, 1.05), (1.12, -0.85), (-1.12, -0.85)], 0.28, 8)
    glyph([pts], P("#FFC52E", hue_dark="#9A2A0A", light=0.4, light_col="#FFFBD0", spec=0.5, rough=0.2), depth=0.45)
    text3d("!", size=1.45, depth=0.12, bevel=0.025, m=M("ink"), loc=(0, -0.31, -0.08))


@icon("info", **GLYPH)
def _info():
    disc(1.0, 0.42, M("blue"), bevel=0.14)
    text3d("i", size=1.5, depth=0.1, bevel=0.02, m=M("white"), loc=(0, -0.25, 0.0))


@icon("sparkle", el=10, az=-10)
def _sparkle():
    def astroid(r, cx=0.0, cy=0.0, n=64, p=3.0):
        return [(cx + r * math.copysign(abs(math.cos(t)) ** p, math.cos(t)),
                 cy + r * math.copysign(abs(math.sin(t)) ** p, math.sin(t)))
                for t in (2 * math.pi * i / n for i in range(n))]
    m = P("#FFD43A", hue_dark="#B0400A", light=0.55, light_col="#FFFFF0", spec=0.6, rough=0.15, emit="#FFC020",
          emit_strength=0.25)
    glyph([astroid(1.0, n=96, p=2.6)], m, depth=0.36, bevel=0.1)
    glyph([astroid(0.42, n=64, p=2.6)], P("#FFE680", hue_dark="#B0400A", light=0.5, spec=0.5), depth=0.22, bevel=0.06,
          loc=(0.9, -0.1, 0.85))


@icon("rotate", **GLYPH)
def _rotate():
    m = M("blue")
    pts = arc_pts(0.8, 120, 400, 36)
    tube(pts, 0.24, m, res=6)
    # arrowhead at the end of the arc (tangent direction)
    a = math.radians(120)
    end = Vector((0.8 * math.cos(a), 0, 0.8 * math.sin(a)))
    tri = L.rounded_poly([(0, 0.55), (0.62, -0.05), (-0.05, -0.62)], 0.08)
    glyph([tri], m, depth=0.36, bevel=0.1, loc=(end.x + 0.05, 0, end.z + 0.05), rot=(90, 75, 0))
    L.tag([o for o in bpy.context.scene.objects if o.type == "CURVE"], "r")


@icon("copy", el=14, az=-14)
def _copy():
    for i, (x, z) in enumerate(((0.32, 0.3), (-0.22, -0.25))):
        card = box((1.3, 0.16, 1.6), (x, i * -0.3, z), M("paper"), bevel=0.1)
        for k in range(4):
            box((0.8 - (0.3 if k == 3 else 0), 0.04, 0.08), (x - 0.05 - (0.15 if k == 3 else 0), i * -0.3 - 0.1,
                                                           z + 0.45 - k * 0.3), P("#C79A62", spec=0, ao=0), bevel=0.02)


# ---------------------------------------------------------------- tokens and faces
def token(color, hue_dark):
    return disc(1.0, 0.5, P(color, hue_dark=hue_dark, light=0.42, light_col="#FFFBD8", spec=0.5, rough=0.18),
                bevel=0.2)


@icon("approval", **GLYPH)
def _approval():
    token("#FFC92E", "#A0300A")
    ink = M("ink")
    for x in (-0.36, 0.36):
        sphere(0.14, (x, -0.42, 0.22), ink, scale=(0.8, 0.4, 1.15))
    tube(arc_pts(0.5, 200, 340, 16, cy=-0.02, y=-0.44), 0.075, ink)
    for x in (-0.62, 0.62):
        sphere(0.16, (x, -0.4, -0.18), P("#FF8A5A", spec=0, rim=0, ao=0), scale=(1, 0.3, 0.7))


@icon("discontent", **GLYPH)
def _discontent():
    token("#F26A3A", "#6A0A30")
    ink = M("ink")
    for x in (-0.36, 0.36):
        sphere(0.13, (x, -0.42, 0.1), ink, scale=(0.85, 0.4, 1.0))
        box((0.42, 0.08, 0.11), (x * 1.05, -0.42, 0.42), ink, rot=(0, -22 if x < 0 else 22, 0), bevel=0.04)
    tube(arc_pts(0.42, 30, 150, 16, cy=-0.62, y=-0.44), 0.075, ink)


@icon("people", az=-14, el=10)
def _people():
    bust((0.6, 0.55, 0.12), 0.88, "#4D8FE6", "#F0B07E", "#6A3A1C")
    bust((-0.35, 0, 0), 1.0, "#F2903A", "#F5BE90", "#4A2A14")


@icon("user", az=-10, el=10)
def _user():
    m = P("#EBD2A8", hue_dark="#8A5A50", shade=0.7, light=0.3, spec=0.15, rough=0.4)
    bust((0, 0, 0), 1.0, None, None, None, m)


def bust(loc, s, shirt, skin, hair, mono=None):
    x, y, z = loc
    body = lathe([(0.0, 0.0), (0.82, 0.0), (0.86, 0.22), (0.78, 0.5), (0.52, 0.7), (0.22, 0.8), (0.0, 0.82)],
                 (x, y, z), mono or P(shirt, hue_dark="#2A1A6A", spec=0.25, rough=0.3), verts=40, sub=2)
    body.scale = (s, s * 0.8, s)
    head = sphere(0.43 * s, (x, y, z + 1.22 * s), mono or M("skin", color=skin), scale=(1, 1, 1.08))
    parts = [body, head]
    if not mono:
        hair_o = sphere(0.46 * s, (x + 0.02 * s, y + 0.05 * s, z + 1.36 * s), P(hair, hue_dark="#1A0A20", spec=0.35,
                                                                                    rough=0.25), scale=(1, 1, 0.75))
        parts.append(hair_o)
        neck = cylinder(0.17 * s, 0.4 * s, (x, y, z + 0.82 * s), M("skin", color=skin), bevel=0)
        parts.append(neck)
    else:
        neck = cylinder(0.17 * s, 0.4 * s, (x, y, z + 0.82 * s), mono, bevel=0)
        parts.append(neck)
    L.tag(parts, "bust%.2f" % x)
    return parts


# ---------------------------------------------------------------- weather and time
@icon("summer", **GLYPH)
def _summer():
    rays = L.rounded_poly(star_pts(1.12, 0.74, n=10, rot=90), 0.06)
    glyph([rays], P("#FF9A1A", hue_dark="#9A1A0A", light=0.35, spec=0.4, rough=0.25), depth=0.3, bevel=0.08,
          loc=(0, 0.12, 0))
    disc(0.68, 0.46, P("#FFD32E", hue_dark="#C0400A", light=0.55, light_col="#FFFFE0", spec=0.55, rough=0.15,
                       emit="#FFC020", emit_strength=0.2), bevel=0.2)


@icon("winter", **GLYPH)
def _winter():
    m = P("#8EDCFF", hue_dark="#1A3A9A", shade=0.55, light=0.5, light_col="#FFFFFF", spec=0.7, rough=0.12)
    objs = []
    for k in range(6):
        a = 90 + 60 * k
        arm = L.rounded_poly([(-0.13, 0.0), (0.13, 0.0), (0.13, 1.0), (0.0, 1.12), (-0.13, 1.0)], 0.05)
        objs.append(glyph([arm], m, depth=0.26, bevel=0.07, rot=(90, -a + 90, 0)))
        for side in (-1, 1):
            br = L.rounded_poly([(-0.09, 0.0), (0.09, 0.0), (0.09, 0.42), (-0.09, 0.42)], 0.05)
            ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
            bx, bz = 0.55 * ca, 0.55 * sa
            objs.append(glyph([br], m, depth=0.2, bevel=0.06, loc=(bx, -0.02, bz), rot=(90, -a + 90 + side * 48, 0)))
    objs.append(glyph([L.rounded_poly(star_pts(0.34, 0.3, n=6, rot=90), 0.03)], m, depth=0.34, bevel=0.07))
    L.tag(objs, "flake")


@icon("spring", az=-14, el=16)
def _spring():
    sphere(0.75, (0, 0, -0.2), P("#9A5A2E", hue_dark="#3A1030", spec=0.15), scale=(1.15, 1.0, 0.42))
    stem = M("leaf", color="#6CC23A")
    tube([(0, 0, 0.05), (0.03, 0, 0.5), (-0.02, 0, 0.9)], 0.07, stem)
    leaf = [(0, 0), (0.25, 0.22), (0.6, 0.3), (0.95, 0.15), (0.7, -0.08), (0.3, -0.1)]
    for side, ang in ((1, 20), (-1, 160)):
        o = glyph([leaf], M("leaf"), depth=0.08, bevel=0.04, smooth_curve=True,
                  loc=(0.0, 0, 0.88), rot=(90, -ang, 0))
        o.rotation_euler.z = math.radians(-25 * side)


@icon("autumn", **GLYPH)
def _autumn():
    pts = []
    lobes = [(90, 1.0), (135, 0.42), (160, 0.82), (195, 0.38), (215, 0.6), (250, 0.3), (270, 0.2),
             (290, 0.3), (325, 0.6), (345, 0.38), (20, 0.82), (45, 0.42)]
    for a, r in lobes:
        pts.append((r * math.cos(math.radians(a)), r * math.sin(math.radians(a)) + 0.1))
    m = P("#F5862A", hue_dark="#8A0A2A", light=0.4, light_col="#FFE890", spec=0.4, rough=0.25)
    glyph([L.rounded_poly(pts, 0.07)], m, depth=0.16, bevel=0.06)
    vein = P("#C04A14", spec=0.1, rim=0, ao=0)
    for a in (90, 160, 20, 215, 325):
        r = 0.7
        tube([(0, -0.13, -0.05), (r * math.cos(math.radians(a)) * 0.9, -0.13, r * math.sin(math.radians(a)) * 0.9 + 0.1)],
             0.025, vein, smooth_path=False)
    tube([(0, -0.02, -0.05), (0.05, -0.02, -0.55), (0.18, -0.02, -0.8)], 0.06, P("#9A4A1A", spec=0.2))


@icon("thermometer", az=-14, el=12)
def _thermometer():
    glass = P("#EAF6FF", hue_dark="#3A4A9A", shade=0.7, light=0.3, light_col="#FFFFFF", spec=0.6, rough=0.12)
    red = P("#EE3628", hue_dark="#5A0A3A", light=0.35, spec=0.6, rough=0.15)
    cylinder(0.26, 1.7, (0, 0, 0.55), glass, bevel=0)
    sphere(0.26, (0, 0, 1.4), glass)
    sphere(0.48, (0, 0, -0.4), red)
    cylinder(0.12, 1.0, (0, -0.16, 0.25), red, bevel=0)
    for k in range(5):
        box((0.22 if k % 2 == 0 else 0.14, 0.05, 0.06), (0.32, -0.14, 0.2 + k * 0.25), M("ink"), bevel=0.02)


@icon("clock", **GLYPH)
def _clock():
    disc(1.0, 0.45, M("gold"), bevel=0.16)
    disc(0.76, 0.12, M("cream"), loc=(0, -0.22, 0), bevel=0.04)
    ink = M("ink")
    for k in range(12):
        a = math.radians(90 - 30 * k)
        big = k % 3 == 0
        box((0.07, 0.04, 0.17 if big else 0.1), (0.62 * math.cos(a), -0.3, 0.62 * math.sin(a)), ink,
            rot=(0, -math.degrees(a) + 90, 0), bevel=0.02)
    box((0.1, 0.05, 0.48), (0, -0.33, 0.2), ink, bevel=0.03)
    box((0.08, 0.05, 0.38), (0.15, -0.33, -0.06), ink, rot=(0, 120, 0), bevel=0.03)
    disc(0.09, 0.08, M("gold"), loc=(0, -0.37, 0), bevel=0.02)
    cylinder(0.12, 0.2, (0, 0, 1.08), M("gold"), bevel=0.03)
    tube(arc_pts(0.18, -30, 210, 16, cy=1.28), 0.05, M("gold"))


@icon("compass", **GLYPH)
def _compass():
    disc(1.0, 0.42, M("gold"), bevel=0.16)
    disc(0.76, 0.1, P("#F5ECD8", hue_dark="#5A4A8A", shade=0.66, spec=0.2), loc=(0, -0.2, 0), bevel=0.04)
    glyph([[(0, 0.66), (0.17, 0.0), (-0.17, 0.0)]], M("red"), depth=0.1, bevel=0.03, loc=(0, -0.28, 0))
    glyph([[(0, -0.66), (0.17, 0.0), (-0.17, 0.0)]], P("#5A6A88", spec=0.3), depth=0.1, bevel=0.03, loc=(0, -0.28, 0))
    disc(0.08, 0.1, M("gold"), loc=(0, -0.34, 0), bevel=0.02)
    cylinder(0.12, 0.2, (0, 0, 1.06), M("gold"), bevel=0.03)
    tube(arc_pts(0.18, -30, 210, 16, cy=1.26), 0.05, M("gold"))


# ---------------------------------------------------------------- tools (built upright, then tilted as a group)
TOOL = dict(el=12, az=-12)


def handle(length=2.4, r=0.12, bend=0.04, m=None):
    return tube([(0, 0, -length / 2), (bend, 0, 0), (0, 0, length / 2)], r, m or M("handle"))


def wraps(z0, n=3, r=0.145, m=None):
    out = []
    for k in range(n):
        out.append(cylinder(r, 0.07, (0, 0, z0 + k * 0.085), m or M("leather"), bevel=0.025, verts=24))
    return out


@icon("axe", **TOOL)
def _axe():
    parts = [handle(2.5)]
    head = [(-0.3, 1.22), (0.14, 1.14), (0.7, 1.6), (0.98, 1.1), (1.0, 0.55), (0.72, 0.05), (0.16, 0.56),
            (-0.3, 0.52)]
    parts.append(glyph([L.rounded_poly(head, [0.06, 0.04, 0.08, 0.1, 0.1, 0.08, 0.04, 0.06])], M("steel"),
                       depth=0.26, bevel=0.06))
    edge = [(0.8, 1.48), (0.98, 1.1), (1.0, 0.55), (0.8, 0.18), (0.88, 0.55), (0.87, 1.1)]
    parts.append(glyph([edge], P("#F4F8FF", hue_dark="#5A6AA0", shade=0.8, light=0.5, spec=0.8, rough=0.1),
                       depth=0.16, bevel=0.03, loc=(0, -0.07, 0)))
    parts += wraps(0.3)
    L.parent(parts, rot=(0, 32, 0))


@icon("pickaxe", **TOOL)
def _pickaxe():
    parts = [handle(2.5)]
    pts = [(-1.08, 0, 0.62), (-0.6, 0, 1.02), (0, 0, 1.18), (0.6, 0, 1.02), (1.08, 0, 0.62)]
    parts.append(tube(pts, 0.21, M("steel"), radii=[0.12, 0.85, 1.25, 0.85, 0.12], res=12))
    parts.append(cylinder(0.17, 0.42, (0, 0, 1.12), M("iron"), bevel=0.05))
    parts += wraps(0.7)
    L.parent(parts, rot=(0, 30, 0))


@icon("build", **TOOL)
def _build():
    parts = [handle(2.3)]
    parts.append(box((1.5, 0.6, 0.66), (0.1, 0, 1.0), M("steel"), bevel=0.1))
    parts.append(box((0.26, 0.68, 0.74), (0.92, 0, 1.0), M("iron"), bevel=0.07))
    parts.append(box((0.42, 0.68, 0.74), (-0.38, 0, 1.0), M("iron"), bevel=0.07))
    parts += wraps(-0.6, 4, 0.12)
    L.parent(parts, rot=(0, 34, 0))


@icon("spear", **TOOL)
def _spear():
    parts = [handle(3.6, 0.1)]
    blade = [(0, 0), (0.34, 0.4), (0.28, 0.9), (0, 1.45), (-0.28, 0.9), (-0.34, 0.4)]
    parts.append(glyph([blade], M("steel"), depth=0.18, bevel=0.06, smooth_curve=True, loc=(0, 0, 1.55)))
    parts.append(tube([(0, -0.05, 1.98), (0, -0.05, 2.65)], 0.02, P("#8A96B0", spec=0.4, rim=0)))
    parts += wraps(1.48, 3, 0.1)
    L.parent(parts, rot=(0, 40, 0))


@icon("tools", **TOOL)
def _tools():
    """Bronze tools (the toolsmith's goods): a bronze axe crossed with a hammer."""
    bronze = P("#E0913A", hue_dark="#5A1A0A", shade=0.5, light=0.5, light_col="#FFF0C0", spec=0.8, rough=0.16,
               metal=True)
    axe = [handle(2.4)]
    head = [(-0.3, 1.2), (0.14, 1.12), (0.66, 1.5), (0.92, 1.08), (0.94, 0.58), (0.68, 0.1), (0.16, 0.56),
            (-0.3, 0.52)]
    axe.append(glyph([L.rounded_poly(head, [0.06, 0.04, 0.08, 0.1, 0.1, 0.08, 0.04, 0.06])], bronze,
                     depth=0.26, bevel=0.06))
    axe += wraps(0.3)
    L.parent(axe, loc=(0, 0.1, 0), rot=(0, -38, 0))
    ham = [handle(2.3)]
    ham.append(box((1.2, 0.56, 0.6), (0.0, 0, 1.0), bronze, bevel=0.1))
    ham.append(box((0.22, 0.62, 0.68), (0.62, 0, 1.0), M("iron"), bevel=0.07))
    ham += wraps(-0.6, 4, 0.12)
    L.parent(ham, loc=(0, -0.1, 0), rot=(0, 38, 0))


@icon("coins", el=16, az=-14)
def _coins():
    """Coins (the treasury, from taxes): two stacks of gold and one coin standing in front."""
    gold = M("gold")
    for (x, y, n) in ((-0.55, 0.3, 5), (0.5, 0.45, 3)):
        for k in range(n):
            cylinder(0.62, 0.2, (x + (k % 2) * 0.03, y, -0.6 + k * 0.22), gold, verts=48, bevel=0.06)
    disc(0.72, 0.2, gold, loc=(0.1, -0.55, -0.15), bevel=0.07)
    disc(0.46, 0.06, M("gold", color="#F2A818"), loc=(0.1, -0.68, -0.15), bevel=0.03)


@icon("goods", el=18, az=-16)
def _goods():
    """Household goods (the weaver's work): a clay pot and a folded roll of cloth."""
    clay = P("#D8763A", hue_dark="#5A1430", light=0.35, light_col="#FFE0B0", spec=0.3, rough=0.35)
    lathe([(0.0, -0.9), (0.42, -0.9), (0.7, -0.55), (0.78, -0.1), (0.62, 0.35), (0.38, 0.55), (0.44, 0.72),
           (0.36, 0.76), (0.0, 0.76)], loc=(-0.5, 0.25, 0), m=clay, verts=48)
    cloth = P("#3E8FD8", hue_dark="#141A6A", light=0.4, light_col="#E8F6FF", spec=0.3, rough=0.4)
    stripe = M("cream")
    box((1.5, 1.1, 0.34), (0.55, -0.25, -0.75), cloth, bevel=0.14, sub=1)
    box((1.5, 0.2, 0.36), (0.55, -0.25, -0.75), stripe, bevel=0.06)
    cylinder(0.32, 1.4, (0.55, -0.3, -0.32), cloth, rot=(0, 90, 0), bevel=0.1, sub=1)
    cylinder(0.33, 0.18, (0.55, -0.3, -0.32), stripe, rot=(0, 90, 0), bevel=0.04)


# ---------------------------------------------------------------- nature
@icon("planks", el=18, az=-16)
def _planks():
    """Sawn planks (the sawpit's goods): a small stack, boards crossing like a timber yard."""
    rnd = random.Random(7)
    for k in range(4):
        m = M("wood_light") if k % 2 else M("wood")
        z = k * 0.3
        for j in range(2):
            y = (j - 0.5) * 0.62 + rnd.uniform(-0.04, 0.04)
            if k % 2 == 0:
                box((2.3, 0.56, 0.26), (rnd.uniform(-0.05, 0.05), y, z), m, bevel=0.06)
            else:
                box((0.56, 2.0, 0.26), (y, rnd.uniform(-0.05, 0.05), z), m, bevel=0.06)


@icon("fish", el=12, az=-10)
def _fish():
    body = [(-1.0, 0.0), (-0.8, 0.36), (-0.3, 0.6), (0.3, 0.5), (0.8, 0.14), (0.8, -0.12), (0.3, -0.42),
            (-0.3, -0.5), (-0.8, -0.3)]
    bm = P("#3FA7E0", hue_dark="#141A6A", light=0.45, light_col="#E8FFFF", spec=0.6, rough=0.16, grad=(0.55, 1.15))
    glyph([body], bm, depth=0.5, bevel=0.2, smooth_curve=True)
    tail = [(0.72, 0.0), (1.2, 0.55), (1.38, 0.5), (1.22, 0.0), (1.38, -0.5), (1.2, -0.55)]
    fin = P("#2E7FC8", hue_dark="#141A6A", light=0.3, spec=0.4)
    glyph([tail], fin, depth=0.16, bevel=0.05, smooth_curve=True)
    glyph([[(-0.25, 0.5), (0.05, 0.85), (0.35, 0.82), (0.4, 0.42)]], fin, depth=0.1, bevel=0.04, smooth_curve=True,
          loc=(0, 0.05, 0))
    sphere(0.16, (-0.6, -0.43, 0.1), M("white"), scale=(1, 0.4, 1))
    sphere(0.085, (-0.62, -0.5, 0.1), M("ink"), scale=(1, 0.4, 1))
    tube(arc_pts(0.3, -40, 40, 12, cx=-0.62, y=-0.46), 0.04, P("#2470B8", spec=0, rim=0, ao=0))
    for k in range(3):
        sphere(0.1, (0.0 + k * 0.25, -0.44, -0.05 + (k % 2) * 0.12), P("#7FCBF2", spec=0.3, rim=0, ao=0),
               scale=(1, 0.3, 0.8))


@icon("wheat", el=12, az=-10)
def _wheat():
    gold = P("#F7C443", hue_dark="#8A3A0A", light=0.45, light_col="#FFF6C8", spec=0.4, rough=0.25)
    stem = P("#E0A63A", hue_dark="#7A3A0A", spec=0.3)
    for ang, h in ((-20, 1.0), (0, 1.15), (20, 1.0)):
        a = math.radians(ang)
        top = Vector((math.sin(a) * h * 1.2, 0, math.cos(a) * h * 1.2))
        tube([(0, 0, -1.0), (math.sin(a) * 0.3, 0, 0.0), tuple(top)], 0.05, stem)
        d = Vector((math.sin(a), 0, math.cos(a)))
        side = Vector((math.cos(a), 0, -math.sin(a)))
        for k in range(6):
            c = top + d * (0.12 + k * 0.17)
            for sgn in (-1, 1):
                g = sphere(0.13, tuple(c + side * 0.11 * sgn), gold, scale=(0.75, 0.75, 1.25))
                g.rotation_euler = (0, a + sgn * 0.45, 0)
        sphere(0.12, tuple(top + d * 1.15), gold, scale=(0.7, 0.7, 1.3)).rotation_euler = (0, a, 0)
    cylinder(0.22, 0.2, (0, 0, -0.45), M("red"), bevel=0.05)


@icon("tree", el=14, az=-12)
def _tree():
    cylinder(0.22, 0.8, (0, 0, -0.3), M("bark"), bevel=0.04)
    col = [("#3FA03A", 1.15, 0.0, 1.0), ("#4BB23E", 0.92, 0.62, 0.9), ("#5CC447", 0.66, 1.18, 0.78)]
    for i, (c, r, z, h) in enumerate(col):
        prof = [(0.0, 0.0), (r * 0.9, 0.0), (r, 0.08), (r * 0.6, h * 0.5), (0.0, h)]
        o = lathe(prof, (0, 0, z), P(c, hue_dark="#0A3A3A", light=0.35, light_col="#F0FF9A", spec=0.3, rough=0.3),
                  verts=9, sub=1)
        o.rotation_euler.z = math.radians(20 * i)
    sphere(0.12, (0, 0, 2.02), P("#7BD650", spec=0.3))


@icon("ore", el=16, az=-16)
def _ore():
    m = P("#66627A", hue_dark="#100C28", shade=0.42, light=0.4, light_col="#E0E0FF", spec=0.25, bands=0.85)
    o = L.hull_rock(1.0, (0, 0, 0), m, seed=5, scale=(1.3, 1.0, 0.9), n=34, bevel=0.04, rot=(10, 20, -15))
    o.modifiers["Bevel"].harden_normals = True
    cop = P("#FF9A3A", hue_dark="#8A1A0A", light=0.55, light_col="#FFF0B0", spec=0.85, rough=0.12, metal=True)
    for i, (x, y, z, r) in enumerate(((-0.45, -0.62, 0.15, 0.36), (0.45, -0.58, -0.08, 0.3), (0.0, -0.42, 0.62, 0.28),
                                      (0.85, -0.3, 0.42, 0.22))):
        n = rock(r, (x, y, z), cop, seed=20 + i, scale=(1, 0.85, 0.9), jitter=0.18, subdiv=1, bevel=0.12)


@icon("hides", el=18, az=-16)
def _hides():
    lower = P("#9C5A2E", hue_dark="#3A0C30", light=0.25, spec=0.2, rough=0.4)
    upper = P("#D4925A", hue_dark="#6A2030", light=0.35, spec=0.25, rough=0.4)
    box((2.0, 1.3, 0.34), (0, 0, 0), lower, bevel=0.15, sub=1)
    roll = cylinder(0.42, 1.85, (0, -0.05, 0.55), upper, rot=(0, 90, 0), bevel=0.12, sub=1)
    # spiral end of the roll
    tube([(1.0, -0.05 + 0.3 * math.cos(t) * (1 - t / 14), 0.55 + 0.3 * math.sin(t) * (1 - t / 14)) for t in
          [i * 0.5 for i in range(25)]], 0.03, P("#8A4A22", spec=0, rim=0, ao=0))
    cord = P("#E9D29A", hue_dark="#6A4A30", spec=0.2)
    for x in (-0.5, 0.5):
        tube([(x, 0.0, 0.98), (x, -0.5, 0.62), (x, -0.62, 0.15), (x, -0.62, -0.05)], 0.055, cord)
        tube([(x, 0.45 * math.cos(t) - 0.05, 0.55 + 0.45 * math.sin(t)) for t in
                     [2 * math.pi * k / 24 for k in range(24)]], 0.05, cord, cyclic=True)


@icon("campfire", el=20, az=-14)
def _campfire():
    rock_m = P("#A9AFB8", hue_dark="#2A2A5A", shade=0.5, light=0.35, spec=0.2)
    for k in range(9):
        a = 2 * math.pi * k / 9 + 0.3
        o = L.hull_rock(0.28, (1.05 * math.cos(a), 0.85 * math.sin(a), -0.05), rock_m, seed=40 + k,
                        scale=(1.2, 1.0, 0.75), n=12, bevel=0.1)
    bark = M("bark")
    for ang in (35, -35, 95):
        a = math.radians(ang)
        cylinder(0.17, 1.8, (0, 0, 0.18 if ang == 95 else 0.1), bark, rot=(90, 0, ang), bevel=0.05)
    flame(scale=0.95, loc=(0, -0.15, 0.25))


def flame(scale=1.0, loc=(0, 0, 0), glow_=True):
    outer = [(0.0, -1.0), (0.66, -0.78), (0.92, -0.22), (0.8, 0.36), (0.58, 0.16), (0.5, 0.66), (0.18, 1.3),
             (0.12, 0.66), (-0.14, 0.9), (-0.45, 0.45), (-0.62, 0.72), (-0.9, 0.1), (-0.86, -0.52), (-0.56, -0.88)]
    layers = [(outer, 1.0, "#FF5A1E", "#9A0A2A", 0.0), (outer, 0.7, "#FFA21C", "#C02A0A", -0.18),
              (outer, 0.42, "#FFE65A", "#E07A10", -0.34)]
    x0, y0, z0 = loc
    objs = []
    for pts, k, c, hd, dy in layers:
        sc = [(x * k * scale, (y * k - (1 - k) * 0.95) * scale) for x, y in pts]
        m = P(c, hue_dark=hd, shade=0.75, light=0.4, light_col="#FFFFD0", spec=0.25, rough=0.3, rim=0.2,
              emit=c, emit_strength=0.35 if glow_ else 0, ao=0.2)
        objs.append(glyph([sc], m, depth=0.3 * scale, bevel=0.1 * scale, smooth_curve=True,
                          loc=(x0, y0 + dy * scale, z0 + 0.95 * scale)))
    return objs


@icon("flame", el=10, az=-10)
def _flame():
    flame()


@icon("food", el=16, az=-14)
def _food():
    apple = P("#E8302A", hue_dark="#5A0A50", light=0.35, light_col="#FFE0B0", spec=0.6, rough=0.15)
    prof = [(0.0, -0.02), (0.25, 0.0), (0.62, 0.12), (0.86, 0.4), (0.92, 0.72), (0.8, 1.02), (0.5, 1.16),
            (0.18, 1.08), (0.0, 0.98)]
    lathe(prof, (-0.55, 0.45, 0.0), apple, verts=36, sub=2)
    tube([(-0.55, 0.45, 0.98), (-0.52, 0.45, 1.22), (-0.43, 0.45, 1.38)], 0.05, M("bark"))
    glyph([[(0, 0), (0.25, 0.14), (0.55, 0.12), (0.72, 0.0), (0.5, -0.1), (0.22, -0.1)]], M("leaf"), depth=0.04,
          bevel=0.025, smooth_curve=True, loc=(-0.47, 0.45, 1.28), rot=(70, -25, 10))
    # roast leg, lying diagonally in front
    meat = P("#C8612A", hue_dark="#4A0A2A", light=0.42, light_col="#FFD890", spec=0.55, rough=0.2)
    bone = P("#F6EBD6", hue_dark="#7A5A7A", shade=0.66, light=0.25, spec=0.3)
    parts = [lathe([(0.0, 0.0), (0.35, 0.05), (0.58, 0.3), (0.62, 0.62), (0.5, 0.95), (0.3, 1.15), (0.17, 1.25),
                    (0.0, 1.27)], (0, 0, 0), meat, verts=32, sub=2)]
    parts.append(cylinder(0.12, 0.6, (0, 0, 1.45), bone, bevel=0.03))
    parts.append(sphere(0.16, (0.1, 0, 1.78), bone))
    parts.append(sphere(0.16, (-0.1, 0, 1.8), bone))
    L.parent(parts, loc=(0.1, -0.55, 0.05), rot=(0, 58, -15), scale=1.1)


@icon("research", el=14, az=-12)
def _research():
    glass = P("#DDF2FF", hue_dark="#3A4A9A", shade=0.72, light=0.35, light_col="#FFFFFF", spec=0.7, rough=0.1)
    lathe([(0.0, 0.0), (0.55, 0.05), (0.85, 0.35), (0.88, 0.62), (0.7, 0.95), (0.28, 1.15), (0.24, 1.7),
           (0.3, 1.78), (0.0, 1.8)], (0, 0, 0), glass, verts=40, sub=2)
    lathe([(0.0, 0.03), (0.6, 0.06), (0.9, 0.36), (0.93, 0.62), (0.0, 0.62)], (0, 0.0, 0.0),
          P("#6CE04A", hue_dark="#0A4A3A", light=0.45, light_col="#F4FFB0", spec=0.4, emit="#60FF40",
            emit_strength=0.25), verts=40, sub=1)
    cylinder(0.26, 0.3, (0, 0, 1.88), P("#B87A44", hue_dark="#4A1A30", spec=0.2), bevel=0.06)
    for x, z, r in ((-0.2, 0.9, 0.08), (0.12, 1.15, 0.06), (-0.05, 1.42, 0.05)):
        sphere(r, (x, -0.3, z), P("#C8FFB0", spec=0.4, rim=0))


# ---------------------------------------------------------------- objects
@icon("bell", el=14, az=-12)
def _bell():
    gold = M("gold")
    prof = [(0.0, 0.1), (0.95, 0.0), (1.02, 0.1), (0.94, 0.24), (0.72, 0.5), (0.6, 0.95), (0.55, 1.2), (0.38, 1.36),
            (0.0, 1.4)]
    parts = [lathe(prof, (0, 0, 0), gold, verts=40, sub=2)]
    parts.append(sphere(0.22, (0, 0, -0.1), P("#D98A1A", hue_dark="#6A1A0A", spec=0.6, metal=True)))
    parts.append(tube([(0.2 * math.cos(t), 0, 1.52 + 0.2 * math.sin(t)) for t in
                       [2 * math.pi * k / 20 for k in range(20)]], 0.06, gold, cyclic=True))
    L.parent(parts, rot=(0, 12, 0))


@icon("trash", el=16, az=-12)
def _trash():
    steel = P("#9FB3C6", hue_dark="#1A2A6A", shade=0.5, light=0.45, light_col="#FFFFFF", spec=0.6, rough=0.18,
              metal=True)
    bm = bmesh.new()
    seg = 48
    rings = [(0.68, -0.9), (0.78, 0.55), (0.8, 0.62)]
    vs = []
    for r, z in rings:
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            rr = r * (1 - 0.045 * max(0, math.cos(10 * a)) ** 4) if z < 0.6 else r
            ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), z)))
        vs.append(ring)
    for a_, b_ in zip(vs, vs[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            bm.faces.new((a_[i], a_[j], b_[j], b_[i]))
    bm.faces.new(list(reversed(vs[0])))
    bm.faces.new(vs[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = L.from_bmesh("Bin", bm, steel)
    L.mod_bevel(o, 0.04, 3, "ANGLE", 40)
    L.smooth(o)
    cylinder(0.9, 0.18, (0, 0, 0.78), steel, r2=0.84, bevel=0.06)
    tube([(-0.3, 0, 0.86), (-0.25, 0, 1.1), (0.25, 0, 1.1), (0.3, 0, 0.86)], 0.07, M("iron"))


@icon("lock", **GLYPH)
def _lock():
    tube([(-0.5, 0, 0.3), (-0.52, 0, 1.0), (-0.3, 0, 1.4), (0.0, 0, 1.5), (0.3, 0, 1.4), (0.52, 0, 1.0),
          (0.5, 0, 0.3)], 0.15, M("steel"))
    box((1.55, 0.6, 1.2), (0, 0, 0), M("gold"), bevel=0.2, segs=4)
    hole = P("#4A2A10", hue_dark="#1A0A08", shade=0.5, spec=0, rim=0, concave=True)
    disc(0.17, 0.1, hole, loc=(0, -0.3, 0.12), bevel=0.02)
    box((0.14, 0.1, 0.36), (0, -0.3, -0.12), hole, bevel=0.03)


@icon("trophy", el=14, az=-12)
def _trophy():
    gold = M("gold")
    prof = [(0.0, 0.0), (0.5, 0.0), (0.52, 0.1), (0.22, 0.22), (0.14, 0.55), (0.16, 0.7), (0.62, 0.95),
            (0.8, 1.35), (0.86, 1.72), (0.92, 1.8), (0.0, 1.8)]
    lathe(prof, (0, 0, 0.3), gold, verts=40, sub=2)
    for sx in (-1, 1):
        tube([(sx * 0.78, 0, 1.95), (sx * 1.18, 0, 1.85), (sx * 1.1, 0, 1.4), (sx * 0.62, 0, 1.2)], 0.09, gold)
    box((1.3, 0.9, 0.38), (0, 0, 0.12), M("wood_dark"), bevel=0.08)
    box((0.6, 0.06, 0.18), (0, -0.47, 0.12), gold, bevel=0.03)
    glyph([L.rounded_poly(star_pts(0.32, 0.14, 5, 90), 0.02)], P("#FFE680", spec=0.6, metal=True), depth=0.08,
          bevel=0.03, loc=(0, -0.72, 1.55))


@icon("settings", **GLYPH)
def _settings():
    teeth, r_out, r_in = 8, 1.0, 0.8
    pts = []
    for k in range(teeth):
        a0 = 2 * math.pi * k / teeth
        w = math.pi / teeth
        for da, r in ((-w * 0.98, r_in), (-w * 0.42, r_in), (-w * 0.32, r_out), (w * 0.32, r_out), (w * 0.42, r_in),
                      (w * 0.98, r_in)):
            pts.append((r * math.cos(a0 + da), r * math.sin(a0 + da)))
    gear = L.rounded_poly(pts, 0.04, 3)
    hole = circle_pts(0.32, 40)
    glyph([gear, hole], P("#AEBFD2", hue_dark="#1A2466", shade=0.5, light=0.45, light_col="#FFFFFF", spec=0.65,
                           rough=0.18, metal=True), depth=0.4, bevel=0.1)


@icon("sick", **GLYPH)
def _sick():
    pts = []
    for i in range(72):
        t = 2 * math.pi * i / 72
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((x / 16, y / 16 + 0.1))
    pts = [pts[0]] + pts[3:34] + pts[39:70]  # drop the points crowding the two cusps
    pts = L.rounded_poly(pts, 0.1, 4)
    glyph([pts], P("#EE3340", hue_dark="#5A0A4A", light=0.4, light_col="#FFD8D0", spec=0.6, rough=0.15), depth=0.55,
          bevel=0.16)
    plaster = P("#FBEFD8", hue_dark="#8A5A7A", shade=0.7, light=0.25, spec=0.25)
    box((1.15, 0.08, 0.36), (0.12, -0.5, 0.02), plaster, rot=(0, 35, 0), bevel=0.05)
    box((0.3, 0.06, 0.28), (0.12, -0.55, 0.02), P("#E8CFA8", spec=0.1), rot=(0, 35, 0), bevel=0.03)


@icon("eye", **GLYPH)
def _eye():
    eye()


@icon("eye_off", **GLYPH)
def _eye_off():
    eye()
    box((2.3, 0.18, 0.26), (0, -0.62, 0), M("red"), rot=(0, 35, 0), bevel=0.1)


def eye():
    pts = []
    n = 32
    for i in range(n + 1):
        t = -1 + 2 * i / n
        pts.append((t * 1.1, 0.62 * (1 - t * t) ** 0.75))
    for i in range(1, n):
        t = 1 - 2 * i / n
        pts.append((t * 1.1, -0.62 * (1 - t * t) ** 0.75))
    glyph([pts], M("white"), depth=0.36, bevel=0.14)
    sphere(0.46, (0, -0.24, 0), M("blue"), scale=(1, 0.32, 1))
    sphere(0.22, (0, -0.37, 0), M("ink"), scale=(1, 0.3, 1))
    sphere(0.09, (-0.14, -0.43, 0.14), P("#FFFFFF", spec=0, rim=0, ao=0, edge=0, shade=1, light=0), scale=(1, 0.3, 1))


@icon("lantern", el=14, az=-12)
def _lantern():
    iron = M("iron")
    cylinder(0.62, 0.22, (0, 0, -0.75), iron, bevel=0.06)
    box((0.95, 0.95, 1.2), (0, 0, 0.0), P("#FFD24A", hue_dark="#E06A10", shade=0.85, light=0.6, light_col="#FFFFF0",
                                          spec=0.5, rough=0.1, rim=0.1, edge=0.1, ao=0.1, emit="#FFB020",
                                          emit_strength=0.9), bevel=0.06)
    for x in (-0.5, 0.5):
        for y in (-0.5, 0.5):
            box((0.12, 0.12, 1.3), (x, y, 0.0), iron, bevel=0.03)
    cylinder(0.72, 0.3, (0, 0, 0.75), iron, r2=0.3, bevel=0.06)
    cylinder(0.25, 0.18, (0, 0, 0.98), iron, bevel=0.04)
    tube([(0.28 * math.cos(t), 0, 1.3 + 0.28 * math.sin(t)) for t in [2 * math.pi * k / 20 for k in range(20)]], 0.06,
         iron, cyclic=True)
    sphere(0.16, (0, 0, -0.2), P("#FFF8D0", spec=0, rim=0, ao=0, edge=0, emit="#FFFFFF", emit_strength=1.5))


@icon("fence", el=14, az=-16)
def _fence():
    rnd = random.Random(2)
    for z in (-0.25, 0.35):
        box((2.5, 0.12, 0.2), (0, 0.13, z), M("wood_dark"), bevel=0.04)
    for k in range(4):
        x = -0.9 + k * 0.6
        h = 1.6 + rnd.uniform(-0.08, 0.08)
        pk = [(-0.2, -0.8), (0.2, -0.8), (0.2, h - 1.1), (0.0, h - 0.82), (-0.2, h - 1.1)]
        c = ("#C9773A", "#D98A48", "#C06E34", "#D4823F")[k]
        glyph([L.rounded_poly(pk, 0.05)], P(c, hue_dark="#4A1440", spec=0.2, rough=0.4), depth=0.15, bevel=0.04,
              loc=(x, 0, 0), rot=(90, rnd.uniform(-4, 4), 0))
        sphere(0.04, (x, -0.11, 0.35), M("iron"))
        sphere(0.04, (x, -0.11, -0.25), M("iron"))


@icon("road", el=14, az=-14)
def _road():
    post = M("wood_dark")
    box((0.2, 0.2, 2.2), (0, 0, 0.1), post, bevel=0.04)
    board = M("wood_light")
    pts = [(-0.75, -0.2), (0.6, -0.2), (0.85, 0.0), (0.6, 0.2), (-0.75, 0.2)]
    glyph([L.rounded_poly(pts, 0.05)], board, depth=0.12, bevel=0.035, loc=(0.35, -0.14, 0.9))
    pts2 = [(0.75, -0.18), (-0.55, -0.18), (-0.8, 0.0), (-0.55, 0.18), (0.75, 0.18)]
    glyph([L.rounded_poly(pts2, 0.05)], board, depth=0.12, bevel=0.035, loc=(-0.3, -0.14, 0.42))
    rk = P("#A9AFB8", hue_dark="#2A2A5A", shade=0.5, light=0.35, spec=0.2)
    for i, (x, y, r) in enumerate(((-0.4, -0.1, 0.3), (0.35, -0.2, 0.26), (0.0, 0.25, 0.3), (0.7, 0.15, 0.2))):
        L.hull_rock(r, (x, y, -0.95), rk, seed=60 + i, scale=(1.3, 1.1, 0.6), n=11, bevel=0.1)
    sphere(0.9, (0, 0.1, -1.1), P("#7CB843", hue_dark="#123A3A", spec=0.1), scale=(1.3, 1.0, 0.25))


@icon("landmark", el=12, az=-16)
def _landmark():
    st = P("#EBDDBE", hue_dark="#6A4A7A", shade=0.58, light=0.3, spec=0.15, rough=0.5)
    st2 = P("#D9C49C", hue_dark="#6A3A6A", shade=0.58, light=0.25, spec=0.15)
    box((2.4, 1.3, 0.2), (0, 0, -0.1), st2, bevel=0.04)
    box((2.15, 1.1, 0.2), (0, 0, 0.1), st, bevel=0.04)
    for k in range(4):
        cylinder(0.15, 1.2, (-0.78 + k * 0.52, -0.25, 0.8), st, bevel=0.02, verts=16)
        box((0.36, 0.36, 0.1), (-0.78 + k * 0.52, -0.25, 0.25), st2, bevel=0.02)
        box((0.36, 0.36, 0.1), (-0.78 + k * 0.52, -0.25, 1.43), st2, bevel=0.02)
    box((2.2, 1.0, 0.95), (0, 0.15, 0.85), st2, bevel=0.03)
    box((2.3, 1.15, 0.22), (0, 0, 1.58), st, bevel=0.04)
    glyph([L.rounded_poly([(-1.25, 0), (1.25, 0), (0, 0.62)], [0.04, 0.04, 0.08])],
          P("#D0563A", hue_dark="#5A0A3A", spec=0.3), depth=1.1, bevel=0.04, loc=(0, 0.0, 1.68))
    disc(0.16, 0.08, M("gold"), loc=(0, -0.62, 1.93), bevel=0.03)


@icon("home", el=16, az=-22)
def _home():
    wall = P("#F4DDB0", hue_dark="#8A4A60", shade=0.6, light=0.3, spec=0.1, rough=0.6)
    box((1.8, 1.3, 1.15), (0, 0, 0.0), wall, bevel=0.04)
    beam = M("wood_dark")
    for x in (-0.88, 0.88):
        box((0.12, 0.08, 1.15), (x, -0.66, 0.0), beam, bevel=0.02)
    box((1.9, 0.08, 0.12), (0, -0.66, 0.55), beam, bevel=0.02)
    gable = glyph([[(-0.65, 0.0), (0.65, 0.0), (0.0, 0.62)]], wall, depth=1.75, bevel=0.0,
                  loc=(0, 0, 0.57), rot=(90, 0, 90))
    roof = P("#E0503A", hue_dark="#5A0A3A", light=0.35, light_col="#FFD8B0", spec=0.35, rough=0.25)
    ang = math.degrees(math.atan2(0.75, 0.82))
    for sgn in (-1, 1):
        box((2.2, 1.12, 0.16), (0, sgn * 0.42, 1.0), roof, rot=(-sgn * ang, 0, 0), bevel=0.06)
    box((2.25, 0.2, 0.2), (0, 0, 1.38), P("#B83A28", spec=0.3), bevel=0.06)
    box((0.3, 0.3, 0.55), (0.55, 0.2, 1.4), P("#B9A9A0", hue_dark="#3A2A5A", spec=0.15), bevel=0.04)
    door = [(-0.24, -0.55), (0.24, -0.55), (0.24, 0.12), (0.0, 0.3), (-0.24, 0.12)]
    glyph([L.rounded_poly(door, [0, 0, 0.12, 0.2, 0.12])], M("wood"), depth=0.08, bevel=0.02, loc=(-0.3, -0.68, -0.02))
    win = M("glow")
    box((0.42, 0.06, 0.38), (0.42, -0.68, 0.05), win, bevel=0.03)
    box((0.06, 0.08, 0.38), (0.42, -0.7, 0.05), beam, bevel=0.01)
    box((0.42, 0.08, 0.06), (0.42, -0.7, 0.05), beam, bevel=0.01)


@icon("storage", el=18, az=-22)
def _storage():
    plank = P("#D99A55", hue_dark="#5A1A3A", light=0.35, spec=0.15, rough=0.5, pattern=("bands", 1.5, "#B87436",
                                                                                         0.5, 0.2))
    frame = P("#9C5426", hue_dark="#3A0C30", spec=0.2)
    box((1.7, 1.7, 1.55), (0, 0, 0), plank, bevel=0.03)
    s = 0.86
    for x in (-s, s):
        for y in (-s, s):
            box((0.18, 0.18, 1.62), (x, y, 0), frame, bevel=0.04)
    for z in (-0.78, 0.78):
        for y in (-s, s):
            box((1.85, 0.18, 0.18), (0, y, z), frame, bevel=0.04)
        for x in (-s, s):
            box((0.18, 1.85, 0.18), (x, 0, z), frame, bevel=0.04)
    box((0.18, 0.1, 2.0), (0, -0.88, 0), frame, rot=(0, 45, 0), bevel=0.04)
    for x in (-s, s):
        for z in (-0.78, 0.78):
            box((0.24, 0.24, 0.24), (x, -s, z), M("iron"), bevel=0.05)


@icon("goals", el=10, az=-24)
def _goals():
    rings = [(1.0, "#EE3B2E"), (0.78, "#FBF5EA"), (0.56, "#EE3B2E"), (0.34, "#FBF5EA"), (0.14, "#FFC21F")]
    for i, (r, c) in enumerate(rings):
        disc(r, 0.3 + i * 0.04, P(c, hue_dark="#5A0A40" if c != "#FBF5EA" else "#5A5AA0",
                                  shade=0.55 if c != "#FBF5EA" else 0.68, light=0.3, spec=0.4, rough=0.2),
             loc=(0, -i * 0.02, 0), bevel=0.06)
    box((0.2, 0.2, 0.9), (0.0, 0.4, -0.8), M("wood_dark"), rot=(-20, 0, 0), bevel=0.04)
    shaft = M("wood_light")
    tube([(0.0, -0.2, 0.0), (0.9, -1.4, 0.75)], 0.05, shaft, smooth_path=False)
    for ang in (0, 120, 240):
        f = glyph([[(0, 0), (0.35, 0.05), (0.42, 0.16), (0.05, 0.12)]], M("red"), depth=0.03, bevel=0.015,
                  loc=(0.8, -1.25, 0.66), rot=(90 + ang, 0, 0))
        f.rotation_mode = "XYZ"


@icon("customs", el=14, az=-20)
def _customs():
    cover = P("#B3362C", hue_dark="#4A0A3A", light=0.32, light_col="#FFC8A0", spec=0.35, rough=0.25)
    pages = M("paper")
    box((1.5, 0.42, 1.95), (0.05, 0, 0), pages, bevel=0.03)
    for y in (-0.24, 0.24):
        box((1.65, 0.1, 2.08), (0, y, 0), cover, bevel=0.04)
    box((0.16, 0.58, 2.08), (-0.8, 0, 0), cover, bevel=0.07)
    gold = M("gold")
    glyph([[(0.82, 1.03), (0.44, 1.03), (0.82, 0.65)]], gold, depth=0.06, bevel=0.02, loc=(0, -0.3, 0))
    glyph([[(0.82, -1.03), (0.82, -0.65), (0.44, -1.03)]], gold, depth=0.06, bevel=0.02, loc=(0, -0.3, 0))
    box((1.1, 0.04, 0.12), (0.0, -0.3, 0.55), gold, bevel=0.02)
    seal = P("#D9302A", hue_dark="#5A0A40", light=0.35, spec=0.6, rough=0.18)
    glyph([L.rounded_poly(star_pts(0.42, 0.36, 12, 90), 0.03)], seal, depth=0.14, bevel=0.06,
          loc=(0.3, -0.36, -0.35))
    glyph([L.rounded_poly(star_pts(0.18, 0.08, 5, 90), 0.02)], P("#FF7A6A", spec=0.4), depth=0.04, bevel=0.02,
          loc=(0.3, -0.48, -0.35))
    for sx in (-1, 1):
        glyph([[(0, 0), (0.12 * sx, -0.55), (0.0, -0.45), (-0.08 * sx, -0.58), (-0.04 * sx, 0)]], seal, depth=0.05,
              bevel=0.02, loc=(0.3 + 0.1 * sx, -0.31, -0.6))


@icon("legacy", el=14, az=-14)
def _legacy():
    paper = M("paper")
    box((1.5, 0.08, 1.75), (0, 0, 0), paper, bevel=0.03)
    for z in (0.95, -0.95):
        cylinder(0.22, 1.75, (0, 0.02, z), paper, rot=(0, 90, 0), bevel=0.05)
        for sx in (-1, 1):
            sphere(0.14, (sx * 0.95, 0.02, z), M("wood"))
    for k in range(4):
        w = 1.0 if k < 3 else 0.6
        box((w, 0.04, 0.07), (-0.05 - (0.2 if k == 3 else 0), -0.06, 0.45 - k * 0.25), P("#B88A58", spec=0, ao=0),
            bevel=0.02)
    seal = P("#D9302A", hue_dark="#5A0A40", light=0.35, spec=0.6, rough=0.18)
    disc(0.26, 0.12, seal, loc=(0.35, -0.12, -0.55), bevel=0.05)
    for sx in (-1, 1):
        glyph([[(0, 0), (0.1 * sx, -0.42), (0.0, -0.34), (-0.07 * sx, -0.45), (-0.03 * sx, 0)]], seal, depth=0.04,
              bevel=0.02, loc=(0.35 + 0.08 * sx, -0.08, -0.7))


@icon("knowledge", el=22, az=-14)
def _knowledge():
    cover = P("#3F7FD6", hue_dark="#141A6A", light=0.3, spec=0.35, rough=0.25)
    pages = M("paper")
    for sx in (-1, 1):
        box((1.05, 1.4, 0.12), (sx * 0.55, 0, -0.05), cover, rot=(0, -sx * 10, 0), bevel=0.04)
        box((0.95, 1.28, 0.2), (sx * 0.5, 0, 0.1), pages, rot=(0, -sx * 12, 0), bevel=0.05)
    for sx in (-1, 1):
        for k in range(3):
            box((0.55, 0.06, 0.03), (sx * 0.5, -0.3 + k * 0.25, 0.22 + 0.0), P("#C79A62", spec=0, ao=0),
                rot=(0, -sx * 12, 0), bevel=0.01)
    sphere(0.42, (0, -0.1, 0.95), P("#FFE27A", hue_dark="#FF9A20", shade=0.9, light=0.7, light_col="#FFFFFF",
                                     spec=0.3, rim=0, edge=0.1, ao=0, emit="#FFD040", emit_strength=1.0))
    m = P("#FFE680", hue_dark="#B0400A", light=0.5, spec=0.5, emit="#FFC020", emit_strength=0.4)
    for x, z, r in ((-0.75, 1.15, 0.2), (0.8, 1.35, 0.16), (0.55, 0.7, 0.12)):
        glyph([L.rounded_poly(star_pts(r, r * 0.35, 4, 90), 0.01)], m, depth=0.08, bevel=0.03, loc=(x, -0.3, z))


@icon("world", el=16, az=-16)
def _world():
    paper = M("paper")
    paper2 = P("#E6C990", hue_dark="#8A4A50", shade=0.6, light=0.3, spec=0.1)
    land = P("#7CC24A", hue_dark="#123A3A", spec=0.15)
    water = P("#5AB4F0", hue_dark="#1A2A7A", spec=0.3)
    red = P("#E0402E", spec=0.2, rim=0)
    for i, (x, ang) in enumerate(((-0.75, 18), (0.0, -18), (0.75, 18))):
        parts = [box((0.78, 0.06, 1.7), (0, 0, 0), paper if i != 1 else paper2, bevel=0.02)]
        if i == 0:
            parts.append(glyph([[(-0.3, 0.3), (0.1, 0.55), (0.3, 0.2), (0.0, -0.1), (-0.25, -0.05)]], land, depth=0.02,
                               bevel=0.01, smooth_curve=True, loc=(0, -0.05, 0.1)))
        if i == 1:
            parts.append(glyph([circle_pts(0.22, 20)], water, depth=0.02, bevel=0.01, loc=(0.0, -0.05, 0.45)))
            parts.append(glyph([[(-0.3, -0.5), (0.25, -0.3), (0.3, -0.7), (-0.1, -0.75)]], land, depth=0.02,
                               bevel=0.01, smooth_curve=True, loc=(0, -0.05, 0)))
        if i == 2:
            for a in (45, -45):
                parts.append(box((0.32, 0.03, 0.08), (0.05, -0.05, -0.35), red, rot=(0, a, 0), bevel=0.01))
        for k in range(3):
            parts.append(box((0.12, 0.03, 0.05), (-0.25 + k * 0.2 + (0.0 if i != 2 else -0.1), -0.05,
                                                   0.2 - k * 0.12 - i * 0.12), red, rot=(0, 20, 0), bevel=0.01))
        L.tag(parts, "panel%d" % i)
        L.parent(parts, loc=(x, 0, 0), rot=(0, 0, ang))


# ---------------------------------------------------------------- laws (the Book of Customs: one picture per custom)
def pot(r=1.0, h=1.1, m=None, loc=(0, 0, 0)):
    """A round-bellied iron cauldron (lathe), open at the top."""
    prof = [(0.0, 0.0), (0.45 * r, 0.02), (0.85 * r, 0.25 * h), (1.0 * r, 0.6 * h), (0.92 * r, 0.92 * h),
            (0.96 * r, 1.0 * h), (0.0, 1.0 * h)]
    return lathe(prof, loc, m or M("iron"), verts=40, sub=2)


@icon("law_shared_stew", el=24, az=-14)
def _law_shared_stew():
    iron = M("iron")
    pot(1.0, 1.1, iron, (0, 0, -0.55))
    disc(0.9, 0.06, P("#C88A3A", hue_dark="#5A2A10", light=0.35, spec=0.6, rough=0.15), loc=(0, 0, 0.5),
         axis_rot=(0, 0, 0), bevel=0.02)
    for x, y, c in ((-0.35, -0.2, "#F2A33A"), (0.3, 0.1, "#E8E0B0"), (0.0, 0.35, "#7CC24A"), (0.45, -0.3, "#F2A33A")):
        sphere(0.14, (x, y, 0.56), P(c, spec=0.4))
    tube([(0.75, -0.2, 1.7), (0.35, -0.1, 0.9), (0.15, 0.0, 0.5)], 0.06, M("wood_light"))
    sphere(0.2, (0.15, 0.0, 0.48), M("wood_light"), scale=(1, 1, 0.5))
    for sx in (-1, 1):
        tube([(sx * 0.98, 0, 0.35), (sx * 1.25, 0, 0.45), (sx * 1.2, 0, 0.7)], 0.07, iron)
    steam = P("#FFFFFF", shade=0.85, light=0.3, spec=0, rim=0.2, alpha=0.9)
    for x, z in ((-0.4, 1.05), (0.1, 1.35), (-0.15, 1.7)):
        sphere(0.2, (x, -0.1, z), steam, scale=(1, 0.6, 0.8))


@icon("law_hearty_meals", el=20, az=-14)
def _law_hearty_meals():
    bowl = P("#B8743E", hue_dark="#4A1A20", spec=0.25, rough=0.4)
    lathe([(0.0, 0.0), (0.5, 0.0), (1.05, 0.4), (1.2, 0.75), (1.1, 0.78), (0.0, 0.78)], (0, 0.2, -0.6), bowl,
          verts=40, sub=2)
    disc(1.08, 0.05, P("#D88A3A", hue_dark="#6A2A10", light=0.4, spec=0.5), loc=(0, 0.2, 0.12), axis_rot=(0, 0, 0))
    meat = P("#C8612A", hue_dark="#4A0A2A", light=0.42, light_col="#FFD890", spec=0.55, rough=0.2)
    bone = P("#F6EBD6", hue_dark="#7A5A7A", shade=0.66, light=0.25, spec=0.3)
    parts = [lathe([(0.0, 0.0), (0.32, 0.05), (0.5, 0.3), (0.52, 0.6), (0.4, 0.9), (0.2, 1.05), (0.0, 1.08)],
                   (0, 0, 0), meat, verts=32, sub=2)]
    parts.append(cylinder(0.1, 0.5, (0, 0, 1.25), bone, bevel=0.03))
    parts.append(sphere(0.14, (0.08, 0, 1.52), bone))
    parts.append(sphere(0.14, (-0.08, 0, 1.54), bone))
    L.parent(parts, loc=(-0.3, 0.0, 0.1), rot=(0, 50, -10))
    bread = P("#E6A858", hue_dark="#7A3A10", light=0.4, spec=0.3)
    sphere(0.48, (0.55, -0.2, 0.3), bread, scale=(1.1, 0.8, 0.6))
    for k in range(3):
        box((0.06, 0.6, 0.05), (0.38 + k * 0.18, -0.2, 0.58), P("#B86A2A", spec=0, ao=0), rot=(0, 0, 30), bevel=0.02)


@icon("law_long_days", **GLYPH)
def _law_long_days():
    wood_ = M("wood")
    for z in (-1.05, 1.05):
        cylinder(0.85, 0.18, (0, 0, z), wood_, bevel=0.06)
    for a in (45, 135, 225, 315):
        x, y = 0.7 * math.cos(math.radians(a)), 0.7 * math.sin(math.radians(a))
        cylinder(0.07, 2.0, (x, y, 0), M("wood_dark"), bevel=0.02)
    glass = P("#DDF2FF", hue_dark="#3A4A9A", shade=0.72, light=0.35, light_col="#FFFFFF", spec=0.8, rough=0.08, alpha=0.75)
    prof = [(0.0, -0.95), (0.55, -0.9), (0.6, -0.5), (0.12, -0.05), (0.12, 0.05), (0.6, 0.5), (0.55, 0.9), (0.0, 0.95)]
    lathe(prof, (0, 0, 0), glass, verts=36, sub=2)
    sand = P("#F2C060", hue_dark="#8A4A10", light=0.4, spec=0.2)
    lathe([(0.0, -0.92), (0.5, -0.88), (0.45, -0.55), (0.0, -0.4)], (0, 0, 0), sand, verts=32, sub=1)
    lathe([(0.0, 0.2), (0.3, 0.32), (0.5, 0.6), (0.0, 0.6)], (0, 0, 0), sand, verts=32, sub=1)
    tube([(0, -0.05, 0.1), (0, -0.05, -0.45)], 0.03, sand)


@icon("law_dawn_to_dusk", el=10, az=-10)
def _law_dawn_to_dusk():
    sun = P("#FFC21F", hue_dark="#C0400A", shade=0.8, light=0.6, light_col="#FFFFE0", spec=0.4, emit="#FFB020",
            emit_strength=0.5)
    glyph([circle_pts(0.75, 48, 0, 0, 0, 180, closed=False)], sun, depth=0.3, bevel=0.08,
          loc=(0, 0, -0.1))
    for k in range(7):
        a = math.radians(15 + k * 25)
        ray = [(-0.1, 0.0), (0.1, 0.0), (0.0, 0.45)]
        glyph([ray], sun, depth=0.2, bevel=0.05, loc=(1.1 * math.cos(a), 0, -0.1 + 1.1 * math.sin(a)),
                  rot=(90, -(math.degrees(a) - 90), 0))
    hill = P("#6FB43A", hue_dark="#123A3A", spec=0.2)
    glyph([L.rounded_poly([(-1.6, -0.12), (-0.8, 0.02), (0, -0.06), (0.8, 0.04), (1.6, -0.12), (1.6, -0.5),
                           (-1.6, -0.5)], 0.05)], hill, depth=0.5, bevel=0.08, loc=(0, -0.3, -0.12))
    glyph([L.rounded_poly([(-1.6, -0.3), (-0.4, -0.18), (0.7, -0.32), (1.6, -0.22), (1.6, -0.62), (-1.6, -0.62)], 0.05)],
          P("#4E9A2A", hue_dark="#0A2A2A", spec=0.2), depth=0.4, bevel=0.08, loc=(0, -0.55, -0.12))


@icon("law_fire_wardens", **TOOL)
def _law_fire_wardens():
    parts = [handle(2.4, 0.13)]
    parts += wraps(0.55, 3, 0.16, P("#C9A060", spec=0.2))
    parts.append(cylinder(0.26, 0.5, (0, 0, 1.2), M("iron"), r2=0.36, bevel=0.05))
    L.parent(parts, rot=(0, 18, 0))
    fl = flame(scale=0.62, loc=(0.32, -0.1, 1.15))
    for o in fl:
        o.rotation_euler.y = math.radians(18)


@icon("law_bitter_remedies", el=22, az=-14)
def _law_bitter_remedies():
    stone = P("#A9AFB8", hue_dark="#2A2A5A", shade=0.5, light=0.35, spec=0.25)
    lathe([(0.0, 0.0), (0.7, 0.0), (0.85, 0.15), (1.0, 0.75), (0.9, 0.8), (0.0, 0.6)], (0, 0, -0.7), stone,
          verts=40, sub=2)
    disc(0.82, 0.04, P("#6CB83A", hue_dark="#0A4A2A", light=0.35, spec=0.4), loc=(0, 0, 0.02), axis_rot=(0, 0, 0))
    tube([(0.2, 0, -0.1), (0.75, 0, 1.25)], 0.12, P("#C9CED6", hue_dark="#2A2C66", spec=0.4), radii=[1.3, 0.9])
    leaf = M("leaf")
    for x, a in ((-0.6, 30), (-0.9, 70), (0.9, -40)):
        glyph([[(0, 0), (0.22, 0.12), (0.5, 0.1), (0.7, 0.0), (0.5, -0.1), (0.22, -0.1)]], leaf, depth=0.04,
              bevel=0.02, smooth_curve=True, loc=(x, -0.6, -0.55), rot=(90, a, 0))


@icon("law_watch_the_sick", el=14, az=-12)
def _law_watch_the_sick():
    brass = M("gold")
    cylinder(0.75, 0.12, (0, 0, -0.95), brass, bevel=0.05)
    tube([(0.72, 0, -0.9), (1.1, 0, -0.7), (1.05, 0, -0.35), (0.8, 0, -0.45)], 0.07, brass)
    wax = P("#FBF3E0", hue_dark="#8A6A60", shade=0.66, light=0.3, spec=0.3, rough=0.3)
    cylinder(0.32, 1.5, (0, 0, -0.15), wax, bevel=0.06)
    for x, z in ((0.25, 0.4), (-0.22, 0.2)):
        sphere(0.09, (x, -0.28, z), wax, scale=(0.7, 0.7, 1.6))
    tube([(0, 0, 0.6), (0, 0, 0.75)], 0.03, P("#3A2314", spec=0, rim=0))
    flame(scale=0.32, loc=(0, -0.05, 0.62))


@icon("law_apprentices", **TOOL)
def _law_apprentices():
    iron = M("iron")
    steel = M("steel")
    anvil = [(-1.2, 0.35), (-0.55, 0.35), (-0.4, 0.5), (0.9, 0.5), (0.95, 0.25), (0.45, 0.1), (0.3, -0.35),
             (0.6, -0.65), (-0.6, -0.65), (-0.3, -0.35), (-0.45, 0.1), (-1.05, 0.2)]
    glyph([L.rounded_poly(anvil, 0.04)], iron, depth=0.7, bevel=0.07, loc=(0, 0, -0.55))
    parts = [handle(1.7, 0.09)]
    parts.append(box((0.8, 0.36, 0.38), (0.1, 0, 0.8), steel, bevel=0.08))
    L.parent(parts, loc=(0.25, -0.5, 0.35), rot=(0, -45, 0))
    for x, z in ((0.7, 0.25), (0.95, 0.45), (0.6, 0.55)):
        glyph([L.rounded_poly(star_pts(0.12, 0.04, 4, 45), 0.01)], P("#FFD24A", emit="#FFB020", emit_strength=0.8,
                                                                       spec=0.2), depth=0.04, bevel=0.01,
              loc=(x, -0.8, z))


@icon("law_day_of_rest", el=12, az=-12)
def _law_day_of_rest():
    m = paint("Moon", "#8A9CF4", rough=0.3, spec=0.55, coat=0.35, hue_dark="#24186A", shade=0.42, light=0.25,
              light_col="#EEF2FF")
    shape2d([crescent()], depth=0.5, bevel=0.17, m=m, loc=(-0.35, 0, -0.15), rot=(90, 0, -18), res=6)
    zm = P("#FBF8F2", hue_dark="#5A5AA0", shade=0.66, light=0.2, spec=0.45, rough=0.2)
    for (x, z, s) in ((0.55, 0.35, 0.7), (1.0, 0.95, 0.52), (1.3, 1.4, 0.38)):
        text3d("z", size=s, depth=0.12, bevel=0.03, m=zm, loc=(x, -0.3, z))
    pillow = P("#F6E2B8", hue_dark="#8A4A60", shade=0.62, light=0.3, spec=0.15, rough=0.5)
    box((2.2, 0.9, 0.42), (0.1, 0.2, -1.0), pillow, bevel=0.2, segs=4)


@icon("law_burial_rites", el=14, az=-16)
def _law_burial_rites():
    stone = P("#B4C2C0", hue_dark="#2C3260", shade=0.48, light=0.4, light_col="#F6FFF8", spec=0.2)
    pts = [(-0.75, -1.0), (0.75, -1.0), (0.75, 0.55)] + [(0.75 * math.cos(math.radians(a)), 0.55 + 0.75 * math.sin(math.radians(a))) for a in range(10, 171, 10)] + [(-0.75, 0.55)]
    glyph([pts], stone, depth=0.42, bevel=0.1, loc=(0, 0.1, 0.1))
    carve = P("#6A7A80", hue_dark="#1A2040", spec=0, rim=0, concave=True)
    box((0.12, 0.06, 0.9), (0, -0.14, 0.45), carve, bevel=0.02)
    box((0.55, 0.06, 0.12), (0, -0.14, 0.62), carve, bevel=0.02)
    earth = P("#7A5A3A", hue_dark="#2A1020", spec=0.1)
    sphere(1.2, (0, 0.1, -1.0), earth, scale=(1.25, 0.8, 0.28))
    for x, c in ((-0.75, "#F2E35A"), (-0.5, "#FFFFFF"), (0.65, "#E86A8A")):
        tube([(x, -0.5, -0.85), (x + 0.05, -0.5, -0.45)], 0.03, M("leaf"))
        for k in range(5):
            a = 2 * math.pi * k / 5
            sphere(0.09, (x + 0.05 + 0.1 * math.cos(a), -0.55, -0.4 + 0.1 * math.sin(a)), P(c, spec=0.3))
        sphere(0.07, (x + 0.05, -0.6, -0.4), P("#FFC21F", spec=0.3))


@icon("law_story_nights", el=12, az=-14)
def _law_story_nights():
    wood_ = M("wood")
    # a lyre: two curved arms on a sound box, a crossbar and strings
    box((1.3, 0.35, 0.55), (0, 0, -0.95), wood_, bevel=0.12)
    for sx in (-1, 1):
        tube([(sx * 0.5, 0, -0.75), (sx * 0.85, 0, -0.1), (sx * 0.65, 0, 0.6), (sx * 0.85, 0, 1.05)], 0.11, wood_)
    tube([(-0.95, 0, 0.85), (0.95, 0, 0.85)], 0.08, M("wood_dark"))
    gold = M("gold")
    for k in range(5):
        x = -0.36 + k * 0.18
        tube([(x, -0.05, -0.7), (x * 1.05, -0.05, 0.85)], 0.018, gold)
    star = P("#FFE680", hue_dark="#B0400A", light=0.5, spec=0.5, emit="#FFC020", emit_strength=0.4)
    for x, z, r in ((1.1, 1.3, 0.2), (-1.15, 1.2, 0.14)):
        glyph([L.rounded_poly(star_pts(r, r * 0.4, 5, 90), 0.01)], star, depth=0.08, bevel=0.03, loc=(x, -0.3, z))


@icon("law_harvest_festival", el=20, az=-14)
def _law_harvest_festival():
    basket = P("#C98A44", hue_dark="#5A2A10", spec=0.2, rough=0.5, pattern=("streaks", 9.0, "#8A5A2A", 0.5, 0.7))
    lathe([(0.0, 0.0), (0.7, 0.0), (1.1, 0.5), (1.2, 0.85), (1.1, 0.88), (0.0, 0.7)], (0, 0.2, -0.85), basket,
          verts=40, sub=2)
    tube([(-1.05, 0.2, 0.0), (-0.6, 0.2, 1.0), (0.6, 0.2, 1.0), (1.05, 0.2, 0.0)], 0.07, basket)
    apple = P("#E8302A", hue_dark="#5A0A50", light=0.35, light_col="#FFE0B0", spec=0.6, rough=0.15)
    pear = P("#A8D040", hue_dark="#2A5A10", light=0.4, spec=0.5)
    for x, y, c in ((-0.45, -0.2, apple), (0.35, -0.25, apple), (0.0, 0.1, pear), (-0.1, -0.45, pear)):
        sphere(0.32, (x, y, 0.1), c)
    grain = P("#F2C35A", hue_dark="#8A4A10", light=0.4, spec=0.3)
    for k, a in enumerate((-25, -10, 8, 22)):
        x0 = 0.55 + k * 0.08
        tube([(x0, 0.35, -0.1), (x0 + 0.5 * math.sin(math.radians(a + 20)), 0.35, 1.0)], 0.03, grain)
        sphere(0.11, (x0 + 0.5 * math.sin(math.radians(a + 20)), 0.35, 1.05), grain, scale=(0.7, 0.7, 1.8))


@icon("law_elders_council", el=14, az=-14)
def _law_elders_council():
    wood_ = M("wood")
    # the elder's staff: a crook with a leather grip and a ribbon, over a small round council table
    tube([(0.1, 0, -1.3), (0.15, 0, 0.6), (0.2, 0, 1.0), (0.5, 0, 1.3), (0.8, 0, 1.15), (0.78, 0, 0.85)], 0.11, wood_)
    wraps(0.05, 3, 0.14)
    ribbon = M("red")
    glyph([[(0, 0), (0.45, -0.1), (0.38, -0.55), (0.2, -0.35), (0.05, -0.6)]], ribbon, depth=0.04, bevel=0.02,
          loc=(0.2, -0.18, 0.75))
    table = M("wood_dark")
    cylinder(1.0, 0.16, (-0.35, 0.3, -0.75), table, bevel=0.05)
    cylinder(0.14, 0.6, (-0.35, 0.3, -1.1), table, bevel=0.03)
    for x, c in ((-0.85, "#4F8FDB"), (-0.35, "#6FB43A"), (0.15, "#F0A030")):
        cylinder(0.14, 0.5, (x, -0.1, -0.45), P(c, spec=0.2), bevel=0.06, r2=0.2)
        sphere(0.12, (x, -0.1, -0.12), M("skin"))


@icon("law_watchmen", el=12, az=-14)
def _law_watchmen():
    parts = [handle(3.4, 0.09)]
    blade = [(0, 0), (0.28, 0.35), (0.22, 0.8), (0, 1.25), (-0.22, 0.8), (-0.28, 0.35)]
    parts.append(glyph([blade], M("steel"), depth=0.16, bevel=0.05, smooth_curve=True, loc=(0, 0, 1.45)))
    L.parent(parts, loc=(0.35, 0.4, 0.2), rot=(0, 28, 0))
    shield = [(-0.95, 0.85), (0.95, 0.85), (0.9, -0.1), (0.5, -0.75), (0.0, -1.1), (-0.5, -0.75), (-0.9, -0.1)]
    glyph([L.rounded_poly(shield, 0.08)], M("blue"), depth=0.3, bevel=0.1, smooth_curve=True, loc=(-0.2, 0, -0.1))
    rim = M("iron")
    tube([(x, -0.17, y - 0.1) for x, y in L.rounded_poly(shield, 0.08)], 0.07, rim, loc=(-0.2, 0, 0), cyclic=True)
    disc(0.26, 0.2, M("gold"), loc=(-0.2, -0.2, 0.0), bevel=0.07)
    glyph([[(-0.08, 0.6), (0.08, 0.6), (0.08, -0.7), (-0.08, -0.7)]], M("gold"), depth=0.05, bevel=0.02,
          loc=(-0.2, -0.2, -0.05))


# ---------------------------------------------------------------- villager portraits (CC0 Quaternius people)
FACES = {
    "face_m1": ("Person_Farmer_Male", {"Main": "#4F8FDB", "Hair": "#6A3A1A"}),
    "face_m2": ("Person_Woodcutter_Male", {"Main": "#6FB43A", "Hair": "#2B1A10"}),
    "face_m3": ("Person_Hooded_Male", {"Hair": "#B8642A"}),
    "face_f1": ("Person_Hunter_Female", {"Hair": "#B8482A", "Hood": "#4E9A3A", "Main": "#5FA844",
                                         "Cloth1": "#8A5A32"}),
    "face_f2": ("Person_Gatherer_Female", {"Top": "#F0A030", "Main": "#F0A030", "Hair": "#5A2E18"}),
    "face_f3": ("Person_Weaver_Female", {"Top": "#3FB3A6", "Main": "#3FB3A6", "Hair": "#E8B850"}),
}
HEAD_PITCH = float(os.environ.get("HEAD_PITCH", "-20"))
PORTRAIT = dict(az=-20, el=7, fill=0.88, ink=0, key_color=(1.0, 0.85, 0.66))


def portrait(model, recolor):
    objs = L.import_fbx(os.path.join(L.MODELS, model + ".fbx"))
    for o in objs:
        if o.name.startswith("Prop_"):
            o.hide_render = True
    cache = {}
    for o in objs:
        if o.type != "MESH" or o.hide_render:
            continue
        o["ink_group"] = "body"
        for slot in o.material_slots:
            src = slot.material
            if src is None:
                continue
            if src.name not in cache:
                pr = src.node_tree.nodes.get("Principled BSDF")
                base = tuple(pr.inputs["Base Color"].default_value)[:3] if pr else (0.5, 0.5, 0.5)
                name = src.name.split(".")[0]
                if name in recolor:
                    base = L.lin(recolor[name])
                if name in ("Eyes",):
                    m = paint("Eyes", "#1A0E08", shade=0.8, light=0.1, spec=0.6, rough=0.1, rim=0, edge=0, ao=0)
                elif name == "EyeWhite":
                    m = paint("EyeWhite", "#F6F0E6", shade=0.8, light=0.1, spec=0.2, rim=0, edge=0, ao=0.2)
                elif name == "Skin":
                    m = paint("Skin", L.mix(base, L.lin("#F29A6C"), 0.55), hue_dark="#B02A4A", shade=0.68,
                              light=0.38, light_col="#FFE0B8", spec=0.08, rough=0.45, grad=(0.92, 1.08), edge=0.18,
                              rim=0.45, rim_col="#FFD9B0")
                elif name == "Hair":
                    m = paint("Hair", base, hue_dark="#1A0A30", shade=0.55, light=0.22, spec=0.07, rough=0.45,
                              grad=(0.85, 1.1))
                else:
                    m = paint(name, base, hue_dark="#2A1A5A", shade=0.55, light=0.28, spec=0.04, rough=0.6,
                              grad=(0.8, 1.1))
                cache[src.name] = m
            slot.material = cache[src.name]
    # friendlier cartoon proportions: a bigger head
    for arm in (o for o in objs if o.type == "ARMATURE"):
        # the relaxed idle pose (level head, looking ahead) instead of the importer's default "Carry"
        idle = next((a for a in bpy.data.actions if a.name.endswith("|Idle")), None)
        if idle and arm.animation_data:
            arm.animation_data.action = idle
            bpy.context.scene.frame_set(1)
        arm.rotation_euler.z += math.radians(-10)  # turn a little towards the camera: eyes at the viewer
        act = arm.animation_data.action if arm.animation_data else None
        if act:  # keep the pose, but let the head scale be ours
            for fc in [f for f in act.fcurves if f.data_path == 'pose.bones["Head"].scale' or
                       f.data_path in ('pose.bones["Head"].rotation_quaternion',
                                       'pose.bones["neck_01"].rotation_quaternion')]:
                act.fcurves.remove(fc)
        for b in ("Head", "neck_01"):  # neutral head, pitched up a touch so the eyes meet the camera
            if b in arm.pose.bones:
                arm.pose.bones[b].rotation_mode = "XYZ"
                arm.pose.bones[b].rotation_euler = (math.radians(HEAD_PITCH / 2), 0, 0)
        pb = arm.pose.bones.get("Head")
        if pb:
            pb.scale = (1.32, 1.32, 1.32)
    bpy.context.view_layer.update()
    pts = L.mesh_points([o for o in objs if o.type == "MESH" and not o.hide_render])
    top = max(p.z for p in pts)
    return top


def _face(name):
    model, recolor = FACES[name]

    def build():
        top = portrait(model, recolor)
        L.STATE["zmin"] = top - 0.52
    return build


for _n in FACES:
    # face_f1 is also the story advisor shown large: a more frontal view so she meets the reader's eye
    ICONS[_n] = (_face(_n), dict(PORTRAIT, az=-8) if _n == "face_f1" else PORTRAIT)


# ---------------------------------------------------------------- driver
def main():
    os.makedirs(RAW, exist_ok=True)
    names = ONLY or list(ICONS)
    for n in names:
        fn, cam = ICONS[n]
        cam = dict(cam)
        fill = cam.pop("fill", 0.86)
        ink = cam.pop("ink", 0)
        L.begin(samples=SAMPLES, **cam)
        if ink:
            L.ink_lines(ink)
        L.STATE["zmin"] = None
        fn()
        L.finish(os.path.join(RAW, n + ".png"), fill=fill, zmin=L.STATE["zmin"])
        print("rendered", n, flush=True)


main()
