using System.Globalization;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// Full-screen dialogs: milestone celebration, welcome back report, story chapters and event decisions.
    /// The landscape screen is short (about 345 units of dialog height on a notched phone), so every dialog
    /// is a wide card with two panes: the headline on the left and the details on the right, whose body
    /// scrolls while its buttons stay put.
    /// </summary>
    public partial class GameUi
    {
        string chosenOption;

        string ModalKey()
        {
            if (store?.Game == null) return "";
            if (store.Celebration != null) return "ms|" + store.Celebration;
            if (store.Offline != null) return "wb";
            if (G.PendingStory != null) return "story|" + G.PendingStory.Id;
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
            var frame = E.Div().Fill().Ignore().Col().Center()
                .Pad(12 + SafeTop, 16 + SafeRight, 12 + SafeBottom, 16 + SafeLeft);
            host.Add(frame);

            VisualElement card;
            if (key.StartsWith("ms|")) card = MilestoneCard();
            else if (key == "wb") card = WelcomeCard();
            else if (key.StartsWith("story|")) card = StoryCard();
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

        /// <summary>The dialog card: two panes side by side, never taller than the screen allows.</summary>
        VisualElement DialogShell(out VisualElement left, out VisualElement right, float leftShare = 40)
        {
            var c = E.Div("dialog").Row(0, Align.Stretch).Radius(28).BgImage(Theme.SheetFill).Border(Theme.Edge);
            c.style.borderTopColor = new Color(1, 1, 1, 0.24f);
            c.style.backgroundColor = Theme.Hex("#101218");
            c.style.overflow = Overflow.Hidden;
            c.style.width = Length.Percent(100);
            c.style.maxWidth = 700;
            c.style.maxHeight = ModalMaxHeight;
            c.style.flexShrink = 1;
            left = E.Div().Col().Ignore();
            left.style.width = Length.Percent(leftShare);
            left.style.flexShrink = 0;
            left.style.justifyContent = Justify.Center;
            right = E.Div().Col().Ignore();
            right.style.flexGrow = 1;
            right.style.flexShrink = 1;
            right.style.minWidth = 0;
            c.Add(left);
            c.Add(right);
            return c;
        }

        /// <summary>A pane body that scrolls when the dialog is at its maximum height.</summary>
        static ScrollView PaneBody(float top, float side, float bottom)
        {
            var s = E.FitScroll();
            s.contentContainer.style.paddingTop = top;
            s.contentContainer.style.paddingLeft = side;
            s.contentContainer.style.paddingRight = side;
            s.contentContainer.style.paddingBottom = bottom;
            return s;
        }

        /// <summary>Buttons under a pane's body (never scrolled away).</summary>
        static VisualElement PaneFooter(float side)
        {
            var f = E.Div().Col().Pad(6, side, 18, side).Ignore();
            f.style.flexShrink = 0;
            return f;
        }

        // MARK: - Story

        /// <summary>The opening scene of a chapter: the story so far and what this generation must do.</summary>
        VisualElement StoryCard()
        {
            var ch = G.PendingStory;
            var c = DialogShell(out var left, out var right, 40);
            left.BgImage(Theme.GoldGlow);
            var head = PaneBody(22, 20, 22);
            head.contentContainer.style.alignItems = Align.Center;
            head.Add(E.Eyebrow($"Chapter {ch.Number} · {ch.Era}").Align(TextAnchor.MiddleCenter));
            head.Add(E.Text(ch.Title, 24, Theme.Ink, Theme.F.Display).Align(TextAnchor.MiddleCenter).Margin(8));
            var goal = E.Div().Row(0, Align.FlexStart).Radius(16).Pad(12, 14).Bg(Theme.Alpha(Theme.Gold, 0.1f)).Border(Theme.Alpha(Theme.Gold, 0.28f)).Margin(16).Ignore();
            goal.style.alignSelf = Align.Stretch;
            goal.Add(E.Icon(ch.Locked ? "lock" : "goals", 16, Theme.Gold));
            var gt = E.Text(ch.Goal, 13, Theme.Ink, Theme.F.TextMedium).Grow();
            gt.style.marginLeft = 9;
            goal.Add(gt);
            head.Add(goal);
            left.Add(head);

            var body = PaneBody(22, 22, 4);
            foreach (var para in ch.Story.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(para)) continue;
                var t = E.Text(para.Trim(), 14, Theme.Body).Margin(0, 0, 10);
                t.style.unityFontStyleAndWeight = FontStyle.Italic;
                body.Add(t);
            }
            right.Add(body);
            var foot = PaneFooter(22);
            foot.Add(E.Button(ch.Number == 1 ? "Begin" : "Continue the story", Act(() => store.DismissStory()), E.Kind.Gold, 48));
            right.Add(foot);
            return c;
        }

        // MARK: - Milestone

        VisualElement MilestoneCard()
        {
            var ms = G.Content.Milestones.LastOrDefault(m => G.State.ReachedMilestones.Contains(m.Id));
            var c = DialogShell(out var left, out var right, 42);
            left.BgImage(Theme.GoldGlow);
            var head = PaneBody(22, 18, 22);
            head.contentContainer.style.alignItems = Align.Center;
            head.Add(E.Image(Theme.Image("logo_mark"), 84, 84));
            head.Add(E.Eyebrow("Milestone reached").Margin(10));
            head.Add(E.Text(ms?.Name ?? "A new chapter", 24, Theme.Ink, Theme.F.Display).Align(TextAnchor.MiddleCenter).Margin(6));
            left.Add(head);

            right.style.justifyContent = Justify.Center;
            var body = PaneBody(24, 22, 4);
            body.Add(E.Text(store.Celebration, 14, Theme.Body));
            var note = E.Div().Row().Margin(14).Ignore();
            note.Add(E.Icon("legacy", 14, Theme.Lavender));
            var nt = E.Text("Recorded in your Legacy Timeline.", 12, Theme.Muted).Grow();
            nt.style.marginLeft = 6;
            note.Add(nt);
            body.Add(note);
            right.Add(body);
            var foot = PaneFooter(22);
            foot.Add(E.Button("Continue", Act(() => store.Celebration = null), E.Kind.Gold, 48));
            right.Add(foot);
            return c;
        }

        // MARK: - Welcome back

        VisualElement WelcomeCard()
        {
            var report = store.Offline;
            var c = DialogShell(out var left, out var right, 38);
            left.BgImage(Theme.GoldGlow);
            var head = PaneBody(22, 18, 22);
            head.contentContainer.style.alignItems = Align.Center;
            head.Add(E.Image(Theme.Image("logo_mark"), 60, 60));
            head.Add(E.Eyebrow("While you were away").Margin(8));
            head.Add(E.Text("Welcome back, Founder", 22, Theme.Ink, Theme.F.Display).Align(TextAnchor.MiddleCenter).Margin(6));
            int mins = (int)(report.Seconds / 60);
            string away = mins >= 60 ? $"{mins / 60} h {mins % 60} min" : $"{mins} min";
            head.Add(E.Text($"Your people kept the fire alive for {away}.", 13, Theme.Body).Align(TextAnchor.MiddleCenter).Margin(6));
            double cap = G.Content.Balance.MaxOfflineSeconds / 3600.0;
            head.Add(E.Text($"Time away counts for up to {cap.ToString("0.#", CultureInfo.InvariantCulture)} hours.", 11.5f, Theme.Muted)
                .Align(TextAnchor.MiddleCenter).Margin(10));
            left.Add(head);

            var body = PaneBody(16, 16, 4);
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
                var n = E.Text(type.DisplayName(), 12.5f, Theme.Body).NoWrap();
                n.style.marginLeft = 6;
                l.Grow();
                l.style.overflow = Overflow.Hidden;
                l.style.marginRight = 6;
                n.Grow();
                l.Add(n);
                cell.Add(l);
                cell.Add(E.Mono(Fmt.Signed(v), 12.5f, v < -0.5 ? Theme.Error : type == ResourceType.Knowledge ? Theme.Lavender : Theme.Mint));
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
            right.Add(body);
            var foot = PaneFooter(16);
            foot.Add(E.Button("Back to the village", Act(() => store.Offline = null), E.Kind.Gold, 48));
            right.Add(foot);
            return c;
        }

        // MARK: - Event decision

        VisualElement EventCard()
        {
            var ev = G.PendingEvent;
            var c = DialogShell(out var left, out var right, 40);

            // Left: the scene (art backdrop, the flame, title and story) and the legacy note.
            left.BgImage(Theme.Gradient("eventart", true, Theme.Hex("#2a3442"), Theme.Hex("#161b22")));
            var glow = E.Div().Abs(0, 0, 0, 0).BgImage(Theme.GoldGlow).Opacity(0.7f).Ignore();
            left.Add(glow);
            var story = PaneBody(16, 18, 16);
            var tag = E.Div().Row().Radius(999).Pad(6, 10).Bg(new Color(0.05f, 0.06f, 0.08f, 0.6f)).Border(Theme.Alpha(Theme.Gold, 0.35f)).Ignore();
            tag.style.alignSelf = Align.FlexStart;
            tag.Add(E.Eyebrow("Decision", Theme.Gold, 9.5f));
            story.Add(tag);
            var mark = E.Icon("flame", 40, Theme.Alpha(Theme.Gold, 0.9f)).Margin(12);
            mark.style.alignSelf = Align.Center;
            story.Add(mark);
            story.Add(E.Text(ev.Title, 21, Theme.Ink, Theme.F.Display).Margin(10));
            story.Add(E.Text(ev.Text, 13, Theme.Body).Margin(6));
            var note = E.Div().Row(0, Align.FlexStart).Margin(12).Ignore();
            note.Add(E.Icon("legacy", 13, Theme.Lavender));
            var nt = E.Text("Your choice is written into the Legacy Timeline.", 11.5f, Theme.Muted).Grow();
            nt.style.marginLeft = 6;
            note.Add(nt);
            story.Add(note);
            left.Add(story);

            // Right: the options (scroll if needed) and the two buttons.
            var options = PaneBody(14, 16, 2);
            options.contentContainer.Add(E.Eyebrow("Choose", Theme.Muted).Margin(2, 0, 0, 4));
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
                options.Add(o);
            }
            right.Add(options);

            var foot = PaneFooter(16);
            var buttons = E.Div().Row().Ignore();
            buttons.Add(E.Button("Decide later", Act(() => store.EventMinimized = true), E.Kind.Ghost, 46).Grow());
            bool ready = chosenOption != null;
            var confirm = E.Button("Confirm", ready ? Act(() => { store.ResolveEvent(chosenOption); chosenOption = null; }) : null,
                                   ready ? E.Kind.Gold : E.Kind.Disabled, 46).Grow();
            confirm.style.marginLeft = 10;
            buttons.Add(confirm);
            foot.Add(buttons);
            right.Add(foot);
            return c;
        }
    }
}
