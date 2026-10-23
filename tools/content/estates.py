#!/usr/bin/env python3
"""The three estates of the realm (idempotent): the Commons (labour), the Clergy and the Crown (kingdom).

Each estate wins favour every hour from what the town does for it, or from gifts of coins; each rank of
favour grants a lasting boon. One estate left two ranks behind grumbles; all three together lift approval.

Usage: python3 tools/content/estates.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT  # noqa: E402

GATHER = ["foraging", "farming", "herding", "woodcutting", "stonecutting", "fishing", "hunting", "mining"]
CRAFT = ["crafting", "construction", "smelting", "sawing", "toolmaking", "baking", "weaving"]

ESTATES = {
    "grantCost": 40, "grantFavour": 20,
    "imbalanceRanks": 2, "imbalanceApproval": 6, "harmonyApproval": 3,
    "estates": [
        {"id": "commons", "name": "Commons", "icon": "people",
         "description": "The farmers, woodcutters and craftsmen. Their favour grows with every pair of busy hands.",
         "jobs": {**{j: 0.4 for j in GATHER}, **{j: 0.6 for j in CRAFT}},
         "ranks": [
             {"favour": 40, "name": "Willing Hands", "description": "Everyone works 5% faster.",
              "effects": [{"kind": "workRateMultiplier", "value": 1.05}]},
             {"favour": 150, "name": "Free Craftsmen", "description": "Building goes 15% faster.",
              "effects": [{"kind": "constructionSpeedMultiplier", "value": 1.15}]},
             {"favour": 400, "name": "Commons' Charter", "description": "Work 5% faster again, and +3 approval.",
              "effects": [{"kind": "workRateMultiplier", "value": 1.05}, {"kind": "approvalBonus", "value": 3}]},
         ]},
        {"id": "clergy", "name": "Clergy", "icon": "bell",
         "description": "Keepers of the stones, the church and the books. Their favour grows with every holy place and every scholar.",
         "buildings": {"ancestor_shrine": 4, "elders_lodge": 2, "healers_hut": 2, "medieval_church": 10,
                       "medieval_scriptorium": 5},
         "jobs": {"research": 0.3, "healing": 0.5},
         "ranks": [
             {"favour": 40, "name": "Blessings", "description": "10% fewer people fall ill.",
              "effects": [{"kind": "sicknessMultiplier", "value": 0.9}]},
             {"favour": 150, "name": "Learned Brothers", "description": "Research goes 15% faster.",
              "effects": [{"kind": "jobRateMultiplier", "job": "research", "value": 1.15}]},
             {"favour": 400, "name": "Feast Days", "description": "+4 approval and people grumble less.",
              "effects": [{"kind": "approvalBonus", "value": 4}, {"kind": "discontentPerHour", "value": -0.5}]},
         ]},
        {"id": "crown", "name": "Crown", "icon": "landmark",
         "description": "The lord's stewards and the king's men. Their favour grows with the taxes paid and the trade that runs.",
         "taxShare": 0.2, "routeFavour": 3,
         "buildings": {"lookout_tower": 3, "trading_post": 2, "medieval_market": 6, "medieval_warehouse": 3},
         "ranks": [
             {"favour": 40, "name": "Royal Charter", "description": "The treasury holds 50% more coins.",
              "effects": [{"kind": "storageMultiplier", "resource": "coins", "value": 1.5}]},
             {"favour": 150, "name": "King's Roads", "description": "Scouts explore 20% faster.",
              "effects": [{"kind": "expeditionSpeedMultiplier", "value": 1.2}]},
             {"favour": 400, "name": "Market Rights", "description": "Neighbours pay 10% more for your goods.",
              "effects": [{"kind": "tradePriceMultiplier", "value": 1.1}]},
         ]},
    ],
}


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    c["balance"]["estates"] = ESTATES
    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"{len(ESTATES['estates'])} estates")


if __name__ == "__main__":
    main()
