using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Founders.Core
{
    public enum LoadResult { Loaded, NoSave, Corrupt, UnsupportedVersion }

    public sealed class LoadOutcome
    {
        public LoadResult Result;
        public GameState State;
        /// <summary>Why a save was rejected (Corrupt).</summary>
        public string Reason;
        /// <summary>Version found in a too-new save (UnsupportedVersion).</summary>
        public int Version;

        public static LoadOutcome Loaded(GameState s) => new LoadOutcome { Result = LoadResult.Loaded, State = s };
        public static readonly LoadOutcome NoSave = new LoadOutcome { Result = LoadResult.NoSave };
        public static LoadOutcome Corrupt(string reason) => new LoadOutcome { Result = LoadResult.Corrupt, Reason = reason };
        public static LoadOutcome Unsupported(int version) => new LoadOutcome { Result = LoadResult.UnsupportedVersion, Version = version };
        public override string ToString() => $"{Result} {Reason}";
    }

    /// <summary>Persistence boundary. A cloud/server implementation can replace the local one later.</summary>
    public interface ISaveStore
    {
        LoadOutcome Load(GameContent content);
        void Save(GameState state, GameContent content, double wallTime);
        void Reset();
    }

    /// <summary>Versioned JSON envelope: { saveVersion, contentVersion, savedAtWallTime, state }.</summary>
    public static class SaveCodec
    {
        public const int CurrentVersion = 1;

        public static string Encode(GameState state, GameContent content, double wallTime) =>
            Json.Write(new Dictionary<string, object>
            {
                ["saveVersion"] = CurrentVersion,
                ["contentVersion"] = content.ContentVersion,
                ["savedAtWallTime"] = wallTime,
                ["state"] = state.ToJson(),
            });

        /// <summary>Decodes and validates save data. Never throws: problems become a LoadOutcome.</summary>
        public static LoadOutcome Decode(string data, GameContent content)
        {
            JObj root;
            int version;
            try
            {
                root = new JObj(Json.Parse(data));
                version = root.Int("saveVersion");
            }
            catch (Exception)
            {
                return LoadOutcome.Corrupt("Save file is not readable JSON.");
            }
            if (version > CurrentVersion) return LoadOutcome.Unsupported(version);
            if (version < 1) return LoadOutcome.Corrupt($"Invalid save version {version}.");
            // Future: migrate older save versions here before decoding.
            GameState state;
            try
            {
                state = GameState.FromJson(root.Obj("state"));
            }
            catch (Exception e)
            {
                return LoadOutcome.Corrupt($"Save data is incomplete: {e.Message}");
            }
            var problems = Validate(state, content.ForMap(state.MapName));
            if (problems.Count > 0) return LoadOutcome.Corrupt(string.Join("; ", problems));
            return LoadOutcome.Loaded(state);
        }

        static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

        /// <summary>Structural checks so a tampered or damaged save can never break the rules.</summary>
        public static List<string> Validate(GameState s, GameContent content)
        {
            var problems = new List<string>();
            if (!s.Resources.IsValid) problems.Add("resources must be finite and non-negative");
            if (s.Population < 0) problems.Add("negative population");
            if (s.Assignments.Total > s.Population) problems.Add("more workers assigned than people");
            if (s.Assignments.Counts.Values.Any(v => v < 0)) problems.Add("negative assignment");
            if (!Finite(s.SimTime) || s.SimTime < 0) problems.Add("invalid simTime");
            if (!Finite(s.LastUpdateWallTime)) problems.Add("invalid lastUpdateWallTime");
            if (content.Epoch(s.EpochId) == null) problems.Add($"unknown epoch {s.EpochId}");
            if (!string.IsNullOrEmpty(s.MapName) && s.MapName != content.Map.Name) problems.Add($"settlement was founded on another map ({s.MapName})");
            foreach (var r in s.TradeRoutes)
            {
                var partner = content.TradePartner(r.PartnerId);
                if (partner == null) problems.Add($"unknown trade partner {r.PartnerId}");
                else if (r.Deal < 0 || r.Deal >= partner.Deals.Count || r.LoadedDeal < 0 || r.LoadedDeal >= partner.Deals.Count) problems.Add("invalid trade deal");
                if (!Finite(r.Elapsed) || r.Elapsed < 0 || !r.Received.IsValid) problems.Add("invalid trade route");
            }
            if (s.TradeRoutes.Select(r => r.PartnerId).Distinct().Count() != s.TradeRoutes.Count) problems.Add("duplicate trade route");
            var ids = new HashSet<int>();
            foreach (var b in s.Buildings)
            {
                if (content.Building(b.DefinitionId) == null) problems.Add($"unknown building {b.DefinitionId}");
                if (!ids.Add(b.Id)) problems.Add($"duplicate building id {b.Id}");
                if (b.Id >= s.NextBuildingId) problems.Add($"building id {b.Id} >= nextBuildingId");
                if (!Finite(b.RemainingWork)) problems.Add("invalid construction progress");
            }
            foreach (var t in s.Researched) if (content.Technology(t) == null) problems.Add($"unknown technology {t}");
            if (s.ActiveResearch != null && content.Technology(s.ActiveResearch.TechId) == null)
                problems.Add($"unknown research {s.ActiveResearch.TechId}");
            foreach (var r in s.ExploredRegions) if (content.Region(r) == null) problems.Add($"unknown region {r}");
            foreach (var kv in s.Speedups)
            {
                if (!Speedups.TryParse(kv.Key, out _, out _)) problems.Add($"unknown speed-up {kv.Key}");
                if (kv.Value < 0) problems.Add($"negative speed-up count {kv.Key}");
            }
            if (s.PendingEventId != null && content.Event(s.PendingEventId) == null) problems.Add($"unknown event {s.PendingEventId}");
            return problems;
        }
    }

    /// <summary>Stores the save as JSON in a directory (the app passes its persistent data folder).
    /// Writes to a temp file first, so a crash mid-save never leaves a half-written save.</summary>
    public sealed class FileSaveStore : ISaveStore
    {
        public readonly string FilePath;

        public FileSaveStore(string directory, string fileName = "founders_save.json")
        {
            FilePath = Path.Combine(directory, fileName);
        }

        public LoadOutcome Load(GameContent content)
        {
            var temp = FilePath + ".tmp";
            if (!File.Exists(FilePath) && File.Exists(temp)) File.Move(temp, FilePath); // crashed between delete and rename
            if (!File.Exists(FilePath)) return LoadOutcome.NoSave;
            string data;
            try { data = File.ReadAllText(FilePath); }
            catch (Exception) { return LoadOutcome.Corrupt("Save file cannot be read."); }
            var outcome = SaveCodec.Decode(data, content);
            if (outcome.Result == LoadResult.Corrupt) Quarantine();
            return outcome;
        }

        public void Save(GameState state, GameContent content, double wallTime)
        {
            var data = SaveCodec.Encode(state, content, wallTime);
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, data);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temp, FilePath);
        }

        public void Reset()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }

        /// <summary>Keeps a copy of a corrupt save next to the original for diagnostics.</summary>
        void Quarantine()
        {
            try
            {
                var backup = FilePath + ".corrupt";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(FilePath, backup);
            }
            catch (Exception)
            {
                // Best effort only.
            }
        }
    }

    /// <summary>In-memory store for tests.</summary>
    public sealed class MemorySaveStore : ISaveStore
    {
        public string Data;
        public LoadOutcome Load(GameContent content) => Data == null ? LoadOutcome.NoSave : SaveCodec.Decode(Data, content);
        public void Save(GameState state, GameContent content, double wallTime) { Data = SaveCodec.Encode(state, content, wallTime); }
        public void Reset() { Data = null; }
    }
}
