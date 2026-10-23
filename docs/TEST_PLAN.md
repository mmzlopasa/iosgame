# Test plan

## Automated (run on every change)

```bash
dotnet test unity/CoreTests -nologo          # 142 rule tests, ~2 min, no Unity needed
dotnet build unity/CoreTests/LibCheck -nologo # core compiles like Unity (.NET Standard 2.1, C# 9)
scripts/unity-compile-check.sh               # all Unity scripts against the real Unity 6.3 DLLs
```

GitHub Actions runs the first two before every iOS build (`unity-build.yml`); `preview.yml`
produces simulator screenshots of the real build.

| Suite | Covers |
|---|---|
| `CoreTests` | resources, placement, construction, jobs, research, events, objectives, milestones, save/load, offline time |
| `PlaythroughTests`, `FullPlaythroughTests` | a scripted player reaches the village; every command, building, decoration, tech and law works |
| `ProgressionTests`, `NodeTests`, `TownTests` | heart levels, land cells, rotation, roads, people, skills |
| `WellbeingTests`, `SurvivalTests` | seasons, fuel, approval, cold, illness, discontent, laws, petitions |
| `HerdingTests`, `MedievalTests`, `StoryTests`, `ProfileTests` | herds, medieval era, chapters, profile |
| `RecipeTests` | recipes: exact input→output, missing input (no waste), scarce input used up exactly, full store stops, no workers, 3-hour absence, firewood first in winter, save/load of `made`, Bronze Dawn requirement, validator |

## Edge cases and where they are tested

| Case | Test |
|---|---|
| Not enough resources | `CoreTests` placement, `FullPlaythroughTests` |
| No workers / no slots | `CoreTests`, `RecipeTests.NoSmeltersNoWork` |
| Full storage | `CoreTests` caps, `RecipeTests.AFullStoreStopsTheFurnace` |
| Missing production inputs | `RecipeTests.NothingIsMadeWithoutTheInputs`, `ScarceInputsAreUsedUpExactlyAndNeverGoNegative` |
| Invalid placement | `CoreTests`, `TownTests` |
| Population over housing | `CoreTests` growth |
| Accelerated / long simulation | `CoreTests` offline, `RecipeTests.ALongAbsenceKeepsTheRecipeExact` |
| Save and load, repeated load | `SaveTests`, `RecipeTests.MadeGoodsSurviveSaveAndCountForGoals` |
| Era requirements | `WellbeingTests.BronzeDawnOpensTheSecondEra`, `MedievalTests` |
| Paused simulation | not applicable (no pause in the mobile design) |

## Manual check on the phone (recipe change)

1. Developer panel (gear) → add resources; research up to Copper Smelting.
2. Build a Mine at an ore vein and a Copper Smelter. Put 2 miners and 2 smelters to work.
3. The smelter card shows *Bronze 2.4/m · 2 Ore + 1 Wood → 1 Bronze*; bronze appears in the top bar.
4. Set miners to 0 and wait until ore runs out: the card says *Stopped: no Ore*; wood stops falling for the furnace.
5. Goals → The Bronze Dawn lists *Make Bronze n/20*.
6. Close and reopen the app: bronze and the counter are unchanged.
