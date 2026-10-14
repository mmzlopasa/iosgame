using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#if FOUNDERS_UGS
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
#endif

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
        /// <summary>The real service when the game is built with it; otherwise one that reports "not set up".</summary>
        public static ICloudBackend Create()
        {
#if FOUNDERS_UGS
            return new UgsCloud();
#else
            return new NoCloud();
#endif
        }

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
            if (m.Contains("project") && m.Contains("id")) return "The cloud is not set up for this build yet.";
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
        static Task<T> Fail<T>() => Task.FromException<T>(new InvalidOperationException("The cloud is not set up for this build (no project id)."));
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

#if FOUNDERS_UGS
    /// <summary>
    /// Unity Gaming Services: Authentication (guest sign-in, username and password binding) and Cloud Save (player
    /// data). Needs the game linked to a Unity Cloud project (its project id is set when the game is built).
    /// </summary>
    public sealed class UgsCloud : ICloudBackend
    {
        bool initialized;

        public bool Configured => !string.IsNullOrEmpty(Application.cloudProjectId);
        public bool SignedIn => initialized && AuthenticationService.Instance.IsSignedIn;
        public string PlayerId => SignedIn ? AuthenticationService.Instance.PlayerId : null;

        async Task Init()
        {
            if (initialized) return;
            await UnityServices.InitializeAsync();
            initialized = true;
        }

        public async Task SignInAsync()
        {
            await Init();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public async Task<string> LoadAsync(string key)
        {
            await SignInAsync();
            var data = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { key });
            return data.TryGetValue(key, out var item) ? item.Value.GetAs<string>() : null;
        }

        public async Task SaveAsync(string key, string value)
        {
            await SignInAsync();
            await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { key, value } });
        }

        public async Task LinkAsync(string username, string password)
        {
            await SignInAsync();
            await AuthenticationService.Instance.AddUsernamePasswordAsync(username, password);
        }

        public async Task SwitchAsync(string username, string password)
        {
            await Init();
            // Keep this device's guest session token: if the new sign-in fails the guest comes back.
            if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(false);
            try
            {
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
            }
            catch
            {
                try { await AuthenticationService.Instance.SignInAnonymouslyAsync(); }
                catch (Exception) { /* offline: signs in again on the next attempt */ }
                throw;
            }
        }

        public async Task SignOutAsync()
        {
            await Init();
            AuthenticationService.Instance.SignOut(true);
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public string Describe(Exception e) => CloudBackends.Friendly(e);
    }
#endif
}
