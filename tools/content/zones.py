#!/usr/bin/env python3
"""Zones painted with the brush (idempotent): homes, woodcutting, forestry and pastures.

Villagers build a home in a homes zone by themselves when one bed or fewer is free; woodcutters fell trees
in woodcutting zones first; forestry zones are never cut and grow back twice as fast; every 4 cells of
pasture give the herders one more place to work.

Usage: python3 tools/content/zones.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT  # noqa: E402

ZONES = {
    "checkSeconds": 60,
    "freeBeds": 1,
    "pastureCellsPerHerder": 4,
    "forestryRegrowMultiplier": 2,
}


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    c["balance"]["zones"] = ZONES
    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print("zones set")


if __name__ == "__main__":
    main()
