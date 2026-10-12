using System;
using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Weather, the heart's warmth, illness and healers, the Book of Customs, discontent and petitions.</summary>
    public class SurvivalTests
    {
        static (Game game, ManualClock clock) Calm(Action<GameContent> more = null) => Fixtures.NewGame(c =>
        {
            c.Balance.Climate.Noise = 0;
            c.Balance.Climate.DailySwing = 0;
            c.Balance.Climate.ColdSnaps.Clear();
            more?.Invoke(c);
        }, full: true);

        /// <summary>Plays in one-minute steps, as someone with the game open (long jumps count as being away).</summary>
        static void Play(Game g, double seconds, ManualClock clock)
        {
            for (double t = 0; t < seconds; t += 60) g.Run(Math.Min(60, seconds - t), clock);
        }

        static void Winter(Game g) => g.State.SimTime = 3.5 * g.Content.Balance.SeasonSeconds;

        static double Average(Game g, Func<double, bool> pick)
        {
            double sum = 0;
            int n = 0;
            double year = g.Content.Balance.SeasonSeconds * 4;
            for (double t = 0; t < 8 * year; t += 600)
                if (pick(t)) { sum += g.TemperatureAt(t); n++; }
            return sum / n;
        }

        [Test]
        public void WinterIsColdAndNightsAreColderThanDays()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            double S = game.Content.Balance.SeasonSeconds, day = game.Content.Balance.Climate.DaySeconds;
            int SeasonOf(double t) => (int)(t / S) % 4;
            double summer = Average(game, t => SeasonOf(t) == 1), winter = Average(game, t => SeasonOf(t) == 3);
            double spring = Average(game, t => SeasonOf(t) == 0), autumn = Average(game, t => SeasonOf(t) == 2);
            Assert.That(summer, Is.GreaterThan(spring));
            Assert.That(spring, Is.GreaterThan(autumn));
            Assert.That(autumn, Is.GreaterThan(winter));
            Assert.That(winter, Is.LessThan(0), "winter freezes");
            double afternoon = Average(game, t => t % day / day is double p && p > 0.4 && p < 0.5);
            double night = Average(game, t => t % day / day is double p && p > 0.85 && p < 0.95);
            Assert.That(afternoon - night, Is.GreaterThan(4));
        }

        [Test]
        public void TheForecastIsTheWeatherThatComes()
        {
            var (game, clock) = Fixtures.NewGame(full: true);
            Winter(game);
            var forecast = game.Forecast();
            Assert.That(forecast.Count, Is.EqualTo(game.Content.Balance.Climate.ForecastPeriods));
            var third = forecast[3];
            game.State.SimTime = (third.StartSimTime + third.EndSimTime) / 2;
            Assert.That(game.Temperature, Is.EqualTo(third.Temperature));
            Assert.That(game.Forecast()[0].Snap?.Id, Is.EqualTo(third.Snap?.Id));
        }

        [Test]
        public void ColdSnapsFallOnlyInTheirSeasonsAndNotTooOften()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            var c = game.Content.Balance.Climate;
            double S = game.Content.Balance.SeasonSeconds;
            int periods = 0, frosts = 0, snaps = 0;
            for (long k = 0; k < 40 * 4 * S / c.PeriodSeconds; k++)
            {
                var season = game.Content.Balance.Seasons[(int)((k + 0.5) * c.PeriodSeconds / S) % 4].Id;
                var snap = game.SnapAt(k);
                if (snap != null) Assert.That(snap.Seasons, Does.Contain(season), $"{snap.Id} in {season}");
                if (season != "winter") continue;
                periods++;
                if (snap?.Id == "great_frost") frosts++;
                if (snap?.Id == "cold_snap") snaps++;
            }
            Assert.That(frosts / (double)periods, Is.InRange(0.04, 0.18));
            Assert.That(snaps / (double)periods, Is.InRange(0.1, 0.35));
        }

        [Test]
        public void TheHeartWarmsWhatIsNearAndReachesFurtherAsItGrows()
        {
            var (game, _) = Calm();
            Winter(game);
            var heart = game.Heart;
            double near = game.HeatAtCell(heart.X + 1, heart.Y), far = game.HeatAtCell(heart.X + 12, heart.Y);
            Assert.That(near, Is.GreaterThan(4));
            Assert.That(far, Is.EqualTo(0));
            var (heat1, radius1) = game.HeartFire();
            heart.Level = 15;
            var (heat15, radius15) = game.HeartFire();
            Assert.That(heat15, Is.GreaterThan(heat1));
            Assert.That(radius15, Is.GreaterThan(radius1 + 3));
        }

        [Test]
        public void BetterHomesAreWarmerAndTheWarmestFillFirst()
        {
            var (game, _) = Calm();
            Winter(game);
            var tent = game.State.Buildings.First(b => b.DefinitionId == "shelter");
            var shelter = game.Content.Building("shelter");
            double before = game.InsulationOf(tent, shelter);
            tent.Level = 10;
            Assert.That(game.InsulationOf(tent, shelter), Is.GreaterThan(before + 3));
            var report = game.Warmth();
            var warmths = report.Homes.Where(h => h.Occupants > 0).Select(h => h.Warmth).ToList();
            Assert.That(warmths, Is.Ordered.Descending);
            Assert.That(report.People.Sum(), Is.EqualTo(game.State.Population));
        }

        [Test]
        public void StokingTheHeartWarmsMoreForMoreWoodAndThenRests()
        {
            var (game, clock) = Calm();
            Winter(game);
            game.TestGrant(new ResourceBundle(wood: 2000));
            var w = game.Content.Balance.Warmth;
            var (heat, radius) = game.HeartFire();
            double fuel = game.FuelPerSecond;
            Expect.Success(game.StokeHeart());
            Assert.That(game.IsStoked, Is.True);
            Assert.That(game.HeartFire().heat, Is.EqualTo(heat + w.StokeHeat).Within(1e-9));
            Assert.That(game.HeartFire().radius, Is.EqualTo(radius + w.StokeRadius).Within(1e-9));
            Assert.That(game.FuelPerSecond, Is.EqualTo(fuel + w.StokeWoodPerSecond).Within(1e-9));
            Assert.That(game.ApprovalFactors().Any(f => f.Label == "Roaring fire"), Is.True);
            Assert.That(game.StokeHeart().Ok, Is.False, "already roaring");
            game.Run(w.StokeSeconds + 1, clock);
            Assert.That(game.IsStoked, Is.False);
            Assert.That(game.StokeHeart().Ok, Is.False, "the fire must rest");
            game.Run(w.StokeCooldownSeconds, clock);
            game.TestGrant(new ResourceBundle(wood: 100));
            Expect.Success(game.StokeHeart());
        }

        [Test]
        public void AStokedFireDiesWithAnEmptyWoodpile()
        {
            var (game, clock) = Calm();
            Winter(game);
            game.State.Resources = new ResourceBundle(food: 200, wood: 60);
            game.SetWorkers(JobType.Woodcutting, 0);
            Expect.Success(game.StokeHeart());
            game.Run(1800, clock);
            Assert.That(game.IsStoked, Is.False);
            Assert.That(game.DrainNotices().Any(n => n.Message.Contains("died down")), Is.True);
        }

        /// <summary>Bitter cold everywhere and no fire: everyone freezes.</summary>
        static (Game, ManualClock) Frozen(Action<GameContent> more = null) => Calm(c =>
        {
            foreach (var k in c.Balance.Climate.SeasonTemperature.Keys.ToList()) c.Balance.Climate.SeasonTemperature[k] = -40;
            more?.Invoke(c);
        });

        [Test]
        public void TheColdMakesPeopleIllAndTheSickDoNotWork()
        {
            var (game, clock) = Frozen();
            game.TestGrant(new ResourceBundle(food: 150, wood: 150));
            Assert.That(game.Warmth().Freezing, Is.EqualTo(game.State.Population));
            double before = game.Economy().Production.Wood;
            game.Run(1800, clock);
            Assert.That(game.SickCount, Is.GreaterThan(0));
            var sickCutter = game.State.People.FirstOrDefault(p => p.Sick && p.Job == "woodcutting");
            int cutters = game.State.Assignments[JobType.Woodcutting];
            Assert.That(game.ActiveWorkers(JobType.Woodcutting), Is.EqualTo(cutters - game.State.People.Count(p => p.Sick && p.Job == "woodcutting")));
            if (sickCutter != null) Assert.That(game.Economy().Production.Wood, Is.LessThan(before));
            Assert.That(game.ApprovalFactors().Any(f => f.Label.StartsWith("Illness")), Is.True);
            Assert.That(game.ApprovalFactors().Any(f => f.Label.StartsWith("Cold homes")), Is.True);
        }

        [Test]
        public void HealersBringTheSickBackQuickly()
        {
            var (game, clock) = Calm();
            var h = game.Content.Balance.Health;
            game.TestGrant(new ResourceBundle(food: 150, wood: 150));
            game.TestResearch("herbal_healing");
            game.AddCompleted("healers_hut", 2, 2);
            Expect.Success(game.SetWorkers(JobType.Foraging, 0));
            Expect.Success(game.SetWorkers(JobType.Healing, 1));
            var patient = game.State.People.First(p => p.Job == null);
            patient.Sick = true;
            Assert.That(game.TreatedPatients(), Does.Contain(patient.Id));
            game.Run(h.TreatedRecoverSeconds / 0.9, clock);
            Assert.That(patient.Sick, Is.False, "well again under care");
        }

        [Test]
        public void UntreatedIllnessInTheColdKillsButTwoAlwaysRemain()
        {
            var (game, clock) = Frozen();
            game.TestGrant(new ResourceBundle(food: 200, wood: 200));
            int before = game.State.Population;
            Play(game, 8 * 3600, clock);
            Assert.That(game.State.Deaths, Is.GreaterThan(0));
            Assert.That(game.State.Population, Is.EqualTo(before - game.State.Deaths));
            Assert.That(game.State.Population, Is.GreaterThanOrEqualTo(2));
            Assert.That(game.State.People.Count, Is.EqualTo(game.State.Population));
            Assert.That(game.State.Assignments.Total, Is.LessThanOrEqualTo(game.State.Population));
            Assert.That(game.State.Legacy.Any(l => l.Kind == "death"), Is.True);
            Assert.That(game.State.Discontent, Is.GreaterThan(0));
        }

        [Test]
        public void NobodyDiesOrLeavesWhileThePlayerIsAway()
        {
            var (game, clock) = Frozen();
            game.TestGrant(new ResourceBundle(food: 200, wood: 200));
            game.State.Discontent = 100;
            int before = game.State.Population;
            game.Run(10 * 3600, clock); // one long absence
            Assert.That(game.State.Deaths, Is.EqualTo(0));
            Assert.That(game.State.Population, Is.EqualTo(before));
            Assert.That(game.SickCount, Is.GreaterThan(0));
            var notices = game.DrainNotices();
            Assert.That(notices.Any(n => n.Message.StartsWith("While you were away")), Is.True);
            Assert.That(notices.Count(n => n.Message.Contains("has fallen ill")), Is.EqualTo(0), "no flood of notices on return");
            // Back in the game, the gravely ill need help soon.
            Play(game, 3600, clock);
            Assert.That(game.State.Deaths, Is.GreaterThan(0));
        }

        [Test]
        public void HungerMakesPeopleIllToo()
        {
            var (game, clock) = Calm();
            game.State.SimTime = 1.5 * game.Content.Balance.SeasonSeconds; // summer: nobody is cold
            game.State.Resources = new ResourceBundle(wood: 100);
            foreach (var j in Enums.Jobs) game.SetWorkers(j, 0);
            game.Run(3600, clock);
            Assert.That(game.State.FoodShortage, Is.True);
            Assert.That(game.SickCount, Is.GreaterThan(0));
        }

        [Test]
        public void LawsNeedTheHeartTimeAndRespectEachOther()
        {
            var (game, clock) = Calm();
            var heart = game.Heart;
            Assert.That(game.LawStatusOf(game.Content.Law("shared_stew")).State, Is.EqualTo(LawState.Locked), "the heart is too small");
            heart.Level = 10;
            double food = game.Economy().Consumption.Food;
            Expect.Success(game.SignLaw("shared_stew"));
            Assert.That(game.Economy().Consumption.Food, Is.LessThan(food));
            var hearty = game.LawStatusOf(game.Content.Law("hearty_meals"));
            Assert.That(hearty.State, Is.EqualTo(LawState.Locked));
            Assert.That(hearty.Reasons.Any(r => r.Contains("Shared Stew")), Is.True);
            var r1 = game.SignLaw("long_days");
            Assert.That(r1.Ok, Is.False, "laws need time between them");
            Assert.That(game.LawStatusOf(game.Content.Law("dawn_to_dusk")).State, Is.EqualTo(LawState.Locked));
            game.Run(game.Content.Balance.Discontent.LawCooldownSeconds + 1, clock);
            double work = game.WorkRate;
            Expect.Success(game.SignLaw("long_days"));
            Assert.That(game.WorkRate, Is.EqualTo(work * 1.15).Within(1e-9));
            Assert.That(game.LawStatusOf(game.Content.Law("dawn_to_dusk")).Reasons.Any(r => r.Contains("Long Days")), Is.False);
            Assert.That(game.State.Legacy.Count(l => l.Kind == "law"), Is.EqualTo(2));
            Assert.That(game.SignLaw("long_days").Ok, Is.False, "already signed");
        }

        [Test]
        public void DiscontentRisesWithHungerAndSlowsWork()
        {
            var (game, clock) = Calm();
            game.State.SimTime = 1.5 * game.Content.Balance.SeasonSeconds;
            game.State.Resources = new ResourceBundle(wood: 100);
            foreach (var j in Enums.Jobs) game.SetWorkers(j, 0);
            game.Run(600, clock);
            Assert.That(game.DiscontentFactors().Any(f => f.Label == "Hunger"), Is.True);
            Assert.That(game.State.Discontent, Is.GreaterThan(0));
            var d = game.Content.Balance.Discontent;
            game.State.Discontent = d.Grumbling + 1;
            Assert.That(game.DiscontentWork, Is.EqualTo(d.GrumblingWork));
            game.State.Discontent = d.Unrest + 1;
            Assert.That(game.DiscontentWork, Is.EqualTo(d.UnrestWork));
            Assert.That(game.DiscontentMood, Is.EqualTo("Unrest"));
        }

        [Test]
        public void ContentPeopleCalmDown()
        {
            var (game, clock) = Calm();
            game.State.SimTime = 1.5 * game.Content.Balance.SeasonSeconds;
            game.TestGrant(new ResourceBundle(food: 150, wood: 150));
            game.State.Discontent = 30;
            game.Run(3600, clock);
            Assert.That(game.State.Discontent, Is.LessThan(30));
        }

        [Test]
        public void APromiseKeptCalmsThePeople()
        {
            var (game, clock) = Calm();
            game.State.SimTime = 1.5 * game.Content.Balance.SeasonSeconds;
            var d = game.Content.Balance.Discontent;
            game.State.Discontent = d.PetitionAt + 5;
            game.Run(5, clock);
            Assert.That(game.PendingPetition, Is.Not.Null, "the people petition");
            game.State.PendingPetition = "full_stores";
            game.State.Resources = new ResourceBundle(food: 20, wood: 50);
            double before = game.State.Discontent;
            Expect.Success(game.AnswerPetition(true));
            Assert.That(game.State.ActivePetition?.PetitionId, Is.EqualTo("full_stores"));
            Assert.That(game.State.Discontent, Is.EqualTo(before - d.PromiseRelief).Within(1e-6));
            game.TestGrant(new ResourceBundle(food: 200));
            game.Run(2, clock);
            Assert.That(game.State.ActivePetition, Is.Null);
            Assert.That(game.State.Discontent, Is.LessThan(before - d.PromiseRelief - d.KeptRelief + 1));
            Assert.That(game.ApprovalFactors().Any(f => f.Label == "Promise kept"), Is.True);
        }

        [Test]
        public void ABrokenPromiseAngersAndARefusalToo()
        {
            var (game, clock) = Calm();
            game.State.SimTime = 1.5 * game.Content.Balance.SeasonSeconds;
            var d = game.Content.Balance.Discontent;
            game.State.PendingPetition = "healer";
            Expect.Success(game.AnswerPetition(true));
            double promised = game.State.Discontent;
            game.State.Discontent = 50;
            game.Run(game.Content.Petition("healer").DeadlineSeconds + 5, clock);
            Assert.That(game.State.ActivePetition, Is.Null);
            Assert.That(game.State.Legacy.Any(l => l.Title.StartsWith("Promise broken")), Is.True);
            Assert.That(promised, Is.EqualTo(0));
            game.State.PendingPetition = "gathering_place";
            game.State.Discontent = 40;
            Expect.Success(game.AnswerPetition(false));
            Assert.That(game.State.Discontent, Is.EqualTo(40 + d.RefusedAnger).Within(1e-6));
            Expect.Failure(game.AnswerPetition(true), GameError.NoPendingEvent);
        }

        [Test]
        public void AtTheTopFamiliesLeave()
        {
            var (game, clock) = Calm();
            game.State.SimTime = 1.5 * game.Content.Balance.SeasonSeconds;
            game.State.Resources = new ResourceBundle(wood: 100);
            foreach (var j in Enums.Jobs) game.SetWorkers(j, 0);
            game.State.Discontent = 100;
            int before = game.State.Population;
            Play(game, game.Content.Balance.Discontent.LeaveSeconds + 10, clock);
            Assert.That(game.State.Population, Is.LessThan(before));
            Assert.That(game.State.People.Count, Is.EqualTo(game.State.Population));
        }

        [Test]
        public void ScoutsBringBackSurvivorsAndSupplies()
        {
            var (game, clock) = Calm();
            game.TestGrant(new ResourceBundle(food: 100, wood: 100));
            int before = game.State.Population;
            var region = game.Content.Region("west_woods");
            Expect.Success(game.StartExpedition(region.Id));
            game.Run(region.ExpeditionSeconds + 1, clock);
            Assert.That(game.State.Population, Is.EqualTo(before + region.Survivors));
            Assert.That(game.State.People.Count, Is.EqualTo(game.State.Population));
            var ridge = game.Content.Region("north_forest");
            double wood = game.State.Resources.Wood;
            Expect.Success(game.StartExpedition(ridge.Id));
            game.Run(ridge.ExpeditionSeconds + 1, clock);
            Assert.That(game.State.Resources.Wood, Is.GreaterThan(wood));
        }

        [Test]
        public void TheSurvivalStateIsSaved()
        {
            var (game, clock) = Calm();
            game.Heart.Level = 5;
            game.State.People[0].Sick = true;
            game.State.People[0].Recovery = 0.4;
            game.State.People[0].Severity = 0.2;
            game.State.Discontent = 33;
            Expect.Success(game.SignLaw("day_of_rest"));
            game.TestGrant(new ResourceBundle(wood: 500));
            Winter(game);
            Expect.Success(game.StokeHeart());
            game.State.PendingPetition = "healer";
            game.State.ActivePetition = new ActivePetition { PetitionId = "rest_day", DeadlineSimTime = 1234 };
            game.State.Deaths = 2;
            var copy = game.State.Clone();
            Assert.That(copy.People[0].Sick, Is.True);
            Assert.That(copy.People[0].Recovery, Is.EqualTo(0.4));
            Assert.That(copy.People[0].Severity, Is.EqualTo(0.2));
            Assert.That(copy.Discontent, Is.EqualTo(game.State.Discontent));
            Assert.That(copy.Laws, Is.EqualTo(new[] { "day_of_rest" }));
            Assert.That(copy.LastLawSimTime, Is.EqualTo(game.State.LastLawSimTime));
            Assert.That(copy.StokedUntilSimTime, Is.EqualTo(game.State.StokedUntilSimTime));
            Assert.That(copy.StokeReadySimTime, Is.EqualTo(game.State.StokeReadySimTime));
            Assert.That(copy.PendingPetition, Is.EqualTo("healer"));
            Assert.That(copy.ActivePetition.PetitionId, Is.EqualTo("rest_day"));
            Assert.That(copy.ActivePetition.DeadlineSimTime, Is.EqualTo(1234));
            Assert.That(copy.Deaths, Is.EqualTo(2));
        }

        [Test]
        public void HealersAreAJobWithASkillAndAHut()
        {
            var c = Fixtures.Content();
            Assert.That(c.JobConfig(JobType.Healing), Is.Not.Null);
            Assert.That(c.Skill(JobType.Healing).Perks.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(c.Building("healers_hut").Slots(JobType.Healing), Is.GreaterThan(0));
            Assert.That(c.Technology("herbal_healing").Effects.Any(e => e.Kind == EffectKind.UnlockBuilding && e.BuildingId == "healers_hut"), Is.True);
            Assert.That(c.Laws.Count, Is.GreaterThanOrEqualTo(12));
            Assert.That(c.Laws.Select(l => l.Branch).Distinct().Count(), Is.EqualTo(2));
        }
    }
}
