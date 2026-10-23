#!/usr/bin/env python3
"""Trade with the neighbouring villages (idempotent).

The scouts meet neighbours as they explore. A Trading Post (and the medieval Market Hall) lets the town sell
and buy batches for coins, or keep lasting routes that carry a good every minute. Each neighbour pays well for
some goods and sells others; buying from one and selling to another never pays (the validator checks).

Usage: python3 tools/content/trade.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT  # noqa: E402
from present_eras import building, levels, upsert, I  # noqa: E402

# Guilds and the Crown's Market Rights raise what the neighbours pay; keep every buy price under every sell price even then.
GUILDS = 1.15
MARKET_RIGHTS = 1.1

TRADE = {
    "routeAmountPerMinute": 6,
    "dealAmount": 10,
    "neighbours": [
        {"id": "marsh_folk", "name": "Marsh Folk",
         "description": "Reed huts on stilts across the wetland. Rich in fish and hides, short of good timber and stone.",
         "landsExplored": 1, "routeCost": 30,
         "buys": {"wood": 0.6, "stone": 0.8, "tools": 9},
         "sells": {"food": 0.9, "hides": 3.0}},
        {"id": "hill_clan", "name": "Hill Clan",
         "description": "Herders and miners in the high valleys. They dig ore and quarry stone, and want bread and cloth.",
         "landsExplored": 3, "routeCost": 60,
         "buys": {"food": 0.6, "goods": 6, "planks": 2.2},
         "sells": {"stone": 1.2, "ore": 3.0}},
        {"id": "river_traders", "name": "River Traders",
         "description": "Boatmen who carry goods up and down the great river. They pay for bronze and fine cloth.",
         "landsExplored": 5, "routeCost": 120,
         "buys": {"bronze": 9, "goods": 6.5, "hides": 2.2},
         "sells": {"tools": 13, "planks": 3.0, "wood": 0.9}},
        {"id": "abbey_of_the_vale", "name": "Abbey of the Vale",
         "description": "Monks who copy books and keep bees. They trade their learning for food, cloth and timber.",
         "landsExplored": 8, "routeCost": 150,
         "buys": {"food": 0.65, "goods": 7, "planks": 2.3, "wood": 0.55},
         "sells": {"knowledge": 4, "bronze": 14}},
    ],
}

BUILDINGS = [
    building("trading_post", "Trading Post",
             "A fenced yard with a scale and a signpost to the neighbours. Sell and buy goods for coins, "
             "and keep 1 lasting trade route.",
             I, (1, 1), {"wood": 45, "stone": 15, "hides": 5}, 45, "trail_signs", tradeRoutes=1),
]


def check(trade):
    for res in {r for n in trade["neighbours"] for r in list(n["buys"]) + list(n["sells"])}:
        top = max((n["buys"].get(res, 0) for n in trade["neighbours"]), default=0) * GUILDS * MARKET_RIGHTS
        low = min((n["sells"][res] for n in trade["neighbours"] if res in n["sells"]), default=1e9)
        assert top < low, f"{res}: sells for {top:.2f} with guilds, buys for {low}"


def main():
    check(TRADE)
    with open(CONTENT) as f:
        c = json.load(f)
    c["balance"]["trade"] = TRADE
    for b in BUILDINGS:
        b = dict(b)
        b["levels"] = levels(b)
        upsert(c["buildings"], b)
    for b in c["buildings"]:
        if b["id"] == "medieval_market":
            b["tradeRoutes"] = 2
            if "trade routes" not in b["description"]:
                b["description"] += " Keeps 2 trade routes to the neighbours."
    for t in c["technologies"]:
        if t["id"] == "trail_signs":
            e = {"kind": "unlockBuilding", "buildingId": "trading_post"}
            if e not in t["effects"]:
                t["effects"].append(e)
            t["description"] = t["description"].replace("can build a Lookout Tower.", "can build a Lookout Tower and a Trading Post.")
        if t["id"] == "guilds":
            e = {"kind": "tradePriceMultiplier", "value": GUILDS}
            if e not in t["effects"]:
                t["effects"].append(e)
            if "Neighbours pay" not in t["description"]:
                t["description"] = t["description"].rstrip(".") + ". Neighbours pay 15% more for your goods."
    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"{len(c['buildings'])} buildings, {len(TRADE['neighbours'])} neighbours")


if __name__ == "__main__":
    main()
