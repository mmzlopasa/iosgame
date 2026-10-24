"""Blender (4.2): the loading-screen key art, painted from the game's own valley.

Builds Green Hollow (the valley in unity/Assets/Custom/Resources/Custom/Map/Map.png, 2 m a pixel) with the
Ultimate Nature packs, the village around the suggested campfire with the game's buildings (the Unity OBJ
export when it is there, else the detailed bw_ FBX models), villagers, a caravan on the trade road, and
renders the shots the loading screen cycles through:

  hollow   the valley at golden hour from over the lake: forests, the river to the mountains, the village
  village  the village up close: detailed houses, people at work, the hearth
  caravan  a caravan coming home along the trade road past the Trading Post
  market   families at the market stalls
  dusk     the village at blue hour, fires and windows lit

blender -b --factory-startup -P tools/loading/blender_loading_art.py -- <out dir>
        [--shots hollow,village] [--res 1920] [--samples 64] [--export <building-models dir>] [--draft]
"""
import json
import math
import os
import random
import sys

import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import scene_lib as L  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def opt(flag, default, cast=str):
    if flag in argv:
        i = argv.index(flag)
        v = cast(argv[i + 1])
        del argv[i:i + 2]
        return v
    return default


SHOTS = opt("--shots", "hollow,village,caravan,market,dusk").split(",")
RES = opt("--res", 1920, int)
SAMPLES = opt("--samples", 64, int)
EXPORT = opt("--export", os.environ.get("BUILDING_MODELS", ""))
DRAFT = "--draft" in argv
if DRAFT:
    argv.remove("--draft")
OUT = argv[0] if argv else "."
os.makedirs(OUT, exist_ok=True)

MAP = os.path.join(L.ASSETS, "Custom", "Resources", "Custom", "Map", "Map.png")
CELL = 2.0
CLASSES = {(110, 170, 70): "meadow", (30, 90, 40): "forest", (40, 110, 220): "water", (235, 215, 130): "sand",
           (150, 150, 150): "stony", (80, 70, 65): "mountain", (190, 90, 40): "ore", (255, 0, 255): "camp"}
rng = random.Random(7)
nrng = np.random.RandomState(7)


# MARK: - The map


def load_map():
    from PIL import Image
    from scipy import ndimage
    im = np.asarray(Image.open(MAP).convert("RGB"))
    n = im.shape[0]
    names = ["meadow", "forest", "water", "sand", "stony", "mountain"]
    cls = np.zeros(im.shape[:2], dtype=np.int64)
    camp = (0, 0)
    for y in range(n):
        for x in range(n):
            c = CLASSES.get(tuple(int(v) for v in im[y, x]), "meadow")
            if c == "camp":
                camp = (x, y)
                c = "meadow"
            if c == "ore":
                c = "mountain"
            cls[y, x] = names.index(c)
    # Upsample to 0.5 m and smooth the pixel steps away (soft masks, then the strongest class wins).
    up = 4
    soft = []
    for i in range(len(names)):
        m = ndimage.zoom((cls == i).astype(np.float32), up, order=1)
        soft.append(ndimage.gaussian_filter(m, 2.2))
    fine = np.argmax(np.stack(soft), 0)
    return names, cls, fine, camp, n


NAMES, CLS, FINE, CAMP, N = load_map()
RESF = CELL / 4  # metres per fine pixel
HALF = N * CELL / 2


def cell_to_world(cx, cy):
    return (cx + 0.5) * CELL - HALF, HALF - (cy + 0.5) * CELL


HEARTH = Vector((*cell_to_world(*CAMP), 0))


def fine_index(x, y):
    fx = np.clip(((x + HALF) / RESF).astype(np.int64), 0, FINE.shape[1] - 1)
    fy = np.clip(((HALF - y) / RESF).astype(np.int64), 0, FINE.shape[0] - 1)
    return fy, fx


def inside(x, y):
    return (np.abs(x) < HALF) & (np.abs(y) < HALF)


def class_at(x, y):
    fy, fx = fine_index(np.asarray(x, dtype=np.float64), np.asarray(y, dtype=np.float64))
    c = FINE[fy, fx]
    return np.where(inside(np.asarray(x), np.asarray(y)), c, NAMES.index("forest"))


def fields():
    """Distance fields in metres on the fine grid."""
    from scipy import ndimage
    out = {}
    for name in NAMES:
        m = FINE == NAMES.index(name)
        out["in_" + name] = ndimage.distance_transform_edt(m) * RESF      # depth inside the class
        out["to_" + name] = ndimage.distance_transform_edt(~m) * RESF     # distance to the class
    return out


F = fields()


def sample(field, x, y):
    fy, fx = fine_index(x, y)
    return F[field][fy, fx]


# MARK: - The village plan (metres from the hearth, +x east, +y north; face = point the door looks at)

