using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// Illness and healing (Frostpunk's sick and its medical posts): people in cold homes, without
    /// a home or without food fall ill. The sick keep their job but do not work. Healers look after
    /// a few patients each, who get well quickly and never worse. Left alone in the cold or in
    /// hunger an illness grows grave and may kill; a death saddens everyone for a while.
    /// </summary>
    public sealed partial class Game
    {
        public bool HasHealth => Content.Balance.Health != null;

        /// <summary>People who fell ill during a long absence (told once on return).</summary>
        int offlineIll;

        public int SickCount
        {
            get
            {
                int n = 0;
                foreach (var p in State.People) if (p.Sick) n++;
                return n;
            }
        }

        /// <summary>Healers at work (not ill themselves).</summary>
        public int ActiveHealers => ActiveWorkers(JobType.Healing);

        /// <summary>How many sick the healers can look after (skilled healers manage a few more).</summary>
        public int CareCapacity => HasHealth ? (int)Math.Floor(SkilledWorkers(JobType.Healing) * Content.Balance.Health.PatientsPerHealer + 1e-6) : 0;

        /// <summary>Workers of a job who are not ill.</summary>
        public int ActiveWorkers(JobType job)
        {
            int assigned = State.Assignments[job];
            if (assigned <= 0) return 0;
            string id = job.Id();
            int sick = 0;
            foreach (var p in State.People) if (p.Sick && p.Job == id) sick++;
            return Math.Max(0, assigned - sick);
        }

        /// <summary>The sick who are under a healer's care: the gravest first.</summary>
        public HashSet<int> TreatedPatients()
        {
            var set = new HashSet<int>();
            int capacity = CareCapacity;
            if (capacity <= 0) return set;
            foreach (var p in State.People.Where(p => p.Sick).OrderByDescending(p => p.Severity).ThenBy(p => p.Id))
            {
                if (set.Count >= capacity) break;
                set.Add(p.Id);
            }
            return set;
        }

        /// <summary>Per-second chance that someone falls ill now (summed over everyone at risk).</summary>
        public double IllnessRate()
        {
            var h = Content.Balance.Health;
            if (h == null) return 0;
            var warmth = Warmth();
            double rate = 0;
            for (int i = 0; i < State.People.Count; i++)
            {
                if (State.People[i].Sick) continue;
                var e = i < warmth.PersonExposure.Length ? warmth.PersonExposure[i] : Exposure.Comfortable;
                if (e == Exposure.Freezing) rate += h.FreezingRate;
                else if (e == Exposure.Cold) rate += h.ColdRate;
                if (State.FoodShortage) rate += h.HungerRate;
            }
            return rate * Multiplier(EffectKind.SicknessMultiplier);
        }

        void AdvanceHealth(double dt)
        {
            var h = Content.Balance.Health;
            if (h == null || State.People.Count == 0) return;
            var warmth = Warmth();
            var exposure = new Dictionary<int, Exposure>();
            for (int i = 0; i < State.People.Count; i++)
                exposure[State.People[i].Id] = i < warmth.PersonExposure.Length ? warmth.PersonExposure[i] : Exposure.Comfortable;

            // Falling ill
            State.IllnessProgress += IllnessRate() * dt;
            var newlySick = new List<string>();
            while (State.IllnessProgress >= 1)
            {
                State.IllnessProgress -= 1;
                var healthy = State.People.Where(p => !p.Sick).ToList();
                if (healthy.Count == 0) { State.IllnessProgress = 0; break; }
                // The most exposed fall ill first; among them the dice decide.
                var worst = healthy.Max(p => exposure[p.Id]);
                var group = healthy.Where(p => exposure[p.Id] == worst).ToList();
                var victim = group[(int)(Hash01(ClimateSeed, (long)(State.SimTime * 10), State.People.Count + State.Deaths) * group.Count) % group.Count];
                victim.Sick = true;
                victim.Recovery = 0;
                victim.Severity = 0;
                newlySick.Add(victim.Name);
            }
            offlineIll += newlySick.Count;
            if (simulatingOffline) newlySick.Clear(); // told once on return
            if (newlySick.Count == 1) Notice(NoticeKind.Warning, $"{newlySick[0]} has fallen ill{(State.Assignments[JobType.Healing] > 0 ? "." : ". Build a healer's hut.")}");
            else if (newlySick.Count > 1) Notice(NoticeKind.Warning, $"{newlySick.Count} people have fallen ill.");

            // Getting well, or worse
            var treated = TreatedPatients();
            double recovery = Multiplier(EffectKind.RecoveryMultiplier);
            double deadly = Multiplier(EffectKind.DeathMultiplier);
            var recovered = new List<Person>();
            var dying = new List<Person>();
            foreach (var p in State.People)
            {
                if (!p.Sick) continue;
                var e = exposure[p.Id];
                if (treated.Contains(p.Id))
                {
                    p.Recovery += dt / h.TreatedRecoverSeconds * recovery;
                    p.Severity = Math.Max(0, p.Severity - dt / h.DeathSeconds);
                }
                else if (e == Exposure.Freezing)
                {
                    p.Severity += 2 * dt / h.DeathSeconds * deadly;
                }
                else if (e == Exposure.Cold || State.FoodShortage)
                {
                    p.Recovery += 0.25 * dt / h.RecoverSeconds * recovery;
                    p.Severity += dt / h.DeathSeconds * deadly;
                }
                else
                {
                    p.Recovery += dt / h.RecoverSeconds * recovery;
                    p.Severity = Math.Max(0, p.Severity - dt / h.DeathSeconds);
                }
                if (p.Recovery >= 1) recovered.Add(p);
                else if (p.Severity >= 1)
                {
                    // Nobody dies while the player is away and cannot help: the worst waits for their return.
                    if (simulatingOffline) p.Severity = Math.Min(p.Severity, 0.95);
                    else dying.Add(p);
                }
            }
            foreach (var p in recovered)
            {
                p.Sick = false;
                p.Recovery = 0;
                p.Severity = 0;
            }
            if (simulatingOffline) recovered.Clear();
            if (recovered.Count == 1) Notice(NoticeKind.Success, $"{recovered[0].Name} is well again.");
            else if (recovered.Count > 1) Notice(NoticeKind.Success, $"{recovered.Count} people are well again.");
            foreach (var p in dying) Die(p);
        }

        /// <summary>Someone dies of illness. The last two people never die: the story goes on.</summary>
        void Die(Person p)
        {
            if (State.Population <= 2)
            {
                p.Severity = 0.999;
                return;
            }
            RemovePerson(p);
            State.Deaths += 1;
            var h = Content.Balance.Health;
            double grief = Multiplier(EffectKind.GriefMultiplier);
            if (h != null && Math.Abs(h.GriefApproval) > 0.01)
                State.Modifiers.Add(new ActiveModifier
                {
                    SourceId = "grief", Effect = new Effect(EffectKind.ApprovalBonus) { Value = h.GriefApproval * grief },
                    ExpiresAtSimTime = State.SimTime + h.GriefSeconds,
                });
            if (Content.Balance.Discontent != null) AddDiscontent(Content.Balance.Discontent.Death * grief);
            Notice(NoticeKind.Warning, $"{p.Name} has died of illness. The village mourns.");
            AddLegacy("death", $"{p.Name} died", State.Deaths == 1
                ? "The first grave was dug beside the camp. Nobody forgot the winter it was dug in."
                : $"{p.Name} was laid to rest; {State.Deaths} have been lost to illness.", p.Id.ToString());
        }

        /// <summary>Takes a person out of the settlement with their job.</summary>
        internal void RemovePerson(Person p)
        {
            State.People.Remove(p);
            if (p.Job != null)
            {
                var job = Enums.ParseJob(p.Job);
                State.Assignments[job] = State.Assignments[job] - 1;
            }
            State.Population = Math.Max(0, State.Population - 1);
            SyncPeople();
        }
    }
}
