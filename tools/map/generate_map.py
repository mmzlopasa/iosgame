#!/usr/bin/env python3
"""Generates the valley map (128×128 cells, 2 m each) and its lands to explore into content.json.

There is no fixed camp any more: on a new game the player chooses where the campfire burns
and where the first shelters stand. The land around that spot ("home_clearing", 15×15 cells,
placed by the game) is explored from the start; the rest of the valley is cut into 16×16-cell
lands that scouts explore one by one, outward from the camp.

The valley:
- a river from the north edge into a great lake in the south-west, and on to the south edge,
  with a brook from the east hills joining it;
- forest everywhere in patches, denser in the west, the north and the far south;
- stony hills (quarries) in the middle band and the north;
- mountains along the east edge and in the north-east ('M', no building) with ore veins at
  their feet ('O', mines);
- open meadows in between (farms), with a wide clearing by the river where the game suggests
  to settle (used by tests, previews and as the camera's first view).

Legend: '.' grass  'T' forest  ',' sand  '~' water  'R' rock (hills)  'M' mountain  'O' ore
Usage: python3 tools/map/generate_map.py   (deterministic: same seed, same valley)
"""
import json
import math
import os
import random

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONTENT = os.path.join(ROOT, "unity", "Assets", "Resources", "content.json")
W = H = 128
TILE = 16
HOME = 15
SEED = 2026
# Suggested campfire (an open meadow on the river's east bank).
START = (6, 7)
# The first clearing of the old valley (21×21 cells in the north-west corner, river and eastern
# ridge included) is kept as it was: it is the suggested start and the core tests play there.
OLD = 21
OLD_ROWS = os.path.join(os.path.dirname(__file__), "first_clearing.txt")


def value_noise(seed, scale):
    rnd = random.Random(seed)
    g = {}

    def grid(ix, iy):
        if (ix, iy) not in g:
            g[(ix, iy)] = rnd.random()
        return g[(ix, iy)]

    def smooth(t):
        return t * t * (3 - 2 * t)

    def f(x, y):
        x, y = x / scale, y / scale
        ix, iy = math.floor(x), math.floor(y)
        fx, fy = smooth(x - ix), smooth(y - iy)
        a, b = grid(ix, iy), grid(ix + 1, iy)
        c, d = grid(ix, iy + 1), grid(ix + 1, iy + 1)
        return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy
    return f


