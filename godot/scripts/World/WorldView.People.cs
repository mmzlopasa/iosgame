using System.Collections.Generic;
using System.Linq;
using Founders.Art;
using Founders.Core;
using Founders.People;
using Godot;

namespace Founders.World
{
    /// <summary>The villagers: where each person goes for their job, what they do there and the way they walk.</summary>
    public sealed partial class WorldView
    {
        static int RandInt(int n) => n <= 0 ? 0 : (int)(GD.Randi() % (uint)n);

        /// <summary>A flat direction at a compass angle (degrees).</summary>
        static Vector3 Dir(float degrees)
        {
            float a = Mathf.DegToRad(degrees);
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        // ------------------------------------------------------------------ Villagers

        const int MaxVillagers = 30;
        Pathing paths;
        /// <summary>Per villager: false = on the way to work, true = carrying the goods home.</summary>
        readonly Dictionary<int, bool> carrying = new Dictionary<int, bool>();
        /// <summary>Per villager: where in a carrying round they are (see <see cref="HaulTask"/>).</summary>
        readonly Dictionary<int, int> haulLeg = new Dictionary<int, int>();
        /// <summary>
        /// Where each person stands (or is walking to) for their task. Nobody takes a spot within
        /// <see cref="PersonSpace"/> of a spot someone else holds, so people never stack.
        /// Keys: villager index, children 9000 + n.
        /// </summary>
        readonly Dictionary<int, Vector3> spots = new Dictionary<int, Vector3>();
        /// <summary>The tree, rock or bush (nature index) each gatherer works at: one person per tree while there are trees enough.</summary>
        readonly Dictionary<int, int> workTargets = new Dictionary<int, int>();
        /// <summary>The cell the player sent a person to (<see cref="Dispatch"/>): worked next.</summary>
        readonly Dictionary<int, (int x, int y)> dispatched = new Dictionary<int, (int x, int y)>();
        /// <summary>The line each sick person lies along (feet ahead), for the healer kneeling beside them.</summary>
        readonly Dictionary<int, Vector3> lying = new Dictionary<int, Vector3>();
        const float PersonSpace = 0.85f;
        const int ChildKey = 9000;
        const int LyingKey = 20000;

        /// <summary>A villager currently playing the given clip (for close-up views), or null.</summary>
        public Villager FindVillager(string clip, string tool = null) =>
            villagers.FirstOrDefault(v => v != null && v.Clip == clip && (tool == null || v.Tool == tool));

        /// <summary>A villager of the given job currently playing the given clip, or null.</summary>
        public Villager FindWorker(JobType job, string clip)
        {
            var game = store.Game;
            for (int i = 0; i < villagers.Count; i++)
                if (villagers[i] != null && villagers[i].Clip == clip && JobOf(game, i) == job) return villagers[i];
            return null;
        }

        /// <summary>Any adult villager (for portrait close-ups), preferring one standing still.</summary>
        public Villager AnyVillager() =>
            villagers.FirstOrDefault(v => v != null && (v.Clip == "Idle" || v.Clip == "Talk" || v.Clip == "Watch")) ?? villagers.FirstOrDefault(v => v != null);

        Pathing Paths
        {
            get
            {
                var map = store.Game.Content.Map;
                if (paths == null) paths = new Pathing(map.Width, map.Height);
                paths.Refresh(store.Game);
                return paths;
            }
        }

        void UpdateVillagers(Game game)
        {
            int wanted = Mathf.Min(game.State.Population, MaxVillagers);
            while (villagers.Count < wanted)
            {
                int index = villagers.Count;
                var person = index < game.State.People.Count ? game.State.People[index] : null;
                var v = Villager.Spawn(peopleRoot, RandomSpot(game), person?.Id ?? index, person?.Female ?? index % 2 == 1, job: person?.Job);
                v.NextTask = from => TaskFor(index, from);
                v.Ground = Ground;
                villagers.Add(v);
            }
            while (villagers.Count > wanted)
            {
                int last = villagers.Count - 1;
                villagers[last].QueueFree();
                villagers.RemoveAt(last);
                Forget(last);
            }
            // Families: children play around the hearth (one for every six people, up to six).
            int kids = Mathf.Min(6, game.State.Population / 6);
            while (children.Count < kids)
            {
                int n = children.Count;
                var c = Villager.Spawn(peopleRoot, RandomSpot(game), ChildKey + n, n % 2 == 0, child: true);
                c.NextTask = from => WithRoute(ChildTask(n), from);
                c.Ground = Ground;
                children.Add(c);
            }
            while (children.Count > kids)
            {
                int last = children.Count - 1;
                children[last].QueueFree();
                children.RemoveAt(last);
                Forget(ChildKey + last);
            }
            // Survival needs over the heads: hunger when food runs out, cold without firewood.
            string needIcon = game.State.FoodShortage ? "food" : game.Freezing ? "winter" : null;
            for (int i = 0; i < villagers.Count; i++)
            {
                bool sick = i < game.State.People.Count && game.State.People[i].Sick;
                villagers[i].SetNeed(sick ? "sick" : needIcon != null && i % 2 == 0 ? needIcon : null);
            }
        }

        void Forget(int owner)
        {
            spots.Remove(owner);
            workTargets.Remove(owner);
            dispatched.Remove(owner);
            carrying.Remove(owner);
            haulLeg.Remove(owner);
            lying.Remove(owner);
            spots.Remove(LyingKey + owner);
        }

        /// <summary>
        /// The player sent someone to a tree, rock, bush or water: the nearest villager of that job
        /// drops what they are doing and walks there now.
        /// </summary>
        public void Dispatch(int x, int y, JobType job)
        {
            var game = store.Game;
            var at = Grid.CellCenter(x, y);
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < villagers.Count; i++)
            {
                if (villagers[i] == null || JobOf(game, i) != job) continue;
                float d = (villagers[i].Position - at).LengthSquared();
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            carrying[best] = false;
            dispatched[best] = (x, y);
            villagers[best].Interrupt();
        }

        readonly List<Villager> children = new List<Villager>();

        /// <summary>Children run between the houses, play and sit by the fire at night.</summary>
        VillagerTask ChildTask(int n)
        {
            var game = store.Game;
            var hearth = game.Heart;
            var center = hearth != null ? Grid.CellCenter(hearth.X, hearth.Y) : Vector3.Zero;
            var task = new VillagerTask { Duration = R(3f, 7f), WalkClip = "Jog" };
            int owner = ChildKey + n;
            if (Atmosphere.Nightness > 0.55f)
            {
                task.Spot = FreeAround(owner, center, 1.5f, 2.1f);
                task.Face = center;
                task.Action = "Sit";
                task.WalkClip = "Walk";
                task.Duration = R(8f, 14f);
                return task;
            }
            task.Spot = Rv() < 0.5f ? Claim(owner, FreeSpot(owner, RandomSpot(game))) : FreeAround(owner, center, 2.5f, 6f);
            task.Action = Rv() < 0.4f ? "Talk" : Rv() < 0.5f ? "Gather" : "Idle";
            return task;
        }

        // ------------------------------------------------------------------ Villager jobs (people go where their work is, and do it)

        /// <summary>The job of villager #index (the person with that index), or null if idle.</summary>
        static JobType? JobOf(Game game, int index)
        {
            if (index >= game.State.People.Count) return null;
            var id = game.State.People[index].Job;
            if (id == null) return null;
            foreach (var job in Enums.Jobs)
                if (job.Id() == id) return job;
            return null;
        }

        /// <summary>How many people before this one share their job (spreads a crew over the nodes).</summary>
        static int RankInJob(Game game, int index)
        {
            var id = index < game.State.People.Count ? game.State.People[index].Job : null;
            int rank = 0;
            for (int i = 0; i < index && i < game.State.People.Count; i++)
                if (game.State.People[i].Job == id) rank++;
            return rank;
        }

        Vector3 Around(Vector3 center, float radius) =>
            Ground.OnGround(center + Dir(R(0f, 360f)) * radius);

        /// <summary>The point itself if people can stand there, otherwise a spot in the nearest open cell.</summary>
        Vector3 Free(Vector3 p)
        {
            var (x, y) = Grid.CellAt(p);
            if (Paths.Walkable(x, y)) return p;
            var c = Paths.NearestWalkable(x, y);
            if (c == null) return p;
            return Ground.OnGround(Grid.CellCenter(c.Value.x, c.Value.y) + new Vector3(R(-0.5f, 0.5f), 0, R(-0.5f, 0.5f)));
        }

        /// <summary>True when someone other than <paramref name="owner"/> holds a spot too close to <paramref name="p"/>.</summary>
        bool SpotTaken(Vector3 p, int owner, float space = PersonSpace)
        {
            foreach (var kv in spots)
            {
                if (kv.Key == owner) continue;
                float dx = kv.Value.X - p.X, dz = kv.Value.Z - p.Z;
                if (dx * dx + dz * dz < space * space) return true;
            }
            return false;
        }

        /// <summary>Holds a spot for a person until their next task.</summary>
        Vector3 Claim(int owner, Vector3 p)
        {
            spots[owner] = p;
            return p;
        }

        /// <summary>A free open spot near <paramref name="p"/> (not held by anyone, not in a trunk or rock).</summary>
        Vector3 FreeSpot(int owner, Vector3 p)
        {
            var q = Free(p);
            for (int i = 0; i < 10 && (SpotTaken(q, owner) || !nature.Clear(q, 0.3f)); i++)
                q = Free(p + Dir(i * 137.5f) * (0.6f + 0.15f * i));
            return q;
        }

        /// <summary>A free spot on a ring around a point (the hearth, an animal, a patient), held for the person.</summary>
        Vector3 FreeAround(int owner, Vector3 center, float rMin, float rMax)
        {
            var p = Free(Around(center, R(rMin, rMax)));
            for (int i = 0; i < 14 && (SpotTaken(p, owner) || !nature.Clear(p, 0.3f)); i++)
                p = Free(Around(center, R(rMin, rMax)));
            return Claim(owner, p);
        }

        /// <summary>
        /// A spot just outside a building, on the side nearest to <paramref name="from"/>, that nobody
        /// else holds (builders spread around the site, the sick lie side by side).
        /// </summary>
        Vector3 BesideBuilding(PlacedBuilding b, Vector3 from, int owner, float inset = 0.3f)
        {
            var fp = store.Game.FootprintOf(b);
            var options = new List<(float d, int x, int y, int ix, int iy)>();
            for (int y = b.Y - 1; y <= b.Y + fp.Height; y++)
                for (int x = b.X - 1; x <= b.X + fp.Width; x++)
                {
                    bool inside = x >= b.X && x < b.X + fp.Width && y >= b.Y && y < b.Y + fp.Height;
                    bool corner = (x == b.X - 1 || x == b.X + fp.Width) && (y == b.Y - 1 || y == b.Y + fp.Height);
                    if (inside || corner || !Paths.Walkable(x, y)) continue;
                    int ix = Mathf.Clamp(x, b.X, b.X + fp.Width - 1), iy = Mathf.Clamp(y, b.Y, b.Y + fp.Height - 1);
                    options.Add(((Grid.CellCenter(x, y) - from).LengthSquared() + R(0f, 3f), x, y, ix, iy));
                }
            if (options.Count == 0) return Claim(owner, FreeSpot(owner, Grid.FootprintCenter(b.X, b.Y, fp)));
            options.Sort((a, c) => a.d.CompareTo(c.d));
            Vector3 spot = Vector3.Zero;
            foreach (var o in options)
            {
                var stand = Grid.CellCenter(o.x, o.y);
                var edge = Grid.CellCenter(o.ix, o.iy);
                for (int k = 0; k < 3; k++)
                {
                    // Along the wall: anywhere on the cell's side, at arm's length from it.
                    var along = U.Cross(edge - stand, Vector3.Up).Normalized() * R(-0.7f, 0.7f);
                    spot = Ground.OnGround(U.Lerp(stand, edge, inset) + along);
                    if (!SpotTaken(spot, owner) && nature.Clear(spot, 0.3f)) return Claim(owner, spot);
                }
            }
            return Claim(owner, spot);
        }

        VillagerTask TaskFor(int index, Vector3 from) => WithRoute(BuildTask(index, from), from);

        VillagerTask WithRoute(VillagerTask task, Vector3 from)
        {
            if (task.WalkClip == null) task.WalkClip = "Walk";
            task.Route = Route(from, task.Spot, task.Approach);
            return task;
        }

        /// <summary>Where finished goods go: the nearest store (pit, granary, barn, market, the hearth) or the workplace.</summary>
        PlacedBuilding DropPoint(Game game, Vector3 from, PlacedBuilding workplace)
        {
            PlacedBuilding best = workplace;
            float bestD = workplace != null ? (Grid.CellCenter(workplace.X, workplace.Y) - from).LengthSquared() : float.MaxValue;
            foreach (var (b, def) in game.OperationalBuildings())
            {
                if (def.StorageValue <= 0 && !def.Heart) continue;
                float d = (Grid.CellCenter(b.X, b.Y) - from).LengthSquared();
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        /// <summary>What a production job carries in and out (models of the goods).</summary>
        static (string input, string output) Loads(JobType job) => job switch
        {
            JobType.Sawing => ("k_log", "k_planks"),
            JobType.Toolmaking => ("k_planks", "k_box"),
            JobType.Smelting => ("k_stone", "k_box"),
            JobType.Baking => ("k_wood", "wicker_basket_01"),
            JobType.Weaving => ("k_bedroll", "ceramic_pot"),
            JobType.Milling => ("wicker_basket_01", "k_bedroll"),
            _ => (null, null),
        };

        /// <summary>The nearest finished store (or the heart) to a workplace, other than the workplace itself.</summary>
        static PlacedBuilding StoreFor(Game game, PlacedBuilding site)
        {
            PlacedBuilding best = null;
            float bestD = float.MaxValue;
            var at = Grid.CellCenter(site.X, site.Y);
            foreach (var (b, def) in game.OperationalBuildings())
            {
                if (b.Id == site.Id || (def.StorageValue <= 0 && !def.Heart)) continue;
                float d = (Grid.CellCenter(b.X, b.Y) - at).LengthSquared();
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        /// <summary>
        /// One leg of a carrying round between the stores and a workplace: 0 walk to the store, 1 carry the input to
        /// the work, 2 work (the caller's task: returns false), 3 carry the product back to the store. Without an
        /// output the round skips the last leg. False when there is no store or it is time to work.
        /// </summary>
        bool HaulTask(Game game, int index, Vector3 from, PlacedBuilding site, string input, string output, ref VillagerTask task)
        {
            var stores = StoreFor(game, site);
            if (stores == null || input == null) return false;
            haulLeg.TryGetValue(index, out int leg);
            haulLeg[index] = output == null ? (leg + 1) % 3 : (leg + 1) % 4;
            var siteCenter = Grid.FootprintCenter(site.X, site.Y, game.FootprintOf(site));
            switch (leg)
            {
                case 0:
                    task.Spot = BesideBuilding(stores, from, index);
                    task.Face = Grid.CellCenter(stores.X, stores.Y);
                    task.Action = "Gather";
                    task.Duration = 1.4f;
                    return true;
                case 1:
                    task.Spot = BesideBuilding(site, from, index);
                    task.Face = siteCenter;
                    task.Carry = input;
                    task.WalkClip = "Carry";
                    task.Action = "Gather";
                    task.Duration = 1.4f;
                    return true;
                case 3:
                    task.Spot = BesideBuilding(stores, from, index);
                    task.Face = Grid.CellCenter(stores.X, stores.Y);
                    task.Carry = output;
                    task.WalkClip = "Carry";
                    task.Action = "Gather";
                    task.Duration = 1.4f;
                    return true;
                default:
                    return false;
            }
        }

        static bool GathersFromLand(JobType job) =>
            job == JobType.Woodcutting || job == JobType.Stonecutting || job == JobType.Mining || job == JobType.Foraging
            || job == JobType.Hunting || job == JobType.Fishing;

        /// <summary>What a gathering job works at in its cells: trees, rocks, bushes, or the cell itself (water, the woods' edge).</summary>
        static string TargetKind(JobType job) =>
            job == JobType.Woodcutting ? "tree" : job == JobType.Stonecutting || job == JobType.Mining ? "rock" : job == JobType.Foraging ? "bush" : null;

        /// <summary>
        /// How far from the centre of what they work at a person stands, and how far to their right
        /// it is (the clips strike to the right of the body). Measured on the clips: the axe head
        /// holds 0.84 m ahead and 0.23 m right at the moment it bites; the kneeling hammer lands
        /// 0.6 m ahead at knee height; hands pick 0.4-0.5 m ahead.
        /// </summary>
        static (float dist, float side) Reach(string action, string tool, float radius)
        {
            switch (action)
            {
                case "Chop": return (0.84f + radius * 0.4f, 0.23f);
                case "Work": return (radius * 0.85f + (tool == "Prop_Pickaxe" ? 0.68f : 0.52f), 0.12f);
                case "Gather": case "Harvest": case "Plant": return (radius * 0.75f + 0.4f, 0f);
                case "Throw": return (radius + 1.1f, 0f);
                default: return (radius + 0.6f, 0f);
            }
        }

        int pathBudget;

        /// <summary>
        /// A work place for a gatherer: the first tree (rock, bush) of the job's cells that nobody else
        /// works at, with an open cell beside it the person can walk to and room to swing. Sets the
        /// spot (beside it, at the clip's reach), what to face and the open cell the path ends in.
        /// </summary>
        bool GatherAt(int owner, JobType job, List<(int x, int y)> cells, Vector3 from, ref VillagerTask task)
        {
            string kind = TargetKind(job);
            var order = new List<(int x, int y)>(cells.Count + 1);
            if (dispatched.TryGetValue(owner, out var sent)) { order.Add(sent); dispatched.Remove(owner); }
            order.AddRange(cells);
            var reach = new Dictionary<(int, int), bool>();
            var fromCell = Grid.CellAt(from);
            pathBudget = 12;
            for (int pass = 0; pass < 2; pass++)
            {
                int tried = 0;
                foreach (var c in order)
                {
                    if (tried >= 40) break;
                    Vector3 obj;
                    float radius;
                    int index = -1;
                    if (kind != null)
                    {
                        if (!nature.TryTarget(c.x, c.y, kind, out var t)) continue;
                        obj = t.Position;
                        radius = kind == "tree" ? Nature.TrunkRadius : t.Radius;
                        index = t.Index;
                        // First pass: one person per tree, rock or bush.
                        if (pass == 0 && workTargets.Any(kv => kv.Key != owner && kv.Value == index)) continue;
                    }
                    else
                    {
                        obj = Ground.OnGround(Grid.CellCenter(c.x, c.y));
                        radius = job == JobType.Hunting ? 2.2f : 1.0f;
                    }
                    tried++;
                    if (StandAt(owner, task.Action, task.Tool, obj, radius, index, c, from, fromCell, reach, ref task))
                    {
                        if (index >= 0) workTargets[owner] = index;
                        else workTargets.Remove(owner);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Puts the person beside <paramref name="obj"/> (centre on the ground, <paramref name="radius"/>)
        /// at the action's reach, coming from an open neighbour of its cell, facing so the swing lands on it.
        /// </summary>
        bool StandAt(int owner, string action, string tool, Vector3 obj, float radius, int ignore, (int x, int y) cell,
                     Vector3 from, (int x, int y) fromCell, Dictionary<(int, int), bool> reach, ref VillagerTask task)
        {
            var (dist, side) = Reach(action, tool, radius);
            var options = new List<(float d, int x, int y)>();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cell.x + dx, y = cell.y + dy;
                    if ((dx == 0 && dy == 0) || !Paths.Walkable(x, y)) continue;
                    options.Add(((Grid.CellCenter(x, y) - from).LengthSquared(), x, y));
                }
            options.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var o in options)
            {
                if (!reach.TryGetValue((o.x, o.y), out bool ok))
                {
                    // Path searches are the costly part: a dozen per task at most (unknown = not reachable).
                    ok = fromCell == (o.x, o.y) || (pathBudget-- > 0 && Paths.FindPath(fromCell, (o.x, o.y)) != null);
                    reach[(o.x, o.y)] = ok;
                }
                if (!ok) continue;
                var away = Grid.CellCenter(o.x, o.y) - obj;
                away.Y = 0;
                if (away.LengthSquared() < 1e-4f) continue;
                away = away.Normalized();
                foreach (float turn in new[] { 0f, 30f, -30f, 60f, -60f })
                {
                    // Forward: from where they stand towards the work; it sits `side` to their right.
                    var forward = -(new Basis(Vector3.Up, Mathf.DegToRad(turn)) * away);
                    var right = new Vector3(-forward.Z, 0f, forward.X);
                    var stand = Ground.OnGround(obj - forward * dist - right * side);
                    var sc = Grid.CellAt(stand);
                    // Stand in the open, or at the edge of the work's own cell; never in another tree's cell or a building.
                    if (sc != cell && !Paths.Walkable(sc.x, sc.y)) continue;
                    if (SpotTaken(stand, owner) || !nature.Clear(stand, 0.25f, ignore)) continue;
                    task.Spot = Claim(owner, stand);
                    task.Face = stand + forward * 4f;
                    task.Approach = (o.x, o.y);
                    return true;
                }
            }
            return false;
        }

        VillagerTask BuildTask(int index, Vector3 from)
        {
            var game = store.Game;
            var task = new VillagerTask { Action = "Idle", Duration = R(3f, 7f) };
            var hearth = game.Heart;
            Vector3 hearthPos = hearth != null ? Grid.CellCenter(hearth.X, hearth.Y) : Vector3.Zero;
            workTargets.Remove(index);
            lying.Remove(index);
            spots.Remove(LyingKey + index);

            // The sick lie down: under a healer's care beside the hut, otherwise by a home.
            var person = index < game.State.People.Count ? game.State.People[index] : null;
            if (person != null && person.Sick) return SickTask(game, person, index, from, hearthPos, task);

            // Night: everyone gathers around the hearth or rests by the houses.
            if (Atmosphere.Nightness > 0.55f)
            {
                if (index % 3 != 0 || hearth == null)
                {
                    task.Face = hearthPos;
                    task.Action = Rv() < 0.6f ? "Sit" : "Talk";
                    task.Spot = task.Action == "Sit" ? FreeAround(index, hearthPos, 1.9f, 2.5f) : FreeAround(index, hearthPos, 1.6f, 2.8f);
                }
                else
                {
                    task.Spot = Claim(index, FreeSpot(index, RandomSpot(game)));
                    task.Action = "Torch";
                }
                task.Duration = R(6f, 12f);
                return task;
            }

            var sites = game.State.Buildings.Where(b => b.Status != BuildingStatus.Complete).ToList();
            var job = JobOf(game, index);
            // Builders, and idle people while something is being built (Manor Lords: free families build).
            if (sites.Count > 0 && (job == JobType.Construction || (job == null && index % 3 != 2)))
            {
                // Builders fetch timber and stone from the stores now and then (goods are carried, not conjured).
                var buildSite = sites[index % sites.Count];
                if (HaulTask(game, index, from, buildSite, index % 2 == 0 ? "k_planks" : "k_stone", null, ref task)) return task;
                return BuildAt(buildSite, task, from, index);
            }
            if (job == null && index % 2 == 0 && MarketVisit(game, index, ref task)) return task;
            if (job == null)
            {
                task.Spot = FreeAround(index, hearthPos, 1.6f, 2.8f);
                task.Face = hearthPos;
                task.Action = Rv() < 0.5f ? "Talk" : "Watch";
                return task;
            }

            PlacedBuilding site = null;
            var places = game.OperationalBuildings().Where(x => game.SlotsOf(x.building, x.def, job.Value) > 0).Select(x => x.building).ToList();
            if (places.Count > 0) site = places[index % places.Count];
            if (site == null)
            {
                task.Spot = FreeAround(index, hearthPos, 1.8f, 2.6f);
                task.Face = hearthPos;
                task.Action = "Watch";
                return task;
            }
            var fp = game.FootprintOf(site);
            var center = Grid.FootprintCenter(site.X, site.Y, fp);
            task.Duration = R(7f, 11f);

            if (GathersFromLand(job.Value))
            {
                // Gatherers walk to the land they take from, work there, then carry the goods home.
                carrying.TryGetValue(index, out bool home);
                if (home)
                {
                    carrying[index] = false;
                    var drop = DropPoint(game, from, site);
                    task.Spot = BesideBuilding(drop, from, index);
                    task.Face = Grid.CellCenter(drop.X, drop.Y);
                    task.WalkClip = job == JobType.Hunting || job == JobType.Fishing || job == JobType.Foraging ? "Walk" : "Carry";
                    task.Carry = job == JobType.Woodcutting ? "k_wood" : job == JobType.Stonecutting || job == JobType.Mining ? "k_stone"
                        : job == JobType.Foraging ? "wicker_basket_01" : null;
                    task.Action = "Gather";
                    task.Duration = 1.6f;
                    return task;
                }
                // The clip and tool of the job (the reach to the work depends on them).
                switch (job.Value)
                {
                    case JobType.Woodcutting:
                        task.Action = "Chop";
                        task.Tool = "Prop_Axe";
                        break;
                    case JobType.Stonecutting:
                        // Quarriers kneel at the rock and dress stone with the hammer.
                        task.Action = "Work";
                        task.Tool = "Prop_Hammer";
                        break;
                    case JobType.Mining:
                        // Miners kneel at the vein and break ore with the pick.
                        task.Action = "Work";
                        task.Tool = Rv() < 0.6f ? "Prop_Pickaxe" : "Prop_Hammer";
                        break;
                    case JobType.Foraging:
                        task.Action = Rv() < 0.65f ? "Gather" : "Harvest";
                        break;
                    case JobType.Hunting:
                        task.Action = Rv() < 0.6f ? "Throw" : "Watch";
                        task.Tool = "Prop_Spear";
                        task.WalkClip = Rv() < 0.4f ? "Jog" : "Walk";
                        break;
                    default: // fishing at the water's edge
                        task.Action = Rv() < 0.7f ? "Throw" : "Idle";
                        task.Tool = "Prop_Spear";
                        break;
                }
                var cells = game.GatherCells(job.Value);
                if (cells.Count > 0 && GatherAt(index, job.Value, cells, from, ref task))
                {
                    carrying[index] = true;
                    return task;
                }
                // Nothing within reach to work: wait at the workplace.
                task.Tool = null;
                task.Spot = BesideBuilding(site, from, index);
                task.Face = center;
                task.Action = "Watch";
                task.WalkClip = "Walk";
                return task;
            }

            switch (job.Value)
            {
                case JobType.Farming:
                {
                    // Each farmer works their own rows of the field, along the furrows.
                    int rank = RankInJob(game, index);
                    int cellsCount = fp.Width * fp.Height;
                    var rows = U.Yaw(site.Rotation * 90f) * Vector3.Forward;
                    for (int k = 0; k < cellsCount * 2; k++)
                    {
                        int c = (rank + k) % cellsCount;
                        var tile = Grid.CellCenter(site.X + c % fp.Width, site.Y + c / fp.Width);
                        var spot = Ground.OnGround(tile + new Vector3(R(-0.6f, 0.6f), 0, R(-0.6f, 0.6f)));
                        task.Spot = spot;
                        if (!SpotTaken(spot, index)) break;
                    }
                    Claim(index, task.Spot);
                    task.Face = task.Spot + rows * (Rv() < 0.5f ? 3f : -3f);
                    string season = game.Season?.Id ?? "summer";
                    task.Action = season == "spring" ? "Plant" : season == "summer" ? (Rv() < 0.5f ? "Water" : "Plant")
                        : season == "autumn" ? "Harvest" : "Gather";
                    return task;
                }
                case JobType.Herding:
                {
                    // Herders walk among the animals with their staff, watch one and lend a hand.
                    task.Tool = "Prop_Spear";
                    task.Action = Rv() < 0.5f ? "Watch" : Rv() < 0.5f ? "Gather" : "Idle";
                    if (herds.TryGetValue(site.Id, out var herd) && herd.animals != null && herd.animals.Count > 0)
                    {
                        var animal = herd.animals[(RankInJob(game, index) + RandInt(herd.animals.Count)) % herd.animals.Count];
                        if (animal != null)
                        {
                            var at = animal.Position;
                            task.Spot = FreeAround(index, at, 1.3f, 1.8f);
                            task.Face = at;
                            return task;
                        }
                    }
                    task.Spot = Claim(index, Ground.OnGround(center + new Vector3(R(-0.8f, 0.8f) * fp.Width, 0, R(-0.8f, 0.8f) * fp.Height)));
                    return task;
                }
                case JobType.Healing:
                    return HealTask(game, index, site, from, center, task);
                case JobType.Smelting:
                case JobType.Sawing:
                case JobType.Toolmaking:
                case JobType.Baking:
                case JobType.Weaving:
                case JobType.Milling:
                {
                    // A working production chain moves its goods by hand: inputs from the stores, the product back.
                    var (input, output) = Loads(job.Value);
                    if (game.Processing(job.Value).State == ProcessingState.Working && HaulTask(game, index, from, site, input, output, ref task))
                        return task;
                    task.Spot = BesideBuilding(site, from, index);
                    task.Face = center;
                    task.Action = "Work";
                    task.Tool = "Prop_Hammer";
                    return task;
                }
                case JobType.Crafting:
                    task.Spot = BesideBuilding(site, from, index);
                    task.Face = center;
                    task.Action = "Work";
                    task.Tool = "Prop_Hammer";
                    return task;
                default:
                    task.Spot = BesideBuilding(site, from, index);
                    task.Face = center;
                    task.Action = "Talk";
                    return task;
            }
        }

        /// <summary>Where a customer stands at the market's four stalls (local to its 6×4 m square), and the stall they face.</summary>
        static readonly (Vector3 stand, Vector3 stall)[] MarketSpots =
        {
            (new Vector3(-1.45f, 0f, 0.25f), new Vector3(-1.45f, 0f, 1.15f)), (new Vector3(1.45f, 0f, 0.25f), new Vector3(1.45f, 0f, 1.15f)),
            (new Vector3(-1.6f, 0f, -0.55f), new Vector3(-2.3f, 0f, -0.55f)), (new Vector3(1.6f, 0f, -0.55f), new Vector3(2.3f, 0f, -0.55f)),
        };

        /// <summary>Free people go shopping: they stand at a market stall, chatting with the seller, and carry a basket home.</summary>
        bool MarketVisit(Game game, int index, ref VillagerTask task)
        {
            var markets = game.OperationalBuildings().Where(x => (x.def.MarketReach ?? 0) > 0).Select(x => x.building).ToList();
            if (markets.Count == 0) return false;
            var m = markets[(index / 2) % markets.Count];
            var center = Grid.FootprintCenter(m.X, m.Y, game.FootprintOf(m));
            var turn = U.Yaw(m.Rotation * 90f);
            var (stand, stall) = MarketSpots[RandInt(MarketSpots.Length)];
            var jitter = new Vector3(R(-0.35f, 0.35f), 0f, R(-0.1f, 0.1f));
            task.Spot = Claim(index, Ground.OnGround(center + turn * U.ToGodot(stand + jitter)));
            task.Face = Ground.OnGround(center + turn * U.ToGodot(stall));
            task.Action = Rv() < 0.6f ? "Talk" : "Idle";
            task.Duration = R(5f, 10f);
            return true;
        }

        /// <summary>
        /// The sick lie on the ground beside the healer's hut when cared for, otherwise beside a home,
        /// along the wall (head and feet parallel to it), until they are well.
        /// </summary>
        VillagerTask SickTask(Game game, Person person, int index, Vector3 from, Vector3 hearthPos, VillagerTask task)
        {
            PlacedBuilding bed = null;
            if (game.TreatedPatients().Contains(person.Id))
                bed = game.OperationalBuildings().Where(x => x.def.Slots(JobType.Healing) > 0).Select(x => x.building).FirstOrDefault();
            if (bed == null)
            {
                var homes = game.OperationalBuildings().Where(x => game.HousingOf(x.building, x.def) > 0).Select(x => x.building).ToList();
                if (homes.Count > 0) bed = homes[person.Id % homes.Count];
            }
            Vector3 toward;
            if (bed != null)
            {
                // Lying takes room: keep 1.6 m from the next person.
                task.Spot = BesideBuilding(bed, from, index, 0.15f);
                toward = Grid.FootprintCenter(bed.X, bed.Y, game.FootprintOf(bed));
                for (int i = 0; i < 6 && SpotTaken(task.Spot, index, 1.6f); i++) task.Spot = BesideBuilding(bed, from, index, 0.15f);
            }
            else
            {
                task.Spot = FreeAround(index, hearthPos, 2.5f, 3.5f);
                toward = hearthPos;
            }
            var wall = toward - task.Spot;
            wall.Y = 0;
            // Facing along the wall: the body lies parallel to it.
            var line = wall.LengthSquared() > 1e-4f ? U.Cross(Vector3.Up, wall.Normalized()) : Vector3.Forward;
            task.Face = task.Spot + line * 3f;
            lying[index] = line;
            spots[LyingKey + index] = task.Spot - line * 0.8f; // the head end of the body is taken too
            task.Action = "Lie";
            task.Duration = R(14f, 22f);
            return task;
        }

        /// <summary>Healers kneel beside a patient and tend them; with nobody sick they brew at the hut.</summary>
        VillagerTask HealTask(Game game, int index, PlacedBuilding hut, Vector3 from, Vector3 center, VillagerTask task)
        {
            var sick = new List<int>();
            for (int i = 0; i < villagers.Count && i < game.State.People.Count; i++)
                if (game.State.People[i].Sick && villagers[i] != null) sick.Add(i);
            if (sick.Count > 0)
            {
                int patient = sick[(RankInJob(game, index) + RandInt(sick.Count)) % sick.Count];
                // Where the patient lies (or is walking to lie down), and along which line.
                var at = spots.TryGetValue(patient, out var p) ? p : villagers[patient].Position;
                var along = lying.TryGetValue(patient, out var l) ? l : -villagers[patient].GlobalBasis.Z;
                along.Y = 0;
                along = along.LengthSquared() > 1e-4f ? along.Normalized() : Vector3.Forward;
                // The "Lie" pose: feet 0.6 m ahead of the spot, hips 0.25 m and head 0.9 m behind it.
                var chest = at - along * 0.5f;
                var right = new Vector3(-along.Z, 0f, along.X);
                task.Spot = Vector3.Zero;
                foreach (var s in new[] { 1f, -1f })
                {
                    var kneel = Ground.OnGround(chest + right * (0.7f * s));
                    var (kx, ky) = Grid.CellAt(kneel);
                    if (!Paths.Walkable(kx, ky) || SpotTaken(kneel, index, 0.6f)) continue;
                    task.Spot = Claim(index, kneel);
                    break;
                }
                if (task.Spot == Vector3.Zero) task.Spot = FreeAround(index, chest, 0.75f, 0.95f);
                task.Face = chest;
                task.Action = "Work";
                task.Tool = null;
                task.Duration = R(8f, 12f);
                return task;
            }
            task.Spot = BesideBuilding(hut, from, index);
            task.Face = center;
            task.Action = Rv() < 0.5f ? "Gather" : "Idle";
            return task;
        }

        /// <summary>One or two people stand at each building site and hammer, kneel and fit parts.</summary>
        VillagerTask BuildAt(PlacedBuilding site, VillagerTask task, Vector3 from, int owner)
        {
            var fp = store.Game.FootprintOf(site);
            bool kneel = Rv() < 0.5f;
            task.Spot = BesideBuilding(site, from, owner, kneel ? 0.4f : 0.3f);
            task.Face = Grid.FootprintCenter(site.X, site.Y, fp);
            task.Action = kneel ? "Work" : "Chop";
            task.Tool = "Prop_Hammer";
            task.Duration = R(6f, 10f);
            return task;
        }

        // ------------------------------------------------------------------ Walking around things

        /// <summary>
        /// Waypoints from <paramref name="from"/> to <paramref name="to"/> around buildings, trees,
        /// rocks and water (over bridges), preferring roads; null when the way is straight and clear.
        /// With <paramref name="approach"/>, the path ends in that open cell (the side of a tree or
        /// rock the person works from) and the last steps go straight from there to the spot.
        /// </summary>
        List<Vector3> Route(Vector3 from, Vector3 to, (int x, int y)? approach = null)
        {
            var p = Paths;
            var a = Grid.CellAt(from);
            var b = approach ?? Grid.CellAt(to);
            var points = new List<Vector3>();
            if (a != b)
            {
                var cells = p.FindPath(a, b);
                if (cells != null)
                {
                    cells = p.Straighten(cells);
                    for (int i = 1; i < cells.Count - 1; i++)
                    {
                        var (x, y) = cells[i];
                        float j = p.IsRoad(x, y) ? 0.15f : 0.35f;
                        points.Add(Ground.OnGround(Grid.CellCenter(x, y) + new Vector3(R(-j, j), 0, R(-j, j))));
                    }
                    // The last open cell before the spot (the spot itself may stand at a tree or inside a field).
                    var last = cells[cells.Count - 1];
                    if (last != Grid.CellAt(to) || approach != null) points.Add(Ground.OnGround(Grid.CellCenter(last.x, last.y)));
                }
            }
            else if (approach != null && Grid.CellAt(to) != b)
            {
                points.Add(Ground.OnGround(Grid.CellCenter(b.x, b.y)));
            }
            return points.Count > 0 ? points : null;
        }

        Vector3 RandomSpot(Game game)
        {
            var list = game.State.Buildings;
            if (list.Count == 0) return Vector3.Zero;
            var b = list[RandInt(list.Count)];
            var fp = game.FootprintOf(b);
            var center = Grid.FootprintCenter(b.X, b.Y, fp);
            // Stand around the building, never on it (the hearth sits in the middle of its cell).
            float reach = Grid.CellSize * (0.55f + 0.5f * Mathf.Max(fp.Width, fp.Height)) + R(0f, 0.6f);
            var offset = Dir(R(0f, 360f)) * reach;
            return Free(Ground.OnGround(center + offset));
        }
    }
}
