using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Founders.Cloud;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    public enum CloudState
    {
        /// <summary>This build is not linked to a cloud project.</summary>
        NotSetUp,
        /// <summary>The player switched cloud saving off.</summary>
        Off,
        Connecting,
        /// <summary>Signed in; the cloud holds this device's progress (or is about to).</summary>
        Ready,
        /// <summary>The cloud holds other progress: the player chooses (see <see cref="CloudSave.Pending"/>).</summary>
        Choosing,
        /// <summary>The last attempt failed (no internet…); it is tried again later.</summary>
        Error,
    }

    /// <summary>
    /// Keeps the settlement and the founder's profile in the cloud: signs in silently as a guest of this device,
    /// uploads a few minutes apart (and when the app goes to the background), and when the cloud holds progress
    /// from another device it asks before anything is replaced (<see cref="CloudSync"/>). Binding a username and
    /// password lets the player open the same progress on a new phone.
    /// </summary>
    public sealed class CloudSave
    {
        public const string Key = "founders_snapshot";
        /// <summary>Seconds between automatic uploads.</summary>
        const double UploadEvery = 120, RetryEvery = 60;

        readonly GameStore store;
        public ICloudBackend Backend { get; private set; }

        public CloudState State { get; private set; } = CloudState.NotSetUp;
        /// <summary>What went wrong last (shown in Settings), or null.</summary>
        public string Problem { get; private set; }
        /// <summary>The other progress found in the cloud, while the player chooses.</summary>
        public CloudSnapshot Pending { get; private set; }
        /// <summary>A call is running (buttons wait).</summary>
        public bool Busy { get; private set; }
        /// <summary>Bumped on every change, for the UI's structural keys.</summary>
        public int Version { get; private set; }

        double nextUpload, nextRetry;

        public CloudSave(GameStore owner, ICloudBackend backend)
        {
            store = owner;
            Backend = backend;
        }

        // MARK: - Remembered on this device

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("cloud.on", 1) == 1;
            set { PlayerPrefs.SetInt("cloud.on", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The username bound on this device (null while the player is a guest).</summary>
        public static string Username
        {
            get { var u = PlayerPrefs.GetString("cloud.user", ""); return u.Length > 0 ? u : null; }
            private set { PlayerPrefs.SetString("cloud.user", value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>When this device last sent or took progress (Unix seconds, 0 never).</summary>
        public static double LastSync
        {
            get => double.TryParse(PlayerPrefs.GetString("cloud.lastSync", "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0;
            private set { PlayerPrefs.SetString("cloud.lastSync", value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); PlayerPrefs.Save(); }
        }

        static string LastUploadId
        {
            get { var u = PlayerPrefs.GetString("cloud.lastUpload", ""); return u.Length > 0 ? u : null; }
            set { PlayerPrefs.SetString("cloud.lastUpload", value ?? ""); PlayerPrefs.Save(); }
        }

        static string DeviceId
        {
            get
            {
                var id = PlayerPrefs.GetString("cloud.device", "");
                if (id.Length == 0) { id = Guid.NewGuid().ToString("N"); PlayerPrefs.SetString("cloud.device", id); PlayerPrefs.Save(); }
                return id;
            }
        }

        public static string DeviceName
        {
            get
            {
                string n = SystemInfo.deviceName;
                if (string.IsNullOrEmpty(n) || n == SystemInfo.unsupportedIdentifier || n == "<unknown>") n = SystemInfo.deviceModel;
                return string.IsNullOrEmpty(n) ? "This device" : n;
            }
        }

        public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        // MARK: - Flow

        /// <summary>Connects (silently) and settles which progress the cloud keeps.</summary>
        public void Start()
        {
            if (!Backend.Configured) { Set(CloudState.NotSetUp); return; }
            if (!Enabled) { Set(CloudState.Off); return; }
            _ = Connect();
        }

        async Task Connect()
        {
            if (Busy) return;
            Busy = true;
            Set(CloudState.Connecting);
            try
            {
                await Backend.SignInAsync();
                string raw = await Backend.LoadAsync(Key);
                var cloud = Decode(raw);
                if (Unreadable(raw, cloud)) return;
                if (CloudSync.Decide(cloud, LastUploadId) == CloudAction.Upload)
                {
                    Busy = false;
                    await Upload();
                }
                else
                {
                    Pending = cloud;
                    Set(CloudState.Choosing);
                }
            }
            catch (Exception e) { Fail(e); }
            finally { Busy = false; Version++; }
        }

        /// <summary>Called after each local save: uploads now and then while the cloud is in step.</summary>
        public void Tick(bool force = false)
        {
            double now = Now;
            if (State == CloudState.Error && Enabled && Backend.Configured && now >= nextRetry && !Busy) { _ = Connect(); return; }
            if (State != CloudState.Ready || Busy) return;
            if (force || now >= nextUpload) _ = Upload();
        }

        async Task Upload()
        {
            if (Busy || (store.Ephemeral && !(Backend is MemoryCloud))) return;
            Busy = true;
            Version++;
            try
            {
                var snap = CloudSnapshot.Make(store.Game.State, store.Content, store.Profile, DeviceId, DeviceName, Now);
                await Backend.SaveAsync(Key, Encode(snap));
                LastUploadId = snap.UploadId;
                LastSync = snap.SavedAt;
                nextUpload = Now + UploadEvery;
                Problem = null;
                Set(CloudState.Ready);
            }
            catch (Exception e) { Fail(e); }
            finally { Busy = false; Version++; }
        }

        /// <summary>Uploads at once (the "Save to cloud" button).</summary>
        public void UploadNow()
        {
            if (State == CloudState.Ready) _ = Upload();
            else if (State == CloudState.Error || State == CloudState.Off) { Enabled = true; _ = Connect(); }
        }

        /// <summary>Looks in the cloud again and offers what is there (the "Load from cloud" button).</summary>
        public async void CheckCloud()
        {
            if (Busy || !Backend.Configured) return;
            Busy = true;
            Version++;
            try
            {
                await Backend.SignInAsync();
                string raw = await Backend.LoadAsync(Key);
                var cloud = Decode(raw);
                if (Unreadable(raw, cloud)) return;
                if (cloud == null) { Problem = "There is no save in the cloud yet."; Set(CloudState.Ready); }
                else { Pending = cloud; Set(CloudState.Choosing); }
            }
            catch (Exception e) { Fail(e); }
            finally { Busy = false; Version++; }
        }

        /// <summary>The player keeps this device's progress: it replaces the cloud's.</summary>
        public void KeepDevice()
        {
            Pending = null;
            Set(CloudState.Ready);
            _ = Upload();
        }

        /// <summary>The player takes the cloud's progress: it replaces this device's.</summary>
        public void UseCloud()
        {
            var cloud = Pending;
            if (cloud == null) return;
            var (state, profile, problem) = CloudSync.Open(cloud, store.Content);
            if (problem != null) { Problem = problem; Pending = null; Set(CloudState.Ready); return; }
            Pending = null;
            LastUploadId = cloud.UploadId;
            LastSync = Now;
            store.Restore(state, profile);
            Set(CloudState.Ready);
            nextUpload = Now + UploadEvery;
        }

        public void SetEnabled(bool on)
        {
            Enabled = on;
            if (!Backend.Configured) return;
            if (on) _ = Connect();
            else { Pending = null; Set(CloudState.Off); }
        }

        /// <summary>Binds a username and password to this device's cloud save. Returns the problem, or null.</summary>
        public async Task<string> Bind(string username, string password)
        {
            var problem = CloudBackends.UsernameProblem(username) ?? CloudBackends.PasswordProblem(password);
            if (problem != null) return problem;
            if (Busy) return "Please wait a moment.";
            Busy = true;
            Version++;
            try
            {
                await Backend.LinkAsync(username, password);
                Username = username;
                Busy = false;
                if (State == CloudState.Ready) await Upload();
                return null;
            }
            catch (Exception e) { return Backend.Describe(e); }
            finally { Busy = false; Version++; }
        }

        /// <summary>Signs in to a bound account (e.g. on a new phone) and offers its progress.</summary>
        public async Task<string> SignIn(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return "Enter the username and the password.";
            if (Busy) return "Please wait a moment.";
            Busy = true;
            Version++;
            try
            {
                Enabled = true;
                await Backend.SwitchAsync(username, password);
                Username = username;
                LastUploadId = null; // a different cloud: nothing here was sent to it
                string raw = await Backend.LoadAsync(Key);
                var cloud = Decode(raw);
                if (Unreadable(raw, cloud)) return Problem;
                Busy = false;
                if (cloud == null) { Set(CloudState.Ready); await Upload(); }
                else { Pending = cloud; Set(CloudState.Choosing); }
                return null;
            }
            catch (Exception e) { return Backend.Describe(e); }
            finally { Busy = false; Version++; }
        }

        /// <summary>Leaves the bound account; this device carries on as a new guest with its progress.</summary>
        public async void SignOut()
        {
            if (Busy) return;
            Busy = true;
            Version++;
            try
            {
                await Backend.SignOutAsync();
                Username = null;
                LastUploadId = null;
                Busy = false;
                Set(CloudState.Ready);
                await Upload();
            }
            catch (Exception e) { Fail(e); }
            finally { Busy = false; Version++; }
        }

        /// <summary>
        /// The cloud holds something this build cannot read (a newer version of the game saved it): it is never
        /// overwritten; the player is told to update.
        /// </summary>
        bool Unreadable(string raw, CloudSnapshot cloud)
        {
            if (string.IsNullOrEmpty(raw) || cloud != null) return false;
            Problem = "The cloud save is from a newer version of the game. Update the game to use it.";
            nextRetry = Now + 3600;
            Set(CloudState.Error);
            return true;
        }

        void Fail(Exception e)
        {
            Problem = Backend.Describe(e);
            Debug.LogWarning("[FOUNDERS] Cloud: " + e.Message);
            nextRetry = Now + RetryEvery;
            Set(CloudState.Error);
        }

        void Set(CloudState s)
        {
            State = s;
            Version++;
        }

        /// <summary>Screenshots: a cloud that is signed in, bound and in step.</summary>
        public void Demo(string username)
        {
            Username = username;
            Backend = new MemoryCloud();
            Enabled = true;
            _ = Connect();
        }

        /// <summary>Screenshots: the cloud holds progress from another device.</summary>
        public void DemoChoice()
        {
            var snap = CloudSnapshot.Make(store.Game.State, store.Content, store.Profile, "other", "iPad", Now - 3 * 3600);
            snap.Population = Math.Max(1, snap.Population - 3);
            snap.Buildings = Math.Max(1, snap.Buildings - 4);
            snap.Prosperity = (int)(snap.Prosperity * 0.8);
            snap.SimTime *= 0.8;
            Pending = snap;
            Set(CloudState.Choosing);
        }

        // MARK: - Encoding (gzip + base64: saves are text and shrink a lot)

        public static string Encode(CloudSnapshot snap)
        {
            var bytes = Encoding.UTF8.GetBytes(snap.Encode());
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) gz.Write(bytes, 0, bytes.Length);
            return "gz:" + Convert.ToBase64String(ms.ToArray());
        }

        public static CloudSnapshot Decode(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            try
            {
                if (!value.StartsWith("gz:", StringComparison.Ordinal)) return CloudSnapshot.Decode(value);
                using var input = new MemoryStream(Convert.FromBase64String(value.Substring(3)));
                using var gz = new GZipStream(input, CompressionMode.Decompress);
                using var reader = new StreamReader(gz, Encoding.UTF8);
                return CloudSnapshot.Decode(reader.ReadToEnd());
            }
            catch (Exception) { return null; }
        }
    }
}
