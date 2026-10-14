using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Founders.Cloud
{
    /// <summary>
    /// Where cloud saves are kept: one value per key for the signed-in player. A player starts as a guest of this
    /// device (signed in silently); binding a username and password lets the same progress be opened on another
    /// phone. Every call may throw; <see cref="Describe"/> turns the error into a sentence for the player.
    /// </summary>
    public interface ICloudBackend
    {
        /// <summary>False when this build has no cloud service, or the game is not linked to one yet.</summary>
        bool Configured { get; }
        bool SignedIn { get; }
        string PlayerId { get; }
        /// <summary>Signs in silently: the same guest (or bound account) as last time on this device.</summary>
        Task SignInAsync();
        /// <summary>The value under a key, or null.</summary>
        Task<string> LoadAsync(string key);
        Task SaveAsync(string key, string value);
        /// <summary>Binds a username and password to the player signed in now (their cloud save stays).</summary>
        Task LinkAsync(string username, string password);
        /// <summary>Signs in to another bound account (e.g. the player's, on a new phone).</summary>
        Task SwitchAsync(string username, string password);
        /// <summary>Leaves a bound account; the device continues as a new guest.</summary>
        Task SignOutAsync();
        string Describe(Exception e);
    }

    public static class CloudBackends
    {
        /// <summary>The game's cloud server when its address is set (<see cref="CloudConfig"/>); otherwise one that reports "not set up".</summary>
        public static ICloudBackend Create() =>
            string.IsNullOrEmpty(CloudConfig.ServerUrl) ? (ICloudBackend)new NoCloud() : new HttpCloud(CloudConfig.ServerUrl);

        /// <summary>Username rules of the service (3–20 of letters, digits and . - @ _).</summary>
        public static string UsernameProblem(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3 || name.Length > 20) return "Username: 3 to 20 characters.";
            foreach (char c in name)
                if (!(c < 128 && (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '@' || c == '_')))
                    return "Username: letters, numbers and . - @ _ only (no spaces).";
            return null;
        }

        /// <summary>Password rules of the service: 8–30 characters with a capital, a small letter, a number and a symbol.</summary>
        public static string PasswordProblem(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8 || password.Length > 30) return "Password: 8 to 30 characters.";
            bool upper = false, lower = false, digit = false, symbol = false;
            foreach (char c in password)
            {
                if (char.IsUpper(c)) upper = true;
                else if (char.IsLower(c)) lower = true;
                else if (char.IsDigit(c)) digit = true;
                else symbol = true;
            }
            if (!(upper && lower && digit && symbol)) return "Password needs a capital letter, a small letter, a number and a symbol.";
            return null;
        }

        /// <summary>Common wording for errors any service may raise.</summary>
        public static string Friendly(Exception e)
        {
            string m = (e?.Message ?? "").ToLowerInvariant();
            if (m.Contains("network") || m.Contains("transport") || m.Contains("timeout") || m.Contains("timed out") ||
                m.Contains("connection") || m.Contains("resolve host") || m.Contains("offline"))
                return "No connection to the cloud. Check the internet and try again.";
            if (m.Contains("already") && (m.Contains("exist") || m.Contains("taken") || m.Contains("use")))
                return "That username is taken, or this account is already bound.";
            if (m.Contains("invalid username or password") || m.Contains("wrong") || m.Contains("credentials"))
                return "Wrong username or password.";
            if (m.Contains("password")) return "The password does not meet the rules.";
            if (m.Contains("not set up")) return "The cloud is not set up for this build yet.";
            string raw = e?.Message ?? "unknown error";
            return "The cloud did not answer (" + (raw.Length > 80 ? raw.Substring(0, 80) + "…" : raw) + ").";
        }
    }

    /// <summary>A build without a cloud service.</summary>
    public sealed class NoCloud : ICloudBackend
    {
        public bool Configured => false;
        public bool SignedIn => false;
        public string PlayerId => null;
        static Task<T> Fail<T>() => Task.FromException<T>(new InvalidOperationException("The cloud is not set up for this build (no server address)."));
        public Task SignInAsync() => Fail<bool>();
        public Task<string> LoadAsync(string key) => Fail<string>();
        public Task SaveAsync(string key, string value) => Fail<bool>();
        public Task LinkAsync(string username, string password) => Fail<bool>();
        public Task SwitchAsync(string username, string password) => Fail<bool>();
        public Task SignOutAsync() => Fail<bool>();
        public string Describe(Exception e) => CloudBackends.Friendly(e);
    }

    /// <summary>An in-memory cloud (screenshots and tests): everything succeeds at once.</summary>
    public sealed class MemoryCloud : ICloudBackend
    {
        readonly Dictionary<string, Dictionary<string, string>> players = new Dictionary<string, Dictionary<string, string>>();
        readonly Dictionary<string, (string password, string player)> accounts = new Dictionary<string, (string, string)>();
        string player;

        public bool Configured => true;
        public bool SignedIn => player != null;
        public string PlayerId => player;

        Dictionary<string, string> Data => players.TryGetValue(player ?? "", out var d) ? d : players[player] = new Dictionary<string, string>();

        public Task SignInAsync()
        {
            player ??= "guest-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            return Task.CompletedTask;
        }

        public Task<string> LoadAsync(string key) => Task.FromResult(Data.TryGetValue(key, out var v) ? v : null);

        public Task SaveAsync(string key, string value)
        {
            Data[key] = value;
            return Task.CompletedTask;
        }

        public Task LinkAsync(string username, string password)
        {
            if (accounts.ContainsKey(username)) return Task.FromException(new InvalidOperationException("Username already exists."));
            accounts[username] = (password, player);
            return Task.CompletedTask;
        }

        public Task SwitchAsync(string username, string password)
        {
            if (!accounts.TryGetValue(username, out var a) || a.password != password)
                return Task.FromException(new InvalidOperationException("Invalid username or password."));
            player = a.player;
            return Task.CompletedTask;
        }

        public Task SignOutAsync()
        {
            player = null;
            return SignInAsync();
        }

        public string Describe(Exception e) => CloudBackends.Friendly(e);
    }

    /// <summary>Where the game's own cloud server is (see server/cloud/README.md).</summary>
    public static class CloudConfig
    {
        /// <summary>
        /// The HTTPS address of the FOUNDERS cloud API, e.g. "https://founders.example.duckdns.org". Empty: no cloud,
        /// saves stay on the device and Settings says the cloud is not set up.
        /// </summary>
        public const string ServerUrl = "https://founders.scyde.dev";
    }

    /// <summary>
    /// The game's own server (server/cloud: a small HTTPS API in front of MariaDB/MySQL). A device signs in as a guest
    /// and keeps its token; binding a username and password lets another device sign in to the same player.
    /// </summary>
    public sealed class HttpCloud : ICloudBackend
    {
        readonly string baseUrl;
        const int TimeoutSeconds = 20;

        public HttpCloud(string url) { baseUrl = (url ?? "").TrimEnd('/'); }

        public bool Configured => baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        public bool SignedIn => Token != null;
        public string PlayerId { get { var p = PlayerPrefs.GetString("cloud.player", ""); return p.Length > 0 ? p : null; } }

        static string Token
        {
            get { var t = PlayerPrefs.GetString("cloud.token", ""); return t.Length > 0 ? t : null; }
        }

        static void Keep(string playerId, string token)
        {
            PlayerPrefs.SetString("cloud.player", playerId ?? "");
            PlayerPrefs.SetString("cloud.token", token ?? "");
            PlayerPrefs.Save();
        }

        /// <summary>A server answer that is not a success (status and its message).</summary>
        public sealed class ServerError : Exception
        {
            public readonly long Status;
            public ServerError(long status, string message) : base(message) { Status = status; }
        }

        [Serializable] sealed class Session { public string playerId; public string token; }
        [Serializable] sealed class Value { public string value; }
        [Serializable] sealed class Credentials { public string username; public string password; }
        [Serializable] sealed class Guest { public string deviceId; }
        [Serializable] sealed class Error { public string error; }

        async Task<string> Send(string method, string path, object body, bool auth)
        {
            using var req = new UnityEngine.Networking.UnityWebRequest(baseUrl + path, method);
            req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            if (body != null)
            {
                req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            if (auth && Token != null) req.SetRequestHeader("Authorization", "Bearer " + Token);
            req.timeout = TimeoutSeconds;
            var done = new TaskCompletionSource<bool>();
            req.SendWebRequest().completed += _ => done.TrySetResult(true);
            await done.Task;
            string text = req.downloadHandler?.text ?? "";
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError)
                throw new ServerError(0, "network: " + req.error);
            if (req.responseCode >= 200 && req.responseCode < 300) return text;
            string message = req.error;
            try { var e = JsonUtility.FromJson<Error>(text); if (!string.IsNullOrEmpty(e?.error)) message = e.error; }
            catch (Exception) { /* not JSON (a proxy page) */ }
            throw new ServerError(req.responseCode, message ?? $"HTTP {req.responseCode}");
        }

        public async Task SignInAsync()
        {
            if (Token != null) return;
            var s = JsonUtility.FromJson<Session>(await Send("POST", "/v1/guest", new Guest { deviceId = SystemInfo.deviceUniqueIdentifier }, false));
            Keep(s.playerId, s.token);
        }

        /// <summary>Runs a call as the signed-in player; a guest whose token the server forgot starts again as a new guest.</summary>
        async Task<string> Authed(string method, string path, object body)
        {
            await SignInAsync();
            try { return await Send(method, path, body, true); }
            catch (ServerError e) when (e.Status == 401 && PlayerPrefs.GetString("cloud.user", "").Length == 0)
            {
                Keep(null, null);
                await SignInAsync();
                return await Send(method, path, body, true);
            }
        }

        public async Task<string> LoadAsync(string key)
        {
            try { return JsonUtility.FromJson<Value>(await Authed("GET", "/v1/save/" + key, null)).value; }
            catch (ServerError e) when (e.Status == 404) { return null; }
        }

        public async Task SaveAsync(string key, string value) => await Authed("PUT", "/v1/save/" + key, new Value { value = value });

        public async Task LinkAsync(string username, string password) =>
            await Authed("POST", "/v1/link", new Credentials { username = username, password = password });

        public async Task SwitchAsync(string username, string password)
        {
            // This device's token is kept until the new sign-in succeeds.
            var s = JsonUtility.FromJson<Session>(await Send("POST", "/v1/login", new Credentials { username = username, password = password }, false));
            Keep(s.playerId, s.token);
        }

        public async Task SignOutAsync()
        {
            try { await Send("POST", "/v1/logout", null, true); }
            catch (Exception) { /* forgotten on this device either way */ }
            Keep(null, null);
            await SignInAsync();
        }

        public string Describe(Exception e)
        {
            if (e is ServerError se)
            {
                if (se.Status == 401 && se.Message.StartsWith("Invalid", StringComparison.Ordinal)) return "Wrong username or password.";
                if (se.Status == 401) return "Signed out of the cloud. Sign in again in Settings → Account.";
                if (se.Status == 429) return se.Message;
                if (se.Status == 409) return se.Message.StartsWith("Username", StringComparison.Ordinal) ? "That username is taken." : "This device is already bound to an account.";
                if (se.Status == 400) return se.Message; // the server's rule, worded for the player
                if (se.Status >= 500) return "The cloud server has a problem. Your progress is safe on this device.";
            }
            return CloudBackends.Friendly(e);
        }
    }
}
