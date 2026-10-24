using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// Zones painted with a brush (Manor Lords' burgage plots and work areas). Homes zones: when the homes are
    /// nearly full, the villagers build a new home there by themselves (the best one the town can afford), nearest
    /// the heart and along the paths first. Woodcutting zones: the woodcutters fell those trees first. Forestry
    /// zones: no one cuts there and felled trees grow back twice as fast. Pastures: every few cells of pasture
    /// give the herders one more place to work.
    /// </summary>
    public sealed partial class Game
    {
        public const string HomesZone = "homes", WoodcuttingZone = "woodcutting", ForestryZone = "forestry", PastureZone = "pasture";
        public static readonly string[] ZoneIds = { HomesZone, WoodcuttingZone, ForestryZone, PastureZone };

        public bool HasZones => Content.Balance.Zones != null;

        /// <summary>Changes whenever a zone is painted or cleared (for views and caches).</summary>
        public int ZonesVersion { get; private set; }

        double zoneClock;

        public string ZoneAt(int x, int y) => State.Zones.TryGetValue(CellKey(x, y), out var z) ? z : null;

        public int ZoneCells(string zone) => State.Zones.Values.Count(z => z == zone);

        /// <summary>Paints a zone on a cell (null clears it).</summary>
        public GameResult SetZone(int x, int y, string zone)
        {
            Update();
            if (!HasZones) return GameError.NotReady("Zones are not available.");
            if (zone != null && !ZoneIds.Contains(zone)) return GameError.UnknownContent(zone);
            var map = Content.Map;
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return GameError.NotReady("Outside the map.");
            int key = CellKey(x, y);
            if (zone == null)
            {
                if (State.Zones.Remove(key)) ZonesVersion++;
                return GameResult.Success;
            }
            if (!IsRevealed(x, y)) return GameError.NotReady("Explore this land first.");
            var t = Content.Map.Terrain(x, y);
            bool land = t != TerrainType.Water && t != TerrainType.Mountain;
            bool open = land && t != TerrainType.Rock && t != TerrainType.Ore && TerrainNow(x, y) != TerrainType.Forest;
            bool ok = zone == WoodcuttingZone || zone == ForestryZone ? land : open;
            if (!ok) return GameError.NotReady(zone == HomesZone ? "Homes need open ground." : "Pastures need open grass.");
            if (State.Zones.TryGetValue(key, out var was) && was == zone) return GameResult.Success;
            State.Zones[key] = zone;
            ZonesVersion++;
            return GameResult.Success;
        }

        /// <summary>Herding places from the painted pastures (only once the town keeps animals).</summary>
        int PastureSlots(JobType job)
        {
            if (job != JobType.Herding || !HasZones || State.Zones.Count == 0) return 0;
            int per = Content.Balance.Zones.PastureCellsPerHerder;
            if (per <= 0) return 0;
            if (pastureKey != (ZonesVersion, State.Zones.Count, State))
            {
                pastureKey = (ZonesVersion, State.Zones.Count, State);
                pastureCells = ZoneCells(PastureZone);
            }
            if (pastureCells < per) return 0;
            bool herds = OperationalBuildings().Any(p => p.def.Slots(JobType.Herding) > 0);
            return herds ? pastureCells / per : 0;
        }

        (int, int, object) pastureKey;
        int pastureCells;

        /// <summary>Gathering order: woodcutting zones first, nothing from forestry zones.</summary>
        int ZoneGatherRank(JobType job, int key)
        {
            if (!State.Zones.TryGetValue(key, out var z)) return 1;
            if (job == JobType.Woodcutting && z == WoodcuttingZone) return 0;
            return 1;
        }

        bool ZoneProtects(JobType job, int key) =>
            job == JobType.Woodcutting && State.Zones.TryGetValue(key, out var z) && z == ForestryZone;

        double ZoneRegrowFactor(int cell) =>
            HasZones && State.Zones.TryGetValue(cell, out var z) && z == ForestryZone ? Math.Max(1, Content.Balance.Zones.ForestryRegrowMultiplier) : 1;

        /// <summary>The home the villagers would build now: the roomiest one unlocked and affordable.</summary>
        public BuildingDefinition ZoneHome() =>
            Content.Buildings.Where(d => d.Buildable && d.HousingValue > 0 && !d.Heart && IsUnlocked(d) && State.Resources.Covers(d.Cost)
                                         && !(d.MaxCount.HasValue && Count(d.Id, true) >= d.MaxCount.Value))
                .OrderByDescending(d => d.HousingValue).ThenBy(d => ResourceTypes.All.Sum(t => d.Cost[t])).FirstOrDefault();

        /// <summary>Once a game minute: villagers build a home in a homes zone when the homes are nearly full.</summary>
        void AdvanceZones(double dt)
        {
            var cfg = Content.Balance.Zones;
            if (cfg == null) return;
            zoneClock += dt;
            if (zoneClock < cfg.CheckSeconds) return;
            zoneClock = 0;
            if (State.Zones.Count == 0 || HousingCapacity() - State.Population > cfg.FreeBeds) return;
            // One home at a time: wait while a home is going up.
            foreach (var b in State.Buildings)
                if (b.Status != BuildingStatus.Complete && (Content.Building(b.DefinitionId)?.HousingValue ?? 0) > 0) return;
            var def = ZoneHome();
            if (def == null) return;
            var spot = ZoneSpot(def);
            if (spot == null) return;
            var placed = PlaceBuildingNow(def.Id, spot.Value.x, spot.Value.y);
            if (placed.Ok) Notice(NoticeKind.Info, $"Villagers started a {def.Name} in the homes zone.");
        }

        /// <summary>Where a home fits wholly in a homes zone: nearest the heart, beside a path first.</summary>
        (int x, int y)? ZoneSpot(BuildingDefinition def)
        {
            var heart = Heart;
            int w = Content.Map.Width;
            var candidates = State.Zones.Where(kv => kv.Value == HomesZone).Select(kv => (x: kv.Key % w, y: kv.Key / w))
                .OrderBy(c => heart == null ? 0 : Math.Abs(c.x - heart.X) + Math.Abs(c.y - heart.Y)).ThenBy(c => c.y).ThenBy(c => c.x).ToList();
            (int x, int y)? fallback = null;
            foreach (var (x, y) in candidates)
            {
                bool inside = true;
                for (int dy = 0; dy < def.Footprint.Height && inside; dy++)
                    for (int dx = 0; dx < def.Footprint.Width && inside; dx++)
                        if (ZoneAt(x + dx, y + dy) != HomesZone) inside = false;
                if (!inside || PlacementIssues(def.Id, x, y).Count > 0) continue;
                bool lane = false;
                for (int yy = y - 1; yy <= y + def.Footprint.Height && !lane; yy++)
                    for (int xx = x - 1; xx <= x + def.Footprint.Width && !lane; xx++)
                        if (IsTrodden(xx, yy)) lane = true;
                if (lane) return (x, y);
                fallback ??= (x, y);
            }
            return fallback;
        }
    }
}
