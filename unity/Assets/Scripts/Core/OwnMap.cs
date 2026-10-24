using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Founders.Core
{
    /// <summary>
    /// The player's own hand-made valley (Custom/Map/Map.png or Map.txt): 128 × 128 cells in the map
    /// letters (<c>.</c> meadow, <c>T</c> forest, <c>R</c> stony hill, <c>~</c> water, <c>,</c> sand,
    /// <c>M</c> mountain, <c>O</c> ore). New games start on it; its lands to explore are cut from it the
    /// way the generator cuts them, and the save keeps only its name.
    /// </summary>
    public static class OwnMap
    {
        public const string Name = "own_valley";
        public const int Size = MapGenerator.Size;
        const int LandSeed = 4242;

        /// <summary>
        /// A valley from rows of map letters. Missing rows and cells are meadow, extra ones are cut off,
        /// unknown letters become meadow. <paramref name="campfire"/> is where the map suggests to settle
        /// (the player still chooses); when it cannot hold a camp the best clearing is suggested instead.
        /// </summary>
        public static MapDefinition Build(IList<string> rows, (int x, int y)? campfire = null, string title = null)
        {
            var g = new char[Size, Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    char c = rows != null && y < rows.Count && rows[y] != null && x < rows[y].Length ? rows[y][x] : '.';
                    g[y, x] = Enums.TerrainFromSymbol(c).HasValue ? c : '.';
                }

            (int x, int y)? start = null;
            if (campfire.HasValue && MapGenerator.CampfireFits(g, campfire.Value.x, campfire.Value.y)) start = campfire;
            start = start ?? MapGenerator.SuggestStart(g);
            if (start == null)
            {
                // No clearing anywhere: one is opened in the middle (as the generator does).
                for (int y = 58; y < 70; y++)
                    for (int x = 58; x < 70; x++) g[y, x] = '.';
                start = MapGenerator.SuggestStart(g) ?? (64, 64);
            }

            var lines = new List<string>(Size);
            for (int y = 0; y < Size; y++)
            {
                var sb = new StringBuilder(Size);
                for (int x = 0; x < Size; x++) sb.Append(g[y, x]);
                lines.Add(sb.ToString());
            }
            var (fx, fy) = start.Value;
            var map = new MapDefinition
            {
                Name = Name,
                Title = string.IsNullOrWhiteSpace(title) ? "Home Valley" : title.Trim(),
                Width = Size, Height = Size, Rows = lines,
                Traits = Traits(g),
                StartBuildings = new List<StartBuilding> { new StartBuilding { BuildingId = "campfire", X = fx, Y = fy } },
            };
            var (sx, sy) = MapGenerator.SettlementSpot(g, fx, fy) ?? (fx + 1, fy + 1);
            foreach (var (dx, dy) in Game.SettlementPattern)
                map.StartBuildings.Add(new StartBuilding { BuildingId = "shelter", X = sx + dx, Y = sy + dy });
            return map;
        }

        /// <summary>The content on the player's valley: the home clearing of the loaded content plus 64 lands cut from the map.</summary>
        public static GameContent ContentFor(GameContent root, MapDefinition map)
        {
            var regions = new List<RegionDefinition>();
            var home = root.Regions.FirstOrDefault(r => r.Home);
            if (home != null) regions.Add(home);
            regions.AddRange(MapGenerator.BuildRegions(map, LandSeed));
            return root.WithMap(map, regions);
        }

        static List<string> Traits(char[,] g)
        {
            int Count(char c) { int n = 0; foreach (var v in g) if (v == c) n++; return n; }
            int all = Size * Size;
            var t = new List<string>();
            int water = Count('~');
            if (water > all * 0.12) t.Add("great waters"); else if (water > 0) t.Add("river valley");
            if (Count('T') > all * 0.35) t.Add("deep forests"); else if (Count('T') > 0) t.Add("forest edge");
            if (Count('R') > all * 0.08) t.Add("hills of stone"); else if (Count('R') > 0) t.Add("stony knolls");
            if (Count('M') + Count('O') > 0) t.Add("iron mountains");
            if (t.Count == 0) t.Add("open plain");
            return t;
        }
    }
}
