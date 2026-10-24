#!/usr/bin/env python3
"""Paths worn by people's feet (idempotent).

People walk from the heart of the town to the homes and workplaces; a cell walked enough becomes a trodden
path that is quicker to walk and speeds up building sites beside it. Unused paths grass over.

Usage: python3 tools/content/paths.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT  # noqa: E402

PATHS = {
    "wearPerPersonPerHour": 1.0,
    "threshold": 6,
    "maxWear": 30,
    "decayPerHour": 0.03,
    "constructionBonus": 0.15,
    "walkCost": 0.75,
}


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    c["balance"]["paths"] = PATHS
    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print("paths set")


if __name__ == "__main__":
    main()
