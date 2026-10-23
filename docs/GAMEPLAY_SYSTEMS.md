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
