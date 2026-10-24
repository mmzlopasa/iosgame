# Feature tracker

✅ works and is tested · 🟡 partial or untested on device · ⬜ missing · ⏸ paused

| Area | Feature | State | Where / notes |
|---|---|---|---|
| World | 64×64 map from content, river, lake, hills, ore veins, forest | ✅ | `content.json → map`, `WorldTerrain.cs` (being rebuilt in another thread) |
| World | Exploration by expeditions, 12 regions | ✅ | `GameProgress.cs` |
| World | Land cells that run out and regrow | ✅ | `GameNodes.cs`, `NodeTests` |
| Time | Fixed step, offline progress without duplication | ✅ | `Game.cs`, `CoreTests` |
| Time | Pause / speed control | ⬜ | wall-clock only (mobile design) |
| Building | Free placement, rotation, terrain rules, limits | ✅ | `GameBuilding.cs` (free building in another thread) |
| Building | Levels 1–20, heart caps others, looks per level | ✅ | `GameTown.cs`, `ProgressionTests` |
| Building | Roads, fences, walls, decorations, bridges | ✅ | `GameTown.cs` |
| Building | Demolish with refund | ✅ | `FullPlaythroughTests` |
| People | Named villagers, jobs, skills and perks | ✅ | `GameTown.cs`, `TownTests` |
| People | Approval, comfort, community | ✅ | `GameWellbeing.cs` |
| People | Warmth, illness, healing, deaths | ✅ | `GameClimate.cs`, `GameHealth.cs`, `SurvivalTests` |
| People | Discontent, laws, petitions | ✅ | `GameLaws.cs` |
| People | Age, families, generations | ⬜ | Phase 3 |
| Economy | Food, wood, stone, ore, hides, knowledge | ✅ | `Resources.cs` |
| Economy | Storage caps, spoilage, shortage | ✅ | `GameEconomy.cs` |
| Economy | **Recipes (inputs → output) with status** | ✅ | `GameEconomy.ApplyRecipes`, `RecipeTests` |
| Economy | **Bronze chain: ore + wood → bronze** | ✅ rules · 🟡 UI | Copper Smelter; UI not yet seen in a build |
| Economy | **Planks (sawpit), tools (toolsmith), bread (bakery)** | ✅ rules · 🟡 UI | `tools/content/present_eras.py`, `PresentEraTests`; iron chain still ⬜ |
| Buildings | **Lasting building effects** (herb garden, lookout, shrine, tavern, bathhouse) | ✅ | `BuildingDefinition.Effects`, `Game.BuildingEffects` |
| Economy | Per-building stores, porters | ⬜ | Phase 5 (shared stockpile today) |
| Economy | Trade | 🟡 | traders event only |
| Research | 29 techs, prerequisites, heart gating | ✅ | `GameProgress.cs` |
| Eras | First Communities, First Civilizations, Medieval | ✅ | milestones in `content.json` |
| Eras | Classical, Exploration, Industrial, Modern, Future | ⬜ / ⏸ | modern paused 2026-10-14 |
| Story | 8 chapters, story scenes | ✅ | `GameStory.cs`, `StoryTests` |
| Story | Legacy Timeline (chronicle) | ✅ | `State.Legacy` |
| Events | 6 events with choices, objectives, milestones | ✅ | `content.json` |
| Environment | Seasons, day/night, weather, temperature | ✅ | `GameWellbeing.cs`, `GameClimate.cs`, `Atmosphere.cs` |
| UI | HUD, build menu, people, research, goals, map, cards | ✅ | `UI/` |
| UI | Recipe status on job rows and building cards | 🟡 | `GameUi.Cards.RecipeText` (compiles; screenshot pending) |
| UI | Economy overview panel | ⬜ | |
| Economy | Needs (food, water, goods), house tiers, taxes in coins, tax level | ✅ | `GameNeeds.cs`, `NeedsTests` |
| Economy | Markets and trade routes: market reach, trading post, caravans to four peoples (barter, some deals for coins) | ✅ | `GameTrade.cs`, `WorldView.Trade.cs`, `TradeTests` |
| Society | Three estates (Commons, Clergy, Crown): favour, ranks, boons, balance | ✅ | `GameEstates.cs`, `EstatesTests` |
| Town | Organic paths worn by foot traffic (faster walking, faster building beside them) | ✅ | `GamePaths.cs`, `PathsTests` |
| Town | Zone brush: homes built by villagers, woodcutting, forestry, pastures | ✅ | `GameZones.cs`, `ZonesTests` |
| Town | Monuments built part by part (Manor, Abbey, Keep) with a plan of parts | ✅ | `GameMonuments.cs`, `MonumentTests` |
| Economy | Goods carried by villagers between stores and workplaces; mill grinds flour for the bakery | ✅ | `WorldView.HaulTask`, `flour.py`, `PresentEraTests` |
| Save | Versioned local save, validation, corruption recovery | ✅ | `Save.cs`, `SaveTests` |
| Save | Cloud save on own server | ✅ | `CloudSave.cs`, `server/cloud` |
| Profile | Founder profile, achievements, settings | ✅ | `Profile.cs`, `ProfileTests` |
| Speed-ups | Whiteout-style speed-up items | 🟡 | other thread |
| Admin | Web dashboard for all players | 🟡 | other thread |
| Military | Raids, defence, equipment | ⬜ | Phase 7 |
