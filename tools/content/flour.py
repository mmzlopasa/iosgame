#!/usr/bin/env python3
"""The bread chain (idempotent): wheat from the fields, flour from the mill, bread from the bakery.

Farms grow grain (Food); the Watermill's millers grind Food into Flour; the Bakery bakes Flour and a little
firewood into far more Food. The goods are carried between the buildings by the workers.

Usage: python3 tools/content/flour.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT  # noqa: E402
from present_eras import JOBS, BUILDINGS, upsert  # noqa: E402

MILLING = {"job": "milling", "name": "Milling",
           "description": "Grind grain between the millstones: 1 Food makes 1 Flour for the bakers.",
           "output": "flour", "inputs": {"food": 1}, "ratePerWorkerPerSecond": 0.12, "unlimitedSlots": False}

MILLER = {"job": "milling", "title": "Miller", "perks": [
    {"level": 2, "name": "Dressed Stones", "description": "Sharp grooves, fine flour.", "bonus": 0.1},
    {"level": 4, "name": "Fair Measure", "description": "No one says the miller cheats.", "bonus": 0.1,
     "effects": [{"kind": "approvalBonus", "value": 1}]},
    {"level": 6, "name": "Sluice Keeper", "description": "Water at the right speed.", "bonus": 0.15},
    {"level": 8, "name": "Bolting Cloth", "description": "White flour for the feast days.", "bonus": 0.15},
    {"level": 10, "name": "Master Miller", "description": "The wheel never stops.", "bonus": 0.25,
     "effects": [{"kind": "jobRateMultiplier", "job": "milling", "value": 1.05}]}]}


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    bal = c["balance"]
    upsert(bal["jobs"], MILLING, key="job")
    upsert(bal["jobs"], next(j for j in JOBS if j["job"] == "baking"), key="job")
    upsert(c["skills"], MILLER, key="job")
    bakery = next(b for b in BUILDINGS if b["id"] == "medieval_bakery")
    for b in c["buildings"]:
        if b["id"] == "medieval_bakery":
            b["description"] = bakery["description"]
        if b["id"] == "medieval_mill":
            slots = [s for s in b["jobSlots"] if s["job"] != "milling"] + [{"job": "milling", "count": 2}]
            b["jobSlots"] = slots
            b["description"] = "A mill on the river. Must touch water. +3 Farming jobs and 2 Milling jobs that grind grain into Flour."
    for o in c["objectives"]:
        if o["id"] == "obj_bread":
            o["hint"] = "Research Bread Ovens, build a Watermill and a Bakery: millers grind grain into flour, bakers bake it into bread."
    for t in c["technologies"]:
        if t["id"] == "watermill" and "Flour" not in t["description"]:
            t["description"] = "The river turns the millstone. Unlocks the Watermill, whose millers grind grain into Flour. +20% farming."
    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print("bread chain set")


if __name__ == "__main__":
    main()
