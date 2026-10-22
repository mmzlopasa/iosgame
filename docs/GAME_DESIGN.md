# Founders — The Long Rise · Game design

This is the living design document. It describes the game as it is built today and the
direction it grows in. `docs/DESIGN_V2.md` (town-builder research and the chapter table) and
`docs/GAMEPLAY_SYSTEMS.md` (seasons, approval, fuel) stay as background; this file is the
summary that wins when they differ.

## Vision

A small band of founders settles a river valley and, generation after generation, turns a
campfire into a civilization. The same settlement, the same map and the same decisions carry
through every era: buildings keep their identity when they grow into their successors, and the
Legacy Timeline (the civilization's chronicle) records what the player built and chose.

- **Genre:** civilization and city building with survival pressure, an economy with production
  chains, technology, exploration and era progression.
- **Platform:** iPhone (and Android) in landscape, touch first, short sessions with offline
  progress. Built with Unity 6.3 LTS.
- **References (system ideas only, never assets, text or layouts):**
  - Whiteout Survival: survival pressure, the heart building that paces everything, upgrades
    that depend on each other, rewards for building and upgrading.
  - Foundation and Manor Lords: free layout, named villagers who walk to work, approval,
    seasons, production that comes from real workplaces.
  - Frostpunk: warmth, illness, discontent, laws and petitions.
- **Not wanted:** premium currencies, energy systems, loot boxes, pay-to-win.

## Core loop

1. Explore the valley (expeditions reveal regions; unexplored land cannot be built on).
2. Gather from the land: trees, rocks, ore veins, berry bushes, fish and game are real cells that
   run out and grow back.
3. Build shelter, food and storage; assign villagers to the places workplaces offer.
4. Produce, store and consume: food and firewood every second, materials for construction,
   and now goods made from other goods (recipes).
5. Grow: people arrive while there is room, a food reserve and enough hope (approval).
6. Research technologies at the hearth, the circle and the lodges; they unlock buildings,
   recipes and lasting effects.
7. Raise the heart (campfire → Village Hearth). Its level paces research and the eras.
8. Meet a milestone's real requirements to open the next stage or era.
9. Survive seasons, cold, illness, discontent, events and petitions along the way.

## Eras (data driven, `content.json → epochs`)

| # | Era (id) | State |
|---|---|---|
| 1 | First Communities (`epoch_first_communities`): camp → stable village | Playable |
| 2 | First Civilizations (`epoch_first_civilizations`): fields, herds, pottery, copper and bronze | Playable |
| 3 | Classical Worlds | Planned (no content) |
| 4 | Medieval Kingdoms (`epoch_medieval_kingdoms`): charter, market town, walled town | Playable, being polished |
| 5–8 | Exploration, Industrial, Modern, Near Future | Planned; the modern era is paused (decision 2026-10-14) |

An era opens through a **milestone**, never through currency alone. Milestones combine the heart
level, a story chapter (which needs game years), technologies, buildings, population, roads,
beauty and, since this change, goods actually produced (e.g. *The Bronze Dawn* needs 20 bronze
cast at the smelter).

## Inhabitants

Every villager is a `Person` with a stable id, name, gender, job, per-job experience (skills
level 0–10 with perks), and illness state. The population lives in homes whose comfort and
insulation decide warmth and approval. Consequences already in the simulation:

- No food → work slows (−40 %) and growth stops; nobody starves to death (decision D6).
- No room → no newcomers. Low approval (< 30 %) → no newcomers.
- Cold homes and hunger → illness; the sick keep their job but do not work; untreated serious
  illness can kill. Healers treat three each.
- High discontent → families leave.
- Skilled workers produce more.

Not yet: age and life stages, families, generational replacement of individual villagers.

## Resources

| Resource | Kind | Source | Used for |
|---|---|---|---|
| Food | natural | foraging, farming, fishing, hunting, herding | eaten every second, spoils without a granary |
| Wood | natural | woodcutting (trees run out, regrow) | building, firewood in autumn and winter, smelting |
| Stone | natural | stone gathering (rocks run out) | building |
| Ore | natural, finite per vein | mining | heart and late building levels, smelting |
| Hides | by-product | hunting, herding | heart and building levels |
| **Bronze** | **processed** | **smelting: 2 ore + 1 wood → 1 bronze** | **Bronze Workshop; the Bronze Dawn milestone** |
| Knowledge | abstract | research | technologies |

Each resource has an id, display name and storage cap (base + storage buildings, levels and tech
multipliers). Knowledge has its own cap.

## Production and recipes

Gathering jobs take from the land around their workplace. **Recipe jobs** turn goods into goods.
A recipe is data on the job (`balance.jobs[].inputs`), so a new chain is a content change:

```json
{ "job": "smelting", "output": "bronze", "inputs": { "ore": 2, "wood": 1 }, "ratePerWorkerPerSecond": 0.02 }
```

Rules (see `GameEconomy.ApplyRecipes`): workers come from the workplace's slots (the Copper
Smelter has 2 smelter places); a recipe only uses stock that is in storage at the start of the
step and not already needed for food, firewood or crafting; it stops when the output store is
full; nothing is made from nothing and no stock goes negative. The UI says why it stands:
*Stopped: no Ore*, *Stopped: the Bronze store is full*, *short of Wood*.

Planned chains (in order): bronze tools that raise gatherers' output; planks (sawpit) for
medieval houses; grain → flour → bread (mill, bakery) for the market town; iron ore → iron →
tools at the smithy.

## Buildings

33 buildings with categories (shelter, food, work, storage, community), footprints, rotation,
terrain rules, technology requirements, levels (each +25 % capacity, the heart caps every other
building's level), looks per level, comfort, insulation and heat. Free placement on a grid of
64×64 cells; roads, fences, walls, lanterns and plants are decorations. Buildings keep their
id through upgrades and era transforms.

## Environment

Four seasons (6 min each), day and night, weather and temperature with forecasts, warmth of
homes, firewood in the cold seasons, regrowing forests and wildlife.

## Technology

23 technologies with prerequisites, knowledge cost, research time, the heart level they need,
the era they belong to, unlocks and effects. Examples of real capability changes: Agriculture
unlocks fields; Copper Smelting unlocks the smelter and its recipe; Bronze Working unlocks the
workshop (built with bronze).

## Events, goals and the chronicle

Six events with choices (some need preparation), eight tutorial objectives, eight story chapters,
14 laws and 6 petitions. The Legacy Timeline records the founding, buildings, decisions,
discoveries and milestones as structured entries.

## Player profile and cloud

Founder profile, achievements and settings; cloud save on the owner's own server
(`server/cloud`). An admin dashboard and a speed-up system are being built in parallel.
