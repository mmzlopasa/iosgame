using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// The card docked on the left for whatever is selected or being placed, built like the kit's building
    /// popups: a wooden plank with the name and level, "Upgrade Stats" (now ➜ next), the workers (portraits,
    /// vacant and locked places, a −/+ stepper), "Upgrade Resources" (requirements and costs with ✓ or Go)
    /// and a button row at the bottom (demolish, a secondary action, the green Upgrade with its time).
    /// </summary>
    public partial class GameUi
    {
        string contextIdentity;
        ScrollView contextBody;

        string ContextKey()
        {
            if (store.DecoratingId != null)
            {
                var d = G.Content.Decoration(store.DecoratingId);
                return $"deco|{store.DecoratingId}|{store.PaintMode}|{store.DecorationRotation}|{(d == null || G.State.Resources.Covers(d.Cost))}|{G.State.Decorations.Count}";
            }
            if (store.PlacingBuildingId != null)
            {
                var issues = store.PlacementIssues();
                var def = G.Content.Building(store.PlacingBuildingId);
                return $"place|{store.PlacingBuildingId}|{(issues.Count > 0 ? issues[0].Message : "ok")}|{store.PlacementRotation}|{store.GhostCell}|{(def != null && G.State.Resources.Covers(def.Cost))}";
            }
            if (store.SelectedNode is (int nx, int ny) && store.SelectedBuildingId == null)
            {
                var n = G.NodeAt(nx, ny);
                return n == null ? "none" : $"node|{nx},{ny}|{n.Ordered}|{n.Depleted}|{G.IsExhausted(n.Job)}|{G.JobCapacity(n.Job)}";
            }
            if (store.SelectedBuildingId != null)
            {
                var b = G.State.Building(store.SelectedBuildingId.Value);
                if (b == null) return "none";
                var def = G.Content.Building(b.DefinitionId);
                var target = def?.UpgradesTo == null ? null : G.Content.Building(def.UpgradesTo);
                string up = target == null ? "" : $"{G.IsUnlocked(target)}{CoverKey(target.Cost)}";
                string jobs = string.Join(",", (def?.JobSlots ?? new List<JobSlot>()).Select(s => $"{G.State.Assignments[s.Job]}/{G.JobCapacity(s.Job)}"));
                var lv = G.NextLevel(b);
                string level = $"{b.Level}|{(lv == null ? "" : CoverKey(lv.Cost))}|{G.HasRoadAccess(b)}|{Time.unscaledTime < confirmDemolishUntil}|" +
                               string.Join("", G.LevelRequirements(b).Select(r => r.Met ? "1" : "0"));
                return $"sel|{b.Id}|{b.DefinitionId}|{b.Status}|{up}|{jobs}|{G.IdleWorkers > 0}|{level}|{(def?.Heart == true ? StokeKey() : "")}";
            }
            return "none";
        }

        /// <summary>Which resources of a cost are covered ("1101"): part of keys so a cost row turns ✓ when it is.</summary>
        string CoverKey(ResourceBundle cost)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in cost.Entries) sb.Append(G.State.Resources[kv.Key] + 1e-6 >= kv.Value ? '1' : '0');
            return sb.ToString();
        }

        void BuildContext(VisualElement host)
        {
            if (store.DecoratingId != null) DecorationCard(host);
            else if (store.PlacingBuildingId != null) PlacementCard(host);
            else if (store.SelectedBuildingId != null) BuildingCard(host);
            else if (store.SelectedNode != null) NodeCard(host);
            // A different selection starts at the top of its card; updates of the same one keep the scroll.
            string identity = $"{store.DecoratingId}|{store.PlacingBuildingId}|{store.SelectedBuildingId}|{store.SelectedNode}";
            bool same = identity == contextIdentity;
            contextIdentity = identity;
            var keep = same ? lastContextScroll : Vector2.zero;
            lastContextScroll = keep;
            if (contextBody != null && keep != Vector2.zero)
            {
                var sv = contextBody;
                EventCallback<GeometryChangedEvent> restore = null;
                restore = _ => { sv.scrollOffset = keep; sv.contentContainer.UnregisterCallback(restore); };
                sv.contentContainer.RegisterCallback(restore);
            }
        }

        Vector2 lastContextScroll;

        /// <summary>The card: plank title and close, a body that scrolls when needed, a fixed button row.</summary>
        VisualElement CardShell(VisualElement host, string title, int? level, Action onClose, out VisualElement body, out VisualElement foot)
        {
            var card = E.Panel().Pad(0, 8, 9, 8);
            card.style.flexShrink = 1;
            card.style.minHeight = 0;
            var plank = E.Plank(title, level, 15, CardW + 8);
            plank.style.marginTop = -4;
            plank.style.marginLeft = -12;
            plank.style.marginRight = -12;
            card.Add(plank);
            card.Add(E.Close(Act(onClose)).Abs(null, -9, -8));
            var scroll = E.FitScroll();
            scroll.contentContainer.style.paddingTop = 5;
            scroll.contentContainer.style.paddingBottom = 2;
            scroll.verticalScroller.valueChanged += _ => lastContextScroll = scroll.scrollOffset;
            contextBody = scroll;
            card.Add(scroll);
            body = scroll.contentContainer;
            foot = E.Div().Row().Ignore();
            foot.style.flexShrink = 0;
            foot.style.paddingTop = 6;
            card.Add(foot);
            host.Add(card);
            return card;
        }

        /// <summary>A footer button that takes an equal share of the row.</summary>
        static VisualElement Share(VisualElement b, VisualElement row)
        {
            b.style.flexGrow = 1;
            b.style.flexBasis = 0;
            b.style.flexShrink = 1;
            b.style.minWidth = 0;
            if (row.childCount > 0) b.style.marginLeft = 6;
            return b;
        }

        static void Lead(VisualElement b, VisualElement row)
        {
            if (row.childCount > 0) b.style.marginLeft = 6;
            row.Add(b);
        }

        /// <summary>A button's second line: a small icon and an outlined text (a cost or a timer).</summary>
        static VisualElement SubLine(string icon, string text, Color? color = null)
        {
            var r = E.Div().Row().Ignore();
            r.Add(E.Icon(icon, 13));
            var l = E.T(text, 10, color);
            l.style.marginLeft = 2;
            r.Add(l);
            return r;
        }

        /// <summary>A whole cost as small icon + number pairs (red where the stores fall short).</summary>
        VisualElement CostLine(ResourceBundle cost, float size = 10.5f, bool center = false)
        {
            var row = E.Div().Row().Ignore();
            row.style.flexWrap = Wrap.Wrap;
            if (center) row.style.justifyContent = Justify.Center;
            if (!cost.Entries.Any()) { row.Add(E.T("Free", size, Theme.Up)); return row; }
            foreach (var kv in cost.Entries)
            {
                bool shortOf = G.State.Resources[kv.Key] + 1e-6 < kv.Value;
                var item = E.Div().Row().Ignore();
                item.style.marginRight = 5;
                item.Add(E.Icon(IconFor(kv.Key), size + 4));
                var t = E.T(Fmt.Compact(kv.Value), size, shortOf ? Theme.Bad : (Color?)null);
                t.style.marginLeft = 1;
                item.Add(t);
                row.Add(item);
            }
            return row;
        }

        /// <summary>The cheapest sub-line for a cost: its first resource (red if short).</summary>
        VisualElement CostSub(ResourceBundle cost)
        {
            var first = cost.Entries.FirstOrDefault();
            if (first.Value <= 0) return SubLine("check", "Free");
            bool shortOf = G.State.Resources[first.Key] + 1e-6 < first.Value;
            return SubLine(IconFor(first.Key), Fmt.Compact(first.Value), shortOf ? Theme.Bad : (Color?)null);
        }

        /// <summary>Resource rows "have / need" with ✓ when covered or Go (who gathers it) when not.</summary>
        void CostRows(VisualElement body, ResourceBundle cost, ref bool sep)
        {
            foreach (var kv in cost.Entries)
            {
                var type = kv.Key;
                double need = kv.Value;
                var r = E.Line(sep, 27);
                sep = true;
                r.Add(E.Icon(IconFor(type), 16).Margin(0, 5));
                var have = E.T("", 11.5f);
                var rest = E.T(" / " + Fmt.Compact(need), 11.5f);
                r.Add(have);
                r.Add(rest);
                r.Add(E.Div().Grow().Ignore());
                bool met = G.State.Resources[type] + 1e-6 >= need;
                r.Add(met ? E.Icon("check", 20) : GoButton(() => { peopleTab = 0; store.ActivePanel = Panel.People; }));
                Bind(() =>
                {
                    double x = G.State.Resources[type];
                    string s = Fmt.Compact(x);
                    if (have.text != s) have.text = s;
                    E.Tint(have, x + 1e-6 >= need ? Theme.Up : Theme.Bad);
                });
                body.Add(r);
            }
        }

        VisualElement GoButton(Action onTap) => E.Button("Go", Act(onTap), E.Kind.Blue, 24, null, 11.5f, null, 46);

        /// <summary>A requirement row: the text, then ✓ or Go.</summary>
        VisualElement RequirementRow(string icon, string text, bool met, Action go, bool sep, float width = 0)
        {
            var r = E.Line(sep, 27);
            r.Add(E.Icon(icon, 16).Margin(0, 5));
            r.Add(E.T(text, 11.5f, met ? (Color?)null : Theme.Pale).Fit((width > 0 ? width : CardW) - 100, 8).Grow());
            r.Add(met ? E.Icon("check", 20) : go != null ? GoButton(go) : E.IconDim("check", 20));
            return r;
        }

        static string FaceFor(Person p) => (p.Female ? "face_f" : "face_m") + (1 + Math.Abs(p.Id) % 3).ToString(CultureInfo.InvariantCulture);

        // MARK: - Placement

        void PlacementCard(VisualElement host)
        {
            var def = G.Content.Building(store.PlacingBuildingId);
            if (def == null) return;
            var issues = store.PlacementIssues();
            bool ok = issues.Count == 0;
            CardShell(host, def.Name, null, store.CancelPlacement, out var body, out var foot);

            var head = E.Div().Row(0, Align.FlexStart).Ignore();
            head.Add(E.Slot(BuildingIcon(def), 48, CellFor(def)));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 8;
            var time = E.Div().Row().Ignore();
            time.Add(E.Icon("clock", 15));
            time.Add(E.T(Fmt.Clock(G.EstimatedSeconds(def.BuildSeconds)), 11.5f).Margin(0, 0, 0, 3));
            if (def.Footprint.Width > 1 || def.Footprint.Height > 1)
                time.Add(E.T($"Size {def.Footprint.Width}×{def.Footprint.Height}", 11, Theme.Pale).Margin(0, 0, 0, 10));
            col.Add(time);
            col.Add(CostLine(def.Cost, 11).Margin(4));
            head.Add(col);
            body.Add(head);

            var status = E.Inset().Row().Margin(6);
            status.Add(E.Icon(ok ? "check" : "alert", 18).NoShrink());
            var msg = E.T(ok ? "Good spot. Drag it or tap the ground to move it." : issues[0].Message, 11.5f, ok ? (Color?)null : Theme.Hex("#FFD2C8")).Wrap().Grow();
            msg.style.marginLeft = 6;
            status.Add(msg);
            body.Add(status);
            // Gatherers' camps: how much of their resource lies within reach of this spot.
            if (store.GhostCell is (int gx, int gy))
                foreach (var slot in def.JobSlots ?? new List<JobSlot>())
                {
                    int n = G.NodesInReach(slot.Job, gx, gy, Game.Rotated(def.Footprint, store.PlacementRotation));
                    if (n < 0) continue;
                    var kind = G.NodeKinds.FirstOrDefault(k => k.Job == slot.Job);
                    body.Add(E.StatRow(IconFor(kind.Resource), $"{kind.Name} in reach", n.ToString(Inv), null, false, n > 4 ? (Color?)null : Theme.Bad).Margin(4));
                }

            Lead(E.Square("rotate", Act(store.RotatePlacement), E.Kind.Blue, 34), foot);
            foot.Add(Share(E.Button("Cancel", Act(store.CancelPlacement), E.Kind.Red, 36), foot));
            bool afford = G.State.Resources.Covers(def.Cost);
            foot.Add(Share(E.Button("Build here", ok ? Act(store.ConfirmPlacement) : null, ok ? E.Kind.Green : E.Kind.Grey, 36, null, 13,
                                    afford ? SubLine("clock", Fmt.Clock(G.EstimatedSeconds(def.BuildSeconds))) : CostSub(def.Cost)), foot));
        }

        // MARK: - Decorations (roads, fences, lights…)

        void DecorationCard(VisualElement host)
        {
            bool removing = store.DecoratingId == GameStore.RemoveTool;
            var def = removing ? null : G.Content.Decoration(store.DecoratingId);
            CardShell(host, removing ? "Clear decorations" : def?.Name ?? "Town", null, store.EndDecorating, out var body, out var foot);
            var head = E.Div().Row(0, Align.FlexStart).Ignore();
            head.Add(E.Slot(removing ? "trash" : DecorIconFor(def), 48, removing ? E.Cell.Common : E.Cell.Epic));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 8;
            string hint = removing ? "Tap a road, fence or ornament to remove it. Half of its cost comes back."
                : store.PaintMode ? "Drag across the ground to draw. Tap the hand to move the camera instead."
                : "Tap the ground to place. Each tap places one.";
            col.Add(E.Ink(hint, 11));
            if (def != null) col.Add(CostLine(def.Cost, 11).Margin(4));
            head.Add(col);
            body.Add(head);
            if (def != null && (def.Beauty > 0 || def.Heat != null))
            {
                var sec = E.Section("Effects", out var rows);
                bool sep = false;
                if (def.Beauty > 0) { rows.Add(E.StatRow("sparkle", "Beauty", $"+{def.Beauty}", null, sep)); sep = true; }
                if (def.Heat != null)
                {
                    rows.Add(E.StatRow("flame", "Warmth", SignedDeg(def.Heat.Heat), null, sep));
                    rows.Add(E.StatRow("compass", "Reach", $"{Num(def.Heat.Radius)} cells"));
                    if (def.Heat.WoodPerSecond > 0) rows.Add(E.StatRow("wood", "Burns in the cold", $"{Num(def.Heat.WoodPerSecond * 3600)}/h"));
                }
                sec.style.marginTop = 6;
                body.Add(sec);
            }
            if (!removing)
            {
                Lead(E.Square(store.PaintMode ? "road" : "compass", Act(store.TogglePaintMode), store.PaintMode ? E.Kind.Orange : E.Kind.Blue, 34), foot);
                Lead(E.Square("rotate", Act(store.RotatePlacement), E.Kind.Blue, 34), foot);
            }
            Lead(E.Square("trash", Act(() => store.BeginDecorating(removing ? "dirt_path" : GameStore.RemoveTool)), E.Kind.Red, 34), foot);
            foot.Add(Share(E.Button("Done", Act(store.EndDecorating), E.Kind.Green, 36, "check"), foot));
        }

        static string DecorIconFor(DecorationDefinition d)
        {
            if (d == null) return "sparkle";
            if (d.IsRoad && !d.IsBridge) return "road"; // roads are drawn into the ground: a symbol reads better
            return "thumb:deco:" + d.Sprite;
        }

        // MARK: - Resource nodes (trees, rocks, ore, berries, fishing water)

        void NodeCard(VisualElement host)
        {
            var (x, y) = store.SelectedNode.Value;
            var n = G.NodeAt(x, y);
            if (n == null) { store.SelectedNode = null; return; }
            string icon = n.Terrain == TerrainType.Forest ? "tree" : n.Terrain == TerrainType.Rock ? "stone" : n.Terrain == TerrainType.Ore ? "ore"
                : n.Terrain == TerrainType.Water ? "fish" : "food";
            CardShell(host, n.Kind?.Name ?? "Resource", null, () => store.SelectedNode = null, out var body, out var foot);
            string jobName = G.Content.JobConfig(n.Job)?.Name ?? n.Job.Id();

            var head = E.Div().Row().Ignore();
            head.Add(E.Slot(icon, 48, CellFor(n.Resource)));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 8;
            col.Add(E.T(n.Ordered ? "Gathered first" : jobName, 12, n.Ordered ? Theme.Gold : (Color?)null));
            col.Add(Live(() => $"{G.State.Assignments[n.Job]} at work", 11, Theme.Pale).Margin(2));
            head.Add(col);
            body.Add(head);

            bool endless = double.IsInfinity(n.Remaining);
            var sec = E.Section($"{n.Resource.DisplayName()} left", out var rows);
            sec.style.marginTop = 6;
            string left = endless ? "Plenty" : n.Depleted ? "Used up" : $"{Fmt.Compact(n.Remaining)} / {Fmt.Compact(n.Capacity)}";
            var r1 = E.StatRow(IconFor(n.Resource), "Amount", left, null, false, n.Depleted ? Theme.Bad : (Color?)null);
            rows.Add(r1);
            Bind(() =>
            {
                var node = G.NodeAt(x, y);
                if (node == null || double.IsInfinity(node.Remaining)) return;
                double regrow = G.RegrowSecondsLeft(node);
                string s = node.Depleted ? (regrow > 0 ? $"back in {Fmt.Clock(regrow)}" : "Used up") : $"{Fmt.Compact(node.Remaining)} / {Fmt.Compact(node.Capacity)}";
                var l = (Label)r1.ElementAt(r1.childCount - 1);
                if (l.text != s) l.text = s;
            });
            if (!endless)
            {
                var bar = LiveBar(() => G.NodeAt(x, y)?.Fraction ?? 0, 10);
                bar.style.marginBottom = 5;
                rows.Add(bar);
            }
            body.Add(sec);
            if (G.JobCapacity(n.Job) == 0)
                body.Add(Notice("build", $"Nobody can gather this yet: build a workplace for {jobName} nearby."));

            if (!n.Depleted)
            {
                string verb = n.Terrain == TerrainType.Forest ? "Send woodcutters" : n.Terrain == TerrainType.Rock ? "Send quarriers"
                    : n.Terrain == TerrainType.Ore ? "Send miners" : n.Terrain == TerrainType.Water ? "Send fishers" : "Send foragers";
                string tool = n.Terrain == TerrainType.Forest ? "axe" : n.Terrain == TerrainType.Water ? "spear" : n.Terrain == TerrainType.Grass ? "food" : "pickaxe";
                if (n.Ordered) Lead(E.Square("close", Act(() => store.ToggleHarvest(x, y)), E.Kind.Red, 34), foot);
                foot.Add(Share(E.Button(verb, Act(() => store.SendGatherer(x, y)), E.Kind.Green, 36, tool, 13, null, CardW - 70), foot));
            }
            else foot.Hidden(true);
        }

        /// <summary>A hint in an inset box with an icon.</summary>
        static VisualElement Notice(string icon, string text, bool bad = false)
        {
            var n = E.Inset().Row(0, Align.FlexStart).Margin(6);
            n.Add(E.Icon(icon, 18).NoShrink());
            var t = E.T(text, 11, bad ? Theme.Hex("#FFD2C8") : (Color?)null).Wrap().Grow();
            t.style.marginLeft = 6;
            n.Add(t);
            return n;
        }

        // MARK: - Buildings

        void BuildingCard(VisualElement host)
        {
            var b = G.State.Building(store.SelectedBuildingId.Value);
            var def = b == null ? null : G.Content.Building(b.DefinitionId);
            if (def == null) { store.SelectedBuildingId = null; return; }
            int id = b.Id;
            bool complete = b.Status == BuildingStatus.Complete;
            CardShell(host, G.DisplayName(b), def.MaxLevel > 1 ? b.Level : (int?)null, () => store.SelectedBuildingId = null, out var body, out var foot);

            // Head: the building's cell, what it is and what it does.
            var head = E.Div().Row(0, Align.FlexStart).Ignore();
            head.Add(E.Slot(BuildingIcon(def, b.Level), 48, CellFor(def)));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 8;
            string state = complete ? Category(def) : b.Status == BuildingStatus.Upgrading ? "Upgrading" : "Under construction";
            col.Add(E.T(state, 11.5f, complete ? (Color?)null : Theme.Gold));
            col.Add(E.Ink(def.Description, 10.5f).Margin(1));
            var next = def.SuccessorId == null ? null : G.Content.Building(def.SuccessorId);
            if (next != null) col.Add(E.Ink($"Becomes the {next.Name} when the settlement grows.", 10, Theme.InkSoft).Margin(2));
            head.Add(col);
            var lv = G.NextLevel(b);
            bool canLevel = lv != null && complete;
            if (def.Heart && G.HasWarmth)
            {
                // The heart shows its reach like the kit's watchtower: the fire on a warm glow, reach now ➜ next.
                body.Add(ReachIllustration(b.Level, canLevel));
                body.Add(E.Ink(def.Description, 10.5f).Align(TextAnchor.MiddleCenter).Margin(0, 6, 0, 6));
            }
            else body.Add(head);

            if (!complete)
            {
                var sec = E.Section("Construction", out var rows);
                sec.style.marginTop = 6;
                var bar = LiveLabeledBar(() => G.State.Building(id)?.Progress ?? 1, () => $"{(int)((G.State.Building(id)?.Progress ?? 1) * 100)}%", 13);
                bar.style.marginTop = 5;
                rows.Add(bar);
                var tr = E.Line(false);
                tr.Add(E.Icon("clock", 15).Margin(0, 5));
                tr.Add(E.T("Time left", 11.5f).Grow());
                tr.Add(Live(() => Fmt.Clock(G.EstimatedSeconds(G.State.Building(id)?.RemainingWork ?? 0)), 12));
                rows.Add(tr);
                body.Add(sec);
            }

            StatsSection(body, b, def, canLevel);
            foreach (var slot in def.JobSlots ?? new List<JobSlot>())
                if (body.Children().All(c => c.name != "workers-" + slot.Job)) body.Add(WorkersSection(slot.Job, b, def, canLevel));
            if (def.Heart && G.HasWarmth && (complete || b.IsOperational)) body.Add(FireSection());

            // Upgrade Resources: what the next level needs.
            bool ready = false, can = false;
            if (canLevel)
            {
                var sec = E.Section($"Upgrade to Lv.{b.Level + 1}", out var rows);
                sec.style.marginTop = 6;
                bool sep = false;
                var reqs = G.LevelRequirements(b);
                foreach (var r in reqs)
                {
                    bool heartReq = !def.Heart && sep == false && r.Text.StartsWith(G.HeartName, StringComparison.Ordinal);
                    Action go = heartReq && G.Heart != null ? () => store.FocusBuilding(G.Heart.Id) : (Action)(() => store.ActivePanel = Panel.Build);
                    rows.Add(RequirementRow(heartReq ? "campfire" : "level_up", r.Text, r.Met, go, sep));
                    sep = true;
                }
                CostRows(rows, lv.Cost, ref sep);
                var look = def.Looks.FirstOrDefault(l => l.Level > b.Level);
                if (look != null && !string.IsNullOrEmpty(look.Name))
                    rows.Add(E.T($"Becomes a {look.Name} at level {look.Level}.", 10.5f, Theme.Gold).Wrap().Margin(2, 0, 3));
                if (def.Heart)
                {
                    var era = G.Content.Milestones.Where(m => !G.State.ReachedMilestones.Contains(m.Id))
                        .Select(m => (m, lvl: m.Requirements.FirstOrDefault(r => r.Kind == ConditionKind.HeartLevelAtLeast)?.Amount ?? 0))
                        .Where(x => x.lvl > b.Level).OrderBy(x => x.lvl).FirstOrDefault();
                    if (era.m != null) rows.Add(E.T($"Level {era.lvl:0} opens: {era.m.Name}.", 10.5f, Theme.Gold).Wrap().Margin(2, 0, 3));
                }
                body.Add(sec);
                ready = reqs.All(r => r.Met);
                can = ready && G.State.Resources.Covers(lv.Cost);
            }

            // Rebuilding as a better building (e.g. a tent into a hut).
            var target = def.UpgradesTo == null ? null : G.Content.Building(def.UpgradesTo);
            bool canRebuild = false;
            if (target != null && complete)
            {
                bool locked = !G.IsUnlocked(target);
                var sec = E.Section($"Rebuild as {target.Name}", out var rows);
                sec.style.marginTop = 6;
                bool sep = false;
                if (locked)
                {
                    string tech = G.Content.Technology(target.RequiresTech ?? "")?.Name ?? "research";
                    rows.Add(RequirementRow("knowledge", $"Research {tech}", false, () => store.ActivePanel = Panel.Research, false));
                    sep = true;
                }
                CostRows(rows, target.Cost, ref sep);
                body.Add(sec);
                canRebuild = !locked && G.State.Resources.Covers(target.Cost);
            }

            // Buttons: demolish, the secondary action, the primary one.
            if (def.Buildable)
            {
                bool confirm = Time.unscaledTime < confirmDemolishUntil;
                Action demolish = Act(() =>
                {
                    if (Time.unscaledTime < confirmDemolishUntil) { confirmDemolishUntil = 0; store.Demolish(id); }
                    else { confirmDemolishUntil = Time.unscaledTime + 3f; foot.schedule.Execute(() => dirty = true).StartingIn(3100); }
                });
                if (confirm) foot.Add(Share(E.Button("Demolish?", demolish, E.Kind.Red, 36, "trash", 12, SubLine("wood", "50% back")), foot));
                else Lead(E.Square("trash", demolish, E.Kind.Red, 36), foot);
            }
            if (def.Heart && G.HasWarmth && (complete || b.IsOperational)) foot.Add(Share(StokeButton(), foot));
            if (target != null && complete)
            {
                var kind = canRebuild ? (canLevel ? E.Kind.Blue : E.Kind.Green) : E.Kind.Grey;
                foot.Add(Share(E.Button("Rebuild", canRebuild ? Act(() => store.Upgrade(id)) : null, kind, 36, null, 13,
                                        SubLine("clock", Fmt.Clock(G.EstimatedSeconds(target.BuildSeconds)))), foot));
            }
            if (canLevel)
                foot.Add(Share(E.Button("Upgrade", can ? Act(() => store.UpgradeLevel(id)) : null, can ? E.Kind.Green : E.Kind.Grey, 36, null, 13,
                                        ready ? SubLine("clock", Fmt.Clock(G.EstimatedSeconds(lv.BuildSeconds))) : SubLine("lock", "Not ready", Theme.Pale)), foot));
            if (foot.childCount == 0) foot.Hidden(true);
        }

        /// <summary>The fire on a warm glow with its reach in cells (now ➜ next level).</summary>
        VisualElement ReachIllustration(int level, bool showNext)
        {
            var w = G.Content.Balance.Warmth;
            double r = w.HeartRadius + w.HeartRadiusPerLevel * (level - 1);
            var box = E.Div("reach").Ignore();
            box.style.height = 62;
            box.style.flexShrink = 0;
            var glow = E.Image(Theme.Image("glow"), 176, 56).Abs(null, 4);
            glow.style.left = Length.Percent(50);
            glow.style.marginLeft = -88;
            box.Add(glow);
            var fire = E.Icon("campfire", 46).Abs(null, 2);
            fire.style.left = Length.Percent(50);
            fire.style.marginLeft = -23;
            box.Add(fire);
            var label = E.Div().Row().Ignore().Abs(null, 30);
            label.style.left = Length.Percent(50);
            label.style.marginLeft = 30;
            label.Add(E.T(Num(r), 11.5f, Theme.Gold));
            if (showNext)
            {
                label.Add(E.Icon("arrow", 12).Margin(0, 3, 0, 3));
                label.Add(E.T($"{Num(r + w.HeartRadiusPerLevel)} cells", 11.5f, Theme.Up));
            }
            else label.Add(E.T("cells", 11.5f, Theme.Gold));
            box.Add(label);
            return box;
        }

        /// <summary>Upgrade Stats: every number the building has, with the next level's value after a green arrow.</summary>
        void StatsSection(VisualElement body, PlacedBuilding b, BuildingDefinition def, bool showNext)
        {
            int level = b.Level;
            double f = Game.LevelFactor(level), f2 = Game.LevelFactor(level + 1);
            string Next(string s) => showNext ? s : null;
            var sec = E.Section(showNext ? "Upgrade Stats" : "Stats", out var rows,
                def.MaxLevel > 1 ? E.T($"Lv.{level}/{def.MaxLevel}", 11, Theme.Lv) : null);
            sec.style.marginTop = 6;
            bool sep = false;
            void Add(VisualElement r) { rows.Add(r); sep = true; }

            if (def.HousingValue > 0)
                Add(E.StatRow("home", "Houses", G.HousingOf(b, def).ToString(Inv), Next(((int)Math.Floor(def.HousingValue * f2 + 0.5)).ToString(Inv)), sep));
            if (def.StorageValue > 0)
                Add(E.StatRow("storage", "Storage", Fmt.Compact(def.StorageValue * f), Next(Fmt.Compact(def.StorageValue * f2)), sep));
            if (def.ProtectedFoodValue > 0)
                Add(E.StatRow("food", "Keeps food", Fmt.Compact(def.ProtectedFoodValue * f), Next(Fmt.Compact(def.ProtectedFoodValue * f2)), sep));
            foreach (var slot in def.JobSlots ?? new List<JobSlot>())
            {
                int now = G.SlotsOf(b, def, slot.Job), then = (int)Math.Floor(def.Slots(slot.Job) * f2 + 0.5);
                if (then == now && level < def.MaxLevel) continue; // only what the next level changes
                Add(E.StatRow("people", $"{G.Content.JobConfig(slot.Job)?.Name ?? slot.Job.Id()} places", now.ToString(Inv), Next(then.ToString(Inv)), sep));
            }
            if (G.HasWarmth)
            {
                var w = G.Content.Balance.Warmth;
                if (def.HousingValue > 0)
                {
                    double ins = G.InsulationOf(b, def);
                    Add(E.StatRow("home", "Walls", SignedDeg(ins), Next(level < def.MaxLevel && w.InsulationPerLevel > 0 ? SignedDeg(ins + w.InsulationPerLevel) : null), sep));
                    int id = b.Id;
                    var inside = E.StatRow("thermometer", "Warmth inside", "", null, true);
                    var value = (Label)inside.ElementAt(inside.childCount - 1);
                    Bind(() =>
                    {
                        HomeWarmth? home = null;
                        foreach (var h in Warm.Homes) if (h.Building.Id == id) { home = h; break; }
                        string s = home is HomeWarmth hw ? $"{Deg(hw.Warmth)} {hw.Exposure}" : "Empty";
                        if (value.text != s) value.text = s;
                        E.Tint(value, home is HomeWarmth hw2 ? ExposureColor(hw2.Exposure) : Theme.Pale);
                    });
                    Add(inside);
                }
                if (def.Heart)
                {
                    double heat = w.HeartHeat + w.HeartHeatPerLevel * (level - 1), radius = w.HeartRadius + w.HeartRadiusPerLevel * (level - 1);
                    Add(E.StatRow("flame", "Warmth", SignedDeg(heat), Next(SignedDeg(heat + w.HeartHeatPerLevel)), sep));
                    Add(E.StatRow("compass", "Reach", $"{Num(radius)} cells", Next(Num(radius + w.HeartRadiusPerLevel)), sep));
                }
                else if (def.Heat != null)
                {
                    Add(E.StatRow("flame", "Warmth", SignedDeg(def.Heat.Heat), null, sep));
                    Add(E.StatRow("compass", "Reach", $"{Num(def.Heat.Radius)} cells", null, sep));
                }
            }
            if (!def.Heart && def.JobSlots != null && def.JobSlots.Count > 0)
                Add(E.StatRow("road", "Road", G.HasRoadAccess(b) ? "+10% work" : "None", null, sep, G.HasRoadAccess(b) ? Theme.Up : Theme.Pale));
            if (rows.childCount > 0) body.Add(sec);
        }

        /// <summary>
        /// Workers of a job, like the kit's Recruit tab: portraits of the people on it, vacant places, places the
        /// next level adds (locked), and the −/+ stepper with the count.
        /// </summary>
        VisualElement WorkersSection(JobType job, PlacedBuilding b, BuildingDefinition def, bool showLocked)
        {
            var config = G.Content.JobConfig(job);
            int workers = G.State.Assignments[job];
            int capacity = G.JobCapacity(job);
            bool unlimited = config?.UnlimitedSlots ?? false;
            var sec = E.Section($"{config?.Name ?? job.Id()}", out var rows, E.T(unlimited ? $"{workers}" : $"{workers}/{capacity}", 11.5f, Theme.Lv));
            sec.name = "workers-" + job;
            sec.style.marginTop = 6;

            // Up to five places in a row (the card is narrow): people first, then vacant, then one locked.
            const int MaxShown = 5;
            float cellW = (CardW - 16 - 16) / MaxShown;
            var grid = E.Div().Row(0, Align.FlexStart).Ignore();
            grid.style.paddingTop = 5;
            string id = job.Id();
            var onJob = G.State.People.Where(p => p.Job == id).Take(MaxShown).ToList();
            foreach (var p in onJob) grid.Add(Portrait(FaceFor(p), p.Name, cellW, false, p.Sick));
            int vacant = unlimited ? 0 : Math.Max(0, capacity - workers);
            for (int i = 0; i < vacant && grid.childCount < MaxShown; i++) grid.Add(Portrait("user", "Vacant", cellW, true, false));
            int more = showLocked ? (int)Math.Floor(def.Slots(job) * Game.LevelFactor(b.Level + 1) + 0.5) - G.SlotsOf(b, def, job) : 0;
            if (more > 0 && grid.childCount < MaxShown) grid.Add(Portrait("lock", $"Lv.{b.Level + 1}", cellW, true, false));
            int hidden = workers - onJob.Count;
            if (hidden > 0 && grid.childCount == MaxShown) grid.Add(E.T($"+{hidden}", 11, Theme.Pale).Abs(null, null, 2, 22));
            if (grid.childCount > 0) { grid.style.position = Position.Relative; rows.Add(grid); }

            var effect = Live(() => EffectText(job, G.State.Assignments[job]), 10.5f, Theme.Pale).Wrap();
            effect.style.marginTop = 3;
            rows.Add(effect);

            var stepper = E.Div().Row().Center().Ignore().Margin(4, 0, 3);
            stepper.Add(E.Square("minus", workers > 0 ? Act(() => store.SetWorkers(job, G.State.Assignments[job] - 1)) : null, workers > 0 ? E.Kind.Red : E.Kind.Grey, 28));
            var count = E.Capsule(24).Pad(0, 10);
            count.style.marginLeft = -2; count.style.marginRight = -2;
            count.Add(E.Icon("user", 16));
            count.Add(E.T(unlimited ? $"{workers}" : $"{workers}/{capacity}", 12.5f).Margin(0, 0, 0, 4));
            stepper.Add(count);
            bool canAdd = G.IdleWorkers > 0 && workers < capacity;
            stepper.Add(E.Square("plus", canAdd ? Act(() => store.SetWorkers(job, G.State.Assignments[job] + 1)) : null, canAdd ? E.Kind.Green : E.Kind.Grey, 28));
            rows.Add(stepper);
            return sec;
        }

        /// <summary>A portrait slot with the name under it (vacant and locked places are dimmer).</summary>
        static VisualElement Portrait(string face, string name, float width, bool dim, bool sick)
        {
            var c = E.Div().Col(Align.Center).Ignore().Size(width);
            var slot = E.Slot(face, 38, E.Cell.Slot, face.StartsWith("face", StringComparison.Ordinal) ? 0.86f : 0.6f, dim && face == "lock");
            if (sick) slot.Add(E.Icon("sick", 15).Abs(null, null, -4, -2));
            c.Add(slot);
            c.Add(E.T(name, 9.5f, dim ? Theme.Pale : (Color?)null).Fit(width - 2, 7).Margin(1));
            return c;
        }

        string EffectText(JobType job, int workers)
        {
            var config = G.Content.JobConfig(job);
            if (config == null) return "";
            if (job == JobType.Healing)
            {
                int sick = G.SickCount, care = G.CareCapacity;
                if (workers == 0) return sick > 0 ? $"{sick} ill and nobody tends them" : "Tends the sick: they get well quickly";
                return sick == 0 ? $"Nobody is ill · room for {care}" : $"Tending {Math.Min(sick, care)} of {sick} ill · room for {care}";
            }
            if (config.Output.HasValue)
            {
                double each = G.RatePerWorker(job) * 60;
                return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0}/m each · {2:0.0}/m total",
                                     config.Output.Value.DisplayName(), each, each * workers);
            }
            switch (job)
            {
                case JobType.Construction:
                    return string.Format(CultureInfo.InvariantCulture, "Build speed ×{0:0.0}", G.ConstructionSpeed());
                case JobType.Crafting:
                    if (workers == 0) return "Tools boost all gathering";
                    return econ.CraftingActive
                        ? string.Format(CultureInfo.InvariantCulture, "Gathering +{0:0}% · uses wood & stone", econ.CraftingBonus * 100)
                        : "Needs wood and stone to work";
                default:
                    return config.Description;
            }
        }
    }
}
