using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Founders.Core
{
    /// <summary>
    /// The player behind the settlement (the founder): name, portrait, portrait frame and play statistics.
    /// It lives in its own file next to the save, so resetting the settlement keeps it, and it travels to the
    /// cloud together with the save.
    /// </summary>
    public sealed class PlayerProfile
    {
        public const int NameMin = 3, NameMax = 16;

        public string Name;
        public string Avatar = Avatars[0].Id;
        public string Frame = Frames[0].Id;
        /// <summary>When the profile was made (Unix seconds).</summary>
        public double CreatedAt;
        public int Renames;
        /// <summary>Seconds the game was open in front of the player.</summary>
        public double PlaySeconds;
        public int Sessions;

        public sealed class Portrait
        {
            public string Id, Name;
            public Portrait(string id, string name) { Id = id; Name = name; }
        }

        /// <summary>A frame around the portrait; unlocked by milestones reached (any settlement on this profile).</summary>
        public sealed class FrameDef
        {
            public string Id, Name;
            public int Milestones;
            public FrameDef(string id, string name, int milestones) { Id = id; Name = name; Milestones = milestones; }
        }

        /// <summary>Portraits (icon names in the UI's icon set).</summary>
        public static readonly Portrait[] Avatars =
        {
            new Portrait("face_m1", "Elder"), new Portrait("face_f1", "Keeper"), new Portrait("face_m2", "Hunter"),
            new Portrait("face_f2", "Healer"), new Portrait("face_m3", "Builder"), new Portrait("face_f3", "Weaver"),
        };

        public static readonly FrameDef[] Frames =
        {
            new FrameDef("wood", "Driftwood", 0), new FrameDef("copper", "Copper", 1), new FrameDef("silver", "Silver", 2),
            new FrameDef("amethyst", "Amethyst", 4), new FrameDef("gold", "Gold", 5),
        };

        /// <summary>The name a new founder gets: "Founder" and the first four digits of the settlement's ID.</summary>
        public static PlayerProfile New(string settlementId, double now)
        {
            string digits = new string((settlementId ?? "").Where(char.IsLetterOrDigit).Take(4).ToArray()).ToUpperInvariant();
            return new PlayerProfile { Name = "Founder" + (digits.Length > 0 ? " " + digits : ""), CreatedAt = now };
        }

        /// <summary>Why a name cannot be used, or null. Letters (any language), digits, spaces, _ - . and '.</summary>
        public static string NameProblem(string name)
        {
            string n = CleanName(name);
            if (n.Length < NameMin) return $"At least {NameMin} characters.";
            if (n.Length > NameMax) return $"At most {NameMax} characters.";
            foreach (char c in n)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-' || c == '.' || c == '\''))
                    return "Use letters, numbers, spaces and _ - . ' only.";
            if (!n.Any(char.IsLetter)) return "Use at least one letter.";
            return null;
        }

        /// <summary>Trims and folds runs of spaces into one.</summary>
        public static string CleanName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
            {
                if (c == ' ' && sb.Length > 0 && sb[sb.Length - 1] == ' ') continue;
                sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
            }
            return sb.ToString();
        }

        /// <summary>Renames the founder. Returns the problem, or null when the name was taken.</summary>
        public string Rename(string name)
        {
            var problem = NameProblem(name);
            if (problem != null) return problem;
            string n = CleanName(name);
            if (n == Name) return null;
            Name = n;
            Renames++;
            return null;
        }

        public static bool FrameUnlocked(string id, int milestones) =>
            Frames.Any(f => f.Id == id && milestones >= f.Milestones);

        /// <summary>Picks a portrait; unknown ids are ignored.</summary>
        public bool SetAvatar(string id)
        {
            if (Avatars.All(a => a.Id != id)) return false;
            Avatar = id;
            return true;
        }

        /// <summary>Picks a frame if it is unlocked.</summary>
        public bool SetFrame(string id, int milestones)
        {
            if (!FrameUnlocked(id, milestones)) return false;
            Frame = id;
            return true;
        }

        public PlayerProfile Clone() => FromJson(ToJson());

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["name"] = Name,
            ["avatar"] = Avatar,
            ["frame"] = Frame,
            ["createdAt"] = CreatedAt,
            ["renames"] = Renames,
            ["playSeconds"] = PlaySeconds,
            ["sessions"] = Sessions,
        };

        /// <summary>Reads a profile; anything missing or unknown falls back to the defaults.</summary>
        public static PlayerProfile FromJson(Dictionary<string, object> raw)
        {
            var o = new JObj(raw, "profile");
            var p = new PlayerProfile
            {
                Name = o.OptStr("name"),
                CreatedAt = o.OptNum("createdAt") ?? 0,
                Renames = Math.Max(0, o.OptInt("renames") ?? 0),
                PlaySeconds = Math.Max(0, o.OptNum("playSeconds") ?? 0),
                Sessions = Math.Max(0, o.OptInt("sessions") ?? 0),
            };
            if (NameProblem(p.Name) != null) p.Name = "Founder";
            p.SetAvatar(o.OptStr("avatar"));
            string frame = o.OptStr("frame");
            if (Frames.Any(f => f.Id == frame)) p.Frame = frame;
            if (double.IsNaN(p.PlaySeconds) || double.IsInfinity(p.PlaySeconds)) p.PlaySeconds = 0;
            return p;
        }

        public string Encode() => Json.Write(ToJson());

        /// <summary>Decodes a stored profile, or null when the text is not a profile.</summary>
        public static PlayerProfile Decode(string text)
        {
            try { return Json.Parse(text) is Dictionary<string, object> d ? FromJson(d) : null; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// One number for how far a settlement has come (the profile's "Prosperity", like a power score):
        /// buildings and their levels, people, discoveries, explored land, legacy and milestones.
        /// </summary>
        public static int Prosperity(GameState s)
        {
            double p = 0;
            foreach (var b in s.Buildings)
                if (b.Status != BuildingStatus.Constructing) p += 60 + 40 * Math.Max(1, b.Level);
            p += 45 * s.Population;
            p += 120 * s.Researched.Count;
            p += 150 * s.ExploredRegions.Count;
            p += 15 * s.Legacy.Count;
            p += 400 * s.ReachedMilestones.Count;
            p += 80 * s.Laws.Count;
            return (int)Math.Round(p);
        }
    }

    /// <summary>Keeps the profile as JSON in a directory (written via a temp file, like the save).</summary>
    public sealed class FileProfileStore
    {
        public readonly string FilePath;

        public FileProfileStore(string directory, string fileName = "founders_profile.json")
        {
            FilePath = Path.Combine(directory, fileName);
        }

        public PlayerProfile Load()
        {
            try { return File.Exists(FilePath) ? PlayerProfile.Decode(File.ReadAllText(FilePath)) : null; }
            catch (Exception) { return null; }
        }

        public void Save(PlayerProfile profile)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, profile.Encode());
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temp, FilePath);
        }
    }

    /// <summary>
    /// What goes to the cloud: the save and the profile, plus a short summary (shown when the player chooses
    /// between this device and the cloud) and where and when it was uploaded.
    /// </summary>
    public sealed class CloudSnapshot
    {
        public const int CurrentFormat = 1;

        public int Format = CurrentFormat;
        /// <summary>A new id for every upload: a device that uploaded it knows the cloud still holds its own progress.</summary>
        public string UploadId;
        public string DeviceId, DeviceName;
        /// <summary>Unix seconds.</summary>
        public double SavedAt;
        public string FounderName, SettlementName, StageId, EpochId;
        public int Population, Buildings, Prosperity;
        public double SimTime;
        /// <summary>The save file's text (<see cref="SaveCodec"/>).</summary>
        public string Save;
        /// <summary>The profile's text.</summary>
        public string Profile;

        public static CloudSnapshot Make(GameState state, GameContent content, PlayerProfile profile, string deviceId, string deviceName, double now)
        {
            return new CloudSnapshot
            {
                UploadId = Guid.NewGuid().ToString("N"),
                DeviceId = deviceId,
                DeviceName = deviceName,
                SavedAt = now,
                FounderName = profile.Name,
                SettlementName = state.SettlementName,
                StageId = state.StageId,
                EpochId = state.EpochId,
                Population = state.Population,
                Buildings = state.Buildings.Count(b => b.Status != BuildingStatus.Constructing),
                Prosperity = PlayerProfile.Prosperity(state),
                SimTime = state.SimTime,
                Save = SaveCodec.Encode(state, content, now),
                Profile = profile.Encode(),
            };
        }

        public string Encode() => Json.Write(new Dictionary<string, object>
        {
            ["format"] = Format,
            ["uploadId"] = UploadId,
            ["deviceId"] = DeviceId,
            ["deviceName"] = DeviceName,
            ["savedAt"] = SavedAt,
            ["founderName"] = FounderName,
            ["settlementName"] = SettlementName,
            ["stageId"] = StageId,
            ["epochId"] = EpochId,
            ["population"] = Population,
            ["buildings"] = Buildings,
            ["prosperity"] = Prosperity,
            ["simTime"] = SimTime,
            ["save"] = Save,
            ["profile"] = Profile,
        });

        /// <summary>Reads a snapshot, or null when it is damaged or from a newer game version.</summary>
        public static CloudSnapshot Decode(string text)
        {
            try
            {
                var o = new JObj(Json.Parse(text), "snapshot");
                var s = new CloudSnapshot
                {
                    Format = o.Int("format"),
                    UploadId = o.Str("uploadId"),
                    DeviceId = o.OptStr("deviceId"),
                    DeviceName = o.OptStr("deviceName"),
                    SavedAt = o.OptNum("savedAt") ?? 0,
                    FounderName = o.OptStr("founderName"),
                    SettlementName = o.OptStr("settlementName"),
                    StageId = o.OptStr("stageId"),
                    EpochId = o.OptStr("epochId"),
                    Population = o.OptInt("population") ?? 0,
                    Buildings = o.OptInt("buildings") ?? 0,
                    Prosperity = o.OptInt("prosperity") ?? 0,
                    SimTime = o.OptNum("simTime") ?? 0,
                    Save = o.Str("save"),
                    Profile = o.OptStr("profile"),
                };
                return s.Format > CurrentFormat ? null : s;
            }
            catch (Exception) { return null; }
        }
    }

    public enum CloudAction
    {
        /// <summary>The cloud is empty or holds this device's own last upload: send this device's progress.</summary>
        Upload,
        /// <summary>The cloud holds progress from another device (or an earlier install): the player chooses.</summary>
        Choose,
    }

    /// <summary>The rule for a device meeting the cloud. It never overwrites progress the player has not seen.</summary>
    public static class CloudSync
    {
        /// <param name="cloud">What the cloud holds (null when empty).</param>
        /// <param name="lastUploadId">The id of this device's last upload or restore (null if none).</param>
        public static CloudAction Decide(CloudSnapshot cloud, string lastUploadId)
        {
            if (cloud == null) return CloudAction.Upload;
            if (!string.IsNullOrEmpty(lastUploadId) && cloud.UploadId == lastUploadId) return CloudAction.Upload;
            return CloudAction.Choose;
        }

        /// <summary>Checks a snapshot can be loaded by this build; returns the state and profile or the reason it cannot.</summary>
        public static (GameState state, PlayerProfile profile, string problem) Open(CloudSnapshot cloud, GameContent content)
        {
            if (cloud == null) return (null, null, "There is no save in the cloud.");
            var outcome = SaveCodec.Decode(cloud.Save, content);
            switch (outcome.Result)
            {
                case LoadResult.Loaded:
                    var profile = cloud.Profile != null ? PlayerProfile.Decode(cloud.Profile) : null;
                    return (outcome.State, profile, null);
                case LoadResult.UnsupportedVersion:
                    return (null, null, "The cloud save is from a newer version of the game. Update the game first.");
                default:
                    return (null, null, "The cloud save could not be read.");
            }
        }
    }
}
