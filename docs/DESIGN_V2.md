# Founders — The Long Rise · Design v2: a real town builder

## Goal

The player builds a settlement in their own design and layout. They place houses,
workplaces, walls, fences, lanterns, trees and roads wherever they want, the way
Manor Lords, Foundation and SimCity BuildIt allow. Every villager is a person with a
name and skills, who walks the roads to work and visibly does the work there. The
eras come slowly. Prehistory (the Stone Age and the Bronze Dawn) and the Middle Ages
are built and polished first. Later eras come only after that.

## What the reference games do (research)

**Manor Lords**
- **Roads** are drawn freely with two or more points. They cost nothing and are built
  instantly. Buildings work without a road, but roads speed up carts and shape the
  plots beside them.
- **Burgage plots** are flexible residential plots, sized and shaped with four points.
  They cost 2 timber per house that fits. They upgrade three ways: extra living space,
  plot level (L2 needs water, a church and market stalls for food, fuel and clothing;
  L3 needs a larger village) and backyard extensions (garden, workshop).
- **Families** are the unit of labour. Each workplace takes families, and unassigned
  families build and haul.
- **Approval** comes from the variety of goods at the market, the church, fuel and
  housing. It controls how fast new families arrive.
- **Seasons**: spring sowing, summer growth, autumn harvest, winter cold. Fuel use
  doubles in winter, and a lack of firewood means freezing.
- Woodcutter's lodges supply firewood (fuel) to the market.

**Foundation (Polymorph Games)**
- **Organic, gridless growth** through zones: residential, market and others.
- **Villagers** have types (labourers, monastics, soldiers) and ranks (serf, commoner,
  citizen). A villager at the right rank for a job works at full efficiency.
- **Workplaces** have specific jobs. Hunters at the Hunter's Hut, Fishermen at the
  Fisher's Hut, Foragers at the Gathering Camp. Carpenters, charcoal burners and
  foresters work elsewhere. Workers are assigned from the workplace or from the
  villager's own window, and the "+" button picks the best fit.
- Houses level up (wood → stone) when villagers' needs are met. Villagers move closer
  to their work if they live too far away.
- **Splendor** and the **Estates** (Labour, Clergy, Kingdom) unlock special buildings.

**Whiteout Survival (HUD)**
- **Top left**: avatar with power and level. **Top**: resource bar.
- **Left**: quest and task card. **Right**: a column of event and timer icons.
- **Bottom**: main bar (City/World, Heroes, Backpack, Alliance, Mail), with chat above
  it.
- Survivor status, offline report and building screens with timers.
- The side icon columns can be collapsed.

Sources:
- [Manor Lords buildings](https://wiki.hoodedhorse.com/Manor_Lords/Buildings)
- [Burgage plot](https://wiki.hoodedhorse.com/Manor_Lords/Burgage_plot)
- [GameSpot upgrade guide](https://www.gamespot.com/articles/manor-lords-upgrade-town-village-guide/1100-6523015/)
- [Game8 burgage levels](https://game8.co/games/Manor-Lords/archives/451369)
- [Foundation villagers](https://wiki.polymorph.games/foundation/Villagers)
- [Foundation jobs](https://foundation-game.fandom.com/wiki/Jobs)
- [Foundation buildings](https://wiki.polymorph.games/foundation/Buildings)
- [Whiteout Survival icons](https://techwiser.com/whiteout-survival-icons-and-symbols-meaning/)

## The story (prehistory and the Middle Ages)

The player is **the Founder**. After a hard winter in the north, a handful of families
follows the river south into an unknown valley: a lake, forest, hills of stone and
mountains with veins of ore. Each chapter is one generation. The Legacy Timeline
records what the player built and decided, and the next chapter inherits it.

| Chapter | Era | Goal (what the player builds) | Slowness (earliest) |
|---|---|---|---|
| 1. The Riverside Camp | Stone Age | Survive the first year: shelter, food, fire. | Year 1 |
| 2. The Stable Village | Stone Age | Permanent homes, a meeting circle, the first roads. | End of year 2 |
| 3. The Bronze Dawn | Late prehistory | Fields, granary, copper and bronze, trade with the river people. | Year 5 |
| 4. The Hillfort | Late prehistory | Walls and palisades, a chieftain's hall, larger roads. | Year 8 |
| 5. The Charter | Middle Ages | A lord grants the village a charter. Plots, church, market. | Year 12 |
| 6. The Market Town | Middle Ages | Craftsmen, stone houses, mill, smithy. | Year 16 |
| 7. The Walled Town | Middle Ages | Town walls, gates, a stone church, a castle on the hill. | Year 20+ |

One game year is 24 minutes (four seasons of 6 minutes), so the Middle Ages begin
after about 4–5 hours of play at the earliest. A chapter needs both time
(`yearAtLeast`) and achievements. Each chapter opens with a short story scene and
records a Legacy entry. The founder's line continues: every ~5 years the leadership
passes to an heir, who is named in the Legacy Timeline.

## Systems (the build order)

**Phase 1 — the town-builder foundation (in progress)**
1. **Large map** (64×64 cells, 128 m), generated from a seed:
   - lake and river (fishing);
   - mountains with ore veins (mines);
   - stony hills (quarries);
   - forest with game (woodcutters, hunters);
   - meadows (farms).
2. **Free layout**:
   - rotatable buildings;
   - roads drawn by dragging (free, built instantly; villagers walk faster on them,
     and workplaces on a road +10 %);
   - decorations: fences, palisades, stone walls, lanterns, bushes, trees, flower
     beds, well, totem.
3. **Building levels 1–3**: upgrades raise slots, housing and storage.
4. **Villagers as people**:
   - name, age, home and job;
   - skills per craft (woodcutting, farming, fishing, hunting, mining, quarrying,
     building, crafting, research);
   - skill grows with work; level 0–5 adds up to +50 % output.
5. **Jobs and animations**:
   - woodcutter chops (axe);
   - miner and quarryman strike (pickaxe);
   - farmer sows, waters and harvests;
   - fisher casts;
   - hunter throws a spear;
   - builders work on the site;
   - porters carry;
   - lanterns at night.
6. **HUD**: Whiteout Survival style, with a hide button (eye) for a clean view of the
   town.
7. **Loading screen**: a video montage of the game.

**Phase 2 — the Middle Ages**
- Medieval houses from the Quaternius Medieval Village MegaKit (CC0).
- Plots with gardens, a church, a market (variety of goods), a mill and a smithy.
- New goods: planks, iron, tools, bread, clothing.
- Lord and charter.

**Phase 3 — polish**
- Balance, tutorial, story scenes, sound, performance and the store release.