V = [
    # key, sprite/model, x, y, face (x, y) or None (towards the hearth)
    ("village_hearth", 0, 0, None),
    ("elders_lodge", 0, 9.5, None),
    ("longhouse", -10.5, 7.5, None),
    ("granary", 9, 8.5, None),
    ("healers_hut", 11.5, 0.5, None),
    ("daub_house", -11.5, -1.5, None),
    ("daub_house", -17.5, 9.5, None),
    ("daub_house", 14.5, 14.5, None),
    ("daub_house", -4.5, 17.5, None),
    ("woven_shelter", -9.5, 15.5, None),
    ("woven_shelter", 6.5, 17.5, None),
    ("shelter", 16.5, 6, None),
    ("shelter", -16, 2.5, None),
    ("toolmaker", -6.5, -3.2, None),
    ("storage_pit", 5.5, -2.8, None),
    ("smokehouse", -14.5, -5, None),
    ("drying_rack", 2.5, -6.5, (2.5, 0)),
    ("bronze_workshop", -22, 1.5, None),
    ("copper_smelter", -26, 7.5, None),
    ("pottery_kiln", -21, 16, None),
    ("hunters_lodge", -30, 15, None),
    ("weavers_hut", -12, 23, None),
    ("wood_camp", -37, 22, None),
    ("sawpit", -31, 27, None),
    ("lookout_tower", 21, -3.5, None),
    ("herb_garden", 18.5, 21, None),
    ("market", 4, 28, (4, 17)),
    ("trading_post", 25, 37, (31, 33)),
]
FIELDS = [(25 + 4.6 * i, 7 + 4.6 * j) for i in range(3) for j in range(3)]
PASTURE = (-6, 36)
# The road: hearth -> market -> trading post -> up the valley along the river towards the mountains.
ROAD = [(0, 2), (2, 12), (4, 22), (8, 31), (16, 33), (24, 33.5), (33, 36), (42, 41), (52, 48), (60, 57), (68, 68), (76, 82)]
LANES = [[(0, 2), (-6, 4), (-14, 6), (-22, 8), (-30, 14), (-36, 20)], [(0, 2), (9, 3), (17, 2), (22, -1)],
         [(4, 22), (-4, 22), (-12, 20)], [(2, 12), (12, 12), (22, 12)]]


def world(x, y):
    return HEARTH.x + x, HEARTH.y + y


def dist_polyline(px, py, pts):
    best = np.full(np.shape(px), 1e9)
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        ax, ay = world(ax, ay)
        bx, by = world(bx, by)
        dx, dy = bx - ax, by - ay
        t = np.clip(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1)
        best = np.minimum(best, np.hypot(px - (ax + t * dx), py - (ay + t * dy)))
    return best


def footprint(key):
    content = json.load(open(os.path.join(L.ASSETS, "Resources", "content.json")))
    for b in content["buildings"]:
        if b["id"] == key:
            return b["footprint"]["width"] * CELL, b["footprint"]["height"] * CELL
    return CELL, CELL


# MARK: - Height


def height(x, y):
    x = np.asarray(x, dtype=np.float64)
    y = np.asarray(y, dtype=np.float64)
    ins = inside(x, y)
    h = 0.45 * L.fbm(x, y, 4, 0.025, seed=1)
    if True:
        f_in = sample("in_forest", x, y)
        h += np.where(ins, np.minimum(f_in, 34) / 34 * 5.5 * (0.6 + 0.4 * L.fbm(x, y, 3, 0.02, seed=3)), 0)
        st = sample("in_stony", x, y)
        h += np.where(ins, np.minimum(st, 8) / 8 * 3.5, 0)
        mo = sample("in_mountain", x, y)
        ridge = 1 - np.abs(L.fbm(x, y, 5, 0.018, seed=5))
        h += np.where(ins, np.where(mo > 0, 6 + np.minimum(mo, 60) * 0.9 + ridge * 10 * np.minimum(mo, 20) / 20, 0), 0)
        wa = sample("in_water", x, y)
        h -= np.where(ins, np.where(wa > 0, 0.9 + np.minimum(wa, 10) * 0.22, 0), 0)
        to_w = sample("to_water", x, y)
        # Banks slope down to the water.
        h -= np.where(ins & (wa <= 0), np.clip(1 - to_w / 5, 0, 1) * 0.5, 0)
    # Beyond the map: low forested hills, then a wall of mountains to the north and east.
    ex = np.maximum(np.abs(x) - HALF, 0)
    ey = np.maximum(np.abs(y) - HALF, 0)
    out = np.hypot(ex, ey)
    hills = 2.5 + out * 0.06 + 9 * (0.5 + 0.5 * L.fbm(x, y, 5, 0.01, seed=9))
    ne = np.clip((x + 0.6 * y) / 420 + 0.35, 0.15, 1.3)
    ridge = 1 - np.abs(L.fbm(x, y, 5, 0.005, seed=11))
    peaks = np.clip(out - 150, 0, None) ** 1.22 * 0.17 * ne * (0.55 + 0.7 * ridge)
    blend = np.clip(out / 18, 0, 1)
    edge = np.where(ins, h, h * (1 - blend) + (hills + peaks) * blend)
    # Keep the village ground level and the road smooth.
    vd = np.hypot(x - HEARTH.x - 2, y - HEARTH.y - 14)
    flat = np.clip(1 - (vd - 26) / 14, 0, 1)
    return edge * (1 - flat * 0.75)


# MARK: - Build


