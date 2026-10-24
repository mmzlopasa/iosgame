#!/usr/bin/env python3
"""Monuments built part by part (idempotent): the Lord's Manor, the Abbey and the Stone Keep.

The player lays out a monument's plot, then raises its parts one by one from the monument's plan; each part
needs the parts it joins onto and brings its own effects, gathering-place points and estate favour.

Usage: python3 tools/content/monuments.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT  # noqa: E402
from present_eras import building, upsert, IV  # noqa: E402


def part(id, name, description, cost, minutes, requires=(), effects=(), community=0, estate=None, favour=0):
    p = {"id": id, "name": name, "description": description, "cost": cost, "buildSeconds": float(minutes * 60),
         "requires": list(requires), "effects": list(effects)}
    if community:
        p["community"] = community
    if estate:
        p["estate"], p["favour"] = estate, favour
    return p


MONUMENTS = [
    building("lords_manor", "Lord's Manor",
             "The lord's seat, raised part by part as you choose: a great hall first, then kitchens, a tower, "
             "a garden and a steward's office. Each part pleases the Crown.",
             IV, (4, 4), {"wood": 120, "stone": 60}, 60, "written_law", maxCount=1,
             parts=[
                 part("great_hall", "Great Hall", "Feasts and the lord's court. A gathering place (+3) and +4 Crown favour an hour.",
                      {"wood": 160, "stone": 120, "planks": 30}, 4, community=3, estate="crown", favour=4),
                 part("manor_kitchen", "Kitchens", "Bread and stew for the hall's guests. +2 approval.",
                      {"wood": 80, "stone": 60, "planks": 15}, 3, ["great_hall"],
                      [{"kind": "approvalBonus", "value": 2}]),
                 part("solar_tower", "Solar Tower", "The lord's chambers and strongroom. The treasury holds 25% more; +3 Crown favour.",
                      {"wood": 60, "stone": 180, "planks": 20}, 5, ["great_hall"],
                      [{"kind": "storageMultiplier", "resource": "coins", "value": 1.25}], estate="crown", favour=3),
                 part("manor_garden", "Walled Garden", "Herbs, roses and a fountain. A gathering place (+2), 5% less illness.",
                      {"wood": 40, "stone": 80}, 3, ["manor_kitchen"],
                      [{"kind": "sicknessMultiplier", "value": 0.95}], community=2),
                 part("stewards_office", "Steward's Office", "Ledgers and the lord's builders. Building goes 10% faster; +3 Crown favour.",
                      {"wood": 70, "stone": 70, "planks": 30, "tools": 5}, 4, ["solar_tower"],
                      [{"kind": "constructionSpeedMultiplier", "value": 1.1}], estate="crown", favour=3),
             ]),
    building("abbey", "Abbey",
             "Monks build their house of prayer and learning part by part: the church, a cloister, an infirmary, "
             "a brewery and a library. Each part pleases the Clergy.",
             IV, (4, 4), {"wood": 100, "stone": 80}, 60, "monastic_orders", maxCount=1,
             parts=[
                 part("abbey_church", "Abbey Church", "Bells and chant. A gathering place (+3) and +6 Clergy favour an hour.",
                      {"wood": 120, "stone": 220, "planks": 30}, 5, community=3, estate="clergy", favour=6),
                 part("cloister", "Cloister", "A quiet garth for study. Research 10% faster; +3 Clergy favour.",
                      {"wood": 60, "stone": 140, "planks": 20}, 4, ["abbey_church"],
                      [{"kind": "jobRateMultiplier", "job": "research", "value": 1.1}], estate="clergy", favour=3),
                 part("infirmary", "Infirmary", "Beds and herbs for the sick. 15% less illness, the sick get well 15% faster.",
                      {"wood": 70, "stone": 90, "planks": 20}, 4, ["cloister"],
                      [{"kind": "sicknessMultiplier", "value": 0.85}, {"kind": "recoveryMultiplier", "value": 1.15}]),
                 part("abbey_brewery", "Brewery", "Ale for feast days. +2 approval, people grumble a little less.",
                      {"wood": 70, "stone": 50, "planks": 20}, 3, ["abbey_church"],
                      [{"kind": "approvalBonus", "value": 2}, {"kind": "discontentPerHour", "value": -0.3}]),
                 part("abbey_library", "Library", "Copied books from far away. Research 10% faster again; +4 Clergy favour.",
                      {"wood": 60, "stone": 100, "planks": 40}, 5, ["cloister"],
                      [{"kind": "jobRateMultiplier", "job": "research", "value": 1.1}], estate="clergy", favour=4),
             ]),
    building("stone_keep", "Stone Keep",
             "A castle raised part by part: a keep tower, a curtain wall, a gatehouse, a watch house and a great "
             "storehouse. Pleases the Crown and keeps the town calm.",
             IV, (4, 4), {"wood": 80, "stone": 120}, 60, "masonry", maxCount=1,
             parts=[
                 part("keep_tower", "Keep Tower", "A tall square tower. Stores hold 15% more food; +3 Crown favour.",
                      {"wood": 80, "stone": 260}, 5, effects=[{"kind": "storageMultiplier", "resource": "food", "value": 1.15}],
                      estate="crown", favour=3),
                 part("curtain_wall", "Curtain Wall", "Stone walls round the bailey. People feel safe: discontent eases.",
                      {"stone": 300}, 6, ["keep_tower"], [{"kind": "discontentPerHour", "value": -0.5}]),
                 part("gatehouse", "Gatehouse", "Riders go out to the lands around. Scouts explore 15% faster.",
                      {"wood": 60, "stone": 140, "planks": 20}, 4, ["curtain_wall"],
                      [{"kind": "expeditionSpeedMultiplier", "value": 1.15}]),
                 part("watch_house", "Watch House", "Guards keep the peace. +1 approval and +2 Commons favour.",
                      {"wood": 70, "stone": 60, "planks": 15}, 3, ["keep_tower"],
                      [{"kind": "approvalBonus", "value": 1}], estate="commons", favour=2),
                 part("great_storehouse", "Great Storehouse", "Vaults under the bailey. Stores hold 20% more wood and stone.",
                      {"wood": 100, "stone": 160, "planks": 30}, 5, ["curtain_wall"],
                      [{"kind": "storageMultiplier", "resource": "wood", "value": 1.2},
                       {"kind": "storageMultiplier", "resource": "stone", "value": 1.2}]),
             ]),
]

UNLOCKS = {"written_law": "lords_manor", "monastic_orders": "abbey", "masonry": "stone_keep"}


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    for m in MONUMENTS:
        upsert(c["buildings"], dict(m))
    for t in c["technologies"]:
        bid = UNLOCKS.get(t["id"])
        if bid:
            e = {"kind": "unlockBuilding", "buildingId": bid}
            if e not in t["effects"]:
                t["effects"].append(e)
            name = next(m["name"] for m in MONUMENTS if m["id"] == bid)
            if name not in t["description"]:
                t["description"] = t["description"].rstrip(".") + f". Lay out the {name}, built part by part."
    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"{len(c['buildings'])} buildings")


if __name__ == "__main__":
    main()
