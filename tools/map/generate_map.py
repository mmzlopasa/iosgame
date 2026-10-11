#!/usr/bin/env python3
"""Generates the large valley map (64×64 cells, 2 m each) into content.json.

The hand-made starting area (the original 21×21 cells: riverside clearing, eastern ridge,
southern marsh) stays in the north-west corner unchanged. The generator then grows the
valley around it:
- the river continues south into a lake (fishing) and leaves it again;
- stony hills with rock in the middle band (quarries);
- mountains along the east and south-east ('M', no building) with ore veins at their
  feet ('O', mines);
- forest in the west and south (woodcutters, hunters) and open meadows (farms).

Legend: '.' grass  'T' forest  ',' sand  '~' water  'R' rock (hills)  'M' mountain  'O' ore
Usage: python3 tools/map/generate_map.py   (deterministic: same seed, same valley)
"""
import json
import math
import os
import random

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONTENT = os.path.join(ROOT, "unity", "Assets", "Resources", "content.json")
W = H = 64
OLD = 21
SEED = 1871


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


def main():
    content = json.load(open(CONTENT))
    old_rows = content["map"]["rows"][:OLD]
    assert all(len(r) >= OLD for r in old_rows)
    grid = [["." for _ in range(W)] for _ in range(H)]
    for y in range(OLD):
        for x in range(OLD):
            grid[y][x] = old_rows[y][x]

    forest = value_noise(SEED, 7.0)
    forest2 = value_noise(SEED + 1, 3.0)
    hills = value_noise(SEED + 2, 6.0)
    peaks = value_noise(SEED + 3, 5.0)
    veins = value_noise(SEED + 4, 2.5)
    rnd = random.Random(SEED + 5)

    def new(x, y):
        return x >= OLD or y >= OLD

    # Base land: forest, meadows, hills.
    for y in range(H):
        for x in range(W):
            if not new(x, y):
                continue
            f = forest(x, y) * 0.7 + forest2(x, y) * 0.3
            west = max(0.0, (24 - x) / 24) * 0.25
            south = max(0.0, (y - 44) / 20) * 0.2
            if f + west + south > 0.62:
                grid[y][x] = "T"
            # Stony hills in the middle band (quarries).
            hx, hy = 31, 13
            d = math.hypot(x - hx, (y - hy) * 1.2)
            if (d < 6 + hills(x, y) * 3) or (hills(x, y) > 0.8 and 22 < x < 46 and y < 40):
                grid[y][x] = "R"

    # Mountains in the east and south-east; ore at their feet.
    for y in range(H):
        for x in range(W):
            if not new(x, y):
                continue
            east = (x - 47) / 9.0
            south_east = ((x - 40) / 14.0 + (y - 44) / 14.0) * 0.6
            m = max(east, south_east) + (peaks(x, y) - 0.5) * 0.9
            if m > 1.0:
                grid[y][x] = "M"
    for y in range(H):
        for x in range(W):
            if not new(x, y) or grid[y][x] == "M":
                continue
            near = any(0 <= x + dx < W and 0 <= y + dy < H and grid[y + dy][x + dx] == "M"
                       for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if near and veins(x, y) > 0.5:
                grid[y][x] = "O"

    # The river continues south from the old map into a lake, then on to the south edge.
    water = set()
    lake_c, lake_r = (15.0, 35.0), (8.5, 6.0)
    for y in range(OLD, H):
        if y < 30:
            cx = 9 + 2.2 * math.sin((y - OLD) / 4.0) + (y - OLD) * 0.45
        elif y > 41:
            cx = 17 + 2.5 * math.sin((y - 41) / 5.0) + (y - 41) * 0.12
        else:
            cx = None
        if cx is not None:
            for x in range(int(round(cx)) - 1, int(round(cx)) + 1):
                water.add((x, y))
    for y in range(H):
        for x in range(W):
            dx, dy = (x - lake_c[0]) / lake_r[0], (y - lake_c[1]) / lake_r[1]
            if dx * dx + dy * dy + (forest2(x, y) - 0.5) * 0.35 < 1.0:
                water.add((x, y))
    for (x, y) in water:
        if 0 <= x < W and 0 <= y < H and new(x, y):
            grid[y][x] = "~"
    # Sandy banks.
    for y in range(H):
        for x in range(W):
            if not new(x, y) or grid[y][x] in "~MO":
                continue
            near = any((x + dx, y + dy) in water for dx in (-1, 0, 1) for dy in (-1, 0, 1))
            if near and rnd.random() < 0.65:
                grid[y][x] = ","

    rows = ["".join(r) for r in grid]
    assert rows[:OLD] and all(rows[y][:OLD] == old_rows[y][:OLD] for y in range(OLD))
    m = content["map"]
    m["width"], m["height"], m["rows"] = W, H, rows
    for t in ("lake", "river valley", "hills of stone", "iron mountains"):
        if t not in m["traits"]:
            m["traits"].append(t)

    regions = [r for r in content["regions"] if r["id"] not in
               ("great_meadow", "iron_mountains", "lake_country", "deep_forest", "southern_peaks")]
    regions += [
        {"id": "great_meadow", "name": "Hills of Stone", "description": "Open meadows and stony hills east of the ridge: good quarries.",
         "x": 21, "y": 0, "width": 20, "height": 21, "revealedAtStart": False, "cost": {"food": 25, "wood": 10},
         "expeditionSeconds": 90, "legacyText": "Scouts crossed the ridge and found hills of good building stone."},
        {"id": "iron_mountains", "name": "Iron Mountains", "description": "Steep peaks with veins of ore at their feet.",
         "x": 41, "y": 0, "width": 23, "height": 32, "revealedAtStart": False, "cost": {"food": 40, "wood": 20},
         "expeditionSeconds": 150, "legacyText": "Scouts came back from the mountains with green and red stones that melt in fire."},
        {"id": "lake_country", "name": "Lake Country", "description": "The river widens into a great lake full of fish.",
         "x": 0, "y": 21, "width": 41, "height": 22, "revealedAtStart": False, "cost": {"food": 30, "wood": 10},
         "expeditionSeconds": 110, "legacyText": "Scouts followed the river south and found a lake as wide as the sky."},
        {"id": "deep_forest", "name": "Deep Forest", "description": "Old woods south of the lake, rich in game.",
         "x": 0, "y": 43, "width": 41, "height": 21, "revealedAtStart": False, "cost": {"food": 35, "wood": 15},
         "expeditionSeconds": 130, "legacyText": "Hunters returned from the deep forest with tales of giant elk."},
        {"id": "southern_peaks", "name": "Southern Peaks", "description": "Wild mountains beyond the lake, with ore in the scree.",
         "x": 41, "y": 32, "width": 23, "height": 32, "revealedAtStart": False, "cost": {"food": 45, "wood": 20},
         "expeditionSeconds": 160, "legacyText": "The southern peaks were mapped for the first time."},
    ]
    content["regions"] = regions
    json.dump(content, open(CONTENT, "w"), indent=1, ensure_ascii=False)
    counts = {}
    for r in rows:
        for ch in r:
            counts[ch] = counts.get(ch, 0) + 1
    print("map", W, "x", H, counts)
    for r in rows[::2]:
        print(r)


if __name__ == "__main__":
    main()