def build_world():
    t_size = 1100
    gm = L.ground_material(grass=(0.27, 0.39, 0.14), meadow=(0.45, 0.51, 0.20), rock=(0.42, 0.39, 0.35))
    res = 360 if DRAFT else 900
    terrain, XYZ = L.terrain("Green Hollow", t_size, res, height, gm)

    def paint(x, y):
        c = class_at(x, y)
        road = np.minimum(dist_polyline(x, y, ROAD) * 0.7, np.minimum.reduce([dist_polyline(x, y, l) for l in LANES]) + 0.5)
        dirt = np.clip(1.35 - road / 1.1, 0, 1) * (0.85 + 0.15 * L.fbm(x, y, 3, 0.4, seed=21))
        # Yards: worn ground around the hearth and in front of the houses.
        hd = np.hypot(x - HEARTH.x, y - HEARTH.y)
        dirt = np.maximum(dirt, np.clip(1 - (hd - 3.5) / 2.5, 0, 1))
        dirt = np.maximum(dirt, np.clip(1 - np.hypot(x - world(4, 28)[0], y - world(4, 28)[1]) / 6.5, 0, 1) * 0.9)
        sand = (c == NAMES.index("sand")).astype(float)
        sand = np.maximum(sand, np.clip(1 - sample("to_water", x, y) / 3.0, 0, 1) * inside(x, y))
        rock = ((c == NAMES.index("stony")) | (c == NAMES.index("mountain"))).astype(float) * 0.85
        return dirt, sand, rock
    L.paint_vertices(terrain, paint)
    L.water("lake", (420, 420), -0.32, (0, 0), color=(0.16, 0.30, 0.30))
    globals()["XYZ"] = XYZ
    return terrain, XYZ


def z_at(x, y):
    return float(L.height_at(XYZ, np.array([x]), np.array([y]))[0])


BROADLEAF = ["Tree_Broadleaf_01", "Tree_Broadleaf_02", "Tree_Broadleaf_03", "Tree_Broadleaf_04", "Tree_Broadleaf_05"]
BIRCH = ["Tree_Birch_01", "Tree_Birch_02", "Tree_Birch_03", "Tree_Birch_04", "Tree_Birch_05"]
FIR = ["Tree_Fir_Tall_01", "Tree_Fir_Tall_02", "Tree_Fir_Short", "Tree_Fir_Sapling"]


def near_any(x, y, points, r):
    return any((x - px) ** 2 + (y - py) ** 2 < r * r for px, py in points)


def plant_nature(cams):
    """Forests from the map (dense, broadleaf in the valley, firs up the slopes), edges of birch and bushes,
    rocks on the stony hills, grass and flowers only where a camera looks closely."""
    village_clear = [(world(x, y), 5.5) for _, x, y, _ in [(v[0], v[1], v[2], v[3]) for v in V]]
    road_pts = ROAD

    def clear(x, y):
        if math.hypot(x - HEARTH.x - 4, y - HEARTH.y - 16) < 30:
            return True
        if dist_polyline(np.array([x]), np.array([y]), road_pts)[0] < 4.5:
            return True
        return False

    count = 0
    lod_far = 2
    for (r, extent, lod) in ((3.0, 300, 0), (4.2, 640, 1), (6.5, 1080, 2)):
        pts = L.poisson(nrng, extent, extent, r, limit=60000)
        for x, y in pts:
            if extent > 300 and max(abs(x), abs(y)) < (300 if extent == 640 else 640) / 2:
                continue
            c = int(class_at(x, y))
            ins = abs(x) < HALF and abs(y) < HALF
            name = NAMES[c]
            if ins and name in ("water", "sand"):
                continue
            if ins and name == "mountain" and rng.random() > 0.3:
                continue  # the rocky heights: scattered firs only
            if ins and name in ("meadow", "stony"):
                # Lone trees and small groves on the meadow, thicker near the forest.
                to_f = sample("to_forest", np.array([x]), np.array([y]))[0]
                if rng.random() > 0.55 * math.exp(-to_f / 3.5) + (0.004 if name == "meadow" else 0):
                    continue
            if clear(x, y):
                continue
            z = z_at(x, y)
            if z < -0.2:
                continue
            depth = sample("in_forest", np.array([x]), np.array([y]))[0] if ins else 40
            hgt = z
            if hgt > 26 or name == "mountain" or (not ins and rng.random() < np.clip((hgt - 12) / 20, 0, 0.85)):
                kind = rng.choice(FIR)
            elif depth < 4 and rng.random() < 0.45:
                kind = rng.choice(BIRCH + ["Bush_Tall", "Bush_Short", "Bush_Flower_Short"])
            elif rng.random() < 0.18:
                kind = rng.choice(FIR[:3])
            else:
                kind = rng.choice(BROADLEAF)
            use_lod = lod if not kind.startswith("Bush") else min(lod, 1)
            s = rng.uniform(0.85, 1.25) * (1.15 if depth > 10 else 1.0)
            L.place(L.unp(kind, use_lod), (x, y, z - 0.15), rng.uniform(0, 360), s)
            count += 1
    print("trees", count)
    # Rocks on the stony hills and boulders at the foot of the mountains.
    for x, y in L.poisson(nrng, 256, 256, 3.5):
        c = NAMES[int(class_at(x, y))]
        if c == "stony" and rng.random() < 0.35:
            kind = rng.choice(["Rock_Large_02", "Rock_Large_02", "Rock_Small_01", "Rock_Small_03"])
            s_ = rng.uniform(0.7, 1.4)
            L.place(L.unp(kind), (x, y, z_at(x, y) - (0.35 if "Large" in kind else 0.08) * s_), rng.uniform(0, 360), s_,
                    tilt=(rng.uniform(-8, 8), rng.uniform(-8, 8)))
        elif c == "mountain" and rng.random() < 0.14:
            s_ = rng.uniform(2.2, 4.5)
            L.place(L.unp("Rock_Large_02"), (x, y, z_at(x, y) - 0.5 * s_), rng.uniform(0, 360), s_,
                    tilt=(rng.uniform(-15, 15), rng.uniform(-15, 15)))
    # Ground cover near the cameras.
    for cam_pos, look, radius in cams:
        cx, cy = cam_pos
        for x, y in L.poisson(nrng, radius * 2, radius * 2, 1.15, limit=40000, origin=(cx, cy)):
            if (x - cx) ** 2 + (y - cy) ** 2 > radius * radius:
                continue
            c = NAMES[int(class_at(x, y))]
            if c in ("water", "sand"):
                continue
            road = dist_polyline(np.array([x]), np.array([y]), ROAD + [(0, 0)])[0]
            lane = min(dist_polyline(np.array([x]), np.array([y]), l)[0] for l in LANES)
            if road < 1.6 or lane < 1.2 or near_any(x, y, [world(v[1], v[2]) for v in V], 3.2):
                continue
            if math.hypot(x - HEARTH.x, y - HEARTH.y) < 5:
                continue
            roll = rng.random()
            if c == "forest":
                kind = rng.choice(["Plant_Fern_High", "Plant_Fern_Low", "Grass_01_High", "Bush_Short"])
            elif roll < 0.55:
                kind = rng.choice(["Grass_01_Low", "Grass_01_Medium", "Grass_02"])
            elif roll < 0.72:
                kind = rng.choice(["Flower_Poppy", "Flower_Lavender", "Plant_Clovers"])
            elif roll < 0.76:
                kind = rng.choice(["Rock_Small_01", "Rock_Small_02", "Mushroom_Liberty_Cap"])
            else:
                continue
            L.place(L.unp(kind), (x, y, z_at(x, y) - 0.03), rng.uniform(0, 360), rng.uniform(0.7, 1.2))
    # Reeds and lilies along the banks near the village.
    for x, y in L.poisson(nrng, 120, 80, 1.6, origin=(HEARTH.x - 10, HEARTH.y - 20)):
        tw = sample("to_water", np.array([x]), np.array([y]))[0]
        iw = sample("in_water", np.array([x]), np.array([y]))[0]
        if 0 < iw < 1.5 and rng.random() < 0.5:
            L.place(L.unp(rng.choice(["Plant_Reeds", "Grass_Reeds"])), (x, y, -0.6), rng.uniform(0, 360), rng.uniform(0.8, 1.3))
        elif 1.5 < iw < 5 and rng.random() < 0.06:
            L.place(L.unp("Plant_Water_Lily"), (x, y, -0.36), rng.uniform(0, 360), 0.35)


