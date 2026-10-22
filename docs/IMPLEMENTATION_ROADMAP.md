# Implementation roadmap

Phases follow the master plan (2026-10-22). Each phase lists what exists, what is missing and
its acceptance test. Status: ✅ done and tested · 🟡 partly done · ⬜ not started.
`IMPLEMENTATION_PLAN.md` (root) is the original Swift-era plan and is kept for history.

Work running in parallel threads (not covered here): the new map and free building with
the player choosing the camp site, the speed-up system, and the admin dashboard.

## Phase 0 — Repository audit ✅ (2026-10-22)

The real state is in `docs/FEATURE_TRACKER.md` and `docs/TECHNICAL_ARCHITECTURE.md`.
Build and test commands are in `docs/TEST_PLAN.md`.

## Phase 1 — Core playable settlement ✅

World, time, inventory, gathering from the land, placement, construction, housing, population,
jobs, food, UI feedback. Accepted by `PlaythroughTests` and `FullPlaythroughTests` (scripted
player reaches the village and plays every command).

## Phase 2 — Functional economy 🟡 (current phase)

- ✅ Storage caps, spoilage, shortage warnings, workforce limits.
- ✅ **Recipe system**: inputs → output on data, no negative stock, no production without
  inputs, stop on full output, status and reason in the UI, lifetime "made" counters for goals.
- ✅ **First chain:** Mine → Ore → Copper Smelter (+ wood) → Bronze → Bronze Workshop and the
  Bronze Dawn milestone.
- ⬜ Bronze tools: a toolmaker recipe (bronze + wood → tools) whose stock raises gatherers'
  output, replacing the abstract crafting bonus in the Bronze era.
- ⬜ Medieval chains: sawpit (wood → planks) for cottages and houses; mill and bakery
  (grain → flour → bread) as a food the market needs; smithy (iron ore + charcoal → iron → tools).
- ⬜ Economy overview panel: per-resource production, consumption and stock forecast.

Acceptance: production chains run by explicit rules; `RecipeTests` prove no duplication,
no negative stock, scarcity and full-store behaviour, long absences and save/load.

## Phase 3 — Population simulation 🟡

Done: needs (food, warmth, comfort, community), housing limits, illness and healing, approval,
discontent, skills. Missing: age and life stages, families, births vs. arrivals, generational
replacement (the founder's heir every ~5 years in the story), education.

## Phase 4 — Research and first era transition ✅

23 technologies, prerequisites, heart-level gating, unlocks; milestones open the Bronze Dawn
and the Medieval Kingdoms with real requirements. Next: an era overview panel that lists what
the next era changes.

## Phase 5 — Logistics and regional growth ⬜

Per-building stores and porters (replace the shared stockpile in `ApplyRecipes` and gathering),
road speed for carrying, local shortages, more resource sites after exploration, a second
settlement.

## Phase 6 — Advanced simulation 🟡

Seasons, weather, events, laws and petitions exist. Missing: trade (traders event only),
social classes, education, environmental consequences, the Classical era.

## Phase 7 — Conflict and great projects ⬜

Raids and defence that need equipment (bronze, iron) and trained people; monuments built in
stages.

## Phase 8 — Polish ⬜ (the owner's priority since 2026-10-14)

UI clarity, tutorial, sound, performance on device, save/load validation on device, store
release. The modern era stays paused until the existing eras are polished.

## Exact next steps

1. Owner: install the Claude GitHub App on `mmzlopasa/iosgame` (or push the delivered branch)
   so CI builds the IPA and the simulator preview of the recipe UI.
2. Bronze tools recipe (Phase 2), with tests like `RecipeTests`.
3. Economy overview panel in People or a new Storage sheet.
4. Planks chain for the medieval houses.
