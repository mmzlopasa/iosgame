#!/usr/bin/env python3
"""Writes the long-term progression into unity/Assets/Resources/content.json (idempotent).

Founders paces its eras like Whiteout Survival paces its furnace: the settlement's heart (the
campfire, later the village hearth) is levelled up, every level costs more and takes longer
than the last, and the heart's level opens the eras:

    level 10  -> Stable Village  (milestone stable_village)
    level 20  -> Bronze Dawn     (milestone first_civilization, era II)
    level 30  -> The Charter     (milestone the_charter, medieval era)

No other building may rise above the heart. Up to level 5 levels are cheap; after that each
level costs about 30 % more than the one before and takes longer to build. Buildings change
their look at levels 5, 10, 15 and 20 (a tent becomes a hut, a cottage, a timber house and
finally a two-storey house).

It also writes each job's skill (ranks 1-10 with perks), the wild food sources (berry bushes,
fish, game), the bridge decoration, the research pacing (techs gated by heart level) and the
time scale (one game day = one season = six real hours).

Usage: python3 tools/content/progression.py [--table]
"""
import json
import math
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONTENT = os.path.join(ROOT, "unity", "Assets", "Resources", "content.json")

DAY = 6 * 3600          # one game day (and one season) in real seconds
HEART_MAX = 30
BUILDING_MAX = 20
RESOURCES = ("food", "wood", "stone", "ore", "hides", "knowledge")


def rnd(x):
    """Round to a tidy number (5s and 10s for bigger amounts)."""
    if x < 20:
        return int(round(x))
    if x < 200:
        return int(round(x / 5.0)) * 5
    if x < 2000:
        return int(round(x / 10.0)) * 10
    return int(round(x / 50.0)) * 50


def growth(n):
    """Cost multiplier of the step from level n to n + 1: gentle up to 5, then ~30 % per level."""
    if n <= 4:
        return 0.5 + 0.25 * n
    return 1.5 * 1.3 ** (n - 4)


def build_time(n, base, cap):
    t = base * (0.6 + 0.2 * n if n <= 4 else 1.4 * 1.28 ** (n - 4))
    return float(min(cap, int(round(t))))


# MARK: - The heart

