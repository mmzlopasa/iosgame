#!/usr/bin/env python3
"""Survival systems in content.json (Frostpunk-inspired): weather and cold snaps, the heart's
warmth and stoking it, home insulation, braziers, illness and healers, discontent, petitions,
the Book of Customs (laws) and survivors found by the scouts.

Idempotent: run after progression.py (which writes the heart, levels, skills and research).

    python3 tools/content/progression.py && python3 tools/content/survival.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from progression import CONTENT, DAY, building_levels  # noqa: E402

HOUR = 3600

CLIMATE = {
    "seasonTemperature": {"spring": 9, "summer": 21, "autumn": 7, "winter": -8},
    "dailySwing": 4,
    "daySeconds": DAY,
    "periodSeconds": 1800,
    "noise": 2.5,
    "forecastPeriods": 8,
    "coldSnaps": [
        {"id": "great_frost", "name": "Great Frost", "drop": 16, "chance": 0.10, "seasons": ["winter"]},
        {"id": "cold_snap", "name": "Cold Snap", "drop": 9, "chance": 0.25, "seasons": ["autumn", "winter"]},
    ],
}

WARMTH = {
    "comfortable": 5, "cold": 0, "freezing": -10,
    "heartHeat": 6, "heartHeatPerLevel": 0.3, "heartRadius": 3, "heartRadiusPerLevel": 0.25,
    "insulationPerLevel": 0.4, "maxHeat": 22,
    "stokeSeconds": HOUR, "stokeCooldownSeconds": 2 * HOUR, "stokeHeat": 6, "stokeRadius": 2,
    "stokeWoodPerSecond": 0.08, "stokeApproval": 3,
}

HEALTH = {
    "coldRate": 1 / (3 * HOUR), "freezingRate": 1 / HOUR, "hungerRate": 1 / (2 * HOUR),
    "recoverSeconds": 2400, "treatedRecoverSeconds": 600, "patientsPerHealer": 3,
    "deathSeconds": 1.5 * HOUR, "griefApproval": -6, "griefSeconds": DAY,
}

DISCONTENT = {
    "hunger": 20, "cold": 8, "freezing": 16, "sick": 10, "homeless": 10,
    "calm": 3, "calmAbove": 55, "calmPerApproval": 0.2, "death": 8,
    "grumbling": 40, "grumblingWork": 0.92, "unrest": 70, "unrestWork": 0.8,
    "leaveSeconds": 1800, "lawCooldownSeconds": 1800,
    "petitionAt": 55, "petitionCooldownSeconds": 3 * HOUR,
    "promiseRelief": 10, "keptRelief": 15, "brokenAnger": 25, "refusedAnger": 8,
    "keptApproval": 8, "keptSeconds": DAY,
}

# Homes keep this much warmer than outside (°C) at level 1.
INSULATION = {
    "shelter": 3, "woven_shelter": 6, "longhouse": 9, "daub_house": 11,
    "medieval_cottage": 13, "medieval_house": 15, "medieval_townhouse": 16,
}

HEALERS_HUT = {
    "id": "healers_hut", "name": "Healer's Hut",
    "description": "Herbs, broth and a warm bed. Each healer looks after 3 sick: they get well four times faster and never worse.",
    "epoch": "epoch_first_communities", "footprint": {"width": 1, "height": 1},
    "cost": {"wood": 30, "stone": 10}, "buildSeconds": 60, "sprite": "healers_hut",
    "allowedTerrain": ["grass", "sand"], "buildable": True, "requiresTech": "herbal_healing",
    "jobSlots": [{"job": "healing", "count": 2}], "insulation": 8, "comfort": 2,
}

HERBAL_HEALING = {
    "id": "herbal_healing", "name": "Herbal Healing",
    "description": "Willow bark, yarrow and broth. Unlocks the Healer's Hut, where the sick get well.",
    "cost": {"knowledge": 20}, "researchSeconds": 300.0, "prerequisites": [], "heartLevel": 3,
    "effects": [{"kind": "unlockBuilding", "buildingId": "healers_hut"}],
}

BRAZIER = {
    "id": "fire_brazier", "name": "Brazier", "category": "Lights",
    "description": "A stone bowl of embers: warms the homes around it (+6° within 3 cells) and burns a little firewood when it is cold.",
    "cost": {"wood": 6, "stone": 4}, "beauty": 1,
    "heat": {"heat": 6, "radius": 3, "woodPerSecond": 0.004},
}

HEALING_JOB = {
    "job": "healing", "name": "Healing",
    "description": "Healers look after the sick: each cares for three, who get well quickly and never worse.",
    "ratePerWorkerPerSecond": 0, "unlimitedSlots": False,
}


def e(kind, value, **kw):
    return {"kind": kind, "value": value, **kw}


# The Book of Customs. Two branches as in Frostpunk: what keeps people alive and what keeps them together.
LAWS = [
    # Survival
    {"id": "shared_stew", "name": "Shared Stew", "branch": "Survival", "heartLevel": 2, "excludes": ["hearty_meals"],
     "description": "One pot for everyone, thinned with water and roots. People eat 15% less, but nobody likes it.",
     "effects": [e("consumptionMultiplier", 0.85), e("approvalBonus", -3)], "onSign": 3,
     "legacyText": "The pot was shared and thin; nobody went without, and nobody had enough."},
    {"id": "hearty_meals", "name": "Hearty Meals", "branch": "Survival", "heartLevel": 2, "excludes": ["shared_stew"],
     "description": "Thick broth and meat for all. People eat 15% more, fall ill less (−20%) and are happier.",
     "effects": [e("consumptionMultiplier", 1.15), e("sicknessMultiplier", 0.8), e("approvalBonus", 2)],
     "legacyText": "Full bellies were the custom: strength came first."},
    {"id": "long_days", "name": "Long Days", "branch": "Survival", "heartLevel": 3,
     "description": "Work from first light until dark. Everyone works 15% more, but falls ill more (+25%) and grumbles.",
     "effects": [e("workRateMultiplier", 1.15), e("sicknessMultiplier", 1.25), e("discontentPerHour", 2)], "onSign": 4,
     "legacyText": "The days grew long; the village grew, and so did its weariness."},
    {"id": "dawn_to_dusk", "name": "Dawn to Dusk", "branch": "Survival", "heartLevel": 9, "requiresLaw": "long_days",
     "description": "Not a moment idle. Another +15% work, but discontent rises quickly and hope fades.",
     "effects": [e("workRateMultiplier", 1.15), e("discontentPerHour", 4), e("approvalBonus", -3)], "onSign": 8,
     "legacyText": "From dawn to dusk the village laboured. It was remembered as a hard age."},
    {"id": "fire_wardens", "name": "Fire Wardens", "branch": "Survival", "heartLevel": 4,
     "description": "Wardens tend the heart through the night: it gives 3° more warmth for 10% more firewood.",
     "effects": [e("heatBonus", 3), e("fuelMultiplier", 1.1)],
     "legacyText": "Someone always watched the fire. It never went out again."},
    {"id": "bitter_remedies", "name": "Bitter Remedies", "branch": "Survival", "heartLevel": 5, "excludes": ["watch_the_sick"],
     "description": "Harsh cures that work: the sick get well 50% faster, but people resent the treatment.",
     "effects": [e("recoveryMultiplier", 1.5), e("approvalBonus", -2)], "onSign": 2,
     "legacyText": "The remedies were bitter and the sick hated them. Most of them lived."},
    {"id": "watch_the_sick", "name": "Watch Over the Sick", "branch": "Survival", "heartLevel": 5, "excludes": ["bitter_remedies"],
     "description": "Nobody is left alone with a fever: grave illness kills far more slowly (−60%), though recovery is a little slower.",
     "effects": [e("deathMultiplier", 0.4), e("recoveryMultiplier", 0.85), e("approvalBonus", 1)],
     "legacyText": "Someone sat beside every sickbed. Fewer died alone."},
    {"id": "apprentices", "name": "Apprentices", "branch": "Survival", "heartLevel": 7,
     "description": "The young learn beside the masters: everyone gains skill 50% faster, at a little cost to work.",
     "effects": [e("experienceMultiplier", 1.5), e("workRateMultiplier", 0.97)],
     "legacyText": "Every master had an apprentice, and every apprentice became a master."},
    # Community
    {"id": "day_of_rest", "name": "Day of Rest", "branch": "Community", "heartLevel": 2,
     "description": "One day in seven nobody works. Work is 7% slower, but people are happier and calmer.",
     "effects": [e("workRateMultiplier", 0.93), e("approvalBonus", 5), e("discontentPerHour", -2)], "onSign": -5,
     "legacyText": "A day of rest was kept; songs were sung on it."},
    {"id": "burial_rites", "name": "Burial Rites", "branch": "Community", "heartLevel": 3,
     "description": "The dead are honoured and remembered: each death brings half the grief and anger.",
     "effects": [e("griefMultiplier", 0.5), e("approvalBonus", 1)],
     "legacyText": "The village learnt to bury its dead with song. Grief was shared, and so it was lighter."},
    {"id": "story_nights", "name": "Story Nights", "branch": "Community", "heartLevel": 6, "requiresBuilding": "community_circle",
     "description": "Stories by the fire after dark: people are happier and the elders learn 10% faster.",
     "effects": [e("approvalBonus", 3), e("jobRateMultiplier", 1.1, job="research"), e("discontentPerHour", -1)], "onSign": -3,
     "legacyText": "Stories were told by the fire each night. Some are still told."},
    {"id": "harvest_festival", "name": "Harvest Festival", "branch": "Community", "heartLevel": 8,
     "description": "A feast each autumn: people eat 5% more but are much happier and calmer.",
     "effects": [e("consumptionMultiplier", 1.05), e("approvalBonus", 4), e("discontentPerHour", -2)], "onSign": -6,
     "legacyText": "The harvest festival was held for the first time; it never stopped."},
    {"id": "elders_council", "name": "Elders' Council", "branch": "Community", "heartLevel": 12,
     "requiresBuilding": "elders_lodge", "excludes": ["watchmen"],
     "description": "The elders settle quarrels: discontent settles much faster and people trust the village more.",
     "effects": [e("discontentPerHour", -4), e("approvalBonus", 3)], "onSign": -8,
     "legacyText": "The elders sat in council. The path of wisdom was chosen."},
    {"id": "watchmen", "name": "Watchmen", "branch": "Community", "heartLevel": 12, "excludes": ["elders_council"],
     "description": "Armed watchmen keep the peace: discontent is crushed quickly, but hope suffers.",
     "effects": [e("discontentPerHour", -7), e("approvalBonus", -4)], "onSign": -12,
     "legacyText": "Watchmen walked the lanes at night. The path of order was chosen."},
]

PETITIONS = [
    {"id": "warm_homes", "title": "Warm Homes",
     "text": "\"Our children shiver at night. Make every home warm before the cold takes one of us.\"",
     "promise": "Nobody will sleep in the cold.", "goal": [{"kind": "coldPeopleAtMost", "amount": 0}], "deadlineSeconds": 2 * HOUR},
    {"id": "healer", "title": "Someone to Tend the Sick",
     "text": "\"The sick lie alone with their fever. We need a healer.\"",
     "promise": "A healer's hut will stand.", "goal": [{"kind": "buildingCount", "id": "healers_hut", "amount": 1}], "deadlineSeconds": 3 * HOUR},
    {"id": "full_stores", "title": "Bread on Every Table",
     "text": "\"Our stores are thin and winter is coming. Fill them.\"",
     "promise": "The stores will hold 150 food.", "goal": [{"kind": "resourceAtLeast", "resource": "food", "amount": 150}], "deadlineSeconds": 2 * HOUR},
    {"id": "heal_the_sick", "title": "Heal the Sick",
     "text": "\"Too many lie ill. Heal them before the illness spreads.\"",
     "promise": "Everyone will be well again.", "goal": [{"kind": "sickAtMost", "amount": 0}], "deadlineSeconds": 2 * HOUR},
    {"id": "gathering_place", "title": "A Place to Gather",
     "text": "\"We work and sleep, and that is all. Give us a place to sit together.\"",
     "promise": "A community circle will be built.", "goal": [{"kind": "buildingCount", "id": "community_circle", "amount": 1}], "deadlineSeconds": 2 * HOUR},
    {"id": "rest_day", "title": "A Day of Rest",
     "text": "\"We are worn out. Give us one day to rest.\"",
     "promise": "A day of rest will be the custom.", "goal": [{"kind": "lawEnacted", "id": "day_of_rest"}], "deadlineSeconds": HOUR},
]

# What the scouts bring back and how long they are away: (minutes, survivors, loot, finding).
# Frostpunk's expeditions take time; the far lands take hours.
REGIONS = {
    "north_forest": (10, 0, {"wood": 40}, "In the northern forest the scouts found a cache of dry firewood."),
    "west_woods": (10, 1, {}, "A hunter lost in the western woods followed the scouts home."),
    "east_ridge": (12, 0, {"stone": 30}, "The scouts carried back fine flint from the ridge."),
    "south_bank": (10, 0, {"food": 30}, "On the south bank the scouts found a fisher's abandoned catch."),
    "eastern_highlands": (25, 0, {"ore": 20, "hides": 10}, "The highlands hide copper-green rocks and old hides in a cave."),
    "southern_marsh": (25, 2, {}, "Two people hiding in the reeds of the marsh joined the village."),
    "great_meadow": (45, 2, {"food": 40}, "Herders on the great meadow chose to settle with us."),
    "iron_mountains": (90, 0, {"ore": 50}, "Red stone that rings when struck: iron, in the mountains."),
    "lake_country": (60, 2, {"food": 60}, "Lake folk came back with the scouts, and baskets of dried fish."),
    "deep_forest": (75, 2, {"hides": 20}, "Two trappers from the deep forest joined the village."),
    "southern_peaks": (120, 3, {"stone": 60}, "A family from the peaks came down to live with us."),
}


def main():
    with open(CONTENT) as f:
        c = json.load(f)
    b = c["balance"]
    b["climate"] = CLIMATE
    b["warmth"] = WARMTH
    b["health"] = HEALTH
    b["discontent"] = DISCONTENT

    if not any(j["job"] == "healing" for j in b["jobs"]):
        b["jobs"].append(HEALING_JOB)

    blds = {x["id"]: x for x in c["buildings"]}
    for bid, ins in INSULATION.items():
        if bid in blds:
            blds[bid]["insulation"] = ins
    hut = dict(HEALERS_HUT)
    hut["levels"] = building_levels(hut)
    if "healers_hut" in blds:
        c["buildings"][c["buildings"].index(blds["healers_hut"])] = hut
    else:
        c["buildings"].insert(c["buildings"].index(blds["storage_pit"]) + 1, hut)

    techs = {t["id"]: t for t in c["technologies"]}
    if "herbal_healing" in techs:
        c["technologies"][c["technologies"].index(techs["herbal_healing"])] = HERBAL_HEALING
    else:
        c["technologies"].insert(2, HERBAL_HEALING)

    decos = {d["id"]: d for d in c["decorations"]}
    if "fire_brazier" in decos:
        c["decorations"][c["decorations"].index(decos["fire_brazier"])] = BRAZIER
    else:
        after = decos.get("torch_post")
        c["decorations"].insert(c["decorations"].index(after) + 1 if after else len(c["decorations"]), BRAZIER)

    for r in c["regions"]:
        if r["id"] in REGIONS:
            minutes, survivors, loot, finding = REGIONS[r["id"]]
            r["expeditionSeconds"] = float(minutes * 60)
            r.pop("survivors", None)
            r.pop("loot", None)
            if survivors:
                r["survivors"] = survivors
            if loot:
                r["loot"] = loot
            r["finding"] = finding

    c["laws"] = LAWS
    c["petitions"] = PETITIONS

    with open(CONTENT, "w") as f:
        json.dump(c, f, indent=1, ensure_ascii=False)
        f.write("\n")


if __name__ == "__main__":
    main()