# MARK: - Buildings


_export = None


def export_index():
    global _export
    if _export is None:
        _export = {}
        if EXPORT and os.path.isdir(EXPORT):
            import glob
            found = glob.glob(os.path.join(EXPORT, "**", "manifest.json"), recursive=True)
            if found:
                base = os.path.dirname(found[0])
                for m in json.load(open(found[0]))["models"]:
                    _export[m["key"]] = (base, m)
        print("exported models:", len(_export))
    return _export


def building(key):
    """The building as the game assembles it: Unity OBJ export first, else its bw_ (or kit) FBX."""
    idx = export_index()
    if key in idx:
        return L.exported(key, *idx[key])
    sprite = key
    for cand in ("bw_" + sprite, sprite):
        if cand in L.MANIFEST["models"]:
            return L.game(cand)
    return None


def build_village():
    placed = []
    for key, x, y, face in V:
        wx, wy = world(x, y)
        col = building(key)
        if col is None:
            print("missing building", key)
            continue
        fx, fy = face if face else (0, 0)
        fx, fy = world(fx, fy)
        rot = math.degrees(math.atan2(-(fx - wx), (fy - wy)))
        z = z_at(wx, wy)
        L.place(col, (wx, wy, z - 0.02), rot, 1.0)
        placed.append((key, wx, wy, rot))
    # Fields of wheat in rows, a fence round the pasture.
    wheat = L.unp("Plant_Wheat")
    for fx, fy in FIELDS:
        wx, wy = world(fx, fy)
        for i in range(9):
            for j in range(14):
                px, py = wx - 1.9 + i * 0.47 + rng.uniform(-0.05, 0.05), wy - 1.9 + j * 0.29
                L.place(wheat, (px, py, z_at(px, py) - 0.05), rng.uniform(0, 360), rng.uniform(1.4, 1.8))
    fence = L.game("k_fence")
    px, py = world(*PASTURE)
    for i in range(12):
        a = i / 12 * 2 * math.pi
        L.place(fence, (px + math.cos(a) * 6, py + math.sin(a) * 4.2, z_at(px, py)), math.degrees(a) + 90, 2.6)
    return placed


def _wood(name="PropWood", tint=(0.82, 0.70, 0.56)):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    return L.textured(name, os.path.join(L.ASSETS, "Art", "Textures", "RoughWood_albedo.jpg"),
                      os.path.join(L.ASSETS, "Art", "Textures", "RoughWood_normal.jpg"), tint=tint)


def _bark():
    if "PropBark" in bpy.data.materials:
        return bpy.data.materials["PropBark"]
    return L.textured("PropBark", os.path.join(L.ASSETS, "Art", "Textures", "Bark_albedo.jpg"),
                      os.path.join(L.ASSETS, "Art", "Textures", "Bark_normal.jpg"), tint=(0.85, 0.75, 0.62))


