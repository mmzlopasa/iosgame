using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// Monuments built part by part (Manor Lords' free-form manors, abbeys and castles). The player lays out the plot,
    /// then chooses which part to raise next from the monument's plan: a part needs the parts it joins onto, costs its
    /// own goods and time, and brings its own lasting effects, gathering-place points and estate favour.
    /// </summary>
    public sealed partial class Game
    {
        public enum PartState { Built, Building, Available, Locked }

        public PartState PartStatus(PlacedBuilding b, MonumentPart part)
        {
            if (b.Parts.Contains(part.Id)) return PartState.Built;
            if (b.PendingPart == part.Id) return PartState.Building;
            return part.Requires.All(r => b.Parts.Contains(r)) ? PartState.Available : PartState.Locked;
        }

        /// <summary>Starts building a part of a finished monument.</summary>
        public GameResult AddPart(int buildingId, string partId)
        {
            Update();
            var b = State.Building(buildingId);
            var def = b == null ? null : Content.Building(b.DefinitionId);
            if (b == null || def == null) return GameError.UnknownContent($"building #{buildingId}");
            var part = def.Part(partId);
            if (part == null) return GameError.UnknownContent(partId);
            if (b.Status != BuildingStatus.Complete) return GameError.BuildingBusy;
            switch (PartStatus(b, part))
            {
                case PartState.Built: return GameError.NotReady($"The {part.Name} already stands.");
                case PartState.Locked:
                    return GameError.NotReady($"Build first: {string.Join(", ", part.Requires.Where(r => !b.Parts.Contains(r)).Select(r => def.Part(r)?.Name ?? r))}.");
            }
            int maxQueue = Content.Balance.MaxConcurrentConstructions;
            if (ActiveConstructionCount() >= maxQueue) return GameError.ConstructionQueueFull(maxQueue);
            if (!State.Resources.Covers(part.Cost)) return GameError.Unaffordable(State.Resources.Shortfall(part.Cost));
            State.Resources = State.Resources - part.Cost;
            b.Status = BuildingStatus.Upgrading;
            b.PendingPart = part.Id;
            b.RemainingWork = part.BuildSeconds;
            b.TotalWork = part.BuildSeconds;
            Notice(NoticeKind.Info, $"Work began on the {part.Name}.");
            return GameResult.Success;
        }

        /// <summary>The finished parts of a building, in plan order.</summary>
        public IEnumerable<MonumentPart> BuiltParts(PlacedBuilding b, BuildingDefinition def)
        {
            if (b.Parts.Count == 0) yield break;
            foreach (var p in def.Parts) if (b.Parts.Contains(p.Id)) yield return p;
        }

        /// <summary>Lasting effects of the parts of every monument (the monument must not be a site).</summary>
        IEnumerable<Effect> PartEffects()
        {
            foreach (var (b, def) in OperationalBuildings())
                foreach (var p in BuiltParts(b, def))
                    foreach (var e in p.Effects) yield return e;
        }

        int PartCommunity() => OperationalBuildings().Sum(x => BuiltParts(x.building, x.def).Sum(p => p.Community));

        double PartFavour(string estateId) =>
            OperationalBuildings().Sum(x => BuiltParts(x.building, x.def).Where(p => p.Estate == estateId).Sum(p => p.Favour));

        /// <summary>Model name of a monument: its plot and the parts that stand, e.g. "lords_manor+great_hall+kitchen".</summary>
        string MonumentSprite(PlacedBuilding b, BuildingDefinition def) =>
            b.Parts.Count == 0 ? def.Sprite : def.Sprite + "+" + string.Join("+", BuiltParts(b, def).Select(p => p.Id));

        /// <summary>Finishes a part (called when its construction ends).</summary>
        void FinishPart(PlacedBuilding b)
        {
            var def = Content.Building(b.DefinitionId);
            var part = def?.Part(b.PendingPart);
            if (!b.Parts.Contains(b.PendingPart)) b.Parts.Add(b.PendingPart);
            b.PendingPart = null;
            b.Status = BuildingStatus.Complete;
            b.RemainingWork = 0;
            Notice(NoticeKind.Success, $"The {part?.Name ?? "new part"} of the {def?.Name ?? "monument"} is finished.");
            GrantSpeedups(Speedups.BuildingImproved, "Monument grows!");
            if (def != null && b.Parts.Count == def.Parts.Count)
                AddLegacy("building", $"{def.Name} completed", $"Every part of the {def.Name} stands.", def.Id);
        }
    }
}
