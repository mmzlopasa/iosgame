# Founders — gameplay systems (inspired by Manor Lords and Foundation)

Founders keeps its own project: a prehistoric camp that grows through the eras, with a
Legacy Timeline. From the two PC city builders we take the systems that make building and
people feel alive, adapted for a phone (short sessions, offline progress, touch).

## What the PC games do

**Manor Lords**
- **Approval** decides whether families move in and how the town behaves. It comes from
  the variety of goods at the market, the church, fuel, and having homes.
- **Families** are the unit of labour: one family per workplace slot. Unassigned
  families build and haul.
- **Seasons** set the rhythm. Spring sowing, summer growth, autumn harvest, winter
  hardship. In winter, fuel use doubles and a lack of firewood means freezing.
- Every family eats and every home burns fuel. The supplies panel shows how many
  months the stores will last.

**Foundation**
- Organic, gridless growth through **zones**, where the village builds itself.
- **Needs and happiness**: food variety, housing level (stone houses for higher
  ranks), religion and safety.
- **Estates** (Labour, Clergy, Kingdom) unlock exclusive buildings. **Splendor**
  measures how beautiful and grand the village is.
- Villagers visibly walk to their work, and paths form where people walk.

Sources: [Manor Lords wiki](https://wiki.hoodedhorse.com/Manor_Lords/Beginner%27s_Guide),
[family management](https://gamerguides.com/manor-lords/guide/basics/overview/getting-families-and-family-management),
[Foundation zones](https://wiki.polymorph.games/foundation/Zones), [Polymorph press kit](https://www.polymorph.games/presskit/).

## What Founders has now

| System | In Founders | Where |
|---|---|---|
| Seasons | 4 seasons of 6 min (24 min per year). Foraging, farming and growth change with the season. Farms give most in autumn and nothing in winter. | `Core/GameWellbeing.cs`, `balance.seasons` |
| Fuel | Autumn and winter burn firewood per person. An empty woodpile means freezing: a big approval loss and a warning. | `FuelPerSecond`, `Freezing` |
| Approval (0–100) | Base 40, plus food security, food variety (wild, fish, grain, meat), warmth, housing comfort, gathering places and event effects. | `ApprovalFactors()` |
| Approval effects | Work speed 0.85–1.15. No newcomers below 30 %, growth ×1.5 at 100 %. | `ApprovalProductivity/Growth` |
| Housing levels | Hide tent (comfort 0) → woven roundhouse (5) → daub house (10); longhouse (8). | `comfort` in content |
| Gathering places | Hearth, community circle, elders' lodge, bronze workshop. | `community` in content |
| People at work | Each villager has the job of their slot. Farmers work the fields, woodcutters go into the trees, stone gatherers to the rocks, builders to construction sites. The idle and the night crowd sit at the hearth. | `WorldView.TaskFor` |
| Day and night | 150 s day with a short night: warm dawn and dusk, glowing hearths. | `Atmosphere.cs` |
| Seasonal look | Golden grass and leaves in autumn, pale in winter, snowfall. | `Atmosphere.cs` |
| Eras | Milestones can open a new epoch. "The Bronze Dawn" opens First Civilizations: farming, herding, pottery, copper and bronze. | `newEpoch` |
| Sound | Hearth, river, birds and wind mixed by what the camera sees; calm music. | `AudioDirector.cs` |

## Speed-ups (earned, never bought)

Like the speed-ups in Whiteout Survival, but only ever earned in play. Rules: `unity/Assets/Scripts/Core/GameSpeedups.cs`; window: `UI/GameUi.Speedups.cs`; tests: `unity/CoreTests/SpeedupTests.cs`.

- **Kinds:** Building (construction, upgrades, levels), Research, Scouting (expeditions) and General (any of them). **Sizes:** 1 min, 5 min, 15 min, 1 h, 3 h.
- **Earned:** starter pack (3× 5 min General, 2× 5 min Building, 1× 5 min Research; older saves get it once on load), every finished building (2× 1 min Building), level 2 / 3 / 4+ (15 min Building / 2× 15 min Building + 5 min General / 1 h Building + 15 min General), hut → house improvements (15 min Building + 5 min General), every discovery (15 min Research + 2× 1 min General), every explored region (15 min Scouting + 5 min General), every goal (5 min General), every milestone (1 h of each kind), every chapter (2× 15 min General + 3 h Building).
- **Used:** the clock button on a timer (HUD), on a building under construction, on the running research and on the scouts opens the speed-up window: use one at a time, or *Finish now* (picks the job's own kind first, large ones while they fit, then the smallest that covers the rest). A building minute is a minute at today's building pace (never less than the base pace). A job a speed-up covers finishes at once, with its normal rewards.
- **Saved** as `speedups` (key `kind_minutes` → count) in the settlement state; a tampered key or negative count is rejected on load. Profile › Stats lists everything owned.

## The second wave of the playable eras (October 2026)

Content for the three eras that are already playable (the modern era stays paused). Data in
`content.json`, written by `tools/content/present_eras.py`; rules tested in `unity/CoreTests/PresentEraTests.cs`.

| Era | Building | Research | What it does |
|---|---|---|---|
| I | Lookout Tower | Trail Signs (heart 6) | scouts explore 25 % faster (the research itself +10 %) |
| I | Herb Garden | Herbal Healing | −10 % illness, +10 % recovery (two gardens stack) |
| I | Smokehouse | Hide Working | protects 60 food, −10 % spoilage, "smoked" food variety |
| I | Ancestor Stones | Ancestor Rites (heart 9) | gathering place +4, −1 discontent per hour |
| II | Sawpit | Carpentry (heart 13) | 2 sawyers: 2 wood → 1 plank |
| II | Apiary | Beekeeping (heart 14) | 2 foraging jobs, "honey" food variety |
| II | Toolsmith | Bronze Tools (heart 19) | 2 toolsmiths: 1 bronze + 1 plank → 2 tools |
| IV | Bakery | Bread Ovens | 3 bakers: each wood burnt gives 4 food, "bread" food variety |
| IV | Carpenter's Yard | Stone Masonry | 3 sawyers |
| IV | Tavern | Burgher Rights | gathering place +8, −2 discontent per hour |
| IV | Scriptorium | Monastic Orders | 3 research jobs |
| IV | Warehouse | Craft Guilds | +400 storage |
| IV | Bathhouse | Stone Masonry | −15 % illness, +20 % recovery, gathering place +3 |

- **Tools:** while the store holds any, every gatherer (foragers, farmers, fishers, hunters, herders,
  woodcutters, quarriers, miners) works 15 % faster; each working gatherer wears out 0.001 tools per
  second (`balance.toolBonus`, `toolWearPerWorkerPerSecond`).
- **Building effects:** a building may carry `effects` (same kinds as research) that last while it
  stands finished; each building of the kind adds them once.
- **Goals after the village:** smokehouse, Ancestor Stones, 20 planks, 10 tools, a bakery, a tavern.
- **Events:** the Master Carpenter (first sawpit), Frost-Cracked Tools (10 tools made), Minstrels at
  the Tavern (first tavern).

## Map generator (Foundation's "Select map")

A new game first opens **Choose Your Valley**: ready-made valleys on the left (Long Valley is the
hand-drawn one; Riverlands, Lakeland, Highlands, Mountain Pass, Wildwood, Open Plains), the valley
from above in the middle with the suggested campfire, and seven sliders (lakes, rivers, islands,
mountains, hills, forest, ore, 0–5 each) with a dice button for another valley of the same kind.
Every valley has a 13-character code (one digit per slider + the seed) to copy, paste or type, so
players can share valleys. Confirm builds the terrain, water and nature (Ultimate Nature) from the
generated map, then the player picks where the campfire and shelters go; the globe button on the
campfire card goes back to the generator until the fire is lit.

Rules (`Core/MapGenerator.cs`, tested in `CoreTests/MapGeneratorTests.cs`): deterministic for a
code; mountains only along edges (plus an inland massif at 4–5), ore at their feet or in the hills;
every valley keeps water (a pond at least), wood, stone and ore, and a valid start. The save keeps
only the map's name (`gen_<code>`): the valley and its 64 lands are drawn again on load.

## Needs, house tiers and taxes (Manor Lords / Foundation)

- Homes climb three tiers with their level: **Huts** (Lv.1, need food), **Houses** (Lv.5, also water), **Burgher Houses** (Lv.10, also household goods). Raising a home into a tier needs that tier's water (a well within 6 cells or the river within 3) or market (stall or hall within 10 cells) nearby, so towns grow around wells and markets.
- Every home pays tax in **Coins** (new treasury, cap 5000): 2 / 5 / 12 per person per hour by tier; half when a need is missing. Needs met lift approval (up to +6), needs missing lower it (down to −8).
- Tax level in People › Wellbeing › Homes and taxes: Low (×0.5, +3 approval), Fair, Heavy (×1.6, −8 approval).
- **Household Goods** are woven at the new **Weaver's Hut** (hide working) from hides and a little wood; burghers use them up only when a market is in reach. New **Market Stall** (permanent settlement) brings markets into the first era.
- Content: `tools/content/living_town.py`; rules: `Core/GameNeeds.cs` (report cached, the UI asks every frame); tests: `CoreTests/NeedsTests.cs`.

## Trade with the neighbours (Manor Lords' trade routes)

- The scouts meet four neighbouring villages as lands are explored (1, 3, 5, 8 lands): Marsh Folk, Hill Clan, River Traders, Abbey of the Vale. Each pays well for some goods and sells others; buying from one and selling to another never pays (checked by the validator and `TradeTests`).
- A **Trading Post** (Trail Signs) keeps 1 route; the medieval **Market Hall** keeps 2. Deals sell or buy 10 at once; a lasting route (costs coins to open) carries 6 goods a minute while there is stock, coins and room.
- **Guilds** make neighbours pay 15% more (`tradePriceMultiplier`). Taxes were raised to 2 / 5 / 12 coins per person per hour so coins matter.
- Map window › Trade tab; the trading post's card has a Trade button. Content: `tools/content/trade.py`; rules: `Core/GameTrade.cs`.

## The three estates of the realm (Manor Lords' development paths)

- **Commons** (labour: every worker), **Clergy** (shrines, church, scriptorium, scholars, healers) and **Crown** (taxes, trade routes, market, lookout, warehouse) win favour every hour; a gift of 40 coins brings 20 favour.
- Three ranks each (40 / 150 / 400 favour) with lasting boons: work and building speed; less illness, faster research, feast days; a bigger treasury, faster scouts, Market Rights (+10% trade prices).
- One estate two ranks behind the others feels forgotten (−6 approval); all three at rank 1+ within a rank of each other stand together (+3).
- People window › Estates tab. Content: `tools/content/estates.py`; rules: `Core/GameEstates.cs`; tests: `EstatesTests`.

## Organic paths (Manor Lords' grid-less towns)

- People walk from the heart of the town to every home (its family) and workplace (its workers); their feet wear the cells on the way (1 wear per person per hour). From 6 wear a cell is a **trodden path**, drawn as a winding dirt lane.
- Paths are quicker to walk (villagers prefer them) and **building sites beside a path go up 15% faster**, so the town grows along the ways people already take. Unused paths grass over (3% of the wear an hour).
- Wear is applied once a game minute, so long absences stay cheap. Content: `tools/content/paths.py`; rules: `Core/GamePaths.cs`; drawing: `WorldView.RepaintGround`; walking: `Pathing`.

## Zones painted with a brush (Manor Lords' plots and work areas)

- Build drawer › **Zones** tab: brushes for **Homes**, **Woodcutting**, **Forestry** and **Pasture**, and an eraser. Drag to paint (the hand button moves the camera instead). Painted cells show as coloured tiles while building.
- **Homes**: when one bed or fewer is free, the villagers start the roomiest home the town can afford in the zone by themselves (paid from the stores, one at a time), nearest the heart and along trodden paths first.
- **Woodcutting**: woodcutters near the area fell those trees first. **Forestry**: never cut, and felled trees grow back twice as fast. **Pasture**: every 4 cells give one more herder a place (once a pasture or pen stands).
- Content: `tools/content/zones.py`; rules: `Core/GameZones.cs`; tests: `ZonesTests`.

## Monuments built part by part (Manor Lords' modular manors)

- Three medieval monuments on a 4×4 plot: **Lord's Manor** (Written Law), **Abbey** (Monastic Orders), **Stone Keep** (Masonry). The player lays out the plot, then picks the next part from the monument's **plan** on its card: a part needs the parts it joins onto, costs its own goods and time, and appears on the model.
- Five parts each, with lasting effects, gathering-place points and estate favour: e.g. Great Hall (+3 community, Crown favour), Solar Tower (bigger treasury), Cloister and Library (faster research, Clergy favour), Infirmary (less illness), Curtain Wall (calmer town), Gatehouse (faster scouts), Great Storehouse (more wood and stone storage).
- Model names carry the parts (`lords_manor+great_hall+solar_tower`); `BuildingModels.Monument` draws the plot and each part. Content: `tools/content/monuments.py`; rules: `Core/GameMonuments.cs`; tests: `MonumentTests`.

## Goods carried across the map, and the bread chain

- Workers at recipe buildings (sawpit, toolsmith, smelter, mill, bakery, weaver) walk to the nearest store, carry the input to their workplace, work, and carry the output back; builders carry planks and stone to the site. This is drawn by `WorldView.HaulTask` only: the economy keeps one shared stockpile, so carrying never slows production and costs nothing per frame beyond the walkers already on the map.
- New resource **Flour**. The mill's **millers** grind grain (Food) into Flour; the bakery's bakers bake Flour (plus a little wood) into much more Food. Content: `tools/content/flour.py`; tests: `PresentEraTests` (bakers and millers).

## Next refinements (to discuss)

1. **Families instead of single people**: housing gives family slots and one family
   staffs one workplace slot (closer to Manor Lords).
2. **Supplies forecast**: "food lasts N days, firewood lasts N days", shown before
   winter.
3. **Organic paths**: paths that wear in where villagers actually walk (Foundation).
4. **Splendor and estates**: decorative buildings and monuments, and choosing which
   group to favour for exclusive buildings.
5. **Market and goods variety**: separate food types (grain, meat, fish, berries) and
   clothing/pottery as goods that raise approval.
6. **Story**: era chapters, named founders and their descendants in the Legacy
   Timeline.