def _bevel(o, w=0.015):
    b = o.modifiers.new("b", "BEVEL")
    b.width = w
    b.segments = 2
    return o


def crate(loc, size=0.55, rot=0.0, parent=None):
    """A slatted crate: a wooden box with darker edge battens."""
    bpy.ops.mesh.primitive_cube_add(size=size, location=(loc[0], loc[1], loc[2] + size / 2))
    o = _bevel(bpy.context.object, size * 0.04)
    o.rotation_euler = (0, 0, math.radians(rot))
    o.data.materials.append(_wood("CrateWood", (0.92, 0.78, 0.58)))
    for dz in (-0.42, 0.42):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, dz * size))
        b = _bevel(bpy.context.object, 0.006)
        b.scale = (size * 1.04, size * 1.04, size * 0.1)
        b.data.materials.append(_wood("CrateEdge", (0.55, 0.42, 0.30)))
        b.parent = o
    if parent:
        o.parent = parent
    return o


def barrel(loc, h=0.75, parent=None):
    bpy.ops.mesh.primitive_cylinder_add(radius=h * 0.36, depth=h, vertices=20, location=(loc[0], loc[1], loc[2] + h / 2))
    o = bpy.context.object
    o.data.materials.append(_wood("BarrelWood", (0.80, 0.60, 0.42)))
    bpy.ops.object.shade_smooth()
    for dz in (-0.32, 0.0, 0.32):
        bpy.ops.mesh.primitive_torus_add(major_radius=h * 0.37 * (1.04 if dz == 0 else 1.0), minor_radius=h * 0.018,
                                         location=(0, 0, dz * h), major_segments=24, minor_segments=6)
        r = bpy.context.object
        r.data.materials.append(L.principled("Iron", L.lin((0.18, 0.16, 0.15)), 0.6))
        r.parent = o
    if parent:
        o.parent = parent
    return o


def log_pile(loc, rot=0.0, n=3):
    """Firewood stacked in a small pyramid."""
    root = bpy.data.objects.new("Logs", None)
    bpy.context.scene.collection.objects.link(root)
    k = 0
    for row in range(n):
        for i in range(n - row):
            x = (i - (n - row - 1) / 2) * 0.22
            bpy.ops.mesh.primitive_cylinder_add(radius=0.1 + rng.uniform(-0.015, 0.015), depth=1.1, vertices=12,
                                                location=(x, rng.uniform(-0.05, 0.05), 0.1 + row * 0.18),
                                                rotation=(math.radians(90), 0, 0))
            c = bpy.context.object
            c.data.materials.append(_bark())
            c.parent = root
            k += 1
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(rot))
    return root


def sack(loc, parent=None, s=1.0):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.5, location=loc)
    o = bpy.context.object
    o.scale = (0.32 * s, 0.38 * s, 0.26 * s)
    bpy.ops.object.shade_smooth()
    o.data.materials.append(L.principled("Cloth", L.lin((0.86, 0.80, 0.66)), 0.95))
    if parent:
        o.parent = parent
    return o


def props():
    """Firewood, crates and baskets that make the yards lived in."""
    for x, y, r in ((-3.2, 2.6, 20), (-34.5, 25, 70), (-7.5, -4.4, 110)):
        wx, wy = world(x, y)
        log_pile((wx, wy, z_at(wx, wy)), r)
    for x, y, s_, r in ((8, 5, 0.55, 15), (8.6, 5.5, 0.45, 40), (22, 34, 0.6, 5), (22.6, 34.7, 0.5, 30), (11, 3, 0.5, 60)):
        wx, wy = world(x, y)
        crate((wx, wy, z_at(wx, wy)), s_, r)
    for x, y in ((-9, 4), (27, 33), (26.2, 32.4), (6.9, 4.6)):
        wx, wy = world(x, y)
        barrel((wx, wy, z_at(wx, wy)))
    items = [("wicker_basket_01", 2.5, -3, 1.3), ("ceramic_pot", -19, 14, 0.5), ("wooden_bowl_01", 1.5, -2, 1.2)]
    for name, x, y, s in items:
        if name in L.MANIFEST["models"]:
            wx, wy = world(x, y)
            L.place(L.game(name), (wx, wy, z_at(wx, wy)), rng.uniform(0, 360), s)


# MARK: - People and animals


