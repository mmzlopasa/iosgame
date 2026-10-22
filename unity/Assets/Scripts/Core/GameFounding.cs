using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// Founding: a new game starts with the band on the move. The player chooses where the
    /// campfire burns, then where the first shelters stand; the land around the fire (the home
    /// clearing) is explored from then on and the rest of the valley waits for the scouts.
    /// </summary>
    public sealed partial class Game
    {
        /// <summary>Shelters of a new settlement, around the cell the player chooses.</summary>
        public static readonly (int dx, int dy)[] SettlementPattern = { (0, 0), (2, 0), (1, 2) };
        /// <summary>How far (cells) the first shelters may stand from the campfire: within its warmth.</summary>
        public const int SettlementReach = 5;

        /// <summary>A new game in which the player still chooses where to settle (see <see cref="Found"/>).</summary>
        public static Game NewUnfoundedGame(GameContent content, IGameClock clock, string settlementName = "Riverside Camp")
        {
            var game = NewGame(content, clock, settlementName, false);
            return game;
        }

        public RegionDefinition HomeRegion => Content.Regions.FirstOrDefault(r => r.Home);

        /// <summary>Where a region lies: the home clearing follows the campfire, the others are fixed.</summary>
        public (int x, int y, int w, int h) RegionRect(RegionDefinition r)
        {
            if (r.Home && State.HomeX >= 0 && State.HomeY >= 0) return (State.HomeX, State.HomeY, r.Width, r.Height);
            return (r.X, r.Y, r.Width, r.Height);
        }

        public bool RegionContains(RegionDefinition r, int x, int y)
        {
            var (rx, ry, rw, rh) = RegionRect(r);
            return x >= rx && x < rx + rw && y >= ry && y < ry + rh;
        }

        /// <summary>The cell where the campfire stands (or would stand), for the camera and the scouts.</summary>
        public (int x, int y) CampCell()
        {
            var fire = State.Buildings.FirstOrDefault(b => Content.Building(b.DefinitionId)?.Id == "campfire");
            if (fire != null) return (fire.X, fire.Y);
            var sb = Content.Map.StartBuildings.FirstOrDefault();
            return sb != null ? (sb.X, sb.Y) : (Content.Map.Width / 2, Content.Map.Height / 2);
        }

        /// <summary>Can people live on this cell at all (water and bare mountain cannot be settled)?</summary>
        public bool IsSettleable(int x, int y)
        {
            var t = Content.Map.Terrain(x, y);
            return t.HasValue && t != TerrainType.Water && t != TerrainType.Mountain;
        }

        /// <summary>Why the campfire cannot burn here (null = it can).</summary>
        public string CampfireIssue(int x, int y)
        {
            var map = Content.Map;
            if (x < 1 || y < 1 || x >= map.Width - 1 || y >= map.Height - 1) return "Too close to the edge of the valley.";
            if (!IsSettleable(x, y)) return Content.Map.Terrain(x, y) == TerrainType.Water ? "A fire cannot burn on water." : "Too steep: the mountain cannot be settled.";
            // Room for the first shelters around the fire.
            int room = 0;
            for (int yy = y - SettlementReach; yy <= y + SettlementReach; yy++)
                for (int xx = x - SettlementReach; xx <= x + SettlementReach; xx++)
                    if ((xx != x || yy != y) && IsSettleable(xx, yy)) room++;
            if (room < 12) return "Not enough dry land around this spot for the shelters.";
            return null;
        }

        /// <summary>The cells the first shelters would take when the settlement is placed at (x, y).</summary>
        public List<(int x, int y)> SettlementCells(int x, int y) => SettlementPattern.Select(p => (x + p.dx, y + p.dy)).ToList();

        /// <summary>Why the first shelters cannot stand here with the campfire at (fx, fy) (null = they can).</summary>
        public string SettlementIssue(int fx, int fy, int x, int y)
        {
            var map = Content.Map;
            foreach (var (cx, cy) in SettlementCells(x, y))
            {
                if (cx < 0 || cy < 0 || cx >= map.Width || cy >= map.Height) return "Too close to the edge of the valley.";
                if (cx == fx && cy == fy) return "The shelters cannot stand in the fire.";
                if (!IsSettleable(cx, cy)) return "Shelters need dry, level ground.";
                if (Math.Max(Math.Abs(cx - fx), Math.Abs(cy - fy)) > SettlementReach) return "Too far from the fire: the shelters must stay within its warmth.";
            }
            return null;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>
        /// Founds the settlement: the campfire at (fx, fy), the first shelters around (sx, sy). The
        /// land around the fire becomes the explored home clearing and time starts to run.
        /// </summary>
        public GameResult Found(int fx, int fy, int sx, int sy)
        {
            if (State.Founded) return GameError.Placement(PlacementIssue.LimitReached(1));
            var issue = CampfireIssue(fx, fy) ?? SettlementIssue(fx, fy, sx, sy);
            if (issue != null) return GameError.Founding(issue);
            PlaceStart(fx, fy, sx, sy);
            State.LastUpdateWallTime = clock.Now();
            var traits = string.Join(", ", Content.Map.Traits);
            AddLegacy("founding", $"{State.SettlementName} is founded",
                $"A small band lights its fire in the valley ({traits}). This place will shape everything built here.", Content.Map.Name);
            BeginStory();
            Notice(NoticeKind.Success, $"{State.SettlementName} is founded. The land around the fire is yours to build on.");
            return GameResult.Success;
        }

        /// <summary>The map's suggested start: the campfire and the shelters as content.json lists them.</summary>
        void FoundAtSuggestedSpot()
        {
            var fire = Content.Map.StartBuildings.FirstOrDefault(b => b.BuildingId == "campfire");
            int fx = fire?.X ?? Content.Map.Width / 2, fy = fire?.Y ?? Content.Map.Height / 2;
            SetHome(fx, fy);
            foreach (var sb in Content.Map.StartBuildings) AddStartBuilding(sb.BuildingId, sb.X, sb.Y);
            State.Founded = true;
        }

        void PlaceStart(int fx, int fy, int sx, int sy)
        {
            SetHome(fx, fy);
            AddStartBuilding("campfire", fx, fy);
            foreach (var (cx, cy) in SettlementCells(sx, sy)) AddStartBuilding("shelter", cx, cy);
            State.Founded = true;
            revealedGrid = null;
            gatherCache.Clear();
            SyncPeople();
        }

        void SetHome(int fx, int fy)
        {
            var home = HomeRegion;
            if (home == null) return;
            State.HomeX = Clamp(fx - home.Width / 2, 0, Math.Max(0, Content.Map.Width - home.Width));
            State.HomeY = Clamp(fy - home.Height / 2, 0, Math.Max(0, Content.Map.Height - home.Height));
            if (!State.ExploredRegions.Contains(home.Id)) State.ExploredRegions.Insert(0, home.Id);
        }

        void AddStartBuilding(string id, int x, int y)
        {
            var def = Content.Building(id);
            if (def == null) return;
            State.Buildings.Add(new PlacedBuilding
            {
                Id = State.NextBuildingId, DefinitionId = def.Id, X = x, Y = y,
                Status = BuildingStatus.Complete, RemainingWork = 0, TotalWork = def.BuildSeconds,
                History = new List<string> { def.Id }, FoundedAtSimTime = State.SimTime,
            });
            State.NextBuildingId += 1;
        }

        // MARK: - Lands to explore

        /// <summary>Lands the scouts have explored (the home clearing does not count).</summary>
        public int LandsExplored => State.ExploredRegions.Count(id => !(Content.Region(id)?.Home ?? false));

        /// <summary>Distance in lands (16-cell steps) from the home clearing to a region.</summary>
        public int RegionDistance(RegionDefinition r)
        {
            var home = HomeRegion;
            if (home == null || r.Home) return 0;
            var (hx, hy, hw, hh) = RegionRect(home);
            var (rx, ry, rw, rh) = RegionRect(r);
            double cx = hx + hw / 2.0, cy = hy + hh / 2.0;
            double dx = Math.Max(0, Math.Max(rx - cx, cx - (rx + rw))), dy = Math.Max(0, Math.Max(ry - cy, cy - (ry + rh)));
            double size = Math.Max(1, Math.Max(rw, rh));
            return 1 + (int)Math.Floor(Math.Max(dx, dy) / size);
        }

        /// <summary>Lands further from the camp take longer to reach and need more supplies.</summary>
        double DistanceFactor(RegionDefinition r) => 1 + 0.5 * Math.Max(0, RegionDistance(r) - 1);

        public ResourceBundle ExpeditionCost(RegionDefinition r)
        {
            var cost = r.Cost * DistanceFactor(r);
            foreach (var t in ResourceTypes.All) cost[t] = Math.Round(cost[t]);
            return cost;
        }

        public double ExpeditionSeconds(RegionDefinition r) => r.ExpeditionSeconds * DistanceFactor(r);

        /// <summary>True when the region borders land that is already explored (scouts go outward, step by step).</summary>
        public bool IsFrontier(RegionDefinition r)
        {
            if (State.ExploredRegions.Contains(r.Id)) return false;
            var (rx, ry, rw, rh) = RegionRect(r);
            // The region itself (the home clearing may lie inside it) and a band two cells wide around
            // it: the home clearing (15 cells) does not fill a whole land, so a one-cell gap may lie
            // between it and the land next door.
            for (int y = ry - 2; y <= ry + rh + 1; y++)
                for (int x = rx - 2; x <= rx + rw + 1; x++)
                    if (RevealedFast(x, y)) return true;
            return false;
        }
    }
}
