# Technical architecture

## Stack

| Part | Technology |
|---|---|
| Engine | Unity **6.3 LTS (6000.3.26f1)**, C# |
| Rendering | Universal Render Pipeline 17.3, Unity Terrain, GPU-instanced nature and grass |
| UI | UI Toolkit (`VisualElement`) built in code (`UI/UiKit.cs`, `UI/GameUi.*.cs`); skins from the Adventure UI Kit (private repo `iosgame-uikit`, pulled in CI) with built-in fallbacks |
| Input | Legacy input (touch and mouse): tap, drag to pan, pinch to zoom |
| Target | iPhone (landscape only); Android builds possible |
| Save | Versioned JSON on the device (`SaveCodec`, `FileSaveStore`), optional cloud copy on the owner's server (`server/cloud`, Node.js + MariaDB) |
| Builds | GitHub Actions only (GameCI): `.github/workflows/unity-build.yml` (tests → Xcode project → unsigned IPA), `preview.yml` (iOS Simulator screenshots) |
| Legacy | `Core/` and `App/`: the original Swift prototype, kept as reference only |

## Layers

```
content.json  ──►  Founders.Core (pure C#, no UnityEngine)  ──►  Unity presentation
 (all data)         Game + GameState + rules                      WorldView, GameUi, Atmosphere…
```

- **`unity/Assets/Resources/content.json`**: every definition and balance number (eras,
  buildings, jobs and recipes, technologies, events, objectives, milestones, chapters, laws,
  petitions, regions, map, skills, climate). Checked by `ContentValidator` on load.
- **`unity/Assets/Scripts/Core/`** (`Founders.Core`, asmdef with `noEngineReferences`): the
  rules. `Game` is a partial class split by topic:

  | File | Concern |
  |---|---|
  | `Game.cs` | clock, fixed-step simulation loop, growth, legacy |
  | `GameEconomy.cs` | production, consumption, storage caps, crafting, **recipes**, job assignment |
  | `GameNodes.cs` | natural resource cells (trees, rocks, veins, bushes, fish, game), depletion and regrowth |
  | `GameBuilding.cs` | placement validation, construction, upgrades |
  | `GameTown.cs` | rotation, levels, roads, decorations, demolition, people and skills |
  | `GameProgress.cs` | conditions, objectives, milestones and era changes, research, exploration |
  | `GameWellbeing.cs` | seasons, fuel, approval |
  | `GameClimate.cs` | weather, temperature, home warmth |
  | `GameHealth.cs` | illness, healing, deaths |
  | `GameLaws.cs` | discontent, laws, petitions, stoking the heart |
  | `GameStory.cs` | chapters |
  | `State.cs` | the saveable data model |
  | `Save.cs` | save envelope, validation, corruption recovery |
  | `Profile.cs` | founder profile and achievements |
- **Presentation** (`unity/Assets/Scripts/*.cs`, `UI/`): reads `Game.State`, calls commands
  (`PlaceBuilding`, `SetWorkers`, `StartResearch`…), never changes state directly.
  `GameStore` owns the game, saving and offline reports. `WorldView` renders the town and
  drives villager tasks. `CaptureDirector` produces automated screenshots in CI.

## Simulation

- **Time:** `Game.Update()` simulates exactly the wall-clock interval since the last call in
  fixed steps (1 s; 5 s for absences over an hour), capped by `maxOfflineSeconds`. Repeated
  calls, restarts and resume never duplicate production (decision D4).
- **Step order:** economy snapshot → apply net change (clamped to 0 and caps) → record goods
  made → drain land cells → food shortage → skills → construction → research → expeditions →
  health → discontent → growth → time, seasons, weather, petitions, modifier expiry.
- **Determinism:** weather and illness dice are seeded from the settlement id; tests use a
  `ManualClock`.

### Economy model

Jobs are the unit of work. A building offers job slots (scaled by its level); the player assigns
people per job across the whole settlement. Output per worker = base rate × tech/law/perk
multipliers × season × approval × road bonus × skill.

**Recipes** (since 2026-10-22): a job config with `inputs` is a recipe (`JobConfig.Inputs`,
`JobConfig.IsRecipe`). `GameEconomy.ApplyRecipes` runs after all other consumption and limits each
recipe by (a) its workers, (b) the stock of each input left after this step's other consumption,
and (c) the room left in the output store. Recipes never use goods gathered in the same step, so
they cannot spend what the land later fails to deliver. `EconomySnapshot.Processing` reports the
state of every recipe (`Working`, `NoWorkers`, `MissingInput` with the input, `OutputFull`) and
`GameState.Made` counts goods made for goals (`ConditionKind.MadeAtLeast`).

**Documented simplification:** one shared stockpile. Workplaces draw from and deliver to the
settlement's storage instantly; there is no carrying of goods between buildings. Villagers do
walk to their work and carry goods home visually. Local logistics (per-building stores, porters,
carts) is Phase 5 and will replace the instant transfer inside `ApplyRecipes` and the gathering
path without changing the content format.

### Resources and jobs as enums

`ResourceType` and `JobType` are C# enums (decision D3: resources are not added lightly). Adding
one touches: the enum, `ResourceTypes.All/Stored`, `Id`/`DisplayName`, `ResourceBundle` (field,
indexer, JSON), and in the UI `IconFor`, `CellFor`, `StripOrder` (resources) or `JobIcon`,
`SkillTitleFallback` (jobs), plus a skill entry in `content.json`. Everything else (recipes,
costs, conditions, storage) is data.

## Save format

`{ saveVersion, contentVersion, savedAtWallTime, state }`. New fields are optional and written
only when present, so older saves load unchanged (e.g. `resources.bronze`, `made`). Loading
rejects newer versions, moves undecodable files to `.corrupt`, and validates structure
(negative resources, unknown ids, more workers than people). Tests: `SaveTests`,
`RecipeTests.MadeGoodsSurviveSaveAndCountForGoals`.

## Testing and validation

| Command | What it checks |
|---|---|
| `dotnet test unity/CoreTests` | 142 rule tests including scripted playthroughs (no Unity needed) |
| `dotnet build unity/CoreTests/LibCheck` | the core compiles as .NET Standard 2.1 / C# 9 like Unity |
| `scripts/unity-compile-check.sh` | every Unity script compiles against the real Unity 6.3 DLLs (no license) |
| GitHub Actions `unity-build.yml` | tests, then the iOS build and IPA (needs Unity license secrets) |
| GitHub Actions `preview.yml` | iOS Simulator screenshots of the real build |

## Known risks and weaknesses

1. **Builds only run in GitHub Actions.** Claude cannot push or start workflows until the Claude
   GitHub App is installed on `mmzlopasa/iosgame`, so visual checks wait for the owner.
2. **Large presentation files** (`WorldView.cs` 1550 lines, `UiKit.cs`, `GameUi.Survival.cs` > 1000)
   are hard to change safely; split by concern when next touched.
3. **Global stockpile** (see above): no local shortages yet.
4. **Enums for resources and jobs** make each new good a small code change (deliberate, but it
   grows with every chain).
5. **No pause or game-speed control** in the core: time always follows the wall clock (fits the
   mobile design; a PC build would need it).
6. **Parallel work** on the map, free building, speed-ups and the admin dashboard edits the same
   `content.json` and core files; merge carefully.
