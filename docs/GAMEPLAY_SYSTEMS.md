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
