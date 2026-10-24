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
            if (store.Founding) return $"found|{store.FoundingFire}|{store.FoundingCell}|{store.FoundingIssue()}";
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
            if (store.Founding) FoundingCard(host);
            else if (store.DecoratingId != null) DecorationCard(host);
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
            var card = E.Panel().Pad(0, 10, 11, 10);
            card.style.flexShrink = 1;
            card.style.minHeight = 0;
            var plank = E.Plank(title, level, Theme.H1, CardW + 8);
            plank.style.marginTop = -6;
            plank.style.marginLeft = -14;
            plank.style.marginRight = -14;
            card.Add(plank);
            card.Add(E.Close(Act(onClose)).Abs(null, -12, -10));
            var scroll = E.FitScroll();
            scroll.contentContainer.style.paddingTop = 8;
            scroll.contentContainer.style.paddingBottom = 2;
            scroll.verticalScroller.valueChanged += _ => lastContextScroll = scroll.scrollOffset;
            contextBody = scroll;
            card.Add(scroll);
            body = scroll.contentContainer;
            foot = E.Div().Row().Ignore();
            foot.style.flexShrink = 0;
            foot.style.paddingTop = 8;
            card.Add(foot);
            host.Add(card);
            return card;
        }

        /// <summary>A footer button that takes an equal share of the row.</summary>
        static VisualElement Share(VisualElement b, VisualElement row)
        {
            b.Shared();
            if (row.childCount > 0) b.style.marginLeft = 8;
            return b;
        }

        static void Lead(VisualElement b, VisualElement row)
        {
            if (row.childCount > 0) b.style.marginLeft = 8;
            row.Add(b);
        }

        /// <summary>A button's second line: a small icon and an outlined text (a cost or a timer).</summary>
        static VisualElement SubLine(string icon, string text, Color? color = null)
        {
            var r = E.Div().Row().Ignore();
            r.Add(E.Icon(icon, 16));
            var l = E.T(text, 13, color);
            l.style.marginLeft = 3;
            r.Add(l);
            return r;
        }

        /// <summary>A whole cost as icon + number pairs (red where the stores fall short); wraps onto a second line.</summary>
        VisualElement CostLine(ResourceBundle cost, float size = Theme.Body, bool center = false)
        {
            var row = E.Div().Row().Ignore();
            row.style.flexWrap = Wrap.Wrap;
            if (center) row.style.justifyContent = Justify.Center;
            if (!cost.Entries.Any()) { row.Add(E.T("Free", size, Theme.Up)); return row; }
            foreach (var kv in cost.Entries)
            {
                bool shortOf = G.State.Resources[kv.Key] + 1e-6 < kv.Value;
                var item = E.Div().Row().Ignore();
                item.style.marginLeft = 3;
                item.style.marginRight = 3;
                item.Add(E.Icon(IconFor(kv.Key), size + 5));
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
                var r = E.Line(sep);
                sep = true;
                r.Add(E.RowIcon(IconFor(type)));
                var have = E.T("", Theme.Num);
                var rest = E.T(" / " + Fmt.Compact(need), Theme.Num);
                r.Add(have);
                r.Add(rest);
                r.Add(E.Div().Grow().Ignore());
                bool met = G.State.Resources[type] + 1e-6 >= need;
                r.Add(RowEnd(met ? E.Icon("check", 28) : GoButton(() => { peopleTab = 0; store.ActivePanel = Panel.People; })));
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

        const float GoW = 68;

        VisualElement GoButton(Action onTap) => E.Button("Go", Act(onTap), E.Kind.Blue, Theme.GoH, null, Theme.Num, null, GoW);

        /// <summary>The right end of a row (✓, Go): a fixed-width box so every check and Go line up.</summary>
        static VisualElement RowEnd(VisualElement content)
        {
            var box = E.Div().Row().Center().Ignore().Size(GoW);
            box.style.marginLeft = 8;
            box.Add(content);
            return box;
        }

        /// <summary>A requirement row: the text (wraps if long), then ✓ or Go. The width is no longer needed.</summary>
        VisualElement RequirementRow(string icon, string text, bool met, Action go, bool sep, float width = 0)
        {
            var r = E.Line(sep);
            r.Add(E.RowIcon(icon));
            r.Add(E.T(text, Theme.Body, met ? (Color?)null : Theme.Pale).Flex());
            r.Add(RowEnd(met ? E.Icon("check", 28) : go != null ? GoButton(go) : E.IconDim("check", 28)));
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

            var head = E.Div().Row().Ignore();
            head.Add(E.Slot(BuildingIcon(def), 64, CellFor(def)));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            var time = E.Div().Row().Ignore();
            time.Add(E.Icon("clock", 20));
            time.Add(E.T(Fmt.Clock(G.EstimatedSeconds(def.BuildSeconds)), Theme.Num).Margin(0, 0, 0, 4));
            if (def.Footprint.Width > 1 || def.Footprint.Height > 1)
                time.Add(E.T($"Size {def.Footprint.Width}×{def.Footprint.Height}", Theme.Body, Theme.Pale).Margin(0, 0, 0, 12));
            col.Add(time);
            col.Add(CostLine(def.Cost).Margin(6, 0, 0, -3));
            head.Add(col);
            body.Add(head);

            var status = E.Inset().Row().Margin(8);
            status.Add(E.Icon(ok ? "check" : "alert", 24).NoShrink());
            var msg = E.T(ok ? "Good spot. Drag it or tap the ground to move it." : issues[0].Message, Theme.Body, ok ? (Color?)null : Theme.Bad).Flex();
            msg.style.marginLeft = 8;
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

            Lead(E.Square("rotate", Act(store.RotatePlacement), E.Kind.Blue, Theme.ButtonH), foot);
            foot.Add(Share(E.Button("Cancel", Act(store.CancelPlacement), E.Kind.Red), foot));
            bool afford = G.State.Resources.Covers(def.Cost);
            foot.Add(Share(E.Button("Build here", ok ? Act(store.ConfirmPlacement) : null, ok ? E.Kind.Green : E.Kind.Grey, Theme.ButtonH, null, 16,
                                    afford ? SubLine("clock", Fmt.Clock(G.EstimatedSeconds(def.BuildSeconds))) : CostSub(def.Cost)), foot));
        }

        // MARK: - Founding

        /// <summary>A new game: first where the campfire burns, then where the first shelters stand.</summary>
        void FoundingCard(VisualElement host)
        {
            bool fireChosen = store.FoundingFire != null;
            string issue = store.FoundingIssue();
            bool ok = issue == null;
            CardShell(host, fireChosen ? "Raise the shelters" : "Light the campfire", null, store.BackFounding, out var body, out var foot);
            var head = E.Div().Row(0, Align.FlexStart).Ignore();
            head.Add(E.Slot(fireChosen ? "home" : "campfire", 64, E.Cell.Epic));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            col.Add(E.Ink(fireChosen
                ? "Tap where your people raise their first three shelters, close to the fire's warmth."
                : "Your band has found a valley. Tap the land where the campfire should burn: the land around it will be yours to build on.", Theme.Body));
            head.Add(col);
            body.Add(head);
            var status = E.Inset().Row().Margin(8);
            status.Add(E.Icon(ok ? "check" : "alert", 24).NoShrink());
            var msg = E.T(ok ? (fireChosen ? "Good, dry ground near the fire." : "A good place to settle.") : issue, Theme.Body, ok ? (Color?)null : Theme.Bad).Flex();
            msg.style.marginLeft = 8;
            status.Add(msg);
            body.Add(status);
            body.Add(E.Ink("Forests, rocks and ore around the camp are resources: you may build over them, but what you build over is gone.", Theme.Small, Theme.Pale).Margin(6));
            if (fireChosen) foot.Add(Share(E.Button("Back", Act(store.BackFounding), E.Kind.Red), foot));
            foot.Add(Share(E.Button(fireChosen ? "Settle here" : "Light the fire here", ok ? Act(store.ConfirmFounding) : null,
                                    ok ? E.Kind.Green : E.Kind.Grey, Theme.ButtonH), foot));
        }

        // MARK: - Decorations (roads, fences, lights…)

        void DecorationCard(VisualElement host)
        {
            bool removing = store.DecoratingId == GameStore.RemoveTool;
            var def = removing ? null : G.Content.Decoration(store.DecoratingId);
            CardShell(host, removing ? "Clear decorations" : def?.Name ?? "Town", null, store.EndDecorating, out var body, out var foot);
            var head = E.Div().Row(0, Align.FlexStart).Ignore();
            head.Add(E.Slot(removing ? "trash" : DecorIconFor(def), 64, removing ? E.Cell.Common : E.Cell.Epic));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            string hint = removing ? "Tap a road, fence or ornament to remove it. Half of its cost comes back."
                : store.PaintMode ? "Drag across the ground to draw. Tap the hand to move the camera instead."
                : "Tap the ground to place. Each tap places one.";
            col.Add(E.Ink(hint, Theme.Body));
            if (def != null) col.Add(CostLine(def.Cost).Margin(6, 0, 0, -3));
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
                sec.style.marginTop = 8;
                body.Add(sec);
            }
            if (!removing)
            {
                Lead(E.Square(store.PaintMode ? "road" : "compass", Act(store.TogglePaintMode), store.PaintMode ? E.Kind.Orange : E.Kind.Blue, Theme.ButtonH), foot);
                Lead(E.Square("rotate", Act(store.RotatePlacement), E.Kind.Blue, Theme.ButtonH), foot);
            }
            Lead(E.Square("trash", Act(() => store.BeginDecorating(removing ? "dirt_path" : GameStore.RemoveTool)), E.Kind.Brown, Theme.ButtonH), foot);
            foot.Add(Share(E.Button("Done", Act(store.EndDecorating), E.Kind.Green, Theme.ButtonH, "check"), foot));
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
            head.Add(E.Slot(icon, 64, CellFor(n.Resource)));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            col.Add(E.T(n.Ordered ? "Gathered first" : jobName, Theme.H2, n.Ordered ? Theme.Gold : (Color?)null).Fit(CardW - 20 - 74));
            col.Add(Live(() => $"{G.State.Assignments[n.Job]} at work", Theme.Body, Theme.Pale).Margin(3));
            head.Add(col);
            body.Add(head);

            bool endless = double.IsInfinity(n.Remaining);
            var sec = E.Section($"{n.Resource.DisplayName()} left", out var rows);
            sec.style.marginTop = 8;
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
                var bar = LiveBar(() => G.NodeAt(x, y)?.Fraction ?? 0, 14);
                bar.style.marginBottom = 8;
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
                if (n.Ordered) Lead(E.Square("trash", Act(() => store.ToggleHarvest(x, y)), E.Kind.Brown, Theme.ButtonH), foot);
                foot.Add(Share(E.Button(verb, Act(() => store.SendGatherer(x, y)), E.Kind.Green, Theme.ButtonH, tool, 16, null, CardW - 20 - (n.Ordered ? Theme.ButtonH + 8 : 0)), foot));
            }
            else foot.Hidden(true);
        }

        /// <summary>A hint in an inset box with an icon.</summary>
        static VisualElement Notice(string icon, string text, bool bad = false)
        {
            var n = E.Inset().Row(0, Align.FlexStart).Margin(8);
            n.Add(E.Icon(icon, 24).NoShrink());
            var t = E.T(text, Theme.Body, bad ? Theme.Bad : (Color?)null).Flex();
            t.style.marginLeft = 8;
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
            // Head (the kit's "Title / Short Description" with the item's icon).
            var head = E.Div().Row(0, Align.FlexStart).Ignore();
            head.Add(E.Slot(BuildingIcon(def, b.Level), 60, CellFor(def)));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            string state = complete ? Category(def) : b.Status == BuildingStatus.Upgrading ? "Upgrading" : "Under construction";
            col.Add(E.T(state, Theme.Num, complete ? (Color?)null : Theme.Gold));
            col.Add(E.Ink(def.Description, Theme.Small).Margin(2));
            var next = def.SuccessorId == null ? null : G.Content.Building(def.SuccessorId);
            if (next != null) col.Add(E.Ink($"Becomes the {next.Name} when the settlement grows.", Theme.Small, Theme.InkSoft).Margin(3));
            head.Add(col);
            body.Add(head);
            var lv = G.NextLevel(b);
            bool canLevel = lv != null && complete;

            if (!complete)
            {
                var sec = E.Section("Construction", out var rows);
                sec.style.marginTop = 8;
                var bar = LiveLabeledBar(() => G.State.Building(id)?.Progress ?? 1, () => $"{(int)((G.State.Building(id)?.Progress ?? 1) * 100)}%", 20);
                bar.style.marginTop = 8;
                rows.Add(bar);
                var tr = E.Line(false);
                tr.Add(E.RowIcon("clock"));
                tr.Add(E.T("Time left", Theme.Body).Flex());
                tr.Add(Live(() => Fmt.Clock(G.EstimatedSeconds(G.State.Building(id)?.RemainingWork ?? 0)), Theme.Num));
                rows.Add(tr);
                rows.Add(SpeedupRow(SpeedupTask.Building(id)));
                body.Add(sec);
            }

            StatsSection(body, b, def, canLevel);
            foreach (var slot in def.JobSlots ?? new List<JobSlot>())
                if (body.Children().All(c => c.name != "workers-" + slot.Job)) body.Add(WorkersSection(slot.Job, b, def, canLevel));

            // Upgrade Resources: what the next level needs.
            bool ready = false, can = false;
            if (canLevel)
            {
                // The kit's "Upgrade Resources": requirements and costs, ✓ when met, Go (or a red amount) when not.
                var sec = E.Section("Upgrade Resources", out var rows, E.T($"Lv.{b.Level + 1}", Theme.Num, Theme.Lv));
                sec.style.marginTop = 8;
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
                    rows.Add(E.T($"Becomes a {look.Name} at level {look.Level}.", Theme.Small, Theme.Gold).Wrap().Margin(4, 0, 6));
                if (def.Heart)
                {
                    var era = G.Content.Milestones.Where(m => !G.State.ReachedMilestones.Contains(m.Id))
                        .Select(m => (m, lvl: m.Requirements.FirstOrDefault(r => r.Kind == ConditionKind.HeartLevelAtLeast)?.Amount ?? 0))
                        .Where(x => x.lvl > b.Level).OrderBy(x => x.lvl).FirstOrDefault();
                    if (era.m != null) rows.Add(E.T($"Level {era.lvl:0} opens: {era.m.Name}.", Theme.Small, Theme.Gold).Wrap().Margin(4, 0, 6));
                }
                body.Add(sec);
                ready = reqs.All(r => r.Met);
                can = ready && G.State.Resources.Covers(lv.Cost);
            }

            // The heart's stoking (its button sits in the footer, as the kit's orange "Finish Now").
            if (def.Heart && G.HasWarmth && (complete || b.IsOperational)) body.Add(FireSection());

            // Rebuilding as a better building (e.g. a tent into a hut).
            var target = def.UpgradesTo == null ? null : G.Content.Building(def.UpgradesTo);
            bool canRebuild = false;
            if (target != null && complete)
            {
                bool locked = !G.IsUnlocked(target);
                var sec = E.Section($"Rebuild as {target.Name}", out var rows);
                sec.style.marginTop = 8;
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
                if (confirm) foot.Add(Share(E.Button("Demolish?", demolish, E.Kind.Red, Theme.ButtonH, "trash", 16, SubLine("wood", "50% back")), foot));
                else Lead(E.Square("trash", demolish, E.Kind.Brown, Theme.ButtonH), foot); // remove: the kit's navy secondary
            }
            if (def.Heart && G.HasWarmth && (complete || b.IsOperational)) foot.Add(Share(StokeButton(), foot));
            if ((def.TradeRoutes ?? 0) > 0 && b.IsOperational)
                foot.Add(Share(E.Button("Trade", Act(() => OpenPanel(Panel.Trade)), canLevel ? E.Kind.Blue : E.Kind.Orange, Theme.ButtonH, "road", 16,
                                        SubLine("road", $"{G.State.TradeRoutes.Count}/{G.TradeRouteSlots()} routes")), foot));
            if (target != null && complete)
            {
                var kind = canRebuild ? (canLevel ? E.Kind.Blue : E.Kind.Orange) : E.Kind.Grey; // yellow when it is the card's primary
                foot.Add(Share(E.Button("Rebuild", canRebuild ? Act(() => store.Upgrade(id)) : null, kind, Theme.ButtonH, null, 16,
                                        SubLine("clock", Fmt.Clock(G.EstimatedSeconds(target.BuildSeconds)))), foot));
            }
            if (canLevel)
                foot.Add(Share(E.Button("Level up", can ? Act(() => store.UpgradeLevel(id)) : null, can ? E.Kind.Orange : E.Kind.Grey, Theme.ButtonH, null, 16,
                                        ready ? SubLine("clock", Fmt.Clock(G.EstimatedSeconds(lv.BuildSeconds))) : SubLine("lock", "Not ready", Theme.Pale)), foot));
            if (foot.childCount == 0) foot.Hidden(true);
        }

        /// <summary>Upgrade Stats: every number the building has, with the next level's value after a green arrow.</summary>
        void StatsSection(VisualElement body, PlacedBuilding b, BuildingDefinition def, bool showNext)
        {
            int level = b.Level;
            double f = Game.LevelFactor(level), f2 = Game.LevelFactor(level + 1);
            string Next(string s) => showNext ? s : null;
            var sec = E.Section(showNext ? "Upgrade Stats" : "Stats", out var rows,
                def.MaxLevel > 1 ? E.T($"Lv.{level}/{def.MaxLevel}", Theme.Num, Theme.Lv) : null);
            sec.style.marginTop = 8;
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
                    // Walls gain a fraction of a degree per level: one decimal, so "now ➜ next" never reads "+9° ➜ +9°".
                    string Deg1(double d) => "+" + d.ToString("0.0", Inv) + "°";
                    Add(E.StatRow("home", "Walls", Deg1(ins), Next(level < def.MaxLevel && w.InsulationPerLevel > 0 ? Deg1(ins + w.InsulationPerLevel) : null), sep));
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
            if ((def.MarketReach ?? 0) > 0)
            {
                Add(E.StatRow("compass", "Reach", $"{Game.MarketReachOf(b, def)} cells", Next(level < def.MaxLevel ? $"{Game.MarketReachOf(new PlacedBuilding { Level = level + 1 }, def)} cells" : null), sep));
                var (served, homed) = G.MarketCoverage();
                Add(E.StatRow("home", "Families served", $"{served}/{homed}", null, sep, served >= homed && homed > 0 ? Theme.Up : (Color?)null));
            }
            if ((def.TradeRoutes ?? 0) > 0)
            {
                int routes = def.TradeRoutes.Value + level / Game.TradeLevelsPerRoute, then = def.TradeRoutes.Value + (level + 1) / Game.TradeLevelsPerRoute;
                Add(E.StatRow("road", "Trade routes", routes.ToString(Inv), Next(level < def.MaxLevel ? then.ToString(Inv) : null), sep));
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
            var sec = E.Section($"{config?.Name ?? job.Id()}", out var rows, E.T(unlimited ? $"{workers}" : $"{workers}/{capacity}", Theme.Num, Theme.Lv));
            sec.name = "workers-" + job;
            sec.style.marginTop = 8;

            // Up to five places in a row (the card is narrow): people first, then vacant, then one locked.
            const int MaxShown = 5;
            float cellW = Mathf.Floor((CardW - 20 - 20) / MaxShown);
            var grid = E.Div().Row(0, Align.FlexStart).Ignore();
            grid.style.paddingTop = 8;
            string id = job.Id();
            var onJob = G.State.People.Where(p => p.Job == id).Take(MaxShown).ToList();
            foreach (var p in onJob) grid.Add(PortraitCell(FaceFor(p), p.Name, cellW, false, p.Sick));
            int vacant = unlimited ? 0 : Math.Max(0, capacity - workers);
            for (int i = 0; i < vacant && grid.childCount < MaxShown; i++) grid.Add(PortraitCell("user", "Vacant", cellW, true, false));
            int more = showLocked ? (int)Math.Floor(def.Slots(job) * Game.LevelFactor(b.Level + 1) + 0.5) - G.SlotsOf(b, def, job) : 0;
            if (more > 0 && grid.childCount < MaxShown) grid.Add(PortraitCell("lock", $"Lv.{b.Level + 1}", cellW, true, false));
            int hidden = workers - onJob.Count;
            if (hidden > 0 && grid.childCount == MaxShown) grid.Add(E.T($"+{hidden}", Theme.Small, Theme.Pale).Abs(null, null, 2, 30));
            if (grid.childCount > 0) { grid.style.position = Position.Relative; rows.Add(grid); }

            var effect = Live(() => EffectText(job, G.State.Assignments[job]), Theme.Small, Theme.Pale).Wrap();
            effect.style.marginTop = 4;
            rows.Add(effect);

            var stepper = E.Div().Row().Center().Ignore().Margin(6, 0, 6);
            stepper.Add(E.Square("minus", workers > 0 ? Act(() => store.SetWorkers(job, G.State.Assignments[job] - 1)) : null, workers > 0 ? E.Kind.Red : E.Kind.Grey, 40));
            var count = E.Capsule(32).Pad(0, 14);
            count.style.marginLeft = -3; count.style.marginRight = -3;
            count.Add(E.Icon("user", 22));
            count.Add(E.T(unlimited ? $"{workers}" : $"{workers}/{capacity}", Theme.Num).Margin(0, 0, 0, 5));
            stepper.Add(count);
            bool canAdd = G.IdleWorkers > 0 && workers < capacity;
            stepper.Add(E.Square("plus", canAdd ? Act(() => store.SetWorkers(job, G.State.Assignments[job] + 1)) : null, canAdd ? E.Kind.Green : E.Kind.Grey, 40));
            rows.Add(stepper);
            return sec;
        }

        /// <summary>A portrait slot with the name under it (vacant and locked places are dimmer).</summary>
        static VisualElement PortraitCell(string face, string name, float width, bool dim, bool sick)
        {
            var c = E.Div().Col(Align.Center).Ignore().Size(width);
            var slot = E.Slot(face, 48, E.Cell.Slot, face.StartsWith("face", StringComparison.Ordinal) ? 0.86f : 0.6f, dim && face == "lock");
            if (sick) slot.Add(E.Icon("sick", 20).Abs(null, null, -5, -3));
            c.Add(slot);
            c.Add(E.T(name, Theme.Small, dim ? Theme.Pale : (Color?)null).Fit(width - 2).Align(TextAnchor.MiddleCenter).Margin(2));
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
            if (config.IsRecipe && config.Output.HasValue) return RecipeText(config, workers);
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

        /// <summary>A recipe job: what it takes and makes, and why it stands still (no ore, store full…).</summary>
        string RecipeText(JobConfig config, int workers)
        {
            var output = config.Output.Value;
            string recipe = string.Join(" + ", config.Inputs.Entries.Select(e => $"{e.Value:0.#} {e.Key.DisplayName()}")) + $" → 1 {output.DisplayName()}";
            if (workers == 0 || econ == null || !econ.Processing.TryGetValue(config.Job, out var st)) return recipe;
            switch (st.State)
            {
                case ProcessingState.OutputFull:
                    return $"Stopped: the {output.DisplayName()} store is full";
                case ProcessingState.MissingInput:
                    return st.Missing.HasValue ? $"Stopped: no {st.Missing.Value.DisplayName()} · {recipe}" : $"Stopped · {recipe}";
                default:
                    string made = string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0}/m", output.DisplayName(), st.Rate * 60);
                    return st.Rate + 1e-9 < st.Capacity && st.Missing.HasValue ? $"{made} · short of {st.Missing.Value.DisplayName()}" : $"{made} · {recipe}";
            }
        }
    }
}
