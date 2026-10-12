using System.Globalization;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// Dialogs over a dimmed world: the story as the kit's dialogue box (a wide parchment box along the
    /// bottom with a villager's portrait and a ▼ continue marker), and the milestone, welcome-back, event
    /// and petition popups as parchment windows with a plank title and a button row. A dialog is never
    /// taller than the screen allows: its body scrolls while its buttons stay put.
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
            // An event and a petition can both wait: whichever is on screen stays until answered, then the other follows.
            var ev = G.PendingEvent;
            bool evOpen = ev != null && !store.EventMinimized;
            var pet = G.PendingPetition;
            if (pet != null && (petitionShowing || !evOpen)) return "pet|" + pet.Id;
            if (evOpen)
                return $"ev|{ev.Id}|{chosenOption}|" + string.Join("", ev.Options.Select(o => G.OptionBlocker(o) == null ? "1" : "0"));
            return "";
        }

        void BuildModal(VisualElement host)
        {
            string key = ModalKey();
            petitionShowing = key.StartsWith("pet|", System.StringComparison.Ordinal);
            if (key == "") return;
            var scrim = E.Div().Fill().Bg(Theme.Scrim);
            host.Add(scrim);
            if (key.StartsWith("story|", System.StringComparison.Ordinal)) { StoryBox(host); return; }

            var frame = E.Div().Fill().Ignore().Col().Center()
                .Pad(PopupTop + 4, 16 + SafeRight, BottomPad + 8, 16 + SafeLeft);
            host.Add(frame);
            VisualElement card;
            if (key.StartsWith("ms|", System.StringComparison.Ordinal)) card = MilestoneCard();
            else if (key == "wb") card = WelcomeCard();
            else if (petitionShowing) card = PetitionCard();
            else card = EventCard();
            frame.Add(card);
            card.style.opacity = 0;
            card.style.scale = new Scale(new Vector3(0.94f, 0.94f, 1));
            card.Transition(240, "opacity", "scale");
            card.schedule.Execute(() =>
            {
                card.style.opacity = 1;
                card.style.scale = new Scale(Vector3.one);
            }).StartingIn(16);
        }

        /// <summary>A dialog window: plank title, a body that scrolls when it must, a fixed button row.</summary>
        VisualElement DialogShell(string title, float width, out VisualElement body, out VisualElement foot)
        {
            float w = Mathf.Min(width, SafeWidth - 40);
            var c = E.Panel().Pad(0, 10, 10, 10);
            c.name = "dialog";
            c.style.width = w;
            c.style.maxHeight = PanelH - PopupTop - BottomPad - 12;
            c.style.flexShrink = 1;
            var plank = E.Plank(title, null, 16, w);
            plank.style.marginLeft = -14;
            plank.style.marginRight = -14;
            plank.style.marginTop = -4;
            c.Add(plank);
            var scroll = E.FitScroll();
            scroll.contentContainer.style.paddingTop = 8;
            scroll.contentContainer.style.paddingBottom = 4;
            c.Add(scroll);
            body = scroll.contentContainer;
            foot = E.Div().Row().Center().Ignore();
            foot.style.flexShrink = 0;
            foot.style.paddingTop = 6;
            c.Add(foot);
            return c;
        }

        // MARK: - Story (the kit's dialogue box)

        void StoryBox(VisualElement host)
        {
            var ch = G.PendingStory;
            float left = SafeLeft + 16, right = SafeRight + 16;
            var box = E.Panel().Abs(left + 96, null, right, BottomPad + 6).Pad(10, 16, 12, 84);
            box.style.maxHeight = PanelH - PopupTop - BottomPad - 10;
            var head = E.Div().Row().Ignore();
            head.Add(E.T($"Chapter {ch.Number} · {ch.Era}", 11, Theme.Pale));
            head.Add(E.Div().Grow().Ignore());
            box.Add(head);
            box.Add(E.T(ch.Title, 17, Theme.Gold).Fit(SafeWidth - 260, 10));
            var scroll = E.FitScroll();
            scroll.contentContainer.style.paddingTop = 4;
            foreach (var para in ch.Story.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(para)) continue;
                scroll.Add(E.T(para.Trim(), 12.5f).Wrap().Margin(0, 0, 6));
            }
            var goal = E.Inset().Row(0, Align.FlexStart).Margin(2);
            goal.Add(E.Icon(ch.Locked ? "lock" : "goals", 18));
            goal.Add(E.T(ch.Goal, 11.5f, Theme.Gold).Wrap().Grow().Margin(0, 0, 0, 6));
            scroll.Add(goal);
            box.Add(scroll);
            var foot = E.Div().Row().Ignore();
            foot.style.flexShrink = 0;
            foot.style.justifyContent = Justify.FlexEnd;
            foot.style.paddingTop = 6;
            foot.Add(E.Button(ch.Number == 1 ? "Begin" : "Continue", Act(() => store.DismissStory()), E.Kind.Green, 34, null, 13.5f, null, 150));
            foot.Add(E.Icon("next", 18).Also(i => i.style.rotate = new Rotate(new Angle(90, AngleUnit.Degree))).Margin(0, 0, 0, 10));
            box.Add(foot);
            host.Add(box);
            // The narrator stands at the left edge of the box, larger than it.
            var portrait = E.Icon("face_f1", 150).Abs(left, null, null, BottomPad + 30);
            host.Add(portrait);
            Pop(box);
        }

        // MARK: - Milestone

        VisualElement MilestoneCard()
        {
            var ms = G.Content.Milestones.LastOrDefault(m => G.State.ReachedMilestones.Contains(m.Id));
            var c = DialogShell("Milestone reached", 460, out var body, out var foot);
            var hero = E.Div().Row().Ignore();
            hero.Add(E.Slot("trophy", 64, E.Cell.Legendary, 0.7f));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            col.Add(E.T(ms?.Name ?? "A new chapter", 17, Theme.Gold).Wrap());
            col.Add(E.Ink(store.Celebration, 11.5f).Margin(3));
            hero.Add(col);
            body.Add(hero);
            var note = E.Inset().Row().Margin(8);
            note.Add(E.Icon("legacy", 18));
            note.Add(E.T("Recorded in your Legacy Timeline.", 11).Margin(0, 0, 0, 6));
            body.Add(note);
            foot.Add(E.Button("Continue", Act(() => store.Celebration = null), E.Kind.Orange, 38, null, 14, null, 200));
            return c;
        }

        // MARK: - Welcome back

        VisualElement WelcomeCard()
        {
            var report = store.Offline;
            var c = DialogShell("Welcome back, Founder", 560, out var body, out var foot);
            int mins = (int)(report.Seconds / 60);
            string away = mins >= 60 ? $"{mins / 60} h {mins % 60} min" : $"{mins} min";
            body.Add(E.T($"Your people kept the fire alive for {away}.", 12.5f).Wrap());
            double cap = G.Content.Balance.MaxOfflineSeconds / 3600.0;
            body.Add(E.Ink($"Time away counts for up to {cap.ToString("0.#", CultureInfo.InvariantCulture)} hours.", 10.5f).Margin(2));

            // Gathered: the kit's reward cells with the amount at the bottom.
            var gathered = E.Section("Gathered", out var rows);
            gathered.style.marginTop = 6;
            var cells = E.Div().Row().Ignore().Margin(6, 0, 5);
            cells.style.flexWrap = Wrap.Wrap;
            foreach (var type in ResourceTypes.All)
            {
                double v = report.Gained[type];
                var cell = RewardCell(IconFor(type), CellFor(type), Fmt.Signed(v) == "0" ? "0" : (v < 0 ? "−" : "+") + Fmt.Compact(System.Math.Abs(v)), 46);
                cell.style.marginRight = 6;
                if (v < -0.5) cell.Add(E.Icon("alert", 16).Abs(null, -4, -4));
                cells.Add(cell);
            }
            rows.Add(cells);
            body.Add(gathered);
            if (report.Events.Count > 0)
            {
                var village = E.Section("The village", out var vrows);
                village.style.marginTop = 6;
                bool sep = false;
                foreach (var line in report.Events.Take(5))
                {
                    var r = E.Line(sep, 24);
                    sep = true;
                    r.Add(E.Icon("check", 16).Margin(0, 5));
                    r.Add(E.T(line, 11).Wrap().Grow());
                    vrows.Add(r);
                }
                body.Add(village);
            }
            foot.Add(E.Button("Back to the village", Act(() => store.Offline = null), E.Kind.Green, 38, null, 14, null, 240));
            return c;
        }

        // MARK: - Event decision

        VisualElement EventCard()
        {
            var ev = G.PendingEvent;
            var c = DialogShell(ev.Title, 600, out var body, out var foot);
            var row = E.Div().Row(0, Align.FlexStart).Ignore();
            // Left: the scene and the story.
            var left = E.Div().Col().Ignore().Size(210);
            var art = E.Div().Row().Center().Radius(9).Border(Theme.Outline, 2).Ignore();
            art.style.height = 70;
            art.BgImage(Theme.Gradient("eventart", true, Theme.Hex("#8A5A32"), Theme.Hex("#4A2E1A")));
            art.Add(E.Icon("campfire", 52));
            left.Add(art);
            left.Add(E.Ink(ev.Text, 11).Margin(5));
            var note = E.Div().Row(0, Align.FlexStart).Ignore().Margin(5);
            note.Add(E.Icon("legacy", 16));
            note.Add(E.Ink("Your choice is written into the Legacy Timeline.", 10, Theme.InkSoft).Grow().Margin(0, 0, 0, 4));
            left.Add(note);
            row.Add(left);

            // Right: the options.
            var right = E.Div().Col().Grow().Ignore();
            right.style.marginLeft = 10;
            foreach (var option in ev.Options)
            {
                var blocker = G.OptionBlocker(option);
                bool on = chosenOption == option.Id;
                var o = E.Div().Col().Skin(on ? "slot" : "section").Pad(7, 9, on ? 10 : 7, 9).Margin(0, 0, 6);
                o.style.flexShrink = 0;
                if (blocker != null) o.style.opacity = 0.6f;
                var head = E.Div().Row(0, Align.FlexStart).Ignore();
                head.Add(on ? E.Icon("check", 20) : E.Icon("next", 18));
                head.Add(E.T(option.Label, 12.5f).Wrap().Grow().Margin(0, 0, 0, 6));
                if (option.Cost.HasValue && !option.Cost.Value.IsZero) head.Add(CostLine(option.Cost.Value, 10.5f).Also(r => r.style.maxWidth = 120));
                o.Add(head);
                if (!string.IsNullOrEmpty(option.TradeOff)) o.Add(E.T(option.TradeOff, 10.5f, Theme.Pale).Wrap().Margin(2, 0, 0, 26));
                if (blocker != null) o.Add(E.T(blocker, 10.5f, Theme.Bad).Wrap().Margin(2, 0, 0, 26));
                if (blocker == null)
                {
                    string id = option.Id;
                    Tap(o, () => chosenOption = id);
                }
                right.Add(o);
            }
            row.Add(right);
            body.Add(row);

            foot.Add(Share(E.Button("Decide later", Act(() => store.EventMinimized = true), E.Kind.Blue, 36, null, 13), foot));
            bool ready = chosenOption != null;
            foot.Add(Share(E.Button("Confirm", ready ? Act(() => { store.ResolveEvent(chosenOption); chosenOption = null; }) : null,
                                    ready ? E.Kind.Green : E.Kind.Grey, 36, null, 13), foot));
            return c;
        }
    }
}
