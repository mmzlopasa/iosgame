using System;
using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>
    /// A headless player that uses every command the game offers (building, levels, upgrades,
    /// rotation, demolition, roads and decorations, jobs, research, expeditions, events,
    /// petitions, customs, stoking, illness, milestones, chapters, offline time, saving) and
    /// checks after every step that the settlement stays consistent.
    /// </summary>
    public class FullPlaythroughTests
    {
        // MARK: - Invariants

        static int checks;

        /// <summary>Everything that must hold at any moment of any game.</summary>
        internal static void Check(Game g, string where, bool afterTime = false)
        {
            checks++;
            var s = g.State;
            var c = g.Content;
            string at = $"[{where} @ {s.SimTime:0}s]";

            Assert.That(s.Resources.IsValid, Is.True, $"{at} resources must be finite and >= 0: {s.Resources}");
            Assert.That(Finite(s.SimTime) && s.SimTime >= 0, Is.True, $"{at} simTime {s.SimTime}");
            if (afterTime)
            {
                // A simulation step always clamps to storage.
                var caps = g.StorageCapacity();
                foreach (var t in ResourceTypes.All)
                    Assert.That(s.Resources[t], Is.LessThanOrEqualTo(caps[t] + 1e-6), $"{at} {t} {s.Resources[t]} above its cap {caps[t]}");
            }

            // People
            Assert.That(s.Population, Is.GreaterThanOrEqualTo(0), at);
            Assert.That(s.People.Count, Is.EqualTo(s.Population), $"{at} people list vs population");
            Assert.That(s.People.Select(p => p.Id).Distinct().Count(), Is.EqualTo(s.People.Count), $"{at} duplicate person ids");
            Assert.That(s.People.All(p => p.Id < s.NextPersonId), Is.True, $"{at} person id >= nextPersonId");
            foreach (var p in s.People)
            {
                Assert.That(p.Job == null || Enums.Jobs.Any(j => j.Id() == p.Job), Is.True, $"{at} {p.Name} has unknown job {p.Job}");
                Assert.That(Finite(p.Recovery) && p.Recovery >= 0 && p.Recovery < 1 + 1e-9, Is.True, $"{at} {p.Name} recovery {p.Recovery}");
                Assert.That(Finite(p.Severity) && p.Severity >= 0 && p.Severity < 1 + 1e-9, Is.True, $"{at} {p.Name} severity {p.Severity}");
                if (!p.Sick) Assert.That(p.Recovery == 0 && p.Severity == 0, Is.True, $"{at} healthy {p.Name} keeps illness progress");
                Assert.That(p.Experience.Values.All(v => Finite(v) && v >= 0), Is.True, $"{at} {p.Name} experience");
            }
            foreach (var job in Enums.Jobs)
            {
                int n = s.Assignments[job];
                Assert.That(n, Is.GreaterThanOrEqualTo(0), $"{at} {job} negative");
                Assert.That(s.People.Count(p => p.Job == job.Id()), Is.EqualTo(n), $"{at} people working {job} vs assignments");
                if (c.JobConfig(job)?.UnlimitedSlots != true)
                    Assert.That(n, Is.LessThanOrEqualTo(g.JobCapacity(job)), $"{at} {job} {n} workers over capacity {g.JobCapacity(job)}");
                Assert.That(g.ActiveWorkers(job), Is.InRange(0, n), $"{at} active {job}");
            }
            Assert.That(s.Assignments.Total, Is.LessThanOrEqualTo(s.Population), $"{at} more workers than people");
            Assert.That(g.IdleWorkers, Is.EqualTo(s.Population - s.Assignments.Total), at);
            Assert.That(g.SickCount, Is.InRange(0, s.Population), at);

            // Meters
            Assert.That(Finite(s.Discontent) && s.Discontent >= 0 && s.Discontent <= 100, Is.True, $"{at} discontent {s.Discontent}");
            Assert.That(Finite(s.GrowthProgress) && s.GrowthProgress >= 0 && s.GrowthProgress < 1 + 1e-9, Is.True, $"{at} growth {s.GrowthProgress}");
            Assert.That(Finite(s.IllnessProgress) && s.IllnessProgress >= 0, Is.True, $"{at} illness {s.IllnessProgress}");
            Assert.That(Finite(s.LeaveProgress) && s.LeaveProgress >= 0 && s.LeaveProgress <= 1, Is.True, $"{at} leave {s.LeaveProgress}");
            Assert.That(g.Approval, Is.InRange(0, 100), at);
            Assert.That(s.Deaths, Is.GreaterThanOrEqualTo(0), at);

            // Buildings
            var cells = new Dictionary<(int, int), int>();
            var ids = new HashSet<int>();
            foreach (var b in s.Buildings)
            {
                var def = c.Building(b.DefinitionId);
                Assert.That(def, Is.Not.Null, $"{at} unknown building {b.DefinitionId}");
                Assert.That(ids.Add(b.Id), Is.True, $"{at} duplicate building id {b.Id}");
                Assert.That(b.Id, Is.LessThan(s.NextBuildingId), $"{at} building id {b.Id} >= next id");
                Assert.That(b.Level, Is.InRange(1, def.MaxLevel), $"{at} {def.Id} level {b.Level}");
                Assert.That(b.Rotation, Is.InRange(0, 3), at);
                Assert.That(Finite(b.RemainingWork) && Finite(b.TotalWork), Is.True, $"{at} {def.Id} work");
                if (b.Status == BuildingStatus.Complete)
                {
                    Assert.That(b.RemainingWork, Is.EqualTo(0), $"{at} complete {def.Id} still has work");
                    Assert.That(b.PendingLevel, Is.EqualTo(0), $"{at} complete {def.Id} has a pending level");
                    Assert.That(b.UpgradeTargetId, Is.Null, $"{at} complete {def.Id} has an upgrade target");
                }
                else
                {
                    Assert.That(b.RemainingWork, Is.GreaterThan(0), $"{at} unfinished {def.Id} without work left");
                    Assert.That(b.Progress, Is.InRange(0.0, 1.0), at);
                }
                if (b.Status == BuildingStatus.Upgrading)
                    Assert.That(b.PendingLevel == b.Level + 1 || (b.PendingLevel == 0 && b.UpgradeTargetId != null), Is.True, $"{at} {def.Id} upgrade target");
                Assert.That(b.History.Count, Is.GreaterThan(0), at);
                Assert.That(b.History.Last(), Is.EqualTo(b.DefinitionId), $"{at} {def.Id} history ends elsewhere");
                var fp = g.FootprintOf(b);
                for (int dy = 0; dy < fp.Height; dy++)
                    for (int dx = 0; dx < fp.Width; dx++)
                    {
                        int x = b.X + dx, y = b.Y + dy;
                        Assert.That(x >= 0 && y >= 0 && x < c.Map.Width && y < c.Map.Height, Is.True, $"{at} {def.Id} outside the map");
                        Assert.That(cells.ContainsKey((x, y)), Is.False, $"{at} {def.Id} #{b.Id} overlaps #{(cells.TryGetValue((x, y), out var o) ? o : -1)} at {x},{y}");
                        cells[(x, y)] = b.Id;
                        var terrain = g.TerrainNow(x, y);
                        Assert.That(terrain.HasValue && def.AllowedTerrain.Contains(terrain.Value), Is.True,
                            $"{at} {def.Id} #{b.Id} stands on {terrain} at {x},{y}");
                        Assert.That(g.BuildingIdAt(x, y), Is.EqualTo(b.Id), at);
                    }
                if (def.MaxCount.HasValue)
                    Assert.That(s.Buildings.Count(x => x.DefinitionId == def.Id), Is.LessThanOrEqualTo(def.MaxCount.Value), $"{at} too many {def.Id}");
            }
            Assert.That(g.Heart, Is.Not.Null, $"{at} the heart is gone");
            Assert.That(g.HeartLevel, Is.GreaterThanOrEqualTo(1), at);
            Assert.That(g.ActiveConstructionCount(), Is.LessThanOrEqualTo(c.Balance.MaxConcurrentConstructions), $"{at} construction queue overflow");

            // Decorations
            var decoIds = new HashSet<int>();
            var decoCells = new HashSet<(int, int)>();
            foreach (var d in s.Decorations)
            {
                Assert.That(c.Decoration(d.DefinitionId), Is.Not.Null, $"{at} unknown decoration {d.DefinitionId}");
                Assert.That(decoIds.Add(d.Id), Is.True, $"{at} duplicate decoration id");
                Assert.That(d.Id, Is.LessThan(s.NextDecorationId), at);
                Assert.That(decoCells.Add((d.X, d.Y)), Is.True, $"{at} two decorations on {d.X},{d.Y}");
                Assert.That(cells.ContainsKey((d.X, d.Y)), Is.False, $"{at} decoration under a building at {d.X},{d.Y}");
                Assert.That(g.DecorationAt(d.X, d.Y)?.Id, Is.EqualTo(d.Id), $"{at} decoration index is stale");
            }

            // Progress
            Assert.That(s.Researched.Distinct().Count(), Is.EqualTo(s.Researched.Count), $"{at} tech researched twice");
            Assert.That(s.Researched.All(t => c.Technology(t) != null), Is.True, at);
            if (s.ActiveResearch != null)
            {
                Assert.That(s.Researched.Contains(s.ActiveResearch.TechId), Is.False, $"{at} researching a known tech");
                Assert.That(Finite(s.ActiveResearch.RemainingSeconds), Is.True, at);
            }
            Assert.That(s.ExploredRegions.Distinct().Count(), Is.EqualTo(s.ExploredRegions.Count), $"{at} region explored twice");
            if (s.ActiveExpedition != null) Assert.That(s.ExploredRegions.Contains(s.ActiveExpedition.RegionId), Is.False, at);
            Assert.That(s.CompletedObjectives.Distinct().Count(), Is.EqualTo(s.CompletedObjectives.Count), at);
            Assert.That(s.ReachedMilestones.Distinct().Count(), Is.EqualTo(s.ReachedMilestones.Count), at);
            Assert.That(s.Laws.Distinct().Count(), Is.EqualTo(s.Laws.Count), $"{at} law signed twice");
            Assert.That(c.Chapters.Any(ch => ch.Number == s.Chapter), Is.True, $"{at} chapter {s.Chapter}");
            if (s.PendingEventId != null) Assert.That(s.ResolvedEvents.ContainsKey(s.PendingEventId), Is.False, $"{at} resolved event pending again");
            Assert.That(s.Legacy.Select(l => l.Id).Distinct().Count(), Is.EqualTo(s.Legacy.Count), at);
            Assert.That(s.Modifiers.All(m => m.Effect != null), Is.True, at);

            // Derived views
            var e = g.Economy();
            foreach (var t in ResourceTypes.All)
                Assert.That(Finite(e.Production[t]) && Finite(e.Consumption[t]) && e.Production[t] >= 0 && e.Consumption[t] >= 0, Is.True, $"{at} economy {t}");
            var w = g.Warmth();
            Assert.That(w.People.Sum(), Is.EqualTo(s.Population), $"{at} warmth counts everyone once");
            if (g.HasWarmth) Assert.That(w.Homeless, Is.EqualTo(Math.Max(0, s.Population - g.HousingCapacity())), $"{at} homeless");
            Assert.That(Finite(w.Temperature), Is.True, at);
            Assert.That(Finite(g.DiscontentPerHour), Is.True, at);
            Assert.That(g.ApprovalFactors().All(f => f.Label != null), Is.True, at);
            if (checks % 10 == 0)
                foreach (var job in Enums.Jobs) Assert.That(Finite(g.RatePerWorker(job)) && g.RatePerWorker(job) >= 0, Is.True, $"{at} rate {job}");

            // Every few checks: the state survives a save and a load unchanged.
            if (checks % 25 == 0) RoundTrip(g, at);
        }

        static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

        static void RoundTrip(Game g, string at)
        {
            string data = SaveCodec.Encode(g.State, g.Content, 1234);
            var outcome = SaveCodec.Decode(data, g.Content);
            Assert.That(outcome.Result, Is.EqualTo(LoadResult.Loaded), $"{at} save does not load back: {outcome.Reason}");
            Assert.That(SaveCodec.Encode(outcome.State, g.Content, 1234), Is.EqualTo(data), $"{at} save changes on a round trip");
        }

        /// <summary>Saves, loads into a new game (as the app does on launch) and goes on with that one.</summary>
        static Game Reload(Game g, ManualClock clock, bool exact = true)
        {
            var store = new MemorySaveStore();
            store.Save(g.State, g.Content, clock.Now());
            var outcome = store.Load(g.Content);
            Assert.That(outcome.Result, Is.EqualTo(LoadResult.Loaded), outcome.Reason);
            var loaded = new Game(g.Content, outcome.State, clock);
            if (exact) Assert.That(SaveCodec.Encode(loaded.State, g.Content, 1), Is.EqualTo(SaveCodec.Encode(g.State, g.Content, 1)), "loading changed the state");
            return loaded;
        }

        static int runs;

        static void Run(Game g, ManualClock clock, double seconds, string where)
        {
            g.Run(seconds, clock);
            g.DrainNotices();
            // The full check is thorough (and slow): every few steps, the essentials in between.
            if (++runs % 5 == 0) Check(g, where, afterTime: true);
            else
            {
                Assert.That(g.State.Resources.IsValid, Is.True, $"[{where}] resources {g.State.Resources}");
                Assert.That(g.State.People.Count, Is.EqualTo(g.State.Population), $"[{where}] people list vs population");
                Assert.That(g.State.Assignments.Total, Is.LessThanOrEqualTo(g.State.Population), $"[{where}] more workers than people");
            }
        }

        static void RunUntil(Game g, ManualClock clock, Func<bool> done, string where, double step = 30, double limit = 48 * 3600)
        {
            double t = 0;
            while (!done())
            {
                Assert.That(t, Is.LessThan(limit), $"{where}: did not happen within {limit / 3600:0} h");
                Run(g, clock, step, where);
                t += step;
            }
        }

        // MARK: - Helpers

        /// <summary>Content with short timers and roomy stores, so every feature can be reached quickly.</summary>
        static (Game game, ManualClock clock) FastGame(Action<GameContent> more = null) => Fixtures.NewGame(c =>
        {
            c.Balance.BaseStorage = 100_000;
            c.Balance.KnowledgeCap = 100_000;
            foreach (var t in c.Technologies) t.ResearchSeconds = Math.Min(t.ResearchSeconds, 60);
            foreach (var r in c.Regions) r.ExpeditionSeconds = Math.Min(r.ExpeditionSeconds, 60);
            foreach (var b in c.Buildings)
            {
                b.BuildSeconds = Math.Min(b.BuildSeconds, 30);
                foreach (var l in b.Levels) l.BuildSeconds = Math.Min(l.BuildSeconds, 30);
            }
            more?.Invoke(c);
        }, full: true);

        static readonly ResourceBundle Plenty = new ResourceBundle(20_000, 20_000, 20_000, 20_000, 20_000, 20_000, 20_000);

        static IEnumerable<(int x, int y)> CellsByDistance(Game g, int cx, int cy)
        {
            var map = g.Content.Map;
            return Enumerable.Range(0, map.Height).SelectMany(y => Enumerable.Range(0, map.Width).Select(x => (x, y)))
                .OrderBy(p => Math.Abs(p.x - cx) + Math.Abs(p.y - cy)).ThenBy(p => p.y).ThenBy(p => p.x);
        }

        static bool OnlyMoney(IEnumerable<PlacementIssue> issues) =>
            issues.All(i => i.Kind == PlacementIssueKind.Unaffordable || i.Kind == PlacementIssueKind.ConstructionQueueFull);

        static (int x, int y)? CellFor(Game g, string id, int rotation = 0, Func<int, int, bool> where = null)
        {
            var h = g.Heart;
            foreach (var (x, y) in CellsByDistance(g, h?.X ?? 6, h?.Y ?? 7))
                if ((where == null || where(x, y)) && OnlyMoney(g.PlacementIssues(id, x, y, rotation))) return (x, y);
            return null;
        }

        static (int x, int y)? DecorationCellFor(Game g, string id, Func<int, int, bool> where = null)
        {
            var h = g.Heart;
            foreach (var (x, y) in CellsByDistance(g, h?.X ?? 6, h?.Y ?? 7))
                if ((where == null || where(x, y)) && OnlyMoney(g.DecorationIssues(id, x, y))) return (x, y);
            return null;
        }

        static int Builders(Game g)
        {
            // Everyone idle helps building, so the queue moves.
            int n = g.IdleWorkers + g.State.Assignments[JobType.Construction];
            Expect.Success(g.SetWorkers(JobType.Construction, n));
            return n;
        }

        static PlacedBuilding Build(Game g, ManualClock clock, string id, int rotation = 0, Func<int, int, bool> where = null)
        {
            RunUntil(g, clock, () => g.ActiveConstructionCount() == 0, $"queue before {id}");
            var cell = CellFor(g, id, rotation, where);
            Assert.That(cell, Is.Not.Null, $"no place for {id} (rotation {rotation})");
            g.DebugGrant(Plenty);
            var before = g.State.Resources;
            var r = g.PlaceBuilding(id, cell.Value.x, cell.Value.y, rotation);
            Expect.Success(r);
            var def = g.Content.Building(id);
            foreach (var t in ResourceTypes.All)
                Assert.That(g.State.Resources[t], Is.EqualTo(before[t] - def.Cost[t]).Within(1e-6), $"{id} cost {t}");
            var b = g.State.Building(r.Value);
            Assert.That(b.Rotation, Is.EqualTo(rotation));
            Assert.That(b.Status, Is.EqualTo(BuildingStatus.Constructing));
            Check(g, $"placed {id}");
            Builders(g);
            RunUntil(g, clock, () => g.State.Building(b.Id)?.Status == BuildingStatus.Complete, $"building {id}", 10);
            return g.State.Building(b.Id);
        }

        static void LevelUp(Game g, ManualClock clock, PlacedBuilding b, int levels)
        {
            for (int i = 0; i < levels && g.NextLevel(b) != null; i++)
            {
                RunUntil(g, clock, () => g.ActiveConstructionCount() == 0, "queue before level");
                g.DebugGrant(Plenty);
                int level = b.Level;
                var res = g.UpgradeLevel(b.Id);
                Assert.That(res.Ok, Is.True, $"{b.DefinitionId} level {level + 1}: {res}");
                Assert.That(b.Status, Is.EqualTo(BuildingStatus.Upgrading));
                Assert.That(b.IsOperational, Is.True, "a building keeps working while it is raised");
                Expect.Failure(g.UpgradeLevel(b.Id), GameError.BuildingBusy);
                Builders(g);
                RunUntil(g, clock, () => b.Status == BuildingStatus.Complete, $"levelling {b.DefinitionId}", 10);
                Assert.That(b.Level, Is.EqualTo(level + 1));
            }
        }

        /// <summary>Raises the heart to a level with the people, storage, homes and research it asks for.</summary>
        static void RaiseHeart(Game g, ManualClock clock, int target)
        {
            var heart = g.Heart;
            for (int guard = 0; heart.Level < target; guard++)
            {
                Assert.That(guard, Is.LessThan(200), $"the heart is stuck at level {heart.Level}: {string.Join(", ", g.LevelRequirements(heart).Where(x => !x.Met).Select(x => x.Text))}");
                var next = g.NextLevel(heart);
                Assert.That(next, Is.Not.Null);
                var people = next.Requirements.FirstOrDefault(r => r.Kind == ConditionKind.PopulationAtLeast && !g.IsMet(r));
                if (people != null)
                {
                    // Newcomers (they join the people list when the game is loaded).
                    g.State.Population = (int)people.Amount.Value;
                    g = Reloaded(g, clock, exact: false);
                    heart = g.Heart;
                }
                foreach (var r in next.Requirements.Where(r => !g.IsMet(r)))
                {
                    switch (r.Kind)
                    {
                        case ConditionKind.BuildingLevelAtLeast:
                        {
                            var best = g.State.Buildings.Where(b => b.DefinitionId == r.Id).OrderByDescending(b => b.Level).FirstOrDefault()
                                       ?? Build(g, clock, r.Id);
                            // Nothing outgrows the heart, so it can only follow up to the heart's level.
                            LevelUp(g, clock, best, Math.Min((int)r.Amount.Value, heart.Level) - best.Level);
                            break;
                        }
                        case ConditionKind.TechCountAtLeast:
                        case ConditionKind.TechResearched:
                            ResearchAvailable(g, clock, r.Kind == ConditionKind.TechResearched ? r.Id : null, (int)(r.Amount ?? 0));
                            break;
                    }
                }
                var unmet = g.LevelRequirements(heart).Where(x => !x.Met).Select(x => x.Text).ToList();
                if (unmet.Count > 0) continue; // a building level that had to wait for the heart; loop again
                LevelUp(g, clock, heart, 1);
            }
        }

        static Game current;
        static Game Reloaded(Game g, ManualClock clock, bool exact = true) => current = Reload(g, clock, exact);

        static void ResearchAvailable(Game g, ManualClock clock, string wanted, int count)
        {
            while ((wanted != null && !g.State.Researched.Contains(wanted)) || g.State.Researched.Count < count)
            {
                var tech = wanted != null && g.GetResearchStatus(wanted).State == ResearchState.Available
                    ? g.Content.Technology(wanted)
                    : g.Content.Technologies.FirstOrDefault(t => g.GetResearchStatus(t.Id).State == ResearchState.Available);
                Assert.That(tech, Is.Not.Null, $"nothing left to research (want {wanted ?? count.ToString()})");
                Research(g, clock, tech.Id);
            }
        }

        static void Research(Game g, ManualClock clock, string id)
        {
            g.DebugGrant(Plenty);
            var before = g.State.Resources.Knowledge;
            Expect.Success(g.StartResearch(id));
            Assert.That(g.State.Resources.Knowledge, Is.EqualTo(before - g.Content.Technology(id).Cost.Knowledge).Within(1e-6));
            Expect.Failure(g.StartResearch(id), GameError.ResearchInProgress);
            Assert.That(g.GetResearchStatus(id).State, Is.EqualTo(ResearchState.InProgress));
            RunUntil(g, clock, () => g.State.Researched.Contains(id), $"research {id}", 10);
            Expect.Failure(g.StartResearch(id), GameError.ResearchAlreadyDone);
        }

        // MARK: - Every function, one after another

        [Test]
        public void EveryCommandOfTheGameWorks()
        {
            var (g, clock) = FastGame();
            current = g;
            Check(g, "new game");
            Assert.That(g.PendingStory, Is.Not.Null, "a new game opens with the first chapter's scene");
            g.DismissStory();
            Assert.That(g.PendingStory, Is.Null);
            g.DebugGrant(Plenty);

            // Jobs: errors first, then everyone at work.
            foreach (var job in Enums.Jobs) Expect.Success(g.SetWorkers(job, 0));
            Expect.Failure(g.SetWorkers(JobType.Farming, 1), GameError.NoJobSlots(0));
            Expect.Failure(g.SetWorkers(JobType.Construction, g.State.Population + 5), GameError.TooManyWorkers(g.IdleWorkers));
            Expect.Success(g.SetWorkers(JobType.Construction, 0));
            Check(g, "jobs");

            // The first buildings of a camp.
            foreach (var id in new[] { "storage_pit", "foraging_camp", "wood_camp", "stone_site", "hunters_lodge", "shelter", "community_circle", "toolmaker" })
                Build(g, clock, id);
            Assert.That(g.Count("wood_camp"), Is.EqualTo(1));
            Expect.Failure(g.PlaceBuilding("community_circle", 0, 0), GameError.Placement(PlacementIssue.LimitReached(1)));
            Expect.Failure(g.PlaceBuilding("campfire", 6, 7), GameError.Placement(PlacementIssue.NotBuildable));
            Expect.Failure(g.PlaceBuilding("nonsense", 6, 7), GameError.UnknownContent("nonsense"));
            Assert.That(g.PlacementIssues("shelter", -1, 3).Any(i => i.Kind == PlacementIssueKind.OutOfBounds), Is.True);
            Assert.That(g.PlacementIssues("shelter", 40, 40).Any(i => i.Kind == PlacementIssueKind.Unexplored), Is.True);

            // Every job takes workers up to its places and lets them go again.
            foreach (var job in Enums.Jobs)
            {
                int cap = Math.Min(g.JobCapacity(job), g.IdleWorkers + g.State.Assignments[job]);
                Expect.Success(g.SetWorkers(job, cap));
                Check(g, $"workers {job}");
                Expect.Success(g.SetWorkers(job, 0));
            }

            // Explore the whole valley.
            foreach (var region in g.Content.Regions.Where(r => !r.RevealedAtStart).OrderBy(r => r.Cost.Entries.Sum(kv => kv.Value)))
            {
                g.DebugGrant(Plenty);
                Expect.Success(g.StartExpedition(region.Id));
                Expect.Failure(g.StartExpedition(region.Id), GameError.ExpeditionInProgress);
                RunUntil(g, clock, () => g.State.ExploredRegions.Contains(region.Id), $"expedition {region.Id}", 10);
                Expect.Failure(g.StartExpedition(region.Id), GameError.RegionAlreadyExplored);
                ResolveAnything(g);
            }
            Assert.That(g.State.ExploredRegions.Count, Is.EqualTo(g.Content.Regions.Count));

            // Sand: homes, fields and roads on the river's sandy banks.
            Func<int, int, bool> sand = (x, y) => g.Content.Map.Terrain(x, y) == TerrainType.Sand;
            var beachHome = Build(g, clock, "shelter", 0, sand);
            Assert.That(g.Content.Map.Terrain(beachHome.X, beachHome.Y), Is.EqualTo(TerrainType.Sand));
            var sandRoad = DecorationCellFor(g, "dirt_path", sand);
            Assert.That(sandRoad, Is.Not.Null, "a path can be laid on the sand");
            Expect.Success(g.PlaceDecoration("dirt_path", sandRoad.Value.x, sandRoad.Value.y));
            Check(g, "road on sand");

            // Raise the heart all the way (it opens every era's research), then study everything.
            RaiseHeart(g, clock, 10);
            g = current;
            ResolveAnything(g);
            ResearchAvailable(g, clock, "permanent_settlement", 0);
            ResolveAnything(g);
            ClaimWhatCanBeClaimed(g, clock);
            RaiseHeart(g, clock, g.Content.Building(g.Heart.DefinitionId).MaxLevel);
            g = current;
            foreach (var tech in g.Content.Technologies.Where(t => t.Epoch == null || g.Content.Epoch(t.Epoch).Index <= g.Content.Epoch(g.State.EpochId).Index))
                if (!g.State.Researched.Contains(tech.Id)) ResearchAvailable(g, clock, tech.Id, 0);
            // The medieval era: its research must be reachable once the charter is signed.
            g.State.EpochId = g.Content.Epochs.OrderBy(e => e.Index).Last().Id;
            foreach (var tech in g.Content.Technologies)
            {
                if (g.State.Researched.Contains(tech.Id)) continue;
                var missing = g.MissingPrerequisites(tech.Id).Where(m => !g.Content.Technologies.Any(t => t.Name == m)).ToList();
                Assert.That(missing, Is.Empty, $"{tech.Name} can never be studied: {string.Join(", ", missing)}");
                ResearchAvailable(g, clock, tech.Id, 0);
            }
            Assert.That(g.State.Researched.Count, Is.EqualTo(g.Content.Technologies.Count), "every technology can be researched");

            // Every building of every era: placed (turned as well), finished, raised, and the limits hold.
            foreach (var def in g.Content.Buildings.Where(b => b.Buildable))
            {
                int rotation = def.Footprint.Width != def.Footprint.Height ? 1 : 0;
                if (def.MaxCount.HasValue && g.Count(def.Id, true) >= def.MaxCount.Value) continue;
                var b = Build(g, clock, def.Id, rotation);
                Assert.That(g.FootprintOf(b), Is.EqualTo(Game.Rotated(def.Footprint, rotation)));
                LevelUp(g, clock, b, 2);
                Assert.That(g.DisplayName(b), Is.Not.Empty);
                Assert.That(g.SpriteOf(b), Is.Not.Empty);
            }
            foreach (var def in g.Content.Buildings.Where(b => b.Buildable && b.MaxCount.HasValue))
            {
                while (g.Count(def.Id, true) < def.MaxCount.Value) Build(g, clock, def.Id);
                var cell = CellFor(g, "shelter") ?? (0, 0);
                Assert.That(g.PlacementIssues(def.Id, cell.x, cell.y).Any(i => i.Kind == PlacementIssueKind.LimitReached), Is.True, $"{def.Id} limit");
            }

            // In-place upgrades: a tent becomes a woven hut, then a daub house.
            var tent = Build(g, clock, "shelter");
            foreach (var to in new[] { "woven_shelter", "daub_house" })
            {
                g.DebugGrant(Plenty);
                Expect.Success(g.UpgradeBuilding(tent.Id));
                Builders(g);
                RunUntil(g, clock, () => tent.Status == BuildingStatus.Complete, $"upgrade to {to}", 10);
                Assert.That(tent.DefinitionId, Is.EqualTo(to));
                Assert.That(tent.History.Last(), Is.EqualTo(to));
            }
            Expect.Failure(g.UpgradeBuilding(tent.Id), GameError.NotUpgradeable);

            // Demolish and rebuild in the same spot: half the cost comes back, workers leave the job.
            var camp = g.State.Buildings.First(b => b.DefinitionId == "foraging_camp");
            Expect.Success(g.SetWorkers(JobType.Foraging, 0));
            Expect.Success(g.SetWorkers(JobType.Construction, 0));
            Expect.Success(g.SetWorkers(JobType.Foraging, Math.Min(g.JobCapacity(JobType.Foraging), g.IdleWorkers)));
            int foragers = g.State.Assignments[JobType.Foraging];
            var spot = (camp.X, camp.Y);
            Expect.Success(g.DemolishBuilding(camp.Id));
            Check(g, "demolished");
            Assert.That(g.State.Assignments[JobType.Foraging], Is.LessThanOrEqualTo(Math.Min(foragers, g.JobCapacity(JobType.Foraging))));
            Expect.Failure(g.DemolishBuilding(camp.Id), GameError.UnknownContent($"building #{camp.Id}"));
            Expect.Failure(g.DemolishBuilding(g.Heart.Id), GameError.NotUpgradeable);
            g.DebugGrant(Plenty);
            Expect.Success(g.PlaceBuilding("foraging_camp", spot.X, spot.Y));
            // Taking down a building that is still going up.
            var unfinished = g.State.Buildings.Last();
            Assert.That(unfinished.Status, Is.EqualTo(BuildingStatus.Constructing));
            Expect.Success(g.DemolishBuilding(unfinished.Id));
            Check(g, "demolished a site");

            // Roads and every decoration (the bridge over the water).
            foreach (var deco in g.Content.Decorations)
            {
                var cell = DecorationCellFor(g, deco.Id);
                Assert.That(cell, Is.Not.Null, $"no place for {deco.Id}");
                g.DebugGrant(Plenty);
                var r = g.PlaceDecoration(deco.Id, cell.Value.x, cell.Value.y, 1);
                Expect.Success(r);
                Assert.That(g.DecorationAt(cell.Value.x, cell.Value.y).Rotation, Is.EqualTo(1));
                Expect.Failure(g.PlaceDecoration(deco.Id, cell.Value.x, cell.Value.y), GameError.Placement(PlacementIssue.Overlaps));
                Check(g, $"decoration {deco.Id}");
                if (deco.IsBridge) Assert.That(g.IsBridge(cell.Value.x, cell.Value.y), Is.True);
                Expect.Success(g.RemoveDecoration(r.Value));
                Assert.That(g.DecorationAt(cell.Value.x, cell.Value.y), Is.Null);
                Expect.Success(g.PlaceDecoration(deco.Id, cell.Value.x, cell.Value.y));
            }
            Expect.Failure(g.RemoveDecoration(-5), GameError.UnknownContent("decoration #-5"));
            // A road along a workplace speeds it up.
            int roadsBefore = g.RoadCount();
            var lodge = g.State.Buildings.First(b => b.DefinitionId == "hunters_lodge");
            foreach (var (x, y) in new[] { (lodge.X - 1, lodge.Y), (lodge.X + 1, lodge.Y), (lodge.X, lodge.Y - 1), (lodge.X, lodge.Y + 1) })
                if (g.DecorationIssues("dirt_path", x, y).Count == 0) g.PlaceDecoration("dirt_path", x, y);
            if (g.RoadCount() > roadsBefore) Assert.That(g.HasRoadAccess(lodge), Is.True);

            // Gathering: send gatherers, mark trees, and the land runs out and grows back.
            var tree = CellsByDistance(g, 6, 7).First(c => g.NodeAt(c.x, c.y)?.Terrain == TerrainType.Forest && g.IsRevealed(c.x, c.y));
            Expect.Success(g.SendGatherer(tree.x, tree.y));
            Assert.That(g.GatherCells(JobType.Woodcutting).First(), Is.EqualTo(tree));
            Expect.Success(g.ToggleHarvestOrder(tree.x, tree.y));
            Assert.That(g.NodeAt(tree.x, tree.y).Ordered, Is.False);
            Expect.Success(g.ToggleHarvestOrder(tree.x, tree.y));
            var bare = CellsByDistance(g, 6, 7).First(c => g.NodeAt(c.x, c.y) == null);
            Expect.Failure(g.SendGatherer(bare.x, bare.y), GameError.UnknownContent($"no resource at {bare.x},{bare.y}"));
            Check(g, "gathering");

            // Customs: sign what can be signed, one at a time.
            Expect.Failure(g.SignLaw("nonsense"), GameError.UnknownContent("nonsense"));
            foreach (var law in g.Content.Laws)
            {
                var status = g.LawStatusOf(law);
                if (status.State != LawState.Available) continue;
                RunUntil(g, clock, () => g.LawCooldownLeft <= 0, "law cooldown", 120);
                if (g.LawStatusOf(law).State != LawState.Available) continue;
                Expect.Success(g.SignLaw(law.Id));
                Assert.That(g.LawSigned(law.Id), Is.True);
                Assert.That(g.SignLaw(law.Id).Ok, Is.False);
                foreach (var ex in law.Excludes) Assert.That(g.LawStatusOf(g.Content.Law(ex)).State, Is.Not.EqualTo(LawState.Available), $"{ex} after {law.Id}");
                Check(g, $"law {law.Id}");
            }
            Assert.That(g.State.Laws.Count, Is.GreaterThan(5));

            // The stoked heart.
            g.DebugGrant(Plenty);
            if (g.HasWarmth)
            {
                Expect.Success(g.StokeHeart());
                Assert.That(g.IsStoked, Is.True);
                Assert.That(g.StokeHeart().Ok, Is.False);
                RunUntil(g, clock, () => !g.IsStoked, "stoke burns out", 120);
                RunUntil(g, clock, () => g.StokeReadyIn <= 0, "stoke cooldown", 120);
                g.DebugGrant(Plenty);
                Expect.Success(g.StokeHeart());
            }

            // Illness: healers care for the sick until they are well.
            for (int i = 0; i < 3; i++) { g.State.People[i].Sick = true; }
            g = Reloaded(g, clock);
            int healers = Math.Min(g.JobCapacity(JobType.Healing), Math.Max(1, g.IdleWorkers));
            Expect.Success(g.SetWorkers(JobType.Construction, 0));
            Expect.Success(g.SetWorkers(JobType.Healing, Math.Min(healers, g.IdleWorkers)));
            Assert.That(g.TreatedPatients().Count, Is.GreaterThan(0));
            RunUntil(g, clock, () => g.SickCount == 0 || g.State.People.All(p => !p.Sick), "the sick get well", 60);

            // Petitions: a promise kept, and one refused.
            Expect.Failure(g.AnswerPetition(true), GameError.NoPendingEvent);
            ResolveAnything(g);
            g.State.Discontent = 90;
            g.State.LastPetitionSimTime = null;
            for (int i = 0; i < 360 && g.PendingPetition == null; i++) Run(g, clock, 10, "waiting for a petition");
            if (g.PendingPetition != null)
            {
                Expect.Success(g.AnswerPetition(true));
                Assert.That(g.PendingPetition, Is.Null);
                Check(g, "promise");
            }
            else
                Assert.That(g.Content.Petitions.All(p => p.Goal.All(g.IsMet)), Is.True, "discontent ran high but nobody petitioned");

            // Milestones, chapters and eras.
            ClaimWhatCanBeClaimed(g, clock);
            Assert.That(g.State.ReachedMilestones, Does.Contain("stable_village"));
            Assert.That(g.Heart.DefinitionId, Is.EqualTo("village_hearth"));
            Expect.Failure(g.ClaimMilestone("stable_village"), GameError.MilestoneAlreadyReached);
            Assert.That(g.State.Chapter, Is.GreaterThan(1));

            // Welcome back: half a day away, simulated once, never twice.
            var before = g.State.Clone();
            clock.Advance(20 * 3600);
            g.Update();
            Assert.That(g.State.SimTime - before.SimTime, Is.EqualTo(g.Content.Balance.MaxOfflineSeconds).Within(1e-6));
            double simTime = g.State.SimTime;
            g.Update();
            Assert.That(g.State.SimTime, Is.EqualTo(simTime), "time away is simulated only once");
            Check(g, "welcome back", afterTime: true);
            Assert.That(g.State.Deaths, Is.EqualTo(before.Deaths), "nobody dies while the player is away");

            // Load from a save into a new game and keep playing.
            g = Reload(g, clock);
            Run(g, clock, 600, "after loading");
            TestContext.Progress.WriteLine($"FULL PLAY: {g.State.Buildings.Count} buildings, {g.State.Decorations.Count} decorations, " +
                                           $"{g.State.Researched.Count} techs, laws {g.State.Laws.Count}, pop {g.State.Population}, chapter {g.State.Chapter}, " +
                                           $"milestones {string.Join(",", g.State.ReachedMilestones)}, {checks} checks");
        }

        /// <summary>A house built in a clearing keeps its ground: the woods grow back only once it is gone.</summary>
        [Test]
        public void NoForestGrowsBackUnderAHouse()
        {
            var (g, clock) = Fixtures.NewGame(c =>
            {
                c.Balance.ForestWood = 10;
                c.Balance.TreeRegrowSeconds = 600;
                c.Balance.GatherReach = 1;
            });
            var camp = CellFor(g, "wood_camp").Value;
            g.AddCompleted("wood_camp", camp.x, camp.y);
            Expect.Success(g.SetWorkers(JobType.Woodcutting, 3));
            var cell = g.GatherCells(JobType.Woodcutting).First();
            RunUntil(g, clock, () => g.TerrainNow(cell.x, cell.y) == TerrainType.Grass, "clearing", 10);
            Expect.Success(g.SetWorkers(JobType.Woodcutting, 0));
            g.TestGrant(new ResourceBundle(100, 100, 100));
            var home = g.PlaceBuilding("shelter", cell.x, cell.y);
            Expect.Success(home);
            Run(g, clock, 2000, "the clearing's regrow time passes");
            Assert.That(g.TerrainNow(cell.x, cell.y), Is.EqualTo(TerrainType.Grass), "no tree grows through the roof");
            Assert.That(g.GatherCells(JobType.Woodcutting), Does.Not.Contain(cell));
            Check(g, "house in the clearing");
            Expect.Success(g.DemolishBuilding(home.Value));
            Run(g, clock, 10, "house taken down");
            Assert.That(g.TerrainNow(cell.x, cell.y), Is.EqualTo(TerrainType.Forest), "the woods return once the house is gone");
        }

        static void ResolveAnything(Game g)
        {
            for (int i = 0; i < 10 && g.PendingEvent != null; i++)
            {
                var ev = g.PendingEvent;
                g.DebugGrant(Plenty);
                var option = ev.Options.FirstOrDefault(o => g.OptionBlocker(o) == null);
                Assert.That(option, Is.Not.Null, $"no option of {ev.Id} can be chosen");
                Expect.Success(g.ResolveEvent(option.Id));
                Assert.That(g.State.ResolvedEvents[ev.Id], Is.EqualTo(option.Id));
                Check(g, $"event {ev.Id}");
            }
            if (g.PendingPetition != null) Expect.Success(g.AnswerPetition(false));
        }

        static void ClaimWhatCanBeClaimed(Game g, ManualClock clock)
        {
            foreach (var ms in g.Content.Milestones)
            {
                if (!g.CanClaimMilestone(ms.Id)) continue;
                Expect.Success(g.ClaimMilestone(ms.Id));
                Check(g, $"milestone {ms.Id}");
                Assert.That(g.State.StageId, Is.EqualTo(ms.NewStage));
                if (g.PendingStory != null) g.DismissStory();
            }
        }

        // MARK: - A long session with the shipped rules

        /// <summary>
        /// A player who plays the shipped game for a long session: builds, levels, assigns jobs,
        /// answers events and petitions, signs customs, stokes the fire in the cold, sends healers,
        /// lays roads and decorations, demolishes and rebuilds, saves and loads, and comes back
        /// after long absences. Every step must keep the settlement consistent.
        /// </summary>
        [Test]
        public void LongSessionKeepsTheSettlementConsistent()
        {
            var (g, clock) = Fixtures.NewGame(full: true);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var rng = new Random(20261013);
            g.DismissStory();
            const double Tick = 60;
            int ticks = (int)(5 * 3600 / Tick);
            int demolished = 0, roads = 0, laws = 0, stokes = 0, petitions = 0, events = 0, loads = 0, absences = 0;
            for (int tick = 0; tick < ticks; tick++)
            {
                // Decisions
                var ev = g.PendingEvent;
                if (ev != null)
                {
                    var option = ev.Options.Where(o => g.OptionBlocker(o) == null).OrderBy(_ => rng.Next()).First();
                    Expect.Success(g.ResolveEvent(option.Id));
                    events++;
                }
                if (g.PendingPetition != null) { Expect.Success(g.AnswerPetition(rng.Next(3) != 0)); petitions++; }
                foreach (var ms in g.Content.Milestones) if (g.CanClaimMilestone(ms.Id)) Expect.Success(g.ClaimMilestone(ms.Id));
                if (g.PendingStory != null) g.DismissStory();

                // Research and scouts
                if (g.State.ActiveResearch == null)
                {
                    var tech = g.Content.Technologies.FirstOrDefault(t => g.GetResearchStatus(t.Id).State == ResearchState.Available && g.State.Resources.Covers(t.Cost));
                    if (tech != null) Expect.Success(g.StartResearch(tech.Id));
                }
                if (g.State.ActiveExpedition == null)
                {
                    var region = g.Content.Regions.FirstOrDefault(r => !g.State.ExploredRegions.Contains(r.Id) && g.State.Resources.Covers(r.Cost) && g.State.Resources.Food > r.Cost.Food + 40);
                    if (region != null) Expect.Success(g.StartExpedition(region.Id));
                }

                // Customs and fire
                var law = g.LawStatuses().FirstOrDefault(l => l.State == LawState.Available);
                if (law != null && g.LawCooldownLeft <= 0 && rng.Next(4) == 0) { Expect.Success(g.SignLaw(law.Law.Id)); laws++; }
                if (g.HasWarmth && g.Warmth().Cold > 0 && !g.IsStoked && g.StokeReadyIn <= 0 && g.State.Resources.Wood > 80 && g.StokeHeart().Ok) stokes++;

                // Building
                if (g.ActiveConstructionCount() < g.Content.Balance.MaxConcurrentConstructions)
                {
                    var heart = g.Heart;
                    if (heart.Status == BuildingStatus.Complete && g.UpgradeLevel(heart.Id).Ok) { }
                    else
                    {
                        var options = g.Content.Buildings.Where(b => b.Buildable && g.IsUnlocked(b) && g.State.Resources.Covers(b.Cost)
                                                                      && (!b.MaxCount.HasValue || g.Count(b.Id, true) < b.MaxCount.Value)).ToList();
                        var upgradeable = g.State.Buildings.Where(b => b.Status == BuildingStatus.Complete && b.Id != heart.Id && g.NextLevel(b) != null
                                                                      && g.LevelRequirements(b).All(r => r.Met) && g.State.Resources.Covers(g.NextLevel(b).Cost)).ToList();
                        if (upgradeable.Count > 0 && rng.Next(2) == 0) Expect.Success(g.UpgradeLevel(upgradeable[rng.Next(upgradeable.Count)].Id));
                        else if (options.Count > 0)
                        {
                            // Homes while people sleep outside, otherwise anything.
                            var def = g.Warmth().Homeless > 0 && options.Any(o => o.HousingValue > 0) ? options.First(o => o.HousingValue > 0) : options[rng.Next(options.Count)];
                            int rot = rng.Next(4);
                            var cell = CellFor(g, def.Id, rot);
                            if (cell != null)
                            {
                                var r = g.PlaceBuilding(def.Id, cell.Value.x, cell.Value.y, rot);
                                Assert.That(r.Ok, Is.True, $"{def.Id} at {cell}: {r}");
                            }
                        }
                    }
                }
                if (tick % 60 == 30 && g.State.Buildings.Count > 8)
                {
                    // Now and then a building is taken down and put up elsewhere.
                    var victim = g.State.Buildings.Where(b => g.Content.Building(b.DefinitionId).Buildable).OrderBy(_ => rng.Next()).First();
                    Expect.Success(g.DemolishBuilding(victim.Id));
                    demolished++;
                }

                // Roads and decorations
                if (tick % 10 == 5)
                {
                    var deco = g.Content.Decorations.Where(d => g.State.Resources.Covers(d.Cost) && (d.RequiresTech == null || g.State.Researched.Contains(d.RequiresTech)))
                                .OrderBy(_ => rng.Next()).FirstOrDefault();
                    var cell = deco == null ? null : DecorationCellFor(g, deco.Id, (x, y) => rng.Next(3) == 0);
                    if (cell != null) { Expect.Success(g.PlaceDecoration(deco.Id, cell.Value.x, cell.Value.y, rng.Next(4))); roads++; }
                    if (rng.Next(5) == 0 && g.State.Decorations.Count > 0)
                        Expect.Success(g.RemoveDecoration(g.State.Decorations[rng.Next(g.State.Decorations.Count)].Id));
                }

                // Gather what is needed most, heal the sick, build with the rest.
                if (tick % 5 == 0) AssignJobs(g);
                if (tick % 25 == 7)
                {
                    var node = CellsByDistance(g, g.Heart.X, g.Heart.Y).Take(400).Select(c => g.NodeAt(c.x, c.y))
                        .FirstOrDefault(n => n != null && !n.Depleted && g.IsRevealed(n.X, n.Y) && g.JobCapacity(n.Job) > 0);
                    if (node != null) Expect.Success(g.SendGatherer(node.X, node.Y));
                }

                if (tick % 5 == 0) Check(g, $"tick {tick}");
                g.Run(Tick, clock);
                g.DrainNotices();
                Check(g, $"tick {tick} after time", afterTime: true);

                // Close the app and open it again; once in a while after hours away.
                if (tick % 60 == 59)
                {
                    g = Reload(g, clock);
                    loads++;
                }
                if (tick % 100 == 99)
                {
                    clock.Advance(4 * 3600);
                    var store = new MemorySaveStore();
                    store.Save(g.State, g.Content, clock.Now());
                    g = new Game(g.Content, store.Load(g.Content).State, clock);
                    int deaths = g.State.Deaths;
                    g.Update();
                    Assert.That(g.State.Deaths, Is.EqualTo(deaths), "nobody dies while the player is away");
                    Check(g, "welcome back", afterTime: true);
                    absences++;
                }
            }
            var s = g.State;
            TestContext.Progress.WriteLine($"LONG SESSION: {s.SimTime / 3600:0.0} h, heart {g.HeartLevel}, pop {s.Population}, buildings {s.Buildings.Count}, " +
                                           $"decorations {s.Decorations.Count}, techs {s.Researched.Count}, regions {s.ExploredRegions.Count}, laws {s.Laws.Count}, " +
                                           $"events {events}, petitions {petitions}, stokes {stokes}, demolished {demolished}, placed decorations {roads}, " +
                                           $"reloads {loads}, absences {absences}, deaths {s.Deaths}, discontent {s.Discontent:0}, season {g.Season?.Id}, {watch.Elapsed.TotalSeconds:0} s");
            Assert.That(g.HeartLevel, Is.GreaterThan(3), "the session made progress");
            Assert.That(absences, Is.GreaterThanOrEqualTo(2));
            Assert.That(s.Population, Is.GreaterThanOrEqualTo(2));
        }

        static void AssignJobs(Game g)
        {
            foreach (var job in Enums.Jobs) g.SetWorkers(job, 0);
            void Fill(JobType job, int wanted)
            {
                int n = Math.Min(wanted, Math.Min(g.JobCapacity(job), g.IdleWorkers + g.State.Assignments[job]));
                if (n > g.State.Assignments[job]) Expect.Success(g.SetWorkers(job, n));
            }
            Fill(JobType.Healing, (g.SickCount + 2) / 3);
            foreach (var food in new[] { JobType.Foraging, JobType.Hunting, JobType.Fishing, JobType.Farming, JobType.Herding })
                while (g.State.Assignments[food] < g.JobCapacity(food) && g.IdleWorkers > 0 && g.Economy().Net.Food <= 0.1)
                    Expect.Success(g.SetWorkers(food, g.State.Assignments[food] + 1));
            Fill(JobType.Woodcutting, g.Economy().Net.Wood < 0.05 ? 6 : 3);
            Fill(JobType.Research, 2);
            Fill(JobType.Stonecutting, 3);
            Fill(JobType.Mining, 2);
            Fill(JobType.Crafting, 1);
            Fill(JobType.Woodcutting, 8);
            Fill(JobType.Construction, g.IdleWorkers);
        }
    }
}