def build_grid():
    grid = [["." for _ in range(W)] for _ in range(H)]
    forest = value_noise(SEED, 11.0)
    forest2 = value_noise(SEED + 1, 4.0)
    hills = value_noise(SEED + 2, 9.0)
    peaks = value_noise(SEED + 3, 8.0)
    veins = value_noise(SEED + 4, 3.0)
    rnd = random.Random(SEED + 5)

    for y in range(H):
        for x in range(W):
            f = forest(x, y) * 0.7 + forest2(x, y) * 0.3
            f += max(0.0, (30 - x) / 30) * 0.22      # western woods
            f += max(0.0, (22 - y) / 22) * 0.15      # northern woods
            f += max(0.0, (y - 104) / 24) * 0.2      # southern woods
            if f > 0.6:
                grid[y][x] = "T"
            h = hills(x, y)
            band = 1.0 - min(1.0, abs(x - 72) / 26.0)
            north = max(0.0, (30 - y) / 30) * (1.0 - min(1.0, abs(x - 60) / 30.0))
            if h + band * 0.22 + north * 0.25 > 0.86:
                grid[y][x] = "R"

    # Mountains along the east edge and in the north-east.
    for y in range(H):
        for x in range(W):
            east = (x - 106) / 12.0
            north_east = ((x - 84) / 22.0 + (34 - y) / 22.0) * 0.62
            south_east = ((x - 92) / 16.0 + (y - 100) / 18.0) * 0.55
            m = max(east, north_east, south_east) + (peaks(x, y) - 0.5) * 0.8
            if m > 1.0:
                grid[y][x] = "M"
    for y in range(H):
        for x in range(W):
            if grid[y][x] == "M":
                continue
            near = any(0 <= x + dx < W and 0 <= y + dy < H and grid[y + dy][x + dx] == "M"
                       for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if near and veins(x, y) > 0.5:
                grid[y][x] = "O"

    # The river: north edge → great lake → south edge, and a brook from the east hills.
    water = set()

    def river_x(y):
        if y < 80:
            return 9 + (y - OLD) * 0.36 + 2.5 * math.sin((y - OLD) / 7.0)
        return 31 + 3.0 * math.sin((y - 96) / 6.0) + (y - 96) * 0.1

    for y in range(OLD, H):
        cx = river_x(y)
        for x in range(int(round(cx)) - 1, int(round(cx)) + 1):
            water.add((x, y))
        if y == OLD:
            # Join the old river's last bend (x 8–9 at the clearing's south edge).
            water.update({(8, y), (9, y)})
    lake_c, lake_r = (30.0, 88.0), (13.0, 8.5)
    for y in range(H):
        for x in range(W):
            dx, dy = (x - lake_c[0]) / lake_r[0], (y - lake_c[1]) / lake_r[1]
            if dx * dx + dy * dy + (forest2(x, y) - 0.5) * 0.35 < 1.0:
                water.add((x, y))
    # The brook comes down from the east and joins the river north of the camp.
    for x in range(int(river_x(44)), 96):
        by = 44 + 5.0 * math.sin(x / 8.0) - (x - 40) * 0.12
        water.add((x, int(round(by))))
    for (x, y) in water:
        if 0 <= x < W and 0 <= y < H and grid[y][x] != "M" and (x >= OLD or y >= OLD):
            grid[y][x] = "~"
    # Sandy banks.
    for y in range(H):
        for x in range(W):
            if grid[y][x] in "~MO" or (x < OLD and y < OLD):
                continue
            near = any((x + dx, y + dy) in water for dx in (-1, 0, 1) for dy in (-1, 0, 1))
            if near and rnd.random() < 0.6:
                grid[y][x] = ","

    # The first clearing, as it always was.
    old = [line.rstrip("\n") for line in open(OLD_ROWS)]
    for y in range(OLD):
        for x in range(OLD):
            grid[y][x] = old[y][x]
    return grid


FOREST_NAMES = ["Whispering Pines", "Old Spruce Wood", "Mossy Hollow", "Elk Woods", "Shadow Firs", "Owl Thicket",
                "Deepwood", "Needle Grove", "Wolf Forest", "Fern Dell", "Silent Timber", "Bear Wood", "Lichen Wood",
                "Pine Ridge Forest", "Hunter's Wood", "Darkfir", "Greenmantle", "Bramble Wood", "Antler Grove",
                "Wild Spruces", "Thornwood", "Crow Forest", "Resin Wood", "Lynx Woods", "Tall Pines"]
ROCK_NAMES = ["Hills of Stone", "Flint Rise", "Grey Knolls", "Boulder Field", "Broken Hills", "Quarry Downs",
              "Rocky Heights", "Stone Teeth", "Windy Crags", "Cairn Hills", "Slate Slopes", "Pebble Heath",
              "Granite Knoll", "Split Rock", "Echo Rocks", "Tumbled Stones"]
MOUNTAIN_NAMES = ["Iron Mountains", "Copper Peaks", "Eagle Crags", "Frost Heights", "Thunder Peaks", "Red Cliffs",
                  "High Scree", "Storm Ridge", "Cold Summits", "Giant's Steps", "Raven Peaks", "Rust Mountains",
                  "Cloud Ridge", "Sky Wall", "Snow Horns", "Stone Crown", "Grey Giants", "Ore Hills"]
WATER_NAMES = ["Great Lake", "Reed Shallows", "Heron Bay", "Mirror Water", "Fishers' Shore", "Willow Banks",
               "Otter Pools", "Misty Mere", "Pike Waters", "Silver Shallows"]
MEADOW_NAMES = ["Great Meadow", "Sunny Fields", "Bee Pastures", "Clover Plain", "Golden Grass", "Windy Downs",
                "Lark Fields", "Deer Meadows", "Flower Vale", "Open Steppe", "Quiet Plain", "Hare Fields",
                "Bluebell Lea", "Green Vale", "Long Meadow", "Daisy Fields", "Riverside Lea", "Barley Flats",
                "Heather Moor", "Wide Plain", "Calm Lea", "Grazing Lands", "Swallow Fields", "Butterfly Vale",
                "Thistle Plain"]
DESCRIPTIONS = {
    "forest": "Thick woods full of timber and game.",
    "rock": "Stony hills: good stone for quarries.",
    "mountain": "Steep peaks; ore shows in the scree at their feet.",
    "water": "Open water and reedy banks, rich in fish.",
    "meadow": "Open grassland: room to farm, herd and build.",
}
FINDINGS = {
    "forest": [("In the woods the scouts found a cache of dry firewood.", {"wood": 40}),
               ("A hunter lost in the woods followed the scouts home.", 1),
               ("The scouts carried back a load of fallen timber.", {"wood": 30})],
    "rock": [("The scouts carried back fine flint from the hills.", {"stone": 30}),
             ("Among the rocks the scouts found a shelter of old hides.", {"hides": 10})],
    "mountain": [("The mountains hide copper-green rocks and old hides in a cave.", {"ore": 20, "hides": 10}),
                 ("Scouts came back with red stones that melt in fire.", {"ore": 15})],
    "water": [("On the shore the scouts found a fisher's abandoned catch.", {"food": 30}),
              ("Two people hiding in the reeds joined the village.", 2)],
    "meadow": [("The scouts gathered wild grain in the meadows.", {"food": 25}),
               ("A wanderer crossing the plain joined the village.", 1),
               None],
}


def build_regions(grid):
    sx, sy = START
    regions = [{
        "id": "home_clearing", "name": "Home Clearing", "description": "The land around your first campfire.",
        "home": True, "x": min(max(0, sx - HOME // 2), W - HOME), "y": min(max(0, sy - HOME // 2), H - HOME), "width": HOME, "height": HOME, "revealedAtStart": True,
        "cost": {}, "expeditionSeconds": 1, "legacyText": "The clearing where the first fire was lit.",
    }]
    pools = {"forest": list(FOREST_NAMES), "rock": list(ROCK_NAMES), "mountain": list(MOUNTAIN_NAMES),
             "water": list(WATER_NAMES), "meadow": list(MEADOW_NAMES)}
    used = set()
    rnd = random.Random(SEED + 9)
    for ty in range(H // TILE):
        for tx in range(W // TILE):
            counts = {}
            for y in range(ty * TILE, ty * TILE + TILE):
                for x in range(tx * TILE, tx * TILE + TILE):
                    counts[grid[y][x]] = counts.get(grid[y][x], 0) + 1
            n = TILE * TILE
            m = counts.get("M", 0) + counts.get("O", 0)
            if m > n * 0.3:
                kind = "mountain"
            elif counts.get("~", 0) > n * 0.25:
                kind = "water"
            elif counts.get("R", 0) > n * 0.18:
                kind = "rock"
            elif counts.get("T", 0) > n * 0.35:
                kind = "forest"
            else:
                kind = "meadow"
            pool = pools[kind]
            name = None
            while pool:
                cand = pool.pop(0)
                if cand not in used:
                    name = cand
                    break
            if name is None:
                k = 2
                base = {"forest": "Woods", "rock": "Hills", "mountain": "Peaks", "water": "Waters", "meadow": "Fields"}[kind]
                while f"{base} {k}" in used:
                    k += 1
                name = f"{base} {k}"
            used.add(name)
            cost = {"food": 10 + (5 if kind in ("mountain", "rock") else 0)}
            if kind == "mountain":
                cost["wood"] = 10
            region = {
                "id": "land_%d_%d" % (tx, ty), "name": name, "description": DESCRIPTIONS[kind],
                "x": tx * TILE, "y": ty * TILE, "width": TILE, "height": TILE, "revealedAtStart": False,
                "cost": cost, "expeditionSeconds": {"mountain": 900.0, "rock": 720.0}.get(kind, 600.0),
                "legacyText": f"Scouts mapped {name}.",
            }
            find = rnd.choice(FINDINGS[kind])
            if find is not None:
                text, gift = find
                region["finding"] = text
                if isinstance(gift, int):
                    region["survivors"] = gift
                else:
                    region["loot"] = gift
            regions.append(region)
    return regions


def main():
    content = json.load(open(CONTENT))
    grid = build_grid()
    rows = ["".join(r) for r in grid]
    m = content["map"]
    sx, sy = START
    m["name"] = "long_valley"
    m["width"], m["height"], m["rows"] = W, H, rows
    m["traits"] = ["river valley", "great lake", "forest edge", "hills of stone", "iron mountains"]
    # Where the game suggests to settle (the campfire first; shelters around it).
    m["startBuildings"] = [{"buildingId": "campfire", "x": sx, "y": sy},
                           {"buildingId": "shelter", "x": sx + 1, "y": sy - 1},
                           {"buildingId": "shelter", "x": sx + 1, "y": sy + 1},
                           {"buildingId": "shelter", "x": sx - 1, "y": sy - 1}]
    content["regions"] = build_regions(grid)
    with open(CONTENT, "w") as f:
        json.dump(content, f, indent=1, ensure_ascii=False)
        f.write("\n")
    counts = {}
    for r in rows:
        for ch in r:
            counts[ch] = counts.get(ch, 0) + 1
    print("map", W, "x", H, counts)
    for r in rows[::2]:
        print(r[::1])


if __name__ == "__main__":
    main()