def people(shot):
    """Villagers posed for the shot (each import is a real rig, so only what a camera sees)."""
    plans = {
        "village": [
            (0, 2.2, 1.0, 200, "Talk", 0.3), (3, 1.0, 2.4, 160, "Idle", 0.6), (5, -2.6, 1.4, 120, "Sit", 0.5),
            (6, -3.4, 3.4, 30, "Carry", 0.4), (8, 4.5, 5.5, 250, "Walk", 0.2), (2, -8.5, 4.6, 140, "Work", 0.5),
            (7, 7.0, -1.0, 300, "Gather", 0.4), (9, -1.0, 12.5, 180, "Walk", 0.6), (4, 9.5, 4.5, 200, "Watch", 0.5),
            (1, -5.5, -1.0, 20, "Chop", 0.25, "Axe"), (10, 12.5, -2.5, 300, "Walk", 0.5),
        ],
        "market": [
            (0, 1.6, 25.2, 160, "Talk", 0.3), (1, 3.0, 24.4, 200, "Idle", 0.5), (2, 6.4, 25.4, 150, "Watch", 0.3),
            (3, 5.2, 23.8, 230, "Talk", 0.7), (4, 2.0, 30.6, 0, "Work", 0.4), (5, 6.5, 30.5, 10, "Idle", 0.2),
            (6, -0.3, 22.2, 120, "Walk", 0.3), (7, 8.5, 22.5, 220, "Carry", 0.5), (8, 5.8, 21.8, 200, "Walk", 0.7),
            (9, 0.0, 27.0, 90, "Gather", 0.5),
        ],
        "caravan": [
            (11, 34.6, 35.9, 108, "Walk", 0.35), (6, 31.8, 33.8, 108, "Walk", 0.7), (2, 26.0, 35.6, 250, "Talk", 0.3),
            (3, 27.6, 36.2, 110, "Watch", 0.4), (9, 23.8, 34.6, 300, "Carry", 0.5),
        ],
        "dusk": [
            (0, 2.2, 1.0, 200, "Sit", 0.4), (3, 1.0, 2.4, 160, "Talk", 0.6), (5, -2.6, 1.4, 120, "Sit", 0.5),
            (8, -1.2, -1.8, 20, "Idle", 0.3), (2, 2.4, -1.4, 330, "Watch", 0.5), (9, -0.5, 12.0, 180, "Lantern", 0.4),
            (4, 9.0, 4.0, 220, "Torch", 0.5),
        ],
    }
    rigs = []
    for entry in plans.get(shot, []):
        idx, x, y, rot, clip, t = entry[:6]
        prop = entry[6] if len(entry) > 6 else None
        wx, wy = world(x, y)
        rigs.append(L.villager(idx, (wx, wy, z_at(wx, wy)), rot, clip, t, prop=prop))
    return rigs


ANIMAL_LENGTH = {"Cow": 2.0, "Sheep": 1.1, "Pig": 1.05, "Horse": 2.1, "Llama": 1.5}


def animal(kind, x, y, rot, clip="Idle", t=0.3):
    before, acts = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(L.ASSETS, "Art", "Resources", "Models", f"Animal_{kind}.fbx"))
    objs = [o for o in bpy.data.objects if o not in before]
    clips = [a for a in bpy.data.actions if a not in acts]
    rig = next((o for o in objs if o.type == "ARMATURE"), objs[0])
    for o in objs:
        for s in o.material_slots:
            if s.material:
                m = s.material
                m.use_nodes = True
                b = m.node_tree.nodes.get("Principled BSDF")
                if b:
                    # The FBX brings a transparency factor that renders the animals invisible in Cycles.
                    for link in list(b.inputs["Alpha"].links):
                        m.node_tree.links.remove(link)
                    b.inputs["Alpha"].default_value = 1.0
                    b.inputs["Roughness"].default_value = 0.85
                    b.inputs["Specular IOR Level"].default_value = 0.2
    act = next((a for a in clips if a.name.split("|")[-1] == clip), None)
    if act and rig.type == "ARMATURE":
        rig.animation_data_create()
        rig.animation_data.action = act
        f0, f1 = act.frame_range
        bpy.context.scene.frame_set(int(f0 + (f1 - f0) * t))
        sv = {pb.name: pb.matrix_basis.copy() for pb in rig.pose.bones}
        rig.animation_data.action = None
        for pb in rig.pose.bones:
            pb.matrix_basis = sv[pb.name]
    bpy.context.view_layer.update()
    meshes = [o for o in objs if o.type == "MESH"]
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for o in meshes:
        for c in o.bound_box:
            v = o.matrix_world @ Vector(c)
            lo, hi = Vector(map(min, lo, v)), Vector(map(max, hi, v))
    size = max(hi.x - lo.x, hi.y - lo.y)
    k = ANIMAL_LENGTH.get(kind, 1.5) / max(size, 1e-3)
    wx, wy = world(x, y)
    rig.scale = rig.scale * k
    rig.location = Vector((wx, wy, z_at(wx, wy)))
    rig.rotation_euler = Euler((rig.rotation_euler.x, rig.rotation_euler.y, math.radians(rot)))
    return rig


def animals(shot):
    if shot in ("hollow", "village", "market", "dusk"):
        for i in range(7):
            animal("Sheep", PASTURE[0] + rng.uniform(-4, 4), PASTURE[1] + rng.uniform(-2.8, 2.8), rng.uniform(0, 360))
        animal("Cow", PASTURE[0] + 2, PASTURE[1] - 1.5, 140)
    if shot in ("village",):
        animal("Pig", 7.5, 12.5, 200)
        animal("Pig", 8.6, 13.2, 100)


