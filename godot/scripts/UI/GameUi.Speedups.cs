using System.Linq;
using Founders.Core;
using Godot;
using Founders.Art;
using Founders.World;

namespace Founders.UI
{
    /// <summary>
    /// Speed-ups: the window that uses them on a running job (a building site, the research, the scouts),
    /// the small speed-up button the timers and cards carry, and the cells that show them as items
    /// (the kind's icon on a rarity cell, bigger speed-ups on rarer cells, the size along the bottom).
    /// </summary>
    public partial class GameUi
    {
        /// <summary>The job the speed-up window is open for, or null.</summary>
        SpeedupTask? speeding;

        public void ShowSpeedups(SpeedupTask task) { speeding = task; dirty = true; }
        public void HideSpeedups() { speeding = null; dirty = true; }

        string SpeedupDialogKey()
        {
            if (speeding == null || store?.Game == null) return "";
            var task = speeding.Value;
            if (!G.IsRunning(task)) { speeding = null; return ""; }
            return $"sp|{task}|" + string.Join(",", G.OwnedSpeedups().Select(s => $"{s.Kind}{s.Minutes}x{s.Count}"));
        }

        /// <summary>The kind's icon (a player's own "speedup_construction" etc. wins when present).</summary>
        static string SpeedupIcon(SpeedupKind kind)
        {
            string custom = "speedup_" + kind.ToString().ToLowerInvariant();
            if (Theme.HasIcon(custom)) return custom;
            switch (kind)
            {
                case SpeedupKind.Construction: return "build";
                case SpeedupKind.Research: return "knowledge";
                case SpeedupKind.Exploration: return "compass";
                default: return "sparkle";
            }
        }

        static E.Cell SpeedupCellKind(int minutes) =>
            minutes >= 180 ? E.Cell.Legendary : minutes >= 60 ? E.Cell.Epic : minutes >= 15 ? E.Cell.Rare : minutes >= 5 ? E.Cell.Uncommon : E.Cell.Common;

        /// <summary>A speed-up as an item: kind icon on its cell, a clock in the corner, the size along the bottom.</summary>
        static VisualElement SpeedupCell(SpeedupStack s, float size, bool count = true)
        {
            var c = RewardCell(SpeedupIcon(s.Kind), SpeedupCellKind(s.Minutes), Speedups.SizeName(s.Minutes), size);
            c.Add(E.Icon("clock", Mathf.Round(size * 0.36f)).Abs(-5, null, null, -4));
            if (count && s.Count > 1) c.Add(E.Badge("×" + s.Count, false, 20).Abs(null, -6, -6));
            return c;
        }

        static string TaskIcon(SpeedupTask task) =>
            task.Kind == SpeedupKind.Research ? "knowledge" : task.Kind == SpeedupKind.Exploration ? "compass" : "build";

        /// <summary>The small orange "speed up" button (a clock) on a timer or a card.</summary>
        VisualElement SpeedupButton(SpeedupTask task, float size = 34)
        {
            var b = E.Square("clock", Act(() => ShowSpeedups(task)), E.Kind.Orange, size);
            b.name = "speedup";
            if (G.SpeedupMinutesFor(task.Kind) > 0) b.Add(E.Badge("+", false, 16).Abs(null, -5, -5));
            return b;
        }

        /// <summary>A full-width "Speed up" row for a card section.</summary>
        VisualElement SpeedupRow(SpeedupTask task)
        {
            var r = E.Line(true, 58);
            r.Add(E.RowIcon("clock"));
            int mins = G.SpeedupMinutesFor(task.Kind);
            r.Add(E.T(mins > 0 ? $"Speed-ups for this: {Fmt.Clock(mins * 60.0)}" : "No speed-ups for this yet", Theme.Body, mins > 0 ? (Color?)null : Theme.Pale).Flex());
            r.Add(E.Button("Speed up", Act(() => ShowSpeedups(task)), mins > 0 ? E.Kind.Orange : E.Kind.Brown, Theme.ButtonH, null, 16, null, 150));
            return r;
        }

