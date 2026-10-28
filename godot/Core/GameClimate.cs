using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>How warm a person's home is.</summary>
    public enum Exposure { Comfortable, Chilly, Cold, Freezing }

    /// <summary>One period of the weather forecast.</summary>
    public struct WeatherPeriod
    {
        public double StartSimTime, EndSimTime;
        /// <summary>Temperature in the middle of the period (°C).</summary>
        public double Temperature;
        /// <summary>The cold snap falling on it, or null.</summary>
        public ColdSnapDefinition Snap;
    }

    /// <summary>A home, how warm it is inside and how many live there.</summary>
    public struct HomeWarmth
    {
        public PlacedBuilding Building;
        public double Warmth;
        public int Occupants;
        public Exposure Exposure;
    }

    /// <summary>The warmth of everyone's home at one moment.</summary>
    public sealed class WarmthReport
    {
        public double Temperature;
        public List<HomeWarmth> Homes = new List<HomeWarmth>();
        /// <summary>People per exposure (index = (int)Exposure), the homeless included.</summary>
        public int[] People = new int[4];
        public int Homeless;
        /// <summary>Warmth where the homeless sleep (beside the heart).</summary>
        public double HomelessWarmth;
        /// <summary>Exposure of each person, in the order of State.People.</summary>
        public Exposure[] PersonExposure = new Exposure[0];

        public int Cold => People[(int)Exposure.Cold] + People[(int)Exposure.Freezing];
        public int Freezing => People[(int)Exposure.Freezing];
    }

    /// <summary>A fire that warms its surroundings: the heart, a building or a brazier.</summary>
    public struct HeatSpot
    {
        public double X, Y, Heat, Radius;
    }

    /// <summary>
    /// Weather and warmth (Frostpunk's heat): the temperature follows the season and the hour,
    /// with cold snaps the forecast warns about. Each home is as warm as the weather plus its
    /// walls plus the fires around it. The heart warms everything within its reach, which grows
    /// with its level, and can be stoked for a while (the generator's overdrive). People who
    /// live in cold homes (or have none) fall ill and grow discontent.
    /// </summary>
    public sealed partial class Game
    {
        WarmthReport stepWarmth;

        public bool HasClimate => Content.Balance.Climate != null && HasSeasons;

        uint ClimateSeed
        {
            get
            {
                uint h = 2166136261u;
                foreach (char ch in State.SettlementId ?? "") h = (h ^ ch) * 16777619u;
                return h;
            }
        }

        static double Hash01(uint seed, long a, int b)
        {
            unchecked
            {
                uint h = seed ^ (uint)(a * 0x9E3779B1L) ^ (uint)((a >> 32) * 0x85EBCA6BL) ^ (uint)(b * 0xC2B2AE35);
                h ^= h >> 16;
                h *= 0x7feb352d;
                h ^= h >> 15;
                h *= 0x846ca68b;
                h ^= h >> 16;
                return (h % 100000) / 100000.0;
            }
        }

        // MARK: - Weather

        SeasonDefinition SeasonAt(double simTime)
        {
            if (!HasSeasons) return null;
            var list = Content.Balance.Seasons;
            int i = (int)(Math.Max(0, simTime) / Content.Balance.SeasonSeconds) % list.Count;
            return list[i];
        }

        double SeasonBase(int index)
        {
            var list = Content.Balance.Seasons;
            var s = list[((index % list.Count) + list.Count) % list.Count];
            return Content.Balance.Climate.SeasonTemperature.TryGetValue(s.Id, out var t) ? t : 10;
        }

        /// <summary>The cold snap of a weather period, or null.</summary>
        public ColdSnapDefinition SnapAt(long period)
        {
            var c = Content.Balance.Climate;
            if (!HasClimate || c.PeriodSeconds <= 0) return null;
            var season = SeasonAt((period + 0.5) * c.PeriodSeconds);
            if (season == null) return null;
            // Snaps last a few periods: the dice are thrown once per block.
            long block = (long)Math.Floor(period / 3.0);
            for (int i = 0; i < c.ColdSnaps.Count; i++)
            {
                var snap = c.ColdSnaps[i];
                if (!snap.Seasons.Contains(season.Id)) continue;
                if (Hash01(ClimateSeed, block, 100 + i) < snap.Chance) return snap;
            }
            return null;
        }

        /// <summary>Outside temperature (°C) at a game time; 15 °C when the weather is off.</summary>
        public double TemperatureAt(double simTime)
        {
            if (!HasClimate) return 15;
            var c = Content.Balance.Climate;
            double S = Content.Balance.SeasonSeconds;
            // Seasons blend into each other around their middles.
            double f = simTime / S - 0.5;
            int i0 = (int)Math.Floor(f);
            double w = f - i0;
            w = w * w * (3 - 2 * w);
            double t = SeasonBase(i0) * (1 - w) + SeasonBase(i0 + 1) * w;
            if (c.DaySeconds > 0)
            {
                double p = (simTime % c.DaySeconds + c.DaySeconds) % c.DaySeconds / c.DaySeconds;
                t += c.DailySwing * Math.Cos(2 * Math.PI * (p - 0.45));
            }
            if (c.PeriodSeconds > 0)
            {
                long period = (long)Math.Floor(simTime / c.PeriodSeconds);
                t += c.Noise * (2 * Hash01(ClimateSeed, period, 7) - 1);
                var snap = SnapAt(period);
                if (snap != null) t -= snap.Drop;
            }
            return Math.Round(t, 1);
        }

        public double Temperature => TemperatureAt(State.SimTime);

        /// <summary>The coming weather periods, starting with the current one.</summary>
        public List<WeatherPeriod> Forecast(int count = -1)
        {
            var list = new List<WeatherPeriod>();
            if (!HasClimate) return list;
            var c = Content.Balance.Climate;
            if (count < 0) count = c.ForecastPeriods;
            long first = (long)Math.Floor(State.SimTime / c.PeriodSeconds);
            for (long k = first; k < first + count; k++)
                list.Add(new WeatherPeriod
                {
                    StartSimTime = k * c.PeriodSeconds, EndSimTime = (k + 1) * c.PeriodSeconds,
                    Temperature = TemperatureAt((k + 0.5) * c.PeriodSeconds), Snap = SnapAt(k),
                });
            return list;
        }

        // MARK: - Fires

        public bool HasWarmth => Content.Balance.Warmth != null && HasClimate;

        public bool IsStoked => State.SimTime < State.StokedUntilSimTime;

        public double StokeReadyIn => Math.Max(0, State.StokeReadySimTime - State.SimTime);

        /// <summary>Warmth (°C at its centre) and reach (cells) of the heart now.</summary>
        public (double heat, double radius) HeartFire()
        {
            var w = Content.Balance.Warmth;
            var heart = Heart;
            if (w == null || heart == null || !heart.IsOperational || Freezing) return (0, 0);
            int level = HeartLevel;
            double heat = w.HeartHeat + w.HeartHeatPerLevel * (level - 1);
            double radius = w.HeartRadius + w.HeartRadiusPerLevel * (level - 1);
            foreach (var e in ActiveEffects()) if (e.Kind == EffectKind.HeatBonus) heat += e.Value ?? 0;
            if (IsStoked) { heat += w.StokeHeat; radius += w.StokeRadius; }
            return (heat, radius);
        }

        /// <summary>Braziers and fire pits are lit only when it is cold enough to need them.</summary>
        bool SmallFiresLit => HasWarmth && !Freezing && Temperature < Content.Balance.Warmth.Comfortable + 5;

        /// <summary>Every fire warming the settlement now.</summary>
        public List<HeatSpot> HeatSpots()
        {
            var list = new List<HeatSpot>();
            if (!HasWarmth) return list;
            var heart = Heart;
            var (heat, radius) = HeartFire();
            if (heart != null && heat > 0)
            {
                var fp = FootprintOf(heart);
                list.Add(new HeatSpot { X = heart.X + fp.Width / 2.0, Y = heart.Y + fp.Height / 2.0, Heat = heat, Radius = radius });
            }
            if (!SmallFiresLit) return list;
            foreach (var (b, def) in OperationalBuildings())
            {
                if (def.Heat == null || def.Heart) continue;
                var fp = FootprintOf(b);
                list.Add(new HeatSpot { X = b.X + fp.Width / 2.0, Y = b.Y + fp.Height / 2.0, Heat = def.Heat.Heat, Radius = def.Heat.Radius });
            }
            foreach (var d in State.Decorations)
            {
                var def = Content.Decoration(d.DefinitionId);
                if (def?.Heat == null) continue;
                list.Add(new HeatSpot { X = d.X + 0.5, Y = d.Y + 0.5, Heat = def.Heat.Heat, Radius = def.Heat.Radius });
            }
            return list;
        }

        /// <summary>Firewood burnt by braziers, fire pits and the stoked heart (on top of the homes' own fires).</summary>
        double FiresFuelPerSecond()
        {
            if (!HasWarmth) return 0;
            double wood = 0;
            if (IsStoked) wood += Content.Balance.Warmth.StokeWoodPerSecond;
            if (Temperature < Content.Balance.Warmth.Comfortable + 5)
            {
                foreach (var (_, def) in OperationalBuildings())
                    if (def.Heat != null && !def.Heart) wood += def.Heat.WoodPerSecond;
                foreach (var d in State.Decorations)
                    if (Content.Decoration(d.DefinitionId)?.Heat is HeatSource h) wood += h.WoodPerSecond;
            }
            return wood;
        }

        static double HeatAt(List<HeatSpot> spots, double x, double y, double cap)
        {
            double sum = 0;
            foreach (var s in spots)
            {
                double dx = s.X - x, dy = s.Y - y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d > s.Radius || s.Radius <= 0) continue;
                sum += s.Heat * (1 - 0.5 * d / s.Radius);
            }
            return Math.Min(cap, sum);
        }

        /// <summary>The extra warmth (°C) the fires give at a cell (for the heat overlay).</summary>
        public double HeatAtCell(int x, int y) =>
            HasWarmth ? HeatAt(HeatSpots(), x + 0.5, y + 0.5, Content.Balance.Warmth.MaxHeat) : 0;

        public Exposure ExposureOf(double warmth)
        {
            var w = Content.Balance.Warmth;
            if (w == null || warmth >= w.Comfortable) return Exposure.Comfortable;
            if (warmth >= w.Cold) return Exposure.Chilly;
            if (warmth >= w.Freezing) return Exposure.Cold;
            return Exposure.Freezing;
        }

        /// <summary>How much warmer than outside a building's walls keep it.</summary>
        public double InsulationOf(PlacedBuilding b, BuildingDefinition def) =>
            def.Insulation + (Content.Balance.Warmth?.InsulationPerLevel ?? 0) * Math.Max(0, b.Level - 1);

        /// <summary>
        /// Who lives where and how warm it is. People take the warmest homes first; those without
        /// a home sleep beside the heart.
        /// </summary>
        public WarmthReport Warmth()
        {
            if (stepWarmth != null) return stepWarmth;
            var report = new WarmthReport { Temperature = Temperature };
            report.PersonExposure = new Exposure[State.People.Count];
            if (!HasWarmth)
            {
                report.People[0] = State.Population;
                return report;
            }
            var w = Content.Balance.Warmth;
            var spots = HeatSpots();
            var homes = new List<HomeWarmth>();
            foreach (var (b, def) in OperationalBuildings())
            {
                int capacity = HousingOf(b, def);
                if (capacity <= 0) continue;
                var fp = FootprintOf(b);
                double warmth = report.Temperature + InsulationOf(b, def) + HeatAt(spots, b.X + fp.Width / 2.0, b.Y + fp.Height / 2.0, w.MaxHeat);
                // Walls and fires keep the cold out; they do not make a summer home hotter than a warm room.
                double shown = Math.Min(warmth, Math.Max(report.Temperature, 22));
                homes.Add(new HomeWarmth { Building = b, Warmth = Math.Round(shown, 1), Occupants = capacity, Exposure = ExposureOf(warmth) });
            }
            homes = homes.OrderByDescending(h => h.Warmth).ThenBy(h => h.Building.Id).ToList();
            var heart = Heart;
            double homeless = report.Temperature;
            if (heart != null)
            {
                var fp = FootprintOf(heart);
                homeless += 0.5 * HeatAt(spots, heart.X + fp.Width / 2.0, heart.Y + fp.Height / 2.0, w.MaxHeat);
            }
            report.HomelessWarmth = Math.Round(homeless, 1);

            int left = State.Population, index = 0;
            for (int i = 0; i < homes.Count; i++)
            {
                var h = homes[i];
                int living = Math.Min(h.Occupants, left);
                h.Occupants = living;
                homes[i] = h;
                left -= living;
                report.People[(int)h.Exposure] += living;
                for (int k = 0; k < living && index < report.PersonExposure.Length; k++) report.PersonExposure[index++] = h.Exposure;
            }
            report.Homes = homes;
            report.Homeless = left;
            var outside = ExposureOf(homeless);
            report.People[(int)outside] += left;
            while (index < report.PersonExposure.Length) report.PersonExposure[index++] = outside;
            return report;
        }

        // MARK: - Stoking the heart

        /// <summary>Burns the heart hot for a while: more warmth over a wider reach, for much more firewood.</summary>
        public GameResult StokeHeart()
        {
            Update();
            var w = Content.Balance.Warmth;
            if (!HasWarmth) return GameError.NotReady("There is no need to stoke the fire.");
            var heart = Heart;
            if (heart == null || !heart.IsOperational) return GameError.NotReady("The heart is not burning yet.");
            if (IsStoked) return GameError.NotReady("The fire is already roaring.");
            if (StokeReadyIn > 0) return GameError.NotReady($"The fire can be stoked again in {Duration(StokeReadyIn)}.");
            double need = w.StokeWoodPerSecond * Math.Min(w.StokeSeconds, 600);
            if (State.Resources.Wood < need) return GameError.Unaffordable(new ResourceBundle(wood: need - State.Resources.Wood));
            State.StokedUntilSimTime = State.SimTime + w.StokeSeconds;
            State.StokeReadySimTime = State.SimTime + w.StokeSeconds + w.StokeCooldownSeconds;
            if (Math.Abs(w.StokeApproval) > 0.01)
                State.Modifiers.Add(new ActiveModifier
                {
                    SourceId = "stoke", Effect = new Effect(EffectKind.ApprovalBonus) { Value = w.StokeApproval },
                    ExpiresAtSimTime = State.StokedUntilSimTime,
                });
            Notice(NoticeKind.Success, $"The {HeartName} roars: more warmth, further out, for {Duration(w.StokeSeconds)}.");
            return GameResult.Success;
        }

        /// <summary>A stoked fire dies down when the woodpile is empty.</summary>
        void TrackStoke()
        {
            if (IsStoked && State.Resources.Wood <= 1e-6)
            {
                State.StokedUntilSimTime = State.SimTime;
                State.Modifiers.RemoveAll(m => m.SourceId == "stoke");
                Notice(NoticeKind.Warning, "The woodpile is empty: the roaring fire has died down.");
            }
        }

        // MARK: - Weather notices

        long lastWarnedSnapBlock = long.MinValue;

        /// <summary>Warns once when a cold snap is coming within the forecast.</summary>
        void TrackWeather()
        {
            if (!HasClimate || simulatingOffline) return;
            var c = Content.Balance.Climate;
            long now = (long)Math.Floor(State.SimTime / c.PeriodSeconds);
            for (long k = now; k < now + Math.Max(1, c.ForecastPeriods); k++)
            {
                var snap = SnapAt(k);
                if (snap == null) continue;
                long block = (long)Math.Floor(k / 3.0);
                if (block == lastWarnedSnapBlock) return;
                lastWarnedSnapBlock = block;
                double inSeconds = k * c.PeriodSeconds - State.SimTime;
                Notice(NoticeKind.Warning, inSeconds <= 0
                    ? $"{snap.Name}! It is {snap.Drop:0}° colder than usual. Keep the fires burning."
                    : $"{snap.Name} in {Duration(inSeconds)}: fill the woodpile and warm the homes.");
                return;
            }
        }

        /// <summary>"2 h 05 min", "12 min", "40 s".</summary>
        public static string Duration(double seconds)
        {
            seconds = Math.Max(0, Math.Ceiling(seconds));
            if (seconds >= 3600) return $"{(int)(seconds / 3600)} h {(int)(seconds % 3600 / 60):00} min";
            if (seconds >= 60) return $"{(int)Math.Ceiling(seconds / 60)} min";
            return $"{(int)seconds} s";
        }
    }
}