def cart(x, y, rot, loaded=True):
    """BuildingModels.Cart: a two-wheeled cart, front (shafts) towards local -y here."""
    wood = L.textured("CartWood", os.path.join(L.ASSETS, "Art", "Textures", "RoughWood_albedo.jpg"),
                      os.path.join(L.ASSETS, "Art", "Textures", "RoughWood_normal.jpg"), tint=(0.82, 0.70, 0.56))
    root = bpy.data.objects.new("Cart", None)
    bpy.context.scene.collection.objects.link(root)
    k = 1.25  # the caravan cart is drawn a little larger than the yard handcart

    def cube(c, s, rot=(0, 0, 0)):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(c[0] * k, -c[2] * k, c[1] * k))
        o = bpy.context.object
        o.scale = (s[0] * k, s[2] * k, s[1] * k)
        o.rotation_euler = Euler((math.radians(rot[0]), math.radians(rot[2]), math.radians(rot[1])))
        o.data.materials.append(wood)
        o.parent = root
        bev = o.modifiers.new("b", "BEVEL")
        bev.width = 0.012
        return o

    def pole(a, b, r):
        a, b = Vector((a[0] * k, -a[2] * k, a[1] * k)), Vector((b[0] * k, -b[2] * k, b[1] * k))
        bpy.ops.mesh.primitive_cylinder_add(radius=r * k * 0.5, depth=(b - a).length, vertices=10, location=(a + b) / 2)
        o = bpy.context.object
        o.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
        o.data.materials.append(wood)
        o.parent = root
        return o

    cube((0, 0.55, 0), (0.95, 0.06, 1.35))
    for sx in (-0.47, 0.47):
        cube((sx, 0.7, 0), (0.04, 0.26, 1.35))
    cube((0, 0.7, 0.66), (0.95, 0.26, 0.04))
    for sx in (-0.36, 0.36):
        pole((sx, 0.55, -0.6), (sx * 0.8, 0.42, -1.55), 0.06)
    for sx in (-0.56, 0.56):
        bpy.ops.mesh.primitive_cylinder_add(radius=0.13 * k, depth=0.12 * k, vertices=16, location=(sx * k, -0.05 * k, 0.42 * k),
                                            rotation=(0, math.radians(90), 0))
        hub = bpy.context.object
        hub.data.materials.append(wood)
        hub.parent = root
        bpy.ops.mesh.primitive_torus_add(major_radius=0.37 * k, minor_radius=0.035 * k, location=(sx * k, -0.05 * k, 0.42 * k),
                                         rotation=(0, math.radians(90), 0), major_segments=28, minor_segments=8)
        rim = bpy.context.object
        rim.data.materials.append(wood)
        rim.parent = root
        for i in range(5):
            a = i * math.pi / 5
            pole((sx, 0.42 + math.sin(a) * 0.36, 0.05 + math.cos(a) * 0.36), (sx, 0.42 - math.sin(a) * 0.36, 0.05 - math.cos(a) * 0.36), 0.03)
    if loaded:
        crate((-0.2 * k, -0.3 * k, 0.58 * k), 0.42 * k, 8, root)
        crate((0.2 * k, -0.28 * k, 0.58 * k), 0.38 * k, -12, root)
        crate((0.0 * k, -0.3 * k, 1.0 * k), 0.32 * k, 25, root)
        barrel((0.2 * k, 0.3 * k, 0.58 * k), 0.55 * k, root)
        sack((-0.22 * k, 0.32 * k, 0.75 * k), root, 1.1 * k)
        sack((-0.05 * k, 0.5 * k, 0.72 * k), root, 0.9 * k)
    wx, wy = world(x, y)
    root.location = (wx, wy, z_at(wx, wy))
    root.rotation_euler = (0, 0, math.radians(rot))
    return root


CARAVAN = (35.0, 37.0)
CARAVAN_TO = (24.0, 33.5)


def caravan():
    """A caravan coming home down the trade road: the horse in front, the loaded cart behind it."""
    hx, hy = CARAVAN
    d = Vector((CARAVAN_TO[0] - hx, CARAVAN_TO[1] - hy)).normalized()
    # Villager and horse rigs face +y at rotation 0; the cart's shafts point along its local -y.
    face = math.degrees(math.atan2(-d.x, d.y))
    animal("Horse", hx, hy, face + HORSE_YAW, "Walk", 0.3)
    cart(hx - d.x * 2.5, hy - d.y * 2.5, face + 180)


HORSE_YAW = 0.0


# MARK: - Shots


SHOT_DEFS = {
    # camera (metres from the hearth), look-at, lens, sun elevation, sun azimuth, ground-cover centre, radius
    "hollow": dict(cam=(-40, -60, 22), look=(10, 30, 10), lens=28, sun=(10, 292), cover=None),
    "village": dict(cam=(-15, -17, 8.5), look=(2, 7, 1.2), lens=30, sun=(16, 286), cover=((-4, 2), 34), dof=24),
    "caravan": dict(cam=(40, 30.5, 2.8), look=(32.5, 36.5, 1.3), lens=30, sun=(9, 300), cover=((34, 34), 30), dof=9.5),
    "market": dict(cam=(2.0, 17.0, 2.7), look=(4.5, 28, 1.4), lens=34, sun=(20, 250), cover=((3, 22), 22), dof=10),
    "dusk": dict(cam=(-12, -14, 6.5), look=(2, 6, 1.5), lens=30, sun=(-3, 300), cover=((-4, 2), 30), dof=19),
}


def light_shot(name, d):
    el, az = d["sun"]
    if name == "dusk":
        L.sky(2.0, az, strength=0.10, sun_strength=0.0, air=2.0, dust=3.0)
        moon = bpy.data.objects.new("moon", bpy.data.lights.new("moon", "SUN"))
        moon.data.energy = 0.22
        moon.data.color = (0.62, 0.72, 1.0)
        moon.data.angle = math.radians(1)
        moon.rotation_euler = Euler((math.radians(50), 0, math.radians(160)))
        bpy.context.scene.collection.objects.link(moon)
        L.mist(15, 260)
        bpy.context.scene.view_settings.exposure = 0.9
    else:
        L.sky(el, az, strength=0.75 if el < 12 else 0.9, sun_strength=4.2 if el < 12 else 4.8,
              warm=(1.0, 0.80, 0.56) if el < 12 else (1.0, 0.88, 0.70), air=1.4, dust=2.5)
        L.mist(25 if name == "hollow" else 12, 700 if name == "hollow" else 320)
        bpy.context.scene.view_settings.exposure = 0.25 if name == "hollow" else 0.1


