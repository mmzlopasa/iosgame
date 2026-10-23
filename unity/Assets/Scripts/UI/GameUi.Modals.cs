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
    /// taller than the screen allows: its body scrolls while its buttons stay put. In portrait every dialog
    /// is a bottom sheet across the screen (the kit's popup frame with secondary + primary buttons), wide
    /// two-column dialogs stack their columns, and the narrator stands on top of the story box.
    /// </summary>
    public partial class GameUi
    {
        string chosenOption;

        /// <summary>The dialog's inner width (its panel padding is 12 + 12).</summary>
        float DialogInnerW(float width) => (Portrait ? SheetW : Mathf.Min(width, SafeWidth - 40)) - 24;

        /// <summary>
        /// A two-column dialog body (the scene on the left, the choices on the right); portrait stacks them, the scene
        /// on top as a short banner.
        /// </summary>
        VisualElement TwoColumns(VisualElement left, VisualElement right)
        {
            var row = E.Div().Row(0, Align.FlexStart).Ignore();
            if (Portrait)
            {
                row.style.flexDirection = FlexDirection.Column;
                row.style.alignItems = Align.Stretch;
                left.style.width = StyleKeyword.Auto;
                right.style.marginLeft = 0;
                right.style.marginTop = 10;
            }
            row.Add(left);
            row.Add(right);
            return row;
        }

        string ModalKey()
        {
            if (store?.Game == null) return "";
            // The cloud's choice and the profile's dialogs come first: they answer the player's own taps.
            string profile = ProfileDialogKey();
            if (profile != "") return profile;
            string speed = SpeedupDialogKey();
            if (speed != "") return speed;
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
                .Pad(PopupTop + 4, (Portrait ? 8 : 16) + SafeRight, BottomPad + 8, (Portrait ? 8 : 16) + SafeLeft);
            if (Portrait) frame.style.justifyContent = Justify.FlexEnd;
            host.Add(frame);
            VisualElement card;
            if (key.StartsWith("pd|", System.StringComparison.Ordinal) || key.StartsWith("cloud|", System.StringComparison.Ordinal)) card = ProfileDialogCard();
            else if (key.StartsWith("sp|", System.StringComparison.Ordinal)) card = SpeedupCard();
            else if (key.StartsWith("ms|", System.StringComparison.Ordinal)) card = MilestoneCard();
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

        /// <summary>Width of the wide dialogs (event, petition) and of their picture column.</summary>
        const float DialogW = 680, DialogArtW = 230;

        /// <summary>A dialog window: plank title, a body that scrolls when it must, a fixed button row.</summary>
        VisualElement DialogShell(string title, float width, out VisualElement body, out VisualElement foot)
        {
            float w = Portrait ? SheetW : Mathf.Min(width, SafeWidth - 40);
            var c = E.Panel().Pad(0, 12, 12, 12);
            c.name = "dialog";
            c.style.width = w;
            c.style.maxHeight = PanelH - PopupTop - BottomPad - 10;
            c.style.flexShrink = 1;
            var plank = E.Plank(title, null, Theme.H1, w);
            plank.style.marginLeft = -16;
            plank.style.marginRight = -16;
            plank.style.marginTop = -6;
            c.Add(plank);
            var scroll = E.FitScroll();
            scroll.contentContainer.style.paddingTop = 10;
            scroll.contentContainer.style.paddingBottom = 4;
            c.Add(scroll);
            body = scroll.contentContainer;
            foot = E.Div().Row().Center().Ignore();
            foot.style.flexShrink = 0;
            foot.style.paddingTop = 8;
            c.Add(foot);
            return c;
        }

        // MARK: - Story (the kit's dialogue box)

        void StoryBox(VisualElement host)
        {
            var ch = G.PendingStory;
            if (Portrait) { StoryBoxPortrait(host, ch); return; }
            float left = SafeLeft + 12, right = SafeRight + 12;
            // The kit's dialogue box: a wide parchment box along the bottom, the narrator standing large at its
            // left end (over its edge), the text beside them and the ▼ marker in the corner.
            const float portrait = 210, overlap = 100;
            float textLeft = portrait - overlap + 14;
            var box = E.Panel().Abs(left + overlap, null, right, BottomPad + 6).Pad(14, 22, 14, textLeft);
            box.style.maxHeight = PanelH - PopupTop - BottomPad - 6;
            float textW = SafeWidth - 24 - overlap - textLeft - 22;
            box.Add(E.T($"Chapter {ch.Number} · {ch.Era}", Theme.Body, Theme.Pale).Fit(textW));
            box.Add(E.T(ch.Title, Theme.H1 + 4, Theme.Gold).Fit(textW, Theme.H2).Margin(2));
            var scroll = E.FitScroll();
            scroll.contentContainer.style.paddingTop = 6;
            foreach (var para in ch.Story.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(para)) continue;
                scroll.Add(E.T(para.Trim(), Theme.Num).Wrap().Margin(0, 0, 8));
            }
            var goal = E.Inset().Row().Margin(2);
            goal.Add(E.Icon(ch.Locked ? "lock" : "goals", 26));
            goal.Add(E.T(ch.Goal, Theme.Body, Theme.Gold).Flex().Margin(0, 0, 0, 8));
            scroll.Add(goal);
            box.Add(scroll);
            var foot = E.Div().Row().Ignore();
            foot.style.flexShrink = 0;
            foot.style.justifyContent = Justify.FlexEnd;
            foot.style.paddingTop = 8;
            foot.Add(E.Button(ch.Number == 1 ? "Begin" : "Continue", Act(() => store.DismissStory()), E.Kind.Green, Theme.ButtonH, null, 17, null, 180));
            foot.Add(E.Icon("next", 26).Also(i => i.style.rotate = new Rotate(new Angle(90, AngleUnit.Degree))).Margin(0, 0, 0, 12));
            box.Add(foot);
            host.Add(box);
            // The narrator stands at the left end of the box, larger than it.
            var face = E.Icon("face_f1", portrait).Abs(left, null, null, BottomPad + 6);
            host.Add(face);
            Pop(box);
        }

        /// <summary>
        /// Portrait: the dialogue box across the bottom, the narrator standing on its top-left edge with the chapter's
        /// title beside their feet, the text and the goal under them, Continue bottom-right.
        /// </summary>
        void StoryBoxPortrait(VisualElement host, ChapterDefinition ch)
        {
            const float portrait = 170, overlap = 34;
            float w = SheetW;
            float left = SafeLeft + (SafeWidth - w) / 2;
            var box = E.Panel().Abs(left, null, null, BottomPad + 6).Pad(14, 16, 14, 16);
            box.style.width = w;
            box.style.maxHeight = PanelH - PopupTop - BottomPad - 6 - (portrait - overlap);
            float textW = w - 32;
            // The heading sits beside the narrator's feet, to the right of where they overlap the box.
            var heading = E.Div().Col().Ignore();
            heading.style.flexShrink = 0;
            heading.style.marginLeft = portrait - 30;
            float headW = textW - (portrait - 30);
            heading.Add(E.T($"Chapter {ch.Number} · {ch.Era}", Theme.Body, Theme.Pale).Fit(headW));
            heading.Add(E.T(ch.Title, Theme.H1 + 2, Theme.Gold).Fit(headW, Theme.Body).Margin(2));
            box.Add(heading);
            var scroll = E.FitScroll();
            scroll.contentContainer.style.paddingTop = 8;
            foreach (var para in ch.Story.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(para)) continue;
                scroll.Add(E.T(para.Trim(), Theme.Num).Wrap().Margin(0, 0, 8));
            }
            var goal = E.Inset().Row().Margin(2);
            goal.Add(E.Icon(ch.Locked ? "lock" : "goals", 26));
            goal.Add(E.T(ch.Goal, Theme.Body, Theme.Gold).Flex().Margin(0, 0, 0, 8));
            scroll.Add(goal);
            box.Add(scroll);
            var foot = E.Div().Row().Ignore();
            foot.style.flexShrink = 0;
            foot.style.justifyContent = Justify.FlexEnd;
            foot.style.paddingTop = 8;
            foot.Add(E.Button(ch.Number == 1 ? "Begin" : "Continue", Act(() => store.DismissStory()), E.Kind.Green, Theme.ButtonH, null, 17, null, 180));
            foot.Add(E.Icon("next", 26).Also(i => i.style.rotate = new Rotate(new Angle(90, AngleUnit.Degree))).Margin(0, 0, 0, 12));
            box.Add(foot);
            // The narrator stands on the box's top-left edge.
            box.Add(E.Icon("face_f1", portrait).Abs(-6, overlap - portrait));
            host.Add(box);
            Pop(box);
        }

        // MARK: - Milestone

        VisualElement MilestoneCard()
        {
            var ms = G.Content.Milestones.LastOrDefault(m => G.State.ReachedMilestones.Contains(m.Id));
            var c = DialogShell("Milestone reached", 520, out var body, out var foot);
            var hero = E.Div().Row().Ignore();
            hero.Add(E.Slot("trophy", 84, E.Cell.Legendary, 0.7f));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 14;
            col.Add(E.T(ms?.Name ?? "A new chapter", Theme.H1, Theme.Gold).Wrap());
            col.Add(E.Ink(store.Celebration, Theme.Body).Margin(4));
            hero.Add(col);
            body.Add(hero);
            var note = E.Inset().Row().Margin(10);
            note.Add(E.Icon("legacy", 24));
            note.Add(E.T("Recorded in your Legacy Timeline.", Theme.Body).Flex().Margin(0, 0, 0, 8));
            body.Add(note);
            foot.Add(E.Button("Continue", Act(() => store.Celebration = null), E.Kind.Orange, Theme.ButtonH, null, 17, null, 240));
            return c;
        }

        // MARK: - Welcome back

        VisualElement WelcomeCard()
        {
            var report = store.Offline;
            var c = DialogShell("Welcome back, Founder", 600, out var body, out var foot);
            int mins = (int)(report.Seconds / 60);
            string away = mins >= 60 ? $"{mins / 60} h {mins % 60} min" : $"{mins} min";
            body.Add(E.T($"Your people kept the fire alive for {away}.", Theme.Num).Wrap());
            double cap = G.Content.Balance.MaxOfflineSeconds / 3600.0;
            body.Add(E.Ink($"Time away counts for up to {cap.ToString("0.#", CultureInfo.InvariantCulture)} hours.", Theme.Small).Margin(3));

            // Gathered: the kit's reward cells with the amount at the bottom.
            var gathered = E.Section("Gathered", out var rows);
            gathered.style.marginTop = 8;
            // Only what changed, centred in the section.
            var cells = E.Div().Row().Ignore().Margin(10, 0, 8);
            cells.style.flexWrap = Wrap.Wrap;
            cells.style.justifyContent = Justify.Center;
            foreach (var type in ResourceTypes.All)
            {
                double v = report.Gained[type];
                if (Fmt.Signed(v) == "0") continue;
                var cell = RewardCell(IconFor(type), CellFor(type), (v < 0 ? "−" : "+") + Fmt.Compact(System.Math.Abs(v)), 50);
                cell.style.marginLeft = 6;
                cell.style.marginRight = 6;
                if (v < -0.5) cell.Add(E.Icon("alert", 22).Abs(null, -6, -6));
                cells.Add(cell);
            }
            rows.Add(cells);
            body.Add(gathered);
            if (report.Events.Count > 0)
            {
                var village = E.Section("The village", out var vrows);
                village.style.marginTop = 8;
                bool sep = false;
                foreach (var line in report.Events.Take(5))
                {
                    var r = E.Line(sep, Theme.RowTightH);
                    sep = true;
                    r.Add(E.RowIcon("check"));
                    r.Add(E.T(line, Theme.Body).Flex());
                    vrows.Add(r);
                }
                body.Add(village);
            }
            foot.Add(E.Button("Back to the village", Act(() => store.Offline = null), E.Kind.Green, Theme.ButtonH, null, 17, null, 280));
            return c;
        }

        // MARK: - Event decision

        VisualElement EventCard()
        {
            var ev = G.PendingEvent;
            var c = DialogShell(ev.Title, DialogW, out var body, out var foot);
            // Left: the scene and the story.
            var left = E.Div().Col().Ignore().Size(DialogArtW);
            var art = E.Div().Row().Center().Ignore();
            art.style.height = Portrait ? 76 : 90;
            // The kit look: the scene in the dark inset; the game's own: a wooden frame.
            if (Theme.KitLook) art.Skin("section");
            else art.Radius(10).Border(Theme.Outline, 2.5f).BgImage(Theme.Gradient("eventart", true, Theme.Hex("#8A5A32"), Theme.Hex("#4A2E1A")));
            art.Add(E.Icon("campfire", 68));
            left.Add(art);
            left.Add(E.Ink(ev.Text, Theme.Body).Margin(8));
            var note = E.Div().Row(0, Align.FlexStart).Ignore().Margin(8);
            note.Add(E.Icon("legacy", 22));
            note.Add(E.Ink("Your choice is written into the Legacy Timeline.", Theme.Small, Theme.InkSoft).Grow().Margin(0, 0, 0, 6));
            left.Add(note);

            // Right: the options.
            var right = E.Div().Col().Grow().Ignore();
            right.style.marginLeft = 12;
            float optionW = Portrait ? DialogInnerW(DialogW) : DialogInnerW(DialogW) - DialogArtW - 12;
            foreach (var option in ev.Options)
            {
                var blocker = G.OptionBlocker(option);
                bool on = chosenOption == option.Id;
                var o = E.Div().Col().Skin(on ? "slot" : "section").Pad(10, 12, on ? 13 : 10, 12).Margin(0, 0, 8);
                o.style.flexShrink = 0;
                o.style.minHeight = 52;
                if (blocker != null) o.style.opacity = 0.6f;
                var head = E.Div().Row().Ignore();
                head.Add(on ? E.Icon("check", 26) : E.Icon("next", 22));
                head.Add(E.T(option.Label, Theme.Num).Flex().Margin(0, 0, 0, 8));
                if (option.Cost.HasValue && !option.Cost.Value.IsZero) head.Add(CostLine(option.Cost.Value).Also(r => { r.style.maxWidth = Mathf.Min(150, optionW * 0.4f); r.style.flexShrink = 0; }));
                o.Add(head);
                if (!string.IsNullOrEmpty(option.TradeOff)) o.Add(E.T(option.TradeOff, Theme.Small, Theme.Pale).Wrap().Margin(3, 0, 0, 34));
                if (blocker != null) o.Add(E.T(blocker, Theme.Small, Theme.Bad).Wrap().Margin(3, 0, 0, 34));
                if (blocker == null)
                {
                    string id = option.Id;
                    Tap(o, () => chosenOption = id);
                }
                right.Add(o);
            }
            body.Add(TwoColumns(left, right));

            // The kit's pair: secondary on the left, primary on the right, each a fixed comfortable width (portrait:
            // an equal share of the sheet's width each).
            float bw = Portrait ? Mathf.Floor((DialogInnerW(DialogW) - 12) / 2) : 220;
            foot.Add(E.Button("Decide later", Act(() => store.EventMinimized = true), E.Kind.Blue, Theme.ButtonH, null, 17, null, bw));
            bool ready = chosenOption != null;
            foot.Add(E.Button("Confirm", ready ? Act(() => { store.ResolveEvent(chosenOption); chosenOption = null; }) : null,
                              ready ? E.Kind.Green : E.Kind.Grey, Theme.ButtonH, null, 17, null, bw).Margin(0, 0, 0, Portrait ? 12 : 16));
            return c;
        }
    }
}