        VisualElement SpeedupCard()
        {
            var task = speeding.Value;
            var c = DialogShell("Speed Up", 640, out var body, out var foot);
            float inner = DialogInnerW(640);

            // The job: its icon, name, a bar and the time left.
            var head = E.Inset().Row().Margin(0, 0, 8);
            head.Add(E.Slot(TaskIcon(task), 54, E.Cell.Tile, 0.7f));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            col.Add(E.T(G.TaskName(task), Theme.H2).Fit(inner - 90));
            var line = E.Div().Row().Ignore().Margin(4);
            line.Add(LiveBar(() => G.TaskProgress(task), 14, task.Kind == SpeedupKind.Research ? E.FillKind.Blue : E.FillKind.Gold).Grow());
            line.Add(Live(() => Fmt.Clock(G.TaskSecondsLeft(task)), Theme.Num).NoShrink().Margin(0, 0, 0, 8));
            col.Add(line);
            head.Add(col);
            body.Add(head);

            var stacks = G.OwnedSpeedups().Where(s => s.Kind == task.Kind || s.Kind == SpeedupKind.General)
                .OrderBy(s => s.Kind == SpeedupKind.General ? 1 : 0).ThenBy(s => s.Minutes).ToList();
            if (stacks.Count == 0)
            {
                body.Add(Notice("clock", "No speed-ups for this yet. Your people earn them by building, levelling up, researching, exploring and reaching goals."));
            }
            else
            {
                var sec = E.Section("Your speed-ups", out var rows);
                bool sep = false;
                foreach (var s in stacks)
                {
                    var r = E.Line(sep, 70);
                    sep = true;
                    r.Add(SpeedupCell(s, 56, false));
                    var info = E.Div().Col().Grow().Ignore();
                    info.style.marginLeft = 12;
                    info.Add(E.T(Speedups.Name(s.Kind, s.Minutes), Theme.Num).Fit(inner - 56 - 12 - 130 - 30));
                    info.Add(E.T($"Owned: {s.Count}", Theme.Body, Theme.Pale).Margin(2));
                    r.Add(info);
                    var kind = s.Kind;
                    int minutes = s.Minutes;
                    r.Add(E.Button("Use", Act(() => store.UseSpeedup(task, kind, minutes)), E.Kind.Green, Theme.ButtonH, null, 16, null, 120));
                    rows.Add(r);
                }
                body.Add(sec);
            }

            foot.Add(E.Button("Close", Act(() => speeding = null), E.Kind.Brown, Theme.ButtonH, null, 16, null, 150).Margin(0, 10));
            var plan = G.FinishPlan(task);
            if (plan.Count > 0)
            {
                bool covers = plan.Sum(s => s.Minutes * 60.0 * s.Count) + 1e-6 >= G.TaskSecondsLeft(task);
                string sub = $"{plan.Sum(s => s.Count)} speed-up{(plan.Sum(s => s.Count) == 1 ? "" : "s")}";
                foot.Add(E.Button(covers ? "Finish now" : "Use all", Act(() => store.FinishWithSpeedups(task)), E.Kind.Green, Theme.ButtonH, null, 16,
                                  SubLine("clock", sub), 230));
            }
            return c;
        }

        /// <summary>The speed-ups the settlement owns, as items (profile › stats).</summary>
        VisualElement SpeedupInventory()
        {
            var block = Block("Speed-ups", out var rows);
            var owned = G.OwnedSpeedups();
            if (owned.Count == 0)
            {
                rows.Add(E.Ink("None yet. Building, levelling up, researching, exploring and reaching goals earn speed-ups.", Theme.Body).Margin(6, 0, 6));
                return block;
            }
            rows.Add(E.Ink("Tap the clock on any running job to use them. Earned by building, levelling up, researching, exploring and reaching goals.",
                           Theme.Small, Theme.InkSoft).Margin(6, 0, 4));
            var cells = E.Div().Row().Ignore().Margin(6, 0, 8);
            cells.style.flexWrap = Wrap.Wrap;
            foreach (var s in owned)
            {
                var cell = SpeedupCell(s, 58);
                cell.style.marginRight = 12;
                cell.style.marginTop = 8;
                cells.Add(cell);
            }
            rows.Add(cells);
            return block;
        }
    }
}
