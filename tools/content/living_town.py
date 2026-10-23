#!/usr/bin/env python3
"""Needs, house tiers, taxes and household goods (idempotent).

Homes climb three tiers with their level (huts, houses, burgher houses). Each tier needs more:
food; then water (a well or the river nearby); then household goods from a market nearby.
Homes with everything they need pay full tax and lift approval; the rest pay half and grumble.
Coins (the treasury) come from taxes; household goods are woven at the Weaver's Hut.

Usage: python3 tools/content/living_town.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT  # noqa: E402
from present_eras import building, levels, upsert, I  # noqa: E402

NEEDS = {
    "waterReach": 6, "riverReach": 3, "marketReach": 10,
    "goodsPerPersonPerSecond": 0.0003,
    "metApproval": 6, "unmetApproval": 8,
    "tiers": [
        {"level": 1, "name": "Huts", "needs": ["food"], "taxPerPersonPerHour": 0.4},
        {"level": 5, "name": "Houses", "needs": ["food", "water"], "taxPerPersonPerHour": 1.2},
        {"level": 10, "name": "Burgher Houses", "needs": ["food", "water", "goods"], "taxPerPersonPerHour": 3},
    ],
    "taxLevels": [
        {"id": "low", "name": "Low", "multiplier": 0.5, "approval": 3},
        {"id": "normal", "name": "Fair", "multiplier": 1.0, "approval": 0},
        {"id": "high", "name": "Heavy", "multiplier": 1.6, "approval": -8},
    ],
    "defaultTax": "normal",
}

BUILDINGS = [
    building("weavers_hut", "Weaver's Hut",
             "A loom and a tanning frame. 2 Weaving jobs turn Hides and a little Wood into Household Goods "
             "(cloth, baskets, pots) that the better homes need.",
             I, (1, 1), {"wood": 35, "stone": 10}, 40, "hide_working",
             jobSlots=[{"job": "weaving", "count": 2}]),
    building("market_stall", "Market Stall",
             "Awnings and baskets where goods change hands. Homes within 10 cells can get Household Goods.",
             I, (1, 1), {"wood": 40, "stone": 15}, 40, "permanent_settlement", market=True, community=2),
]

JOB = {"job": "weaving", "name": "Weaving",
       "description": "Weave cloth and baskets: 1 Hide and half a Wood make 2 Household Goods.",
       "output": "goods", "inputs": {"hides": 0.5, "wood": 0.25}, "ratePerWorkerPerSecond": 0.04, "unlimitedSlots": False}

SKILL = {"job": "weaving", "title": "Weaver", "perks": [
    {"level": 2, "name": "Even Weft", "description": "Cloth without holes.", "bonus": 0.1},
    {"level": 4, "name": "Dyer", "description": "Colours the town likes.", "bonus": 0.1,
     "effects": [{"kind": "approvalBonus", "value": 1}]},
    {"level": 6, "name": "Basket Maker", "description": "Nothing is wasted.", "bonus": 0.15},
    {"level": 8, "name": "Tanner's Friend", "description": "Uses every scrap of hide.", "bonus": 0.15},
    {"level": 10, "name": "Master Weaver", "description": "Cloth the traders ask for.", "bonus": 0.25,
     "effects": [{"kind": "jobRateMultiplier", "job": "weaving", "value": 1.05}]}]}


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    bal = c["balance"]
    bal["needs"] = NEEDS
    bal["treasuryCap"] = 5000
    upsert(bal["jobs"], JOB, key="job")
    upsert(c["skills"], SKILL, key="job")
    for b in BUILDINGS:
        b = dict(b)
        b["levels"] = levels(b)
        upsert(c["buildings"], b)
    for b in c["buildings"]:
        if b["id"] == "medieval_market":
            b["market"] = True
            b["description"] = "A tiled hall where the valley trades. Big community boost, extra storage, and homes within 10 cells can get Household Goods."
    for d in c["decorations"]:
        if d["id"] == "well":
            d["water"] = True
            d["description"] = "A stone-lined well. Homes within 6 cells have water (houses need it)."
    for t in c["technologies"]:
        if t["id"] == "hide_working":
            e = {"kind": "unlockBuilding", "buildingId": "weavers_hut"}
            if e not in t["effects"]:
                t["effects"].append(e)
        if t["id"] == "permanent_settlement":
            e = {"kind": "unlockBuilding", "buildingId": "market_stall"}
            if e not in t["effects"]:
                t["effects"].append(e)
    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"{len(c['buildings'])} buildings")


if __name__ == "__main__":
    main()