def fires(night):
    """The hearth (and at night the torches and windows) as warm emissive flames with a point light."""
    def glow(name, color, strength):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs["Color"].default_value = (*color, 1)
        em.inputs["Strength"].default_value = strength
        nt.links.new(em.outputs[0], out.inputs["Surface"])
        return m

    k = 1.6 if night else 1.0
    layers = [(glow("FlameOuter", (1.0, 0.30, 0.06), 3.5 * k), 1.0, 1.0), (glow("FlameMid", (1.0, 0.55, 0.14), 5 * k), 0.68, 0.8),
              (glow("FlameCore", (1.0, 0.80, 0.42), 8 * k), 0.38, 0.55)]

    def tongue(x, y, z, s, mat, w, h, lean):
        """One tongue of flame: a teardrop (round at the base, drawn up to a point) leaning a little."""
        bpy.ops.mesh.primitive_uv_sphere_add(radius=1, segments=16, ring_count=10, location=(x, y, z))
        o = bpy.context.object
        for v in o.data.vertices:
            t = (v.co.z + 1) / 2  # 0 at the bottom, 1 at the top
            narrow = (1 - t) ** 0.8 if t > 0.35 else 1.0
            v.co.x *= narrow
            v.co.y *= narrow
            v.co.z = v.co.z * (1.6 if v.co.z > 0 else 0.5)
        bpy.ops.object.shade_smooth()
        o.scale = (w * s, w * s, h * s)
        o.rotation_euler = (math.radians(lean[0]), math.radians(lean[1]), 0)
        o.data.materials.append(mat)
        o.visible_shadow = False
        return o

    spots = [(0, 0, 0.35, 1.0)]
    if night:
        spots += [(-6.5, -2.4, 1.4, 0.35), (11.5, 1.6, 1.3, 0.3), (-21.5, 2.4, 1.0, 0.45), (4, 25.5, 2.2, 0.3), (0.2, 8.0, 1.4, 0.3),
                  (-10.5, 5.6, 1.2, 0.3), (8.6, 7.2, 1.2, 0.3), (14.6, 12.8, 1.1, 0.25), (-4.4, 15.8, 1.1, 0.25)]
    for x, y, h, s in spots:
        wx, wy = world(x, y)
        z = z_at(wx, wy) + h
        for j in range(4 if s >= 1 else 2):
            ox, oy = (rng.uniform(-0.12, 0.12) * s, rng.uniform(-0.12, 0.12) * s) if j else (0, 0)
            hj = rng.uniform(0.75, 1.1) if j else 1.15
            lean = (rng.uniform(-10, 10), rng.uniform(-10, 10))
            for mat, w, hk in layers:
                tongue(wx + ox, wy + oy, z + 0.12 * s, s, mat, 0.17 * w, 0.42 * hj * hk, lean)
        lt = bpy.data.objects.new("fire", bpy.data.lights.new("fire", "POINT"))
        lt.data.energy = (900 if night else 300) * s
        lt.data.color = (1.0, 0.55, 0.22)
        lt.data.shadow_soft_size = 0.3
        lt.location = (wx, wy, z + 0.6 * s)
        bpy.context.scene.collection.objects.link(lt)


def shoot(name):
    d = SHOT_DEFS[name]
    cx, cy, cz = d["cam"]
    lx, ly, lz = d["look"]
    wx, wy = world(cx, cy)
    tx, ty = world(lx, ly)
    cam = L.camera((wx, wy, z_at(wx, wy) + cz), (tx, ty, z_at(tx, ty) + lz), d["lens"], d.get("dof"), fstop=5.6)
    light_shot(name, d)
    return cam


ALL_COVERS = [((-4, 2), 34), ((34, 34), 30), ((3, 22), 22), ((-6, -12), 26)]


def prepare(path):
    """The valley, its nature and the village, saved once to a .blend the shots start from."""
    global XYZ
    if os.path.exists(path):
        bpy.ops.wm.open_mainfile(filepath=path)
        L.forget()
        X = np.linspace(-550, 550, (360 if DRAFT else 900) + 1)
        XX, YY = np.meshgrid(X, X)
        XYZ = (XX, YY, height(XX, YY))
        L._placed = None
        return
    L.reset(RES, int(RES * 9 / 16), SAMPLES)
    build_world()
    plant_nature([(world(cx, cy), None, r) for (cx, cy), r in ALL_COVERS] if not DRAFT else [])
    build_village()
    props()
    bpy.ops.wm.save_as_mainfile(filepath=path)


def main():
    width = RES
    height_px = int(RES * 9 / 16)
    cache = os.path.join(OUT, "world_draft.blend" if DRAFT else "world.blend")
    for name in SHOTS:
        prepare(cache)
        scene = bpy.context.scene
        scene.render.resolution_x, scene.render.resolution_y = width, height_px
        scene.cycles.samples = 16 if DRAFT else SAMPLES
        people(name)
        animals(name)
        if name == "caravan":
            caravan()
        fires(name == "dusk")
        shoot(name)
        L.render(os.path.join(OUT, f"{name}.png"))


main()