def heart_levels():
    levels = []
    for n in range(1, HEART_MAX):  # step n -> n + 1
        k = n + 1
        g = n if n <= 4 else 4 * 1.24 ** (n - 4)
        if n > 20:
            g = 4 * 1.24 ** 16 * 1.12 ** (n - 20)
        steep = 1 if k <= 5 else 3.5  # cheap up to level 5, then a real effort
        cost = {"wood": rnd(30 * g * steep)}
        if k >= 3:
            cost["stone"] = rnd(16 * g * steep)
        if k >= 7:
            cost["hides"] = rnd(3 * g)
        if k >= 12:
            cost["ore"] = rnd(3 * g)
        if k >= 16:
            cost["food"] = rnd(10 * g)
        seconds = 60 * (0.8 + 0.4 * n if n <= 4 else 42 * 1.36 ** (n - 4))
        seconds = float(min(36 * 3600, int(round(seconds))))
        req = [{"kind": "populationAtLeast", "amount": int(round(6 + 1.2 * (k - 1))) if k <= 10 else 17 + 2 * (k - 10)}]
        if 4 <= k <= 20:
            req.append({"kind": "buildingLevelAtLeast", "id": "storage_pit", "amount": max(1, k - 4)})
        if k > 20:
            req.append({"kind": "buildingLevelAtLeast", "id": "granary", "amount": k - 18})
        if k >= 6:
            # One more than the early techs alone (herbal healing counts as well).
            req.append({"kind": "techCountAtLeast", "amount": min(16, 2 + (k - 4) // 2) if k < 21 else min(16, 10 + (k - 20) // 2)})
        gates = {10: "permanent_settlement", 15: "agriculture", 20: "bronze_working", 25: "pottery", 30: "written_law"}
        if k in gates:
            req.append({"kind": "techResearched", "id": gates[k]})
        if k >= 8 and k <= 20:
            req.append({"kind": "buildingLevelAtLeast", "id": "shelter", "amount": max(1, k - 6)})
        levels.append({"cost": cost, "buildSeconds": seconds, "requirements": req})
    return levels


# MARK: - Other buildings

# How each building looks from a level on: (level, model, name or None).
LOOKS = {
    "shelter": [(5, "woven_shelter", "Hut"), (10, "daub_house", "Cottage"), (15, "medieval_cottage", "Timber House"),
                (20, "medieval_house", "Two-Storey House")],
    "woven_shelter": [(10, "daub_house", "Cottage"), (15, "medieval_cottage", "Timber House"), (20, "medieval_house", "Two-Storey House")],
    "daub_house": [(15, "medieval_cottage", "Timber House"), (20, "medieval_house", "Two-Storey House")],
    "longhouse": [(15, "medieval_townhouse", "Great Longhouse")],
    "medieval_cottage": [(10, "medieval_house", "Townhouse")],
    "campfire": [(5, "campfire_ring", "Stone Campfire")],
    "village_hearth": [(15, "hearth_pavilion", "Hearth Pavilion"), (25, "hearth_great", "Great Hearth")],
}

# Buildings with a shorter ladder (big monuments and special sites).
MAX_LEVEL = {"medieval_church": 10, "medieval_market": 15, "community_circle": 15, "elders_lodge": 15,
             "bronze_workshop": 15, "medieval_mill": 15, "medieval_smithy": 15}


def building_levels(b):
    base = {k: v for k, v in b.get("cost", {}).items() if v > 0}
    if not base:
        base = {"wood": 15}
    total = sum(base.values())
    top = MAX_LEVEL.get(b["id"], BUILDING_MAX)
    levels = []
    for n in range(1, top):
        k = n + 1
        f = growth(n)
        cost = {r: rnd(v * f) for r, v in base.items()}
        if k >= 3 and "stone" not in base:
            cost["stone"] = rnd(total * 0.4 * f)
        if k >= 8 and "hides" not in base:
            cost["hides"] = rnd(total * 0.06 * f)
        if k >= 12 and "ore" not in base:
            cost["ore"] = rnd(total * 0.08 * f)
        levels.append({"cost": {r: v for r, v in cost.items() if v > 0},
                       "buildSeconds": build_time(n, b["buildSeconds"], 10 * 3600)})
    return levels


# MARK: - Skills

SKILLS = [
    ("foraging", "Forager", [
        (2, "Keen Eye", "Spots ripe berries others miss.", 0.1, []),
        (4, "Herb Lore", "Knows which roots keep: food spoils a little slower.", 0.05, [{"kind": "spoilageMultiplier", "value": 0.92}]),
        (6, "Long Baskets", "Carries more each trip.", 0.15, []),
        (8, "Wild Harvest", "Finds food even in lean seasons.", 0.15, []),
        (10, "Master Forager", "The land feeds the whole camp.", 0.25, [{"kind": "jobRateMultiplier", "job": "foraging", "value": 1.05}]),
    ]),
    ("farming", "Farmer", [
        (2, "Furrows", "Straight furrows, better yields.", 0.1, []),
        (4, "Seed Saver", "Keeps the best seed for next year.", 0.1, []),
        (6, "Crop Rotation", "Rested fields give more.", 0.15, [{"kind": "jobRateMultiplier", "job": "farming", "value": 1.05}]),
        (8, "Weather Wise", "Sows and reaps at the right moment.", 0.15, []),
        (10, "Master Farmer", "Fields as far as the eye can see.", 0.25, [{"kind": "growthMultiplier", "value": 1.05}]),
    ]),
    ("herding", "Herder", [
        (2, "Gentle Hand", "Calm animals give more milk.", 0.1, []),
        (4, "Shearing", "More hides from every herd.", 0.1, []),
        (6, "Breeder", "Strong young every spring.", 0.15, [{"kind": "jobRateMultiplier", "job": "herding", "value": 1.05}]),
        (8, "Drover", "Moves herds to the best grass.", 0.15, []),
        (10, "Master Herder", "The finest herds in the valley.", 0.25, []),
    ]),
    ("fishing", "Fisher", [
        (2, "Patient", "Waits for the big one.", 0.1, []),
        (4, "Weir Builder", "Better traps in the stream.", 0.1, []),
        (6, "Net Weaver", "Nets catch whole shoals.", 0.15, []),
        (8, "River Reader", "Knows where the fish rest.", 0.15, [{"kind": "jobRateMultiplier", "job": "fishing", "value": 1.05}]),
        (10, "Master Fisher", "Never comes home empty-handed.", 0.25, []),
    ]),
    ("hunting", "Hunter", [
        (2, "Tracker", "Reads tracks in mud and grass.", 0.1, []),
        (4, "Silent Step", "Gets close before the deer notice.", 0.1, []),
        (6, "Spear Thrower", "Rarely misses.", 0.15, []),
        (8, "Tanner", "Brings back more hides.", 0.15, [{"kind": "spoilageMultiplier", "value": 0.95}]),
        (10, "Master Hunter", "Feared by wolves.", 0.25, [{"kind": "jobRateMultiplier", "job": "hunting", "value": 1.05}]),
    ]),
    ("woodcutting", "Woodcutter", [
        (2, "Sharp Edge", "Keeps the axe keen.", 0.1, []),
        (4, "Strong Swing", "Fells trees in fewer strokes.", 0.1, []),
        (6, "Forester", "Plants a sapling for every tree.", 0.15, [{"kind": "jobRateMultiplier", "job": "woodcutting", "value": 1.05}]),
        (8, "Timber Hauler", "Drags whole logs home.", 0.15, []),
        (10, "Master Woodcutter", "The forest's own friend.", 0.25, [{"kind": "constructionSpeedMultiplier", "value": 1.05}]),
    ]),
    ("stonecutting", "Quarrier", [
        (2, "Stone Sense", "Finds the seams in rock.", 0.1, []),
        (4, "Wedge and Lever", "Splits blocks cleanly.", 0.1, []),
        (6, "Mason's Eye", "Picks the best stones for building.", 0.15, [{"kind": "constructionSpeedMultiplier", "value": 1.05}]),
        (8, "Quarry Master", "Opens whole faces of rock.", 0.15, []),
        (10, "Master Quarrier", "Stone obeys.", 0.25, [{"kind": "jobRateMultiplier", "job": "stonecutting", "value": 1.05}]),
    ]),
    ("mining", "Miner", [
        (2, "Lamp Bearer", "Works deeper in the dark.", 0.1, []),
        (4, "Vein Follower", "Follows ore through the rock.", 0.1, []),
        (6, "Timbering", "Safe shafts, steady work.", 0.15, []),
        (8, "Smelter's Friend", "Brings purer ore.", 0.15, [{"kind": "jobRateMultiplier", "job": "mining", "value": 1.05}]),
        (10, "Master Miner", "The mountain gives up its treasure.", 0.25, []),
    ]),
    ("crafting", "Craftsman", [
        (2, "Steady Hands", "Fewer spoiled pieces.", 0.1, []),
        (4, "Toolsmith", "Better tools for every worker.", 0.1, []),
        (6, "Thrifty", "Wastes less wood and stone.", 0.15, []),
        (8, "Artisan", "Beautiful, lasting work.", 0.15, []),
        (10, "Master Craftsman", "Tools that last a lifetime.", 0.25, [{"kind": "jobRateMultiplier", "job": "woodcutting", "value": 1.03},
                                                                       {"kind": "jobRateMultiplier", "job": "stonecutting", "value": 1.03}]),
    ]),
    ("construction", "Builder", [
        (2, "Plumb Line", "Straight walls go up faster.", 0.1, []),
        (4, "Scaffolder", "Works high and safe.", 0.1, [{"kind": "constructionSpeedMultiplier", "value": 1.03}]),
        (6, "Joiner", "Timber joints that fit first time.", 0.15, []),
        (8, "Foreman", "Keeps the whole crew busy.", 0.15, [{"kind": "constructionSpeedMultiplier", "value": 1.05}]),
        (10, "Master Builder", "Raises halls in days.", 0.25, [{"kind": "constructionSpeedMultiplier", "value": 1.07}]),
    ]),
    ("research", "Elder", [
        (2, "Storyteller", "Remembers the old ways.", 0.1, []),
        (4, "Star Watcher", "Reads the seasons in the sky.", 0.1, []),
        (6, "Teacher", "Shares what they know.", 0.15, [{"kind": "jobRateMultiplier", "job": "research", "value": 1.05}]),
        (8, "Lore Keeper", "Nothing learnt is lost.", 0.15, []),
        (10, "Sage", "The wisest of the valley.", 0.25, [{"kind": "growthMultiplier", "value": 1.03}]),
    ]),
    ("healing", "Healer", [
        (2, "Willow Bark", "Eases fever and pain.", 0.1, []),
        (4, "Clean Hands", "Fewer fall ill around the healer.", 0.1, [{"kind": "sicknessMultiplier", "value": 0.95}]),
        (6, "Bone Setter", "Mends what the cold and work break.", 0.15, [{"kind": "recoveryMultiplier", "value": 1.1}]),
        (8, "Midwife", "Mothers and babies come through.", 0.15, [{"kind": "growthMultiplier", "value": 1.03}]),
        (10, "Master Healer", "Death waits outside the door.", 0.25, [{"kind": "deathMultiplier", "value": 0.8}]),
    ]),
]


def skills():
    out = []
    for job, title, perks in SKILLS:
        out.append({"job": job, "title": title, "perks": [
            {"level": lv, "name": name, "description": desc, "bonus": bonus, **({"effects": eff} if eff else {})}
            for lv, name, desc, bonus, eff in perks]})
    return out


# MARK: - Research pacing: (heart level, research minutes, knowledge cost)

RESEARCH = {
    "improved_stone_tools": (1, 4, 15), "food_preservation": (1, 4, 15),
    "structural_weaving": (3, 6, 25), "shared_labor": (4, 6, 30),
    "fishing": (4, 8, 35), "hide_working": (5, 8, 40), "permanent_settlement": (7, 12, 60),
    "oral_tradition": (8, 12, 70), "longhouses": (10, 16, 90), "agriculture": (12, 20, 110),
    "animal_husbandry": (14, 24, 130), "pottery": (15, 24, 140), "copper_smelting": (16, 30, 170),
    "bronze_working": (18, 40, 220), "written_law": (24, 60, 320), "masonry": (30, 60, 360),
    "ironworking": (30, 75, 400), "watermill": (30, 75, 400), "animal_breeding": (30, 75, 400),
    "guilds": (31, 90, 460), "burgher_rights": (32, 100, 500), "monastic_orders": (32, 110, 540),
}


def _lattice(ix, iy, seed):
    v = (ix * 374761393 + iy * 668265263 + seed) & 0xFFFFFFFF
    v = ((v ^ (v >> 13)) * 1274126177) & 0xFFFFFFFF
    return (v ^ (v >> 16)) / 0xFFFFFFFF


def _noise(x, y, scale, seed=977):
    fx, fy = x / scale, y / scale
    ix, iy = int(math.floor(fx)), int(math.floor(fy))
    tx, ty = fx - ix, fy - iy
    tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
    a = _lattice(ix, iy, seed) * (1 - tx) + _lattice(ix + 1, iy, seed) * tx
    b = _lattice(ix, iy + 1, seed) * (1 - tx) + _lattice(ix + 1, iy + 1, seed) * tx
    return a * (1 - ty) + b * ty


CAMP = (6, 7)


def thin_forest(m):
    """Opens meadows in the forest (once): woods stay in clusters, the land between is free to plan."""
    if m.get("forestThinned"):
        return
    rows = [list(r) for r in m["rows"]]
    h, w = len(rows), len(rows[0])
    for y in range(h):
        for x in range(w):
            if rows[y][x] != "T" or math.hypot(x - CAMP[0], y - CAMP[1]) < 7:
                continue
            n = _noise(x, y, 6.0) * 0.75 + _noise(x + 100, y + 100, 2.5) * 0.25
            if n < 0.47:
                rows[y][x] = "."
    m["rows"] = ["".join(r) for r in rows]
    m["forestThinned"] = True


def thin_forest_more(m):
    """Second clearing (once, about 40 % of the woods): the valley was too cluttered to read.

    Lone trees and thin strips in open land go first, the land around the camp opens up, but the
    trees right next to the camp (the first wood camp's) stay, and the Northern Forest keeps its woods.
    """
    if m.get("forestThinning", 1 if m.get("forestThinned") else 0) >= 2:
        return
    rows = [list(r) for r in m["rows"]]
    h, w = len(rows), len(rows[0])

    def neighbours(x, y):
        return sum(1 for dx in (-1, 0, 1) for dy in (-1, 0, 1)
                   if (dx or dy) and 0 <= x + dx < w and 0 <= y + dy < h and rows[y + dy][x + dx] == "T")

    out = [r[:] for r in rows]
    for y in range(h):
        for x in range(w):
            if rows[y][x] != "T":
                continue
            d = math.hypot(x - CAMP[0], y - CAMP[1])
            if d < 3.5:
                continue
            n = _noise(x, y, 5.0, 4241) * 0.7 + _noise(x + 50, y + 50, 2.0, 4241) * 0.3
            t = 0.46
            if d < 12:
                t += 0.06           # open up the land around the start
            if neighbours(x, y) <= 3:
                t += 0.2            # lone trees and thin strips in the meadows
            if y <= 2 and x < 12:
                t -= 0.35           # the Northern Forest (an expedition goes there)
            if n < t:
                out[y][x] = "."
    m["rows"] = ["".join(r) for r in out]
    m["forestThinning"] = 2


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    b = c["balance"]
    before = sum(r.count("T") for r in c["map"]["rows"])
    thin_forest(c["map"])
    thin_forest_more(c["map"])
    after = sum(r.count("T") for r in c["map"]["rows"])
    if before != after:
        print(f"forest cells: {before} -> {after}")

    # Time: a day lasts six real hours and each season one day, so a year is four days.
    b["seasonSeconds"] = DAY
    b["maxOfflineSeconds"] = 12 * 3600
    b["skillSecondsPerLevel"] = 150
    b["skillBonusPerLevel"] = 0.05
    b["maxSkillLevel"] = 10
    b["knowledgeCap"] = 600
    b["forestWood"] = 100
    b["growthSecondsPerPerson"] = 150
    b["maxBuildersPerSite"] = 4
    b["rockStone"] = 160
    b["baseStorage"] = 200
    b["nodes"] = [
        {"id": "berries", "name": "Berry Bush", "terrain": "grass", "resource": "food", "job": "foraging",
         "capacity": 70, "regrowSeconds": 600, "density": 0.1},
        {"id": "fish", "name": "Fishing Water", "terrain": "water", "resource": "food", "job": "fishing",
         "capacity": 30, "regrowSeconds": 600},
        {"id": "game", "name": "Game Trail", "terrain": "forest", "resource": "food", "job": "hunting",
         "capacity": 20, "regrowSeconds": 900, "density": 0.6, "slot": 1},
    ]

    for bld in c["buildings"]:
        bid = bld["id"]
        if bid in ("campfire", "village_hearth"):
            bld["heart"] = True
            bld["levels"] = heart_levels()
            # The heart gathers a little of everything so the first tap on a rock finds a worker.
            slots = {s["job"]: s["count"] for s in bld.get("jobSlots", [])}
            slots.setdefault("stonecutting", 1)
            bld["jobSlots"] = [{"job": j, "count": n} for j, n in slots.items()]
            bld["storage"] = 100 if bid == "campfire" else 150
        elif bld.get("buildable"):
            bld["levels"] = building_levels(bld)
        elif bid in ("woven_shelter", "daub_house"):
            bld["levels"] = building_levels(bld)
        if bid == "storage_pit":
            bld["storage"] = 150
        # The river's sandy banks are building land like the meadows.
        allowed = bld.get("allowedTerrain")
        if allowed and "grass" in allowed and "sand" not in allowed:
            allowed.insert(allowed.index("grass") + 1, "sand")
        if bid in LOOKS:
            bld["looks"] = [{"level": lv, "sprite": sp, **({"name": nm} if nm else {})} for lv, sp, nm in LOOKS[bid]]

    # The eras open with the heart's level instead of the passing years.
    for ms in c["milestones"]:
        reqs = [r for r in ms["requirements"] if r["kind"] not in ("yearAtLeast", "heartLevelAtLeast")]
        heart = {"stable_village": 10, "first_civilization": 20, "the_charter": 30, "market_town": 30, "walled_town": 30}.get(ms["id"])
        if heart:
            reqs.insert(0, {"kind": "heartLevelAtLeast", "amount": heart})
        if ms["id"] == "market_town":
            reqs.append({"kind": "buildingLevelAtLeast", "id": "medieval_market", "amount": 5})
        if ms["id"] == "walled_town":
            reqs.append({"kind": "buildingLevelAtLeast", "id": "medieval_church", "amount": 5})
        ms["requirements"] = [r for i, r in enumerate(reqs) if r not in reqs[:i]]
    for ch in c["chapters"]:
        reqs = [r for r in ch.get("requirements", []) if r["kind"] != "yearAtLeast"]
        if ch["id"] == "ch2_stable_village" and not any(r["kind"] == "heartLevelAtLeast" for r in reqs):
            reqs.insert(0, {"kind": "heartLevelAtLeast", "amount": 15})
        if ch["id"] == "ch4_hillfort" and not any(r["kind"] == "heartLevelAtLeast" for r in reqs):
            reqs.insert(0, {"kind": "heartLevelAtLeast", "amount": 25})
        if "requirements" in ch:
            ch["requirements"] = reqs

    for t in c["technologies"]:
        if t["id"] in RESEARCH:
            lvl, minutes, cost = RESEARCH[t["id"]]
            # The heart tops out at HEART_MAX: a tech asking for more could never be studied.
            t["heartLevel"] = min(lvl, HEART_MAX)
            t["researchSeconds"] = float(minutes * 60)
            t["cost"] = {"knowledge": cost}

    c["skills"] = skills()

    decos = {d["id"]: d for d in c["decorations"]}
    for fid in ("wattle_fence", "palisade", "stone_wall"):
        if fid in decos:
            decos[fid]["blocks"] = True
    if "wooden_bridge" not in decos:
        c["decorations"].insert(2, {
            "id": "wooden_bridge", "name": "Wooden Bridge", "category": "Roads",
            "description": "Planks on stout piles: people cross the river here.",
            "cost": {"wood": 12}, "road": True, "bridge": True})
    # Over the water, or on the sandy bank beside it (where the river looks wider than its cells).
    next(d for d in c["decorations"] if d["id"] == "wooden_bridge")["allowedTerrain"] = ["water", "sand"]

    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")

    if "--table" in sys.argv:
        print("heart level costs:")
        for n, lv in enumerate(heart_levels(), start=2):
            print(f"  L{n:2d}  {lv['cost']}  {lv['buildSeconds'] / 60:7.1f} min  {[(r['kind'], r.get('id'), r.get('amount')) for r in lv['requirements']]}")
        shelter = next(x for x in c["buildings"] if x["id"] == "shelter")
        print("shelter:")
        for n, lv in enumerate(shelter["levels"], start=2):
            print(f"  L{n:2d}  {lv['cost']}  {lv['buildSeconds'] / 60:6.1f} min")


if __name__ == "__main__":
    main()
