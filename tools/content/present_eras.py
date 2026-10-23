#!/usr/bin/env python3
"""Adds the second wave of content for the eras the game already has (idempotent).

The modern era is paused, so this fills the three playable eras instead:

  First Communities   Lookout Tower, Herb Garden, Smokehouse, Ancestor Stones
  First Civilizations Sawpit (wood -> planks), Apiary, Toolsmith (bronze + planks -> tools)
  Medieval Kingdoms   Bakery, Carpenter's Yard, Tavern, Scriptorium, Warehouse, Bathhouse

Two new goods: planks (sawn from wood) and tools (cast from bronze and planks). Tools in store
make every gatherer faster and wear out as they work (balance.toolBonus, toolWearPerWorkerPerSecond).
Buildings may carry lasting "effects" (a herb garden keeps sickness away, a lookout speeds the
scouts). New research opens them, six goals follow the village founding, and three events
react to them. Level ladders come from progression.py so they grow like every other building.

Usage: python3 tools/content/present_eras.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT, building_levels  # noqa: E402

GRASS = ["grass", "sand"]


def building(id, name, description, epoch, size, cost, seconds, tech, **extra):
    b = {"id": id, "name": name, "description": description, "epoch": epoch,
         "footprint": {"width": size[0], "height": size[1]}, "cost": cost, "buildSeconds": seconds,
         "sprite": id, "allowedTerrain": list(GRASS), "buildable": True, "requiresTech": tech}
    b.update(extra)
    return b


I, II, IV = "epoch_first_communities", "epoch_first_civilizations", "epoch_medieval_kingdoms"

BUILDINGS = [
    # MARK: Era I
    building("lookout_tower", "Lookout Tower",
             "A tall timber tower over the valley. Scouts explore new lands 25% faster.",
             I, (1, 1), {"wood": 45, "stone": 15}, 50, "trail_signs", maxCount=1,
             effects=[{"kind": "expeditionSpeedMultiplier", "value": 1.25}]),
    building("herb_garden", "Herb Garden",
             "Beds of yarrow, mint and willow. Fewer people fall ill (-10%) and the sick get well 10% faster.",
             I, (1, 1), {"wood": 20, "stone": 10}, 30, "herbal_healing", maxCount=2,
             effects=[{"kind": "sicknessMultiplier", "value": 0.9}, {"kind": "recoveryMultiplier", "value": 1.1}]),
    building("smokehouse", "Smokehouse",
             "Fish and meat hang in slow smoke. Protects 60 food, food spoils 10% slower, and smoked food counts for food variety.",
             I, (1, 1), {"wood": 35, "stone": 15}, 40, "hide_working", protectedFood=60, foodSource="smoked",
             effects=[{"kind": "spoilageMultiplier", "value": 0.9}]),
    building("ancestor_shrine", "Ancestor Stones",
             "Tall stones where the first founders rest. A gathering place, and people grumble less.",
             I, (2, 2), {"wood": 30, "stone": 90}, 90, "ancestor_rites", maxCount=1, community=4,
             effects=[{"kind": "discontentPerHour", "value": -1}]),
    # MARK: Era II
    building("sawpit", "Sawpit",
             "A log over a pit and a two-man saw. 2 Sawing jobs turn 2 Wood into 1 Plank.",
             II, (1, 1), {"wood": 40, "stone": 20}, 45, "carpentry", jobSlots=[{"job": "sawing", "count": 2}]),
    building("apiary", "Apiary",
             "Straw hives humming with bees. +2 Foraging jobs, and honey counts for food variety.",
             II, (1, 1), {"wood": 30, "stone": 10}, 35, "beekeeping", foodSource="honey",
             jobSlots=[{"job": "foraging", "count": 2}]),
    building("toolsmith", "Toolsmith",
             "Moulds, hammers and a whetstone. 2 Toolmaking jobs turn 1 Bronze and 1 Plank into 2 Tools. "
             "While there are tools in store every gatherer works 15% faster.",
             II, (1, 1), {"wood": 40, "stone": 40, "bronze": 10}, 60, "bronze_tools",
             jobSlots=[{"job": "toolmaking", "count": 2}]),
    # MARK: Era IV
    building("medieval_bakery", "Bakery",
             "A brick oven that never cools. 3 Baking jobs bake bread: each Wood burnt gives 4 Food. Bread counts for food variety.",
             IV, (2, 2), {"wood": 80, "stone": 90, "planks": 30}, 90, "bread_ovens", foodSource="bread",
             jobSlots=[{"job": "baking", "count": 3}]),
    building("medieval_carpenter", "Carpenter's Yard",
             "A roofed yard of saws and trestles. 3 Sawing jobs turn Wood into Planks.",
             IV, (2, 2), {"wood": 70, "stone": 40}, 70, "masonry", jobSlots=[{"job": "sawing", "count": 3}]),
    building("medieval_tavern", "Tavern",
             "Ale, stew and songs by the fire. A big gathering place, and people grumble much less.",
             IV, (2, 2), {"wood": 90, "stone": 100, "planks": 40}, 100, "burgher_rights", maxCount=2, community=8,
             effects=[{"kind": "discontentPerHour", "value": -2}]),
    building("medieval_scriptorium", "Scriptorium",
             "Quiet desks where scribes copy books. +3 Research jobs.",
             IV, (2, 2), {"wood": 80, "stone": 140, "planks": 30}, 110, "monastic_orders", maxCount=2,
             jobSlots=[{"job": "research", "count": 3}]),
    building("medieval_warehouse", "Warehouse",
             "A great timber store with a hoist. +400 storage for every good.",
             IV, (3, 3), {"wood": 140, "stone": 100, "planks": 60}, 110, "guilds", storage=400),
    building("medieval_bathhouse", "Bathhouse",
             "Hot water and clean linen. Fewer people fall ill (-15%), the sick get well 20% faster, and people like to meet there.",
             IV, (2, 2), {"wood": 70, "stone": 120}, 90, "masonry", maxCount=1, community=3,
             effects=[{"kind": "sicknessMultiplier", "value": 0.85}, {"kind": "recoveryMultiplier", "value": 1.2}]),
]

# Shorter ladders for the special buildings (as progression.MAX_LEVEL).
MAX_LEVEL = {"lookout_tower": 10, "ancestor_shrine": 10, "herb_garden": 15, "medieval_tavern": 15,
             "medieval_bathhouse": 10, "medieval_scriptorium": 15, "toolsmith": 15}


def levels(b):
    out = building_levels(b)
    top = MAX_LEVEL.get(b["id"])
    return out[:top - 1] if top else out


def tech(id, name, description, heart, minutes, cost, prereqs, effects, epoch=None):
    t = {"id": id, "name": name, "description": description, "cost": {"knowledge": cost},
         "researchSeconds": float(minutes * 60), "prerequisites": prereqs, "effects": effects, "heartLevel": heart}
    if epoch:
        t["epoch"] = epoch
    return t


def unlock(bid):
    return {"kind": "unlockBuilding", "buildingId": bid}


TECHS = [
    tech("trail_signs", "Trail Signs",
         "Blazes on trees and cairns on hills. Scouts explore 10% faster and can build a Lookout Tower.",
         6, 8, 40, ["shared_labor"], [{"kind": "expeditionSpeedMultiplier", "value": 1.1}, unlock("lookout_tower")]),
    tech("ancestor_rites", "Ancestor Rites",
         "The founders are honoured at tall stones. Unlocks the Ancestor Stones.",
         9, 14, 80, ["oral_tradition"], [unlock("ancestor_shrine")]),
    tech("carpentry", "Carpentry",
         "Saws, wedges and straight planks. Wood gathering +5% and unlocks the Sawpit.",
         13, 20, 115, ["agriculture"], [{"kind": "jobRateMultiplier", "job": "woodcutting", "value": 1.05}, unlock("sawpit")]),
    tech("beekeeping", "Beekeeping",
         "Wild swarms are coaxed into straw hives. Unlocks the Apiary.",
         14, 20, 120, ["agriculture"], [unlock("apiary")]),
    tech("bronze_tools", "Bronze Tools",
         "Axes, sickles and picks cast in bronze. Unlocks the Toolsmith.",
         19, 45, 250, ["bronze_working", "carpentry"], [unlock("toolsmith")]),
    tech("bread_ovens", "Bread Ovens",
         "Flour from the mill becomes bread in brick ovens. Unlocks the Bakery.",
         30, 75, 420, ["watermill"], [unlock("medieval_bakery")], epoch=IV),
]

# Existing research that now also opens a new building.
EXTRA_UNLOCKS = {
    "herbal_healing": ["herb_garden"], "hide_working": ["smokehouse"],
    "masonry": ["medieval_carpenter", "medieval_bathhouse"], "guilds": ["medieval_warehouse"],
    "burgher_rights": ["medieval_tavern"], "monastic_orders": ["medieval_scriptorium"],
}

JOBS = [
    {"job": "sawing", "name": "Sawing", "description": "Saw logs into straight planks: 2 Wood make 1 Plank.",
     "output": "planks", "inputs": {"wood": 2}, "ratePerWorkerPerSecond": 0.05, "unlimitedSlots": False},
    {"job": "toolmaking", "name": "Toolmaking",
     "description": "Cast and haft tools: 1 Bronze and 1 Plank make 2 Tools. Tools make gatherers faster.",
     "output": "tools", "inputs": {"bronze": 0.5, "planks": 0.5}, "ratePerWorkerPerSecond": 0.03, "unlimitedSlots": False},
    {"job": "baking", "name": "Baking", "description": "Bake bread from the town's flour: each Wood burnt in the oven gives 4 Food.",
     "output": "food", "inputs": {"wood": 0.25}, "ratePerWorkerPerSecond": 0.3, "unlimitedSlots": False},
]

SKILLS = [
    {"job": "sawing", "title": "Sawyer", "perks": [
        {"level": 2, "name": "True Line", "description": "Follows the chalk line.", "bonus": 0.1},
        {"level": 4, "name": "Set Teeth", "description": "Keeps the saw sharp and set.", "bonus": 0.1},
        {"level": 6, "name": "Quarter Sawn", "description": "Planks that never warp.", "bonus": 0.15,
         "effects": [{"kind": "constructionSpeedMultiplier", "value": 1.03}]},
        {"level": 8, "name": "Pit Master", "description": "Two logs in the time of one.", "bonus": 0.15},
        {"level": 10, "name": "Master Sawyer", "description": "Not a splinter wasted.", "bonus": 0.25,
         "effects": [{"kind": "jobRateMultiplier", "job": "sawing", "value": 1.05}]}]},
    {"job": "toolmaking", "title": "Toolsmith", "perks": [
        {"level": 2, "name": "Good Moulds", "description": "Fewer cracked castings.", "bonus": 0.1},
        {"level": 4, "name": "Keen Edge", "description": "Tools that cut clean.", "bonus": 0.1},
        {"level": 6, "name": "Ash Hafts", "description": "Handles that never split.", "bonus": 0.15},
        {"level": 8, "name": "Tempered", "description": "Hard and springy bronze.", "bonus": 0.15,
         "effects": [{"kind": "jobRateMultiplier", "job": "mining", "value": 1.03}]},
        {"level": 10, "name": "Master Toolsmith", "description": "Every worker asks for their tools.", "bonus": 0.25,
         "effects": [{"kind": "jobRateMultiplier", "job": "toolmaking", "value": 1.05}]}]},
    {"job": "baking", "title": "Baker", "perks": [
        {"level": 2, "name": "Early Riser", "description": "Bread before dawn.", "bonus": 0.1},
        {"level": 4, "name": "Sourdough", "description": "Bread that keeps for days.", "bonus": 0.1,
         "effects": [{"kind": "spoilageMultiplier", "value": 0.95}]},
        {"level": 6, "name": "Hot Oven", "description": "Bakes more with less wood.", "bonus": 0.15},
        {"level": 8, "name": "Feast Loaves", "description": "Bread for every table.", "bonus": 0.15},
        {"level": 10, "name": "Master Baker", "description": "The smell fills the whole town.", "bonus": 0.25,
         "effects": [{"kind": "approvalBonus", "value": 2}]}]},
]

OBJECTIVES = [
    {"id": "obj_smokehouse", "title": "Smoke food for the winter",
     "hint": "Research Hide Working, then build a Smokehouse: it keeps food from spoiling.",
     "conditions": [{"kind": "buildingCount", "id": "smokehouse", "amount": 1}], "reward": {"food": 40}},
    {"id": "obj_ancestors", "title": "Raise the Ancestor Stones",
     "hint": "Research Ancestor Rites, then build the Ancestor Stones near the hearth.",
     "conditions": [{"kind": "buildingCount", "id": "ancestor_shrine", "amount": 1}], "reward": {"knowledge": 30}},
    {"id": "obj_planks", "title": "Saw the first planks",
     "hint": "Research Carpentry, build a Sawpit and put two sawyers to work.",
     "conditions": [{"kind": "madeAtLeast", "resource": "planks", "amount": 20}], "reward": {"wood": 60}},
    {"id": "obj_tools", "title": "Forge bronze tools",
     "hint": "Research Bronze Tools and build a Toolsmith. Tools need bronze and planks.",
     "conditions": [{"kind": "madeAtLeast", "resource": "tools", "amount": 10}], "reward": {"stone": 80, "ore": 20}},
    {"id": "obj_bread", "title": "Bake the town's bread",
     "hint": "Research Bread Ovens and build a Bakery: bakers turn firewood into food.",
     "conditions": [{"kind": "buildingCount", "id": "medieval_bakery", "amount": 1}], "reward": {"planks": 40}},
    {"id": "obj_tavern", "title": "Open a tavern",
     "hint": "Research Burgher Rights and build a Tavern: people grumble less.",
     "conditions": [{"kind": "buildingCount", "id": "medieval_tavern", "amount": 1}], "reward": {"knowledge": 80}},
]

EVENTS = [
    {"id": "master_carpenter", "title": "The Master Carpenter",
     "text": "A carpenter from the hills has heard the sound of your saw. He offers to stay for a while.",
     "trigger": [{"kind": "buildingCount", "id": "sawpit", "amount": 1}],
     "options": [
         {"id": "hire", "label": "Feed him and let him work",
          "tradeOff": "Give 40 food. He saws 40 planks and the sawyers work 25% faster for 2 hours.",
          "cost": {"food": 40},
          "effects": [{"kind": "grantResources", "resources": {"planks": 40}},
                      {"kind": "jobRateMultiplier", "job": "sawing", "value": 1.25, "durationSeconds": 7200}],
          "legacyText": "A master carpenter from the hills taught the valley to join timber."},
         {"id": "learn", "label": "Learn his joints",
          "tradeOff": "Give 30 wood for practice: +40 knowledge.", "cost": {"wood": 30},
          "effects": [{"kind": "grantResources", "resources": {"knowledge": 40}}],
          "legacyText": "The carpenter's joints were remembered long after he left."},
         {"id": "decline", "label": "Thank him and let him go", "tradeOff": "No cost, no gain.", "effects": [],
          "legacyText": "The carpenter went on over the hills."}]},
    {"id": "frost_cracked_tools", "title": "Frost-Cracked Tools",
     "text": "A hard frost split the hafts of many tools in the store. The workers grumble over broken handles.",
     "trigger": [{"kind": "madeAtLeast", "resource": "tools", "amount": 10}],
     "baseEffects": [{"kind": "approvalBonus", "value": -3, "durationSeconds": 3600}],
     "options": [
         {"id": "mend", "label": "Fit new hafts",
          "tradeOff": "Give 15 planks: the tools are mended and 10 more are made.", "cost": {"planks": 15},
          "effects": [{"kind": "grantResources", "resources": {"tools": 10}}],
          "legacyText": "Every tool in the valley got a new ash haft that winter."},
         {"id": "make_do", "label": "Make do",
          "tradeOff": "No cost, but wood and stone gathering are 15% slower for 1 hour.",
          "effects": [{"kind": "jobRateMultiplier", "job": "woodcutting", "value": 0.85, "durationSeconds": 3600},
                      {"kind": "jobRateMultiplier", "job": "stonecutting", "value": 0.85, "durationSeconds": 3600}],
          "legacyText": "The valley worked on with cracked tools until spring."}]},
    {"id": "tavern_minstrels", "title": "Minstrels at the Tavern",
     "text": "Travelling minstrels have come to the new tavern. The whole town wants to hear them.",
     "trigger": [{"kind": "buildingCount", "id": "medieval_tavern", "amount": 1}],
     "options": [
         {"id": "feast", "label": "Hold a feast",
          "tradeOff": "Give 80 food: +8 hope for 4 hours.", "cost": {"food": 80},
          "effects": [{"kind": "approvalBonus", "value": 8, "durationSeconds": 14400}],
          "legacyText": "The minstrels' feast was sung about for a generation."},
         {"id": "news", "label": "Ask them for news",
          "tradeOff": "Give 20 food for their supper: +60 knowledge from far-off towns.", "cost": {"food": 20},
          "effects": [{"kind": "grantResources", "resources": {"knowledge": 60}}],
          "legacyText": "The minstrels brought news of kingdoms beyond the mountains."},
         {"id": "quiet", "label": "Keep the town at work", "tradeOff": "No cost, no gain.", "effects": [],
          "legacyText": "The minstrels played to an empty square and moved on."}]},
]


def upsert(items, item, key="id", after=None):
    """Replaces the item with the same key, or inserts it (after the item with id `after`, else at the end)."""
    for i, old in enumerate(items):
        if old.get(key) == item[key]:
            items[i] = item
            return
    if after is not None:
        for i, old in enumerate(items):
            if old.get(key) == after:
                items.insert(i + 1, item)
                return
    items.append(item)


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    bal = c["balance"]
    bal["toolBonus"] = 0.15
    bal["toolWearPerWorkerPerSecond"] = 0.001
    for job in JOBS:
        upsert(bal["jobs"], job, key="job")
    for s in SKILLS:
        upsert(c["skills"], s, key="job")

    for b in BUILDINGS:
        b = dict(b)
        b["levels"] = levels(b)
        upsert(c["buildings"], b)

    # Each new study sits right after the one it builds on (the research list keeps content order).
    ids = {t["id"] for t in TECHS}
    c["technologies"] = [t for t in c["technologies"] if t["id"] not in ids]
    for t in TECHS:
        upsert(c["technologies"], t, after=t["prerequisites"][0])
    for tid, bids in EXTRA_UNLOCKS.items():
        t = next(x for x in c["technologies"] if x["id"] == tid)
        for bid in bids:
            if unlock(bid) not in t["effects"]:
                t["effects"].append(unlock(bid))

    prev = "obj_village"
    for o in OBJECTIVES:
        upsert(c["objectives"], o, after=prev)
        prev = o["id"]
    for e in EVENTS:
        upsert(c["events"], e)

    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"{len(c['buildings'])} buildings, {len(c['technologies'])} techs, {len(c['objectives'])} goals, {len(c['events'])} events")


if __name__ == "__main__":
    main()
