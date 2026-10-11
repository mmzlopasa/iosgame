using System.Globalization;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>Full-screen dialogs: milestone celebration, welcome back report and event decisions.</summary>
    public partial class GameUi
    {
        string chosenOption;

        string ModalKey()
        {
            if (store?.Game == null) return "";
            if (store.Celebration != null) return "ms|" + store.Celebration;
            if (store.Offline != null) return "wb";
            var ev = G.PendingEvent;
            if (ev != null && !store.EventMinimized)
                return $"ev|{ev.Id}|{chosenOption}|" + string.Join("", ev.Options.Select(o => G.OptionBlocker(o) == null ? "1" : "0"));
            return "";
        }

        void BuildModal(VisualElement host)
        {
            string key = ModalKey();
            if (key == "") return;
            var scrim = E.Div().Fill().Bg(new Color(0.03f, 0.035f, 0.05f, 0.62f));
            host.Add(scrim);
            var frame = E.Div().Fill().Ignore().Col().Center().Pad(0, 16);
            host.Add(frame);

            VisualElement card;
            if (key.StartsWith("ms|")) card = MilestoneCard();
            else if (key == "wb") card = WelcomeCard();
            else card = EventCard();
            frame.Add(card);

            card.style.opacity = 0;
            card.style.scale = new Scale(new Vector3(0.96f, 0.96f, 1));
            card.Transition(300, "opacity", "scale");
            card.schedule.Execute(() =>
            {
                card.style.opacity = 1;
                card.style.scale = new Scale(Vector3.one);
            }).StartingIn(16);
        }

        static VisualElement DialogShell()
        {
            var c = E.Div("dialog").Col().Radius(28).BgImage(Theme.SheetFill).Border(Theme.Edge);
            c.style.borderTopColor = new Color(1, 1, 1, 0.24f);
            c.style.overflow = Overflow.Hidden;
            c.style.width = Length.Percent(100);
            c.style.maxWidth = 420;
            return c;
        }

        // MARK: - Milestone

        VisualElement MilestoneCard()
        {
            var ms = G.Content.Milestones.LastOrDefault(m => G.State.ReachedMilestones.Contains(m.Id));
            var c = DialogShell();
            var top = E.Div().Col(Align.Center).Pad(28, 20, 8, 20).BgImage(Theme.GoldGlow).Ignore();
            top.Add(E.Image(Theme.Image("logo_mark"), 96, 96));
            top.Add(E.Eyebrow("Milestone reached").Margin(10));
            top.Add(E.Text(ms?.Name ?? "A new chapter", 26, Theme.Ink, Theme.F.Display).Align(TextAnchor.MiddleCenter).Margin(6));
            c.Add(top);
            var body = E.Div().Col().Pad(12, 20, 20, 20).Ignore();
            body.Add(E.Text(store.Celebration, 14, Theme.Body).Align(TextAnchor.MiddleCenter));
            var note = E.Div().Row().Center().Margin(14).Ignore();
            note.Add(E.Icon("legacy", 14, Theme.Lavender));
            var nt = E.Text("Recorded in your Legacy Timeline.", 12, Theme.Muted);
            nt.style.marginLeft = 6;
            note.Add(nt);
            body.Add(note);
            body.Add(E.Button("Continue", Act(() => store.Celebration = null), E.Kind.Gold, 48).Margin(16));
            c.Add(body);
            return c;
        }

        // MARK: - Welcome back

        VisualElement WelcomeCard()
        {
            var report = store.Offline;
            var c = DialogShell();
            var top = E.Div().Col(Align.Center).Pad(26, 20, 10, 20).BgImage(Theme.GoldGlow).Ignore();
            top.Add(E.Image(Theme.Image("logo_mark"), 64, 64));
            top.Add(E.Eyebrow("While you were away").Margin(8));
            top.Add(E.Text("Welcome back, Founder", 24, Theme.Ink, Theme.F.Display).Align(TextAnchor.MiddleCenter).Margin(6));
            int mins = (int)(report.Seconds / 60);
            string away = mins >= 60 ? $"{mins / 60} h {mins % 60} min" : $"{mins} min";
            top.Add(E.Text($"Your people kept the fire alive for {away}.", 13, Theme.Body).Align(TextAnchor.MiddleCenter).Margin(6));
            c.Add(top);

            var body = E.Div().Col().Pad(10, 16, 18, 16).Ignore();
            var gathered = E.Div().Col().Glass(20).Pad(12).Ignore();
            gathered.Add(E.Eyebrow("Gathered", Theme.Muted));
            var grid = E.Div().Row().Margin(8).Ignore();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.SpaceBetween;
            foreach (var type in ResourceTypes.All)
            {
                double v = report.Gained[type];
                var cell = E.Div().Row().Between().Radius(14).Pad(9, 11).Bg(new Color(1, 1, 1, 0.045f)).Margin(0, 0, 6).Ignore();
                cell.style.width = Length.Percent(48.5f);
                var l = E.Div().Row().Ignore();
                l.Add(E.Icon(IconFor(type), 14, TintFor(type)));
                var n = E.Text(type.DisplayName(), 12.5f, Theme.Body);
                n.style.marginLeft = 6;
                l.Add(n);
                cell.Add(l);
                cell.Add(E.Mono(Fmt.Signed(v), 13.5f, v < -0.5 ? Theme.Error : type == ResourceType.Knowledge ? Theme.Lavender : Theme.Mint));
                grid.Add(cell);
            }
            gathered.Add(grid);
            body.Add(gathered);

            if (report.Events.Count > 0)
            {
                var village = E.Div().Col().Glass(20).Pad(12).Margin(10).Ignore();
                village.Add(E.Eyebrow("The village", Theme.Muted));
                foreach (var line in report.Events.Take(5))
                    village.Add(Check(line, true).Margin(8));
                body.Add(village);
            }
            double cap = G.Content.Balance.MaxOfflineSeconds / 3600.0;
            body.Add(E.Text($"Time away counts for up to {cap.ToString("0.#", CultureInfo.InvariantCulture)} hours.", 11.5f, Theme.Muted)
                .Align(TextAnchor.MiddleCenter).Margin(10));
            body.Add(E.Button("Back to the village", Act(() => store.Offline = null), E.Kind.Gold, 48).Margin(14));
            c.Add(body);
            return c;
        }

        // MARK: - Event decision

        VisualElement EventCard()
        {
            var ev = G.PendingEvent;
            var c = DialogShell();

            var art = E.Div().Size(null, 132).Ignore();
            art.BgImage(Theme.Gradient("eventart", true, Theme.Hex("#2a3442"), Theme.Hex("#161b22")));
            var glow = E.Div().Abs(0, 0, 0, 0).BgImage(Theme.GoldGlow).Opacity(0.7f).Ignore();
            art.Add(glow);
            var mark = E.Div().Abs(null, null, null, 0).Ignore();
            mark.style.left = 0; mark.style.right = 0; mark.style.top = 0;
            mark.Center();
            mark.Add(E.Icon("flame", 46, Theme.Alpha(Theme.Gold, 0.9f)));
            art.Add(mark);
            var tag = E.Div().Row().Radius(999).Pad(6, 10).Bg(new Color(0.05f, 0.06f, 0.08f, 0.6f)).Border(Theme.Alpha(Theme.Gold, 0.35f)).Abs(16, 14).Ignore();
            tag.Add(E.Eyebrow("Decision", Theme.Gold, 9.5f));
            art.Add(tag);
            c.Add(art);

            var body = E.Div().Col().Pad(16, 18, 18, 18).Ignore();
            body.Add(E.Text(ev.Title, 22, Theme.Ink, Theme.F.Display));
            body.Add(E.Text(ev.Text, 13.5f, Theme.Body).Margin(6));

            foreach (var option in ev.Options)
            {
                var blocker = G.OptionBlocker(option);
                bool on = chosenOption == option.Id;
                var o = E.Div().Col().Radius(18).Pad(12, 14).Margin(10);
                if (on) o.BgImage(Theme.GoldGlow).Border(Theme.Alpha(Theme.Gold, 0.6f));
                else o.Bg(new Color(1, 1, 1, 0.05f)).Border(new Color(1, 1, 1, 0.12f));
                if (blocker != null) o.style.opacity = 0.55f;
                var head = E.Div().Row().Ignore();
                var radio = E.Div().Size(18, 18).Radius(9).Center().Border(on ? Theme.Gold : Theme.Muted, 1.5f).Ignore();
                if (on) radio.Add(E.Div().Size(9, 9).Radius(4.5f).Bg(Theme.Gold).Ignore());
                head.Add(radio);
                var label = E.Text(option.Label, 14, Theme.Ink, Theme.F.TextBold).Grow();
                label.style.marginLeft = 9;
                head.Add(label);
                o.Add(head);
                if (!string.IsNullOrEmpty(option.TradeOff)) o.Add(E.Text(option.TradeOff, 12, Theme.Body).Margin(5, 0, 0, 27));
                if (option.Cost.HasValue && !option.Cost.Value.IsZero) o.Add(CostChips(option.Cost.Value).Margin(6, 0, 0, 27));
                if (blocker != null) o.Add(E.Text(blocker, 11.5f, Theme.Error).Margin(5, 0, 0, 27));
                if (blocker == null)
                {
                    string id = option.Id;
                    Tap(o, () => chosenOption = id);
                }
                body.Add(o);
            }

            var note = E.Div().Row().Margin(12).Ignore();
            note.Add(E.Icon("legacy", 13, Theme.Lavender));
            var nt = E.Text("Your choice is written into the Legacy Timeline.", 11.5f, Theme.Muted);
            nt.style.marginLeft = 6;
            note.Add(nt);
            body.Add(note);

            var buttons = E.Div().Row().Margin(14).Ignore();
            buttons.Add(E.Button("Decide later", Act(() => store.EventMinimized = true), E.Kind.Ghost, 46).Grow());
            bool ready = chosenOption != null;
            var confirm = E.Button("Confirm", ready ? Act(() => { store.ResolveEvent(chosenOption); chosenOption = null; }) : null,
                                   ready ? E.Kind.Gold : E.Kind.Disabled, 46).Grow();
            confirm.style.marginLeft = 10;
            buttons.Add(confirm);
            body.Add(buttons);
            c.Add(body);
            return c;
        }
    }
}
