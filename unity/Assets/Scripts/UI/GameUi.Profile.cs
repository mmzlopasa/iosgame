using System;
using System.Globalization;
using System.Linq;
using Founders.Cloud;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// The founder's profile and the settings, after the profile screens of mobile strategy games: the portrait in its
    /// frame, the name (rename), the ID (copy), a prosperity score and quick tiles (Portrait, Rename, Cloud Save,
    /// Settings); a Stats tab and an Achievements tab (milestones and the portrait frames they unlock). Settings has
    /// Game, Account (cloud save, binding an account, signing in on a new phone) and About. Dialogs for the portrait,
    /// the name, the account and the cloud choice open over the world. Everything is built from the kit's parts
    /// (<see cref="E"/>), so a new skin restyles these screens too.
    /// </summary>
    public partial class GameUi
    {
        int profileTab, settingsTab;
        /// <summary>The profile dialog on screen: "avatar", "rename", "bind" or "signin" (null: none).</summary>
        string profileDialog;
        string fieldName = "", fieldUser = "", fieldPass = "", dialogError;
        bool dialogBusy;
        int versionTaps;

        PlayerProfile Founder => store.Profile;
        CloudSave Cloud => store.Cloud;

        /// <summary>The Account tab wants the player (a choice to make, or a problem).</summary>
        bool CloudNeedsPlayer => Cloud.State == CloudState.Choosing || Cloud.State == CloudState.Error;

        static bool DevTools
        {
            get => Debug.isDebugBuild || PlayerPrefs.GetInt("dev.tools", 0) == 1;
            set { PlayerPrefs.SetInt("dev.tools", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        int CurrentTab() => store.ActivePanel switch
        {
            Panel.People => peopleTab,
            Panel.Profile => profileTab,
            Panel.Settings => settingsTab,
            _ => mapTab,
        };

        void SetTab(int i)
        {
            switch (store.ActivePanel)
            {
                case Panel.People: peopleTab = i; break;
                case Panel.Profile: profileTab = i; break;
                case Panel.Settings: settingsTab = i; break;
                default: mapTab = i; break;
            }
        }

        /// <summary>Opens a profile window on a tab.</summary>
        public void ShowProfile(int tab = 0) { profileTab = tab; store.ActivePanel = Panel.Profile; dirty = true; }
        public void ShowSettings(int tab = 0) { settingsTab = tab; store.ActivePanel = Panel.Settings; dirty = true; }

        /// <summary>Opens a profile dialog (capture mode uses it too).</summary>
        public void ShowProfileDialog(string kind)
        {
            profileDialog = kind;
            dialogError = null;
            fieldName = kind == "rename" ? Founder.Name : fieldName;
            if (kind == "bind" || kind == "signin") fieldPass = "";
            dirty = true;
        }

        void CloseDialog() { profileDialog = null; dialogError = null; dialogBusy = false; fieldPass = ""; }

        static E.Cell FrameCell(string frame) => frame switch
        {
            "copper" => E.Cell.Uncommon,
            "silver" => E.Cell.Rare,
            "amethyst" => E.Cell.Epic,
            "gold" => E.Cell.Legendary,
            _ => E.Cell.Common,
        };

        string FounderId()
        {
            string raw = (G.State.SettlementId ?? "").Replace("-", "").ToUpperInvariant();
            if (raw.Length < 8) raw = raw.PadRight(8, '0');
            return raw.Substring(0, 4) + "-" + raw.Substring(4, 4);
        }

        /// <summary>"just now", "5 min ago", "3 h ago", "2 days ago".</summary>
        static string Ago(double unixSeconds)
        {
            if (unixSeconds <= 0) return "never";
            double s = Math.Max(0, CloudSave.Now - unixSeconds);
            if (s < 60) return "just now";
            if (s < 3600) return $"{(int)(s / 60)} min ago";
            if (s < 86400) return $"{(int)(s / 3600)} h ago";
            int d = (int)(s / 86400);
            return d == 1 ? "1 day ago" : $"{d} days ago";
        }

        static string Date(double unixSeconds) => unixSeconds <= 0 ? "—" :
            DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds).ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        /// <summary>Hours and minutes: "12 h 05 min", "42 min".</summary>
        static string Hours(double seconds)
        {
            int m = (int)(Math.Max(0, seconds) / 60);
            return m >= 60 ? $"{m / 60} h {m % 60:00} min" : $"{m} min";
        }

        string StageNameOf(string epochId, string stageId) =>
            G.Content.Epoch(epochId)?.Stages.FirstOrDefault(s => s.Id == stageId)?.Name ?? stageId ?? "—";

        /// <summary>The cloud's state in a few words, with an icon and a colour.</summary>
        (string icon, string text, Color? color) CloudLine()
        {
            switch (Cloud.State)
            {
                case CloudState.NotSetUp: return ("storage", "Saved on this device", Theme.Pale);
                case CloudState.Off: return ("storage", "Cloud save is off", Theme.Pale);
                case CloudState.Connecting: return ("world", "Connecting to the cloud…", Theme.Icy);
                case CloudState.Choosing: return ("alert", "Choose which progress to keep", Theme.Gold);
                case CloudState.Error: return ("alert", "Cloud: will retry soon", Theme.Bad);
                default: return ("check", Cloud.Busy ? "Saving to the cloud…" : $"Cloud synced {Ago(CloudSave.LastSync)}", Theme.Up);
            }
        }

        // MARK: - Profile window

        string ProfileKey() =>
            $"{Founder.Name}|{Founder.Avatar}|{Founder.Frame}|{G.State.ReachedMilestones.Count}|{G.State.Buildings.Count}|{G.State.StageId}|{G.State.Researched.Count}|{Cloud.State}|{Cloud.Busy}";

        void BuildProfilePanel(VisualElement host)
        {
            switch (profileTab)
            {
                case 1: BuildProfileStats(host); break;
                case 2: BuildProfileAchievements(host); break;
                default: BuildProfileMain(host); break;
            }
        }

        void BuildProfileMain(VisualElement host)
        {
            // The hero card: portrait in its frame, name and ID, the settlement, prosperity.
            var hero = E.Inset().Row(0, Align.FlexStart).Margin(0, 0, 8);
            hero.Pad(10, 12);
            const float face = 104;
            var left = E.Div().Col(Align.Center).Ignore();
            left.style.width = face;
            var portrait = E.Slot(Founder.Avatar, face, FrameCell(Founder.Frame), 0.84f).Rarity(FrameCell(Founder.Frame));
            Tap(portrait, () => ShowProfileDialog("avatar"));
            left.Add(portrait);
            left.Add(E.T(PlayerProfile.Frames.FirstOrDefault(f => f.Id == Founder.Frame)?.Name + " frame", Theme.Small, Theme.Pale).Fit(face + 10).Margin(4));
            hero.Add(left);

            float scoreW = Portrait ? 0 : 128;
            float midW = InnerW - 24 - face - 14 - (scoreW > 0 ? scoreW + 10 : 0);
            var mid = E.Div().Col().Grow().Ignore();
            mid.style.marginLeft = 14;
            mid.Add(E.T(Founder.Name, Theme.H1 + 4, Theme.Gold).Fit(midW, Theme.H2));
            var idRow = E.Div().Row().Ignore().Margin(4);
            var id = E.Capsule(30);
            id.Add(E.T("ID " + FounderId(), Theme.Body));
            id.Add(E.Icon("copy", 20).Margin(0, 0, 0, 6));
            Tap(id, () => { GUIUtility.systemCopyBuffer = FounderId(); store.Notify("Founder ID copied."); });
            idRow.Add(id);
            mid.Add(idRow);
            var home = E.Div().Row().Ignore().Margin(6);
            home.Add(E.Icon("home", 22));
            home.Add(E.T(G.State.SettlementName ?? "Settlement", Theme.Num).Fit(midW - 28).Margin(0, 0, 0, 6));
            mid.Add(home);
            mid.Add(E.T($"{G.StageName} · Era {Fmt.Roman(G.Content.Epoch(G.State.EpochId)?.Index ?? 1)} · {G.EpochName}", Theme.Body, Theme.Pale).Fit(midW).Margin(2));
            var (cIcon, _, cColor) = CloudLine();
            var cloudRow = E.Div().Row().Margin(6);
            cloudRow.Add(E.Icon(cIcon, 20));
            cloudRow.Add(LiveFit(() => CloudLine().text, Theme.Body, midW - 26, cColor).Margin(0, 0, 0, 6));
            Tap(cloudRow, () => ShowSettings(1));
            mid.Add(cloudRow);
            hero.Add(mid);

            if (scoreW > 0)
            {
                var score = E.Div().Col(Align.Center).Skin("slot").Pad(10, 8).Ignore();
                score.style.width = scoreW;
                score.style.alignSelf = Align.Center;
                score.Add(E.Icon("sparkle", 34));
                score.Add(Live(() => PlayerProfile.Prosperity(G.State).ToString("#,0", CultureInfo.InvariantCulture), Theme.H1, Theme.Gold).Margin(2));
                score.Add(E.T("Prosperity", Theme.Small, Theme.Pale));
                hero.Add(score);
            }
            host.Add(hero);

            // Quick tiles.
            var tiles = E.Div().Row().Ignore().Margin(0, 0, 8);
            tiles.style.justifyContent = Justify.SpaceBetween;
            var actions = new (string icon, string label, Action act, bool badge)[]
            {
                ("user", "Portrait", () => ShowProfileDialog("avatar"), false),
                ("legacy", "Rename", () => ShowProfileDialog("rename"), false),
                ("world", "Cloud Save", () => ShowSettings(1), CloudNeedsPlayer),
                ("settings", "Settings", () => ShowSettings(0), false),
            };
            float tileW = Mathf.Floor((InnerW - 8 * (actions.Length - 1)) / actions.Length);
            foreach (var a in actions)
            {
                var t = E.Div().Col(Align.Center).Skin("section").Pad(8, 4).Size(tileW);
                t.style.flexShrink = 0;
                t.Add(E.Slot(a.icon, 50, E.Cell.Tile, 0.64f));
                t.Add(E.T(a.label, Theme.Body).Fit(tileW - 10).Margin(4));
                if (a.badge) t.Add(E.Badge("!").Abs(null, 4, 6));
                var act = a.act;
                Tap(t, act);
                tiles.Add(t);
            }
            host.Add(tiles);

            if (Portrait)
            {
                var score = E.Inset().Row().Margin(0, 0, 8);
                score.Add(E.Icon("sparkle", 28));
                score.Add(E.T("Prosperity", Theme.Num).Flex().Margin(0, 0, 0, 8));
                score.Add(Live(() => PlayerProfile.Prosperity(G.State).ToString("#,0", CultureInfo.InvariantCulture), Theme.H2, Theme.Gold));
                host.Add(score);
            }

            var grid = E.Div().Row().Ignore();
            grid.style.justifyContent = Justify.SpaceBetween;
            var stats = new (string label, Func<string> value, string icon)[]
            {
                ("People", () => $"{G.State.Population}", "people"),
                ("Buildings", () => $"{G.State.Buildings.Count(b => b.Status == BuildingStatus.Complete)}", "home"),
                ("Played", () => Hours(Founder.PlaySeconds), "clock"),
            };
            float w = Mathf.Floor((InnerW - 16) / 3);
            foreach (var s in stats)
            {
                var c = StatBox(s.icon, s.label, s.value);
                c.style.flexGrow = 0;
                c.style.width = w;
                grid.Add(c);
            }
            host.Add(grid);
        }

        void BuildProfileStats(VisualElement host)
        {
            var town = Block("Settlement", out var rows);
            int techs = G.Content.Technologies.Count;
            int best = G.State.Buildings.Count == 0 ? 0 : G.State.Buildings.Max(b => b.Level);
            var lines = new (string icon, string label, string value)[]
            {
                ("home", "Name", G.State.SettlementName ?? "—"),
                ("people", "Population", $"{G.State.Population}"),
                ("build", "Buildings", $"{G.State.Buildings.Count(b => b.Status == BuildingStatus.Complete)}"),
                ("level_up", "Highest building level", $"Lv.{best}"),
                ("knowledge", "Discoveries", $"{G.State.Researched.Count}/{techs}"),
                ("compass", "Regions explored", $"{G.State.ExploredRegions.Count}/{G.Content.Regions.Count}"),
                ("customs", "Customs signed", $"{G.State.Laws.Count}"),
                ("legacy", "Legacy entries", $"{G.State.Legacy.Count}"),
                ("clock", "Age of the settlement", Fmt.Clock(G.State.SimTime)),
            };
            for (int i = 0; i < lines.Length; i++)
                rows.Add(E.StatRow(lines[i].icon, lines[i].label, lines[i].value, null, i > 0));
            host.Add(town);

            var me = Block("Founder", out var frows);
            frows.Add(E.StatRow("user", "Name", Founder.Name, null, false));
            frows.Add(E.StatRow("info", "Founder ID", FounderId()));
            frows.Add(E.StatRow("sparkle", "Prosperity", PlayerProfile.Prosperity(G.State).ToString("#,0", CultureInfo.InvariantCulture), null, true, Theme.Gold));
            frows.Add(E.StatRow("clock", "Time played", Hours(Founder.PlaySeconds)));
            frows.Add(E.StatRow("bell", "Sessions", $"{Founder.Sessions}"));
            frows.Add(E.StatRow("spring", "Founder since", Date(Founder.CreatedAt)));
            host.Add(me);
        }

        void BuildProfileAchievements(VisualElement host)
        {
            var ms = Block("Milestones", out var mrows);
            var cells = E.Div().Row().Ignore().Margin(8, 0, 6);
            cells.style.flexWrap = Wrap.Wrap;
            // As many 112-wide cells as the section holds, sharing its width evenly.
            float rowW = InnerW - 20;
            float cellW = Mathf.Floor(rowW / Mathf.Max(1, Mathf.Floor(rowW / 112)));
            foreach (var m in G.Content.Milestones)
            {
                bool got = G.State.ReachedMilestones.Contains(m.Id);
                var c = E.Div().Col(Align.Center).Ignore().Size(cellW);
                c.style.marginBottom = 6;
                c.Add(E.Slot(got ? "trophy" : "lock", 56, got ? E.Cell.Legendary : E.Cell.Common, 0.62f, !got));
                c.Add(E.T(m.Name, Theme.Body, got ? (Color?)null : Theme.Pale).TwoLines(cellW - 4).Margin(3));
                cells.Add(c);
            }
            mrows.Add(cells);
            host.Add(ms);

            var fr = Block("Portrait frames", out var frows);
            frows.Add(FrameRow(rowW));
            host.Add(fr);
        }

        /// <summary>The frames: unlocked ones are picked with a tap, locked ones say how many milestones they need.</summary>
        VisualElement FrameRow(float rowW)
        {
            var row = E.Div().Row(0, Align.FlexStart).Ignore().Margin(8, 0, 6);
            int reached = G.State.ReachedMilestones.Count;
            float cellW = Mathf.Floor(rowW / PlayerProfile.Frames.Length);
            foreach (var f in PlayerProfile.Frames)
            {
                bool open = reached >= f.Milestones;
                bool on = Founder.Frame == f.Id;
                var c = E.Div().Col(Align.Center).Size(cellW);
                var slot = E.Slot(open ? Founder.Avatar : "lock", 60, FrameCell(f.Id), open ? 0.84f : 0.5f, !open).Rarity(FrameCell(f.Id));
                if (on) slot.Add(E.Icon("check", 22).Abs(null, null, -4, 2)); // on the corner, clear of the name under it
                c.Add(slot);
                c.Add(E.T(f.Name, Theme.Body, open ? (Color?)null : Theme.Pale).Fit(cellW - 4).Margin(3));
                if (!open) c.Add(E.T(f.Milestones == 1 ? "1 milestone" : $"{f.Milestones} milestones", Theme.Small, Theme.Pale).Fit(cellW - 4));
                string fid = f.Id;
                if (open) Tap(c, () => store.SetFrame(fid));
                else c.pickingMode = PickingMode.Ignore;
                row.Add(c);
            }
            return row;
        }

        // MARK: - Settings window

        string SettingsKey() =>
            $"{settingsTab}|{Prefs.Quality}|{Prefs.BatterySaver}|{Prefs.Haptics}|{AudioDirector.MusicOn}|{AudioDirector.SoundOn}|{Time.unscaledTime < confirmResetUntil}|" +
            $"{Cloud.State}|{Cloud.Busy}|{Cloud.Problem}|{CloudSave.Username}|{CloudSave.Enabled}|{DevTools}";

        void BuildSettingsPanel(VisualElement host)
        {
            switch (settingsTab)
            {
                case 1: BuildAccountTab(host); break;
                case 2: BuildAboutTab(host); break;
                default: BuildGameTab(host); break;
            }
        }

        void BuildGameTab(VisualElement host)
        {
            var gfx = Block("Graphics", out var grows);
            var seg = E.Div().Row(0, Align.FlexEnd).Ignore().Margin(8, 0, 8);
            string[] names = { "Smooth", "Balanced", "Cinematic" };
            for (int i = 0; i < names.Length; i++)
            {
                int q = i;
                var t = E.Tab(names[i], Prefs.Quality == i, Act(() => { Prefs.Quality = q; Prefs.Apply(); }));
                t.style.flexGrow = 1;
                t.style.flexBasis = 0;
                if (i > 0) t.style.marginLeft = 6;
                seg.Add(t);
            }
            grows.Add(seg);
            host.Add(gfx);

            var game = Block("Sound & feel", out var rows);
            rows.Add(ToggleRow("Music", "Calm themes between the seasons", AudioDirector.MusicOn, v => AudioDirector.MusicOn = v, false));
            rows.Add(ToggleRow("Sound", "Fire, river, birds, wind and taps", AudioDirector.SoundOn, v => AudioDirector.SoundOn = v, true));
            rows.Add(ToggleRow("Haptics", "A tap of vibration on milestones", Prefs.Haptics, v => Prefs.Haptics = v, true));
            rows.Add(ToggleRow("Battery saver", "30 fps, lighter on the battery", Prefs.BatterySaver, v => { Prefs.BatterySaver = v; Prefs.Apply(); }, true));
            host.Add(game);

            var lang = Block("Language", out var lrows);
            lrows.Add(E.StatRow("world", "Language", "English", null, false));
            host.Add(lang);
        }

        void BuildAccountTab(VisualElement host)
        {
            float rowW = InnerW - 20;

            // Cloud save.
            var cloud = Block("Cloud save", out var crows);
            var (icon, _, color) = CloudLine();
            var status = E.Line(false, 52);
            status.Add(E.Slot(icon, 40, E.Cell.Tile, 0.62f).Margin(0, 10));
            var col = E.Div().Col().Grow().Ignore();
            col.Add(LiveFit(() => CloudLine().text, Theme.Num, rowW - 60, color));
            string sub = Cloud.State switch
            {
                CloudState.NotSetUp => "This build is not linked to the cloud yet. Progress is kept on this device.",
                CloudState.Off => "Turn it on to keep a copy of your settlement online.",
                CloudState.Error => Cloud.Problem ?? "The cloud did not answer.",
                CloudState.Choosing => "Another device saved progress to the cloud.",
                _ => Cloud.Problem ?? $"Last saved to the cloud: {Ago(CloudSave.LastSync)}",
            };
            col.Add(E.T(sub, Theme.Small, Theme.Pale).Wrap().Margin(2));
            status.Add(col);
            crows.Add(status);
            if (Cloud.State != CloudState.NotSetUp)
            {
                crows.Add(ToggleRow("Cloud save", "Saves your settlement online every few minutes", CloudSave.Enabled, v => Cloud.SetEnabled(v), true));
                if (CloudSave.Enabled && Cloud.State != CloudState.Choosing)
                {
                    var btns = E.Div().Row().Ignore().Margin(8, 0, 8);
                    bool ready = !Cloud.Busy && (Cloud.State == CloudState.Ready || Cloud.State == CloudState.Error);
                    btns.Add(Share(E.Button("Save to cloud", ready ? Act(Cloud.UploadNow) : null, ready ? E.Kind.Green : E.Kind.Grey, 42, null, Theme.Num, null, (rowW - 8) / 2), btns));
                    btns.Add(Share(E.Button("Load from cloud", ready ? Act(Cloud.CheckCloud) : null, ready ? E.Kind.Blue : E.Kind.Grey, 42, null, Theme.Num, null, (rowW - 8) / 2), btns));
                    crows.Add(btns);
                }
            }
            host.Add(cloud);

            // The account: a guest of this device, or bound to a username.
            var acc = Block("Account", out var arows);
            string user = CloudSave.Username;
            var who = E.Line(false, 52);
            who.Add(E.Slot(Founder.Avatar, 40, FrameCell(Founder.Frame), 0.84f).Rarity(FrameCell(Founder.Frame)).Margin(0, 10));
            var wcol = E.Div().Col().Grow().Ignore();
            wcol.Add(E.T(user != null ? $"Bound: {user}" : "Guest on this device", Theme.Num).Fit(rowW - 60));
            wcol.Add(E.T(user != null ? "Sign in with this username on a new phone to continue there."
                                      : "Bind an account so you keep your settlement if you change or lose your phone.",
                         Theme.Small, Theme.Pale).Wrap().Margin(2));
            who.Add(wcol);
            arows.Add(who);
            if (Cloud.State != CloudState.NotSetUp && CloudSave.Enabled)
            {
                var btns = E.Div().Row().Ignore().Margin(8, 0, 8);
                float bw = (rowW - 8) / 2;
                if (user == null)
                    btns.Add(Share(E.Button("Bind account", Cloud.Busy ? null : Act(() => ShowProfileDialog("bind")), Cloud.Busy ? E.Kind.Grey : E.Kind.Orange, 42, null, Theme.Num, null, bw), btns));
                else
                    btns.Add(Share(E.Button("Sign out", Cloud.Busy ? null : Act(Cloud.SignOut), Cloud.Busy ? E.Kind.Grey : E.Kind.Red, 42, null, Theme.Num, null, bw), btns));
                btns.Add(Share(E.Button("Sign in", Cloud.Busy ? null : Act(() => ShowProfileDialog("signin")), Cloud.Busy ? E.Kind.Grey : E.Kind.Blue, 42, null, Theme.Num, null, bw), btns));
                arows.Add(btns);
            }
            arows.Add(E.StatRow("info", "Founder ID", FounderId()));
            arows.Add(E.StatRow("home", "This device", CloudSave.DeviceName));
            arows.Add(E.StatRow("storage", "Saved here", store.SaveStatus, null, true, Theme.Pale));
            host.Add(acc);
        }

        void BuildAboutTab(VisualElement host)
        {
            var about = Block("About", out var rows);
            rows.Add(E.StatRow("info", "Game", $"{G.Content.Branding.Title}", null, false));
            rows.Add(E.StatRow("level_up", "Version", Application.version));
            rows.Add(E.StatRow("storage", "Save format", $"{SaveCodec.CurrentVersion} · content {G.Content.ContentVersion}"));
            host.Add(about);

            var help = Block("Help", out var hrows);
            hrows.Add(E.Ink("Your settlement is saved on this phone all the time. With cloud save on, a copy is kept online too. " +
                            "Bind an account (Settings → Account) to open the same settlement on a new phone: install the game, open Account, tap Sign in.",
                            Theme.Body).Margin(8, 0, 8));
            host.Add(help);

            if (DevTools)
            {
                var dev = Block("Developer", out var drows);
                float devW = (InnerW - 20 - 3 * 8) / 4;
                var r1 = E.Div().Row().Ignore().Margin(8);
                r1.Add(Share(E.Button("Save now", Act(store.Save), E.Kind.Blue, 42, null, Theme.Num, null, devW), r1));
                r1.Add(Share(E.Button("+50 all", Act(store.DebugGrant), E.Kind.Blue, 42, null, Theme.Num, null, devW), r1));
                r1.Add(Share(E.Button("+1 min", Act(() => store.DebugFastForward(1)), E.Kind.Blue, 42, null, Theme.Num, null, devW), r1));
                r1.Add(Share(E.Button("+10 min", Act(() => store.DebugFastForward(10)), E.Kind.Blue, 42, null, Theme.Num, null, devW), r1));
                drows.Add(r1);
                drows.Add(E.Div().Size(null, 8).Ignore());
                host.Add(dev);
            }

            var danger = Block("Start over", out var xrows);
            bool armed = Time.unscaledTime < confirmResetUntil;
            xrows.Add(E.Ink("Erases this settlement and starts a new camp. Your founder profile stays.", Theme.Small, Theme.InkSoft).Margin(8));
            var reset = E.Button(armed ? "Tap again to erase this settlement" : "Reset settlement", Act(() =>
            {
                if (Time.unscaledTime < confirmResetUntil) { confirmResetUntil = 0; store.ResetPrototype(); store.ActivePanel = Panel.None; }
                else
                {
                    confirmResetUntil = Time.unscaledTime + 4f;
                    sheet.schedule.Execute(() => dirty = true).StartingIn(4100);
                }
            }), E.Kind.Red, 42, "trash", Theme.Num, null, InnerW - 20);
            reset.style.marginTop = 8;
            reset.style.marginBottom = 8;
            xrows.Add(reset);
            host.Add(danger);

            // Five taps on the version line show the developer tools.
            var line = E.Ink($"{G.Content.Branding.Title} · {G.Content.Branding.Subtitle} · v{Application.version} · Unity {Application.unityVersion}",
                             Theme.Small, Theme.InkSoft).Align(TextAnchor.MiddleCenter).Margin(4);
            line.pickingMode = PickingMode.Position;
            line.OnTap(() =>
            {
                if (++versionTaps < 5) return;
                versionTaps = 0;
                DevTools = !DevTools;
                store.Notify(DevTools ? "Developer tools on." : "Developer tools off.");
                dirty = true;
            });
            host.Add(line);
        }

        /// <summary>A setting with the kit's ON/OFF switch.</summary>
        VisualElement ToggleRow(string label, string hint, bool value, Action<bool> set, bool separator)
        {
            var row = E.Line(separator, 56);
            var col = E.Div().Col().Grow().Ignore();
            col.Add(E.T(label, Theme.Num));
            col.Add(E.T(hint, Theme.Small, Theme.Pale).Fit(InnerW - 20 - 90));
            row.Add(col);
            // The kit's stone switch when the kit is in; otherwise the game's own.
            var sw = E.KitSwitch(value, 40)?.Ignore();
            if (sw == null)
            {
                sw = E.Div().Row().Skin(value ? "btn_green" : "btn_grey").Pad(0, 5, 4, 7).Size(80, 36).Ignore();
                var knob = E.Div().Skin("slot").Size(24, 24).Ignore();
                var text = E.T(value ? "ON" : "OFF", Theme.Body).Grow().Align(TextAnchor.MiddleCenter);
                if (value) { sw.Add(text); sw.Add(knob); }
                else { sw.Add(knob); sw.Add(text); }
            }
            row.Add(sw);
            Tap(row, () => set(!value));
            return row;
        }

        // MARK: - Dialogs (portrait, name, account, the cloud's choice)

        /// <summary>The structural key of the profile dialogs ("" when none is open).</summary>
        string ProfileDialogKey()
        {
            if (store?.Game == null) return "";
            if (store.Cloud.State == CloudState.Choosing && store.Cloud.Pending != null) return "cloud|" + store.Cloud.Pending.UploadId + "|" + store.Cloud.Busy;
            if (profileDialog == null) return "";
            return $"pd|{profileDialog}|{dialogError}|{dialogBusy}|{Founder.Avatar}|{Founder.Frame}";
        }

        VisualElement ProfileDialogCard()
        {
            if (store.Cloud.State == CloudState.Choosing && store.Cloud.Pending != null) return CloudChoiceCard();
            return profileDialog switch
            {
                "avatar" => AvatarCard(),
                "rename" => RenameCard(),
                _ => AccountCard(profileDialog == "signin"),
            };
        }

        VisualElement AvatarCard()
        {
            var c = DialogShell("Founder Portrait", 600, out var body, out var foot);
            float inner = DialogInnerW(600);
            body.Add(E.T("Portrait", Theme.Num, Theme.Gold).Margin(0, 0, 6));
            var faces = E.Div().Row().Ignore();
            faces.style.flexWrap = Wrap.Wrap;
            float cellW = Mathf.Floor(inner / Mathf.Max(1, Mathf.Min(PlayerProfile.Avatars.Length, Mathf.Floor(inner / 92))));
            foreach (var a in PlayerProfile.Avatars)
            {
                bool on = Founder.Avatar == a.Id;
                var cell = E.Div().Col(Align.Center).Size(cellW).Margin(0, 0, 4);
                var slot = E.Slot(a.Id, 66, on ? FrameCell(Founder.Frame) : E.Cell.Slot, 0.84f).Rarity(on ? FrameCell(Founder.Frame) : E.Cell.Slot);
                if (on) slot.Add(E.Icon("check", 22).Abs(null, null, -4, 2)); // on the corner, clear of the name under it
                cell.Add(slot);
                cell.Add(E.T(a.Name, Theme.Body, on ? Theme.Gold : (Color?)null).Fit(cellW - 4).Margin(3));
                string id = a.Id;
                Tap(cell, () => store.SetAvatar(id));
                faces.Add(cell);
            }
            body.Add(faces);
            body.Add(E.T("Frame", Theme.Num, Theme.Gold).Margin(6, 0, 0));
            body.Add(FrameRow(inner));
            foot.Add(E.Button("Done", Act(CloseDialog), E.Kind.Green, Theme.ButtonH, null, 17, null, 240));
            return c;
        }

        VisualElement RenameCard()
        {
            var c = DialogShell("Rename Founder", 520, out var body, out var foot);
            float inner = DialogInnerW(520);
            body.Add(E.Ink("Choose the name other founders will know you by.", Theme.Body).Margin(0, 0, 8));
            body.Add(Field(fieldName, v => fieldName = v, false, inner, PlayerProfile.NameMax, "Founder name"));
            body.Add(E.T($"{PlayerProfile.NameMin}–{PlayerProfile.NameMax} characters: letters, numbers, spaces and _ - . '", Theme.Small, Theme.Pale).Wrap().Margin(6));
            if (dialogError != null) body.Add(E.T(dialogError, Theme.Body, Theme.Bad).Wrap().Margin(6));
            float bw = Portrait ? Mathf.Floor((inner - 12) / 2) : 200;
            foot.Add(E.Button("Cancel", Act(CloseDialog), E.Kind.Blue, Theme.ButtonH, null, 17, null, bw));
            foot.Add(E.Button("Save", Act(() =>
            {
                dialogError = store.RenameFounder(fieldName);
                if (dialogError == null) { CloseDialog(); store.Notify("Your new name is set."); }
            }), E.Kind.Green, Theme.ButtonH, null, 17, null, bw).Margin(0, 0, 0, 12));
            return c;
        }

        VisualElement AccountCard(bool signIn)
        {
            var c = DialogShell(signIn ? "Sign In" : "Bind Account", 560, out var body, out var foot);
            float inner = DialogInnerW(560);
            body.Add(E.Ink(signIn
                ? "Sign in with the username you bound on your other phone. If its cloud save differs from this device, you choose which one to keep."
                : "Choose a username and a password. With them you can open this settlement on any phone.", Theme.Body).Margin(0, 0, 8));
            body.Add(E.T("Username", Theme.Body, Theme.Pale).Margin(0, 0, 3));
            body.Add(Field(fieldUser, v => fieldUser = v, false, inner, 20, "username"));
            body.Add(E.T("Password", Theme.Body, Theme.Pale).Margin(8, 0, 3));
            body.Add(Field(fieldPass, v => fieldPass = v, true, inner, 30, "password"));
            if (!signIn) body.Add(E.T("8–30 characters with a capital letter, a small letter, a number and a symbol.", Theme.Small, Theme.Pale).Wrap().Margin(6));
            if (dialogError != null) body.Add(E.T(dialogError, Theme.Body, Theme.Bad).Wrap().Margin(6));
            float bw = Portrait ? Mathf.Floor((inner - 12) / 2) : 200;
            foot.Add(E.Button("Cancel", dialogBusy ? null : Act(CloseDialog), dialogBusy ? E.Kind.Grey : E.Kind.Blue, Theme.ButtonH, null, 17, null, bw));
            foot.Add(E.Button(dialogBusy ? "Please wait…" : signIn ? "Sign in" : "Bind", dialogBusy ? null : Act(() => SubmitAccount(signIn)),
                              dialogBusy ? E.Kind.Grey : E.Kind.Green, Theme.ButtonH, null, 17, null, bw).Margin(0, 0, 0, 12));
            return c;
        }

        async void SubmitAccount(bool signIn)
        {
            string user = (fieldUser ?? "").Trim();
            dialogBusy = true;
            dialogError = null;
            dirty = true;
            string problem;
            try { problem = signIn ? await Cloud.SignIn(user, fieldPass) : await Cloud.Bind(user, fieldPass); }
            catch (Exception e) { problem = CloudBackends.Friendly(e); }
            dialogBusy = false;
            dirty = true;
            if (problem != null) { dialogError = problem; return; }
            CloseDialog();
            store.Notify(signIn ? $"Signed in as {user}." : $"Account bound: {user}.");
        }

        VisualElement CloudChoiceCard()
        {
            var cloud = Cloud.Pending;
            var c = DialogShell("Choose Your Progress", DialogW, out var body, out var foot);
            float inner = DialogInnerW(DialogW);
            body.Add(E.Ink("The cloud holds progress from another device. Which one do you want to keep? The other one is replaced.", Theme.Body).Margin(0, 0, 8));
            var row = E.Div().Row(0, Align.Stretch).Ignore();
            float colW = Portrait ? inner : Mathf.Floor((inner - 12) / 2);
            if (Portrait) row.style.flexDirection = FlexDirection.Column;
            var s = G.State;
            row.Add(ProgressColumn("This device", CloudSave.DeviceName, "Now", Founder.Name, Founder.Avatar, s.SettlementName, StageNameOf(s.EpochId, s.StageId),
                                   s.Population, s.Buildings.Count(b => b.Status != BuildingStatus.Constructing), PlayerProfile.Prosperity(s), s.SimTime, colW));
            var right = ProgressColumn("Cloud", cloud.DeviceName ?? "Another device", Ago(cloud.SavedAt), cloud.FounderName, PlayerProfile.Decode(cloud.Profile ?? "")?.Avatar ?? "face_m1",
                                       cloud.SettlementName, StageNameOf(cloud.EpochId, cloud.StageId), cloud.Population, cloud.Buildings, cloud.Prosperity, cloud.SimTime, colW);
            if (Portrait) right.style.marginTop = 10; else right.style.marginLeft = 12;
            row.Add(right);
            body.Add(row);
            float bw = Portrait ? Mathf.Floor((inner - 12) / 2) : 240;
            foot.Add(E.Button("Keep this device", Cloud.Busy ? null : Act(Cloud.KeepDevice), Cloud.Busy ? E.Kind.Grey : E.Kind.Blue, Theme.ButtonH, null, 17, null, bw));
            foot.Add(E.Button("Load cloud", Cloud.Busy ? null : Act(Cloud.UseCloud), Cloud.Busy ? E.Kind.Grey : E.Kind.Green, Theme.ButtonH, null, 17, null, bw).Margin(0, 0, 0, 12));
            return c;
        }

        VisualElement ProgressColumn(string title, string device, string when, string founder, string avatar, string settlement, string stage,
                                     int population, int buildings, int prosperity, double simTime, float width)
        {
            var col = E.Inset().Col();
            col.style.width = width;
            var head = E.Div().Row().Ignore();
            head.Add(E.Slot(avatar, 52, E.Cell.Tile, 0.84f));
            var hc = E.Div().Col().Grow().Ignore().Margin(0, 0, 0, 10);
            hc.Add(E.T(title, Theme.H2, Theme.Gold).Fit(width - 90));
            hc.Add(E.T($"{device} · {when}", Theme.Small, Theme.Pale).Fit(width - 90));
            head.Add(hc);
            col.Add(head);
            // Compact rows (two figures each) so both columns fit without scrolling.
            col.Add(MiniRow(("user", founder ?? "—", null), ("home", settlement ?? "Settlement", null), false));
            col.Add(MiniRow(("level_up", stage ?? "—", null), ("clock", Fmt.Clock(simTime), null), true));
            col.Add(MiniRow(("people", $"{population} people", null), ("build", $"{buildings} buildings", null), true));
            col.Add(MiniRow(("sparkle", prosperity.ToString("#,0", CultureInfo.InvariantCulture), Theme.Gold), (null, "Prosperity", Theme.Pale), true));
            return col;
        }

        /// <summary>A short row of two icon + text pairs.</summary>
        static VisualElement MiniRow((string icon, string text, Color? color) a, (string icon, string text, Color? color) b, bool separator)
        {
            var r = E.Line(separator, 32);
            foreach (var (icon, text, color) in new[] { a, b })
            {
                var cell = E.Div().Row().Grow().Ignore();
                if (icon != null) cell.Add(E.Icon(icon, 20).Margin(0, 6));
                cell.Add(E.T(text, Theme.Body, color).Flex());
                r.Add(cell);
            }
            return r;
        }

        /// <summary>A text field in the kit's look: a parchment inset with dark ink.</summary>
        static TextField Field(string value, Action<string> changed, bool password, float width, int maxLength, string name)
        {
            var f = new TextField { value = value ?? "", isPasswordField = password, maxLength = maxLength, name = name };
            if (password) f.maskChar = '•';
            f.style.width = width;
            f.style.height = 44;
            f.style.marginLeft = 0;
            f.style.marginRight = 0;
            f.style.fontSize = Theme.Num + 1;
            var input = f.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                var st = input.style;
                // The kit's input field: the dark inset with a lilac rim and white words (the Profile demo).
                st.backgroundColor = Theme.KitLook ? Theme.Hex("#1A1029") : Theme.Wood1;
                st.color = Theme.KitLook ? Color.white : Theme.InkDark;
                if (Theme.KitLook) { input.Border(Theme.Rim, 2).Radius(12); st.unityFontDefinition = Theme.Display; }
                st.paddingLeft = 12;
                st.paddingRight = 12;
                st.unityTextAlign = TextAnchor.MiddleLeft;
                input.Radius(8);
                input.Border(Theme.Outline, 2);
            }
            f.RegisterValueChangedCallback(e => changed(e.newValue));
            return f;
        }
    }
}
