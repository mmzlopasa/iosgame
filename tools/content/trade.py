#!/usr/bin/env python3
"""Markets and trade routes, after Foundation (idempotent).

A Market serves the homes around it (happier families, and household goods for the higher house tiers). The
Trading Post sends caravans along trade routes to four peoples beyond the valley: each trip takes goods the town
has plenty of and brings home what it lacks (some deals bring coins). One more route every 4 levels; the medieval
Market Hall runs two more. Guilds and the Crown's Market Rights make caravans bring home more.

This replaces the earlier coin trade with the neighbours (balance.trade), which is removed.

Usage: python3 tools/content/trade.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT, building_levels  # noqa: E402
from present_eras import upsert  # noqa: E402

GUILDS = 1.15

MARKET = {'id': 'market',
 'name': 'Market',
 'description': 'Open stalls where families swap food, pots and hides. Homes within 8 cells are happier (up to +8 '
                'approval), and people gather here.',
 'epoch': 'epoch_first_communities',
 'footprint': {'width': 3, 'height': 2},
 'cost': {'wood': 45, 'stone': 25},
 'buildSeconds': 70,
 'sprite': 'market',
 'allowedTerrain': ['grass', 'sand'],
 'buildable': True,
 'requiresTech': 'barter',
 'community': 3,
 'marketReach': 8,
 'maxCount': 3}

TRADING_POST = {'id': 'trading_post',
 'name': 'Trading Post',
 'description': 'A warehouse on the road out of the valley. Caravans leave from here along trade routes: send what '
                'you have plenty of, bring home what you lack. One more route at levels 4, 8, 12 and 16.',
 'epoch': 'epoch_first_civilizations',
 'footprint': {'width': 2, 'height': 2},
 'cost': {'wood': 80, 'stone': 50},
 'buildSeconds': 90,
 'sprite': 'trading_post',
 'allowedTerrain': ['grass', 'sand'],
 'buildable': True,
 'requiresTech': 'trade_routes',
 'storage': 100,
 'tradeRoutes': 1,
 'maxCount': 1}

BARTER = {'id': 'barter',
 'name': 'Barter',
 'description': 'Neighbours swap what they have too much of. Unlocks the Market: homes near a market are happier.',
 'cost': {'knowledge': 80},
 'researchSeconds': 900.0,
 'prerequisites': ['permanent_settlement'],
 'heartLevel': 9,
 'effects': [{'kind': 'unlockBuilding', 'buildingId': 'market'}]}

TRADE_ROUTES = {'id': 'trade_routes',
 'name': 'Trade Routes',
 'description': 'Pack animals and carts carry goods over the hills. Unlocks the Trading Post and trade routes to the '
                'peoples beyond the valley.',
 'cost': {'knowledge': 150},
 'researchSeconds': 1500.0,
 'prerequisites': ['barter', 'pottery'],
 'heartLevel': 15,
 'effects': [{'kind': 'unlockBuilding', 'buildingId': 'trading_post'}]}

PARTNERS = [{'id': 'reed_folk',
  'name': 'Reed Folk',
  'direction': 'east',
  'description': 'Fishers and basket weavers on the lakes downriver. They always have dried fish to spare.',
  'tripSeconds': 180,
  'openCost': {'food': 30, 'wood': 20},
  'deals': [{'give': {'wood': 20}, 'get': {'food': 30}},
            {'give': {'stone': 15}, 'get': {'food': 25}},
            {'give': {'hides': 6}, 'get': {'food': 35}},
            {'give': {'planks': 15}, 'get': {'coins': 25}}],
  'legacyText': 'The first caravan went downriver. The Reed Folk sent it home heavy with dried fish.'},
 {'id': 'hill_clan',
  'name': 'Hill Clan',
  'direction': 'north',
  'description': 'Herders on the high pastures. They trade hides, quarried stone and a little ore for grain.',
  'tripSeconds': 240,
  'openCost': {'food': 40, 'wood': 30},
  'deals': [{'give': {'food': 30}, 'get': {'hides': 10}},
            {'give': {'wood': 25}, 'get': {'stone': 30}},
            {'give': {'food': 25}, 'get': {'ore': 12}},
            {'give': {'goods': 6}, 'get': {'coins': 40}}],
  'legacyText': 'The Hill Clan shared salt and bread with our traders: a road over the ridge is open.'},
 {'id': 'pine_valley',
  'name': 'Pine Valley',
  'direction': 'west',
  'description': 'Woodcutters in the deep pine forests. Timber is cheap there, stone is not.',
  'tripSeconds': 200,
  'openCost': {'food': 30, 'stone': 20},
  'deals': [{'give': {'food': 25}, 'get': {'wood': 35}},
            {'give': {'stone': 20}, 'get': {'wood': 30}},
            {'give': {'hides': 8}, 'get': {'wood': 45}}],
  'legacyText': 'Rafts of pine logs now float down from Pine Valley.'},
 {'id': 'copper_merchants',
  'name': 'Copper Merchants',
  'direction': 'south',
  'description': 'Travelling smiths from the southern mountains. They bring ore and finished bronze for hides and '
                 'food.',
  'tripSeconds': 300,
  'openCost': {'food': 60, 'wood': 60, 'stone': 40},
  'requiresTech': 'copper_smelting',
  'deals': [{'give': {'hides': 15}, 'get': {'ore': 25}},
            {'give': {'food': 50}, 'get': {'bronze': 4}},
            {'give': {'wood': 60}, 'get': {'bronze': 5}},
            {'give': {'bronze': 5}, 'get': {'coins': 60}}],
  'legacyText': 'Bronze from the south: the Copper Merchants now come every season.'}]


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    c["balance"].pop("trade", None)
    c["tradePartners"] = PARTNERS
    for b, after in ((MARKET, "longhouse"), (TRADING_POST, "bronze_workshop")):
        b = dict(b)
        b["market"] = b["id"] == "market"  # the market also brings household goods to the homes around it
        if not b["market"]:
            b.pop("market")
        b["levels"] = building_levels(b)
        upsert(c["buildings"], b, after=after)
    upsert(c["technologies"], BARTER, after="oral_tradition")
    upsert(c["technologies"], TRADE_ROUTES, after="pottery")
    for b in c["buildings"]:
        if b["id"] == "medieval_market":
            b["tradeRoutes"] = 2
            b["description"] = b["description"].replace("Keeps 2 trade routes to the neighbours.",
                                                        "Runs 2 more trade routes for the caravans.")
    for t in c["technologies"]:
        if t["id"] == "trail_signs":
            t["effects"] = [e for e in t["effects"] if e != {"kind": "unlockBuilding", "buildingId": "trading_post"}]
            t["description"] = t["description"].replace("a Lookout Tower and a Trading Post.", "a Lookout Tower.")
        if t["id"] == "guilds":
            t["description"] = t["description"].replace("Neighbours pay 15% more for your goods.",
                                                        "Caravans bring home 15% more.")
    for e in c["balance"].get("estates", {}).get("estates", []):
        for r in e["ranks"]:
            r["description"] = r["description"].replace("Neighbours pay 10% more for your goods.",
                                                        "Caravans bring home 10% more.")
    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"{len(c['buildings'])} buildings, {len(PARTNERS)} trade partners")


if __name__ == "__main__":
    main()
