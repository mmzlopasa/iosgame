import Foundation

/// A condition with a player-facing description and whether it is met.
public struct RequirementStatus: Equatable, Sendable {
    public var text: String
    public var met: Bool
}

extension Game {
    // MARK: - Conditions

    public func isMet(_ c: Condition) -> Bool {
        let amount = c.amount ?? 1
        switch c.kind {
        case .resourceAtLeast:
            guard let r = c.resource else { return false }
            return state.resources[r] + 1e-9 >= amount
        case .buildingCount:
            return Double(count(of: c.id ?? "")) >= amount
        case .housingAtLeast:
            return Double(housingCapacity()) >= amount
        case .populationAtLeast:
            return Double(state.population) >= amount
        case .techResearched:
            return state.researched.contains(c.id ?? "")
        case .techCountAtLeast:
            return Double(state.researched.count) >= amount
        case .eventResolved:
            return state.resolvedEvents[c.id ?? ""] != nil
        case .objectiveCompleted:
            return state.completedObjectives.contains(c.id ?? "")
        case .milestoneReached:
            return state.reachedMilestones.contains(c.id ?? "")
        case .regionExplored:
            return state.exploredRegions.contains(c.id ?? "")
        case .distinctJobsAtLeast:
            return Double(JobType.allCases.filter { state.assignments[$0] > 0 }.count) >= amount
        case .foodBalancePositive:
            return economy().net.food > 0
        }
    }

    public func describe(_ c: Condition) -> String {
        let amount = Int((c.amount ?? 1).rounded())
        switch c.kind {
        case .resourceAtLeast:
            let r = c.resource ?? .food
            return "\(r.displayName) \(Int(state.resources[r].rounded(.down)))/\(amount)"
        case .buildingCount:
            let name = content.building(c.id ?? "")?.name ?? (c.id ?? "?")
            return "\(name) \(count(of: c.id ?? ""))/\(amount)"
        case .housingAtLeast:
            return "Housing \(housingCapacity())/\(amount)"
        case .populationAtLeast:
            return "Population \(state.population)/\(amount)"
        case .techResearched:
            return "Research \(content.technology(c.id ?? "")?.name ?? "?")"
        case .techCountAtLeast:
            return "Technologies \(state.researched.count)/\(amount)"
        case .eventResolved:
            return "Decide: \(content.event(c.id ?? "")?.title ?? "?")"
        case .objectiveCompleted:
            return "Complete \(content.objective(c.id ?? "")?.title ?? "?")"
        case .milestoneReached:
            return "Reach \(content.milestone(c.id ?? "")?.name ?? "?")"
        case .regionExplored:
            return "Explore \(content.region(c.id ?? "")?.name ?? "?")"
        case .distinctJobsAtLeast:
            let n = JobType.allCases.filter { state.assignments[$0] > 0 }.count
            return "Jobs in use \(n)/\(amount)"
        case .foodBalancePositive:
            let perMinute = economy().net.food * 60
            return String(format: "Food balance %+.1f/min (must be positive)", perMinute)
        }
    }

    public func status(of conditions: [Condition]) -> [RequirementStatus] {
        conditions.map { RequirementStatus(text: describe($0), met: isMet($0)) }
    }

    // MARK: - Objectives

    /// First objective not completed yet (the tutorial's current step).
    public var currentObjective: ObjectiveDefinition? {
        content.objectives.first { !state.completedObjectives.contains($0.id) }
    }

    /// Completes objectives, opens events. Rewards are granted exactly once per objective.
    func evaluateProgress() {
        var changed = true
        // Loop because completing one objective can satisfy another (objectiveCompleted conditions).
        while changed {
            changed = false
            for obj in content.objectives where !state.completedObjectives.contains(obj.id) {
                guard obj.conditions.allSatisfy({ isMet($0) }) else { continue }
                state.completedObjectives.append(obj.id)
                if let reward = obj.reward { apply(Effect(kind: .grantResources, resources: reward), sourceId: obj.id) }
                let rewardText = obj.reward.map { " Reward: \($0.shortDescription)" } ?? ""
                notices.append(GameNotice(.success, "Objective complete: \(obj.title).\(rewardText)"))
                changed = true
            }
        }
        checkEventTriggers()
    }

    // MARK: - Milestones

    public func milestoneRequirements(_ id: String) -> [RequirementStatus] {
        status(of: content.milestone(id)?.requirements ?? [])
    }

    public func canClaimMilestone(_ id: String) -> Bool {
        guard let ms = content.milestone(id), !state.reachedMilestones.contains(id) else { return false }
        return ms.requirements.allSatisfy { isMet($0) }
    }

    /// Reaches a milestone: changes the settlement stage and transforms buildings in place,
    /// keeping their ids, positions and history. Can only happen once.
    @discardableResult
    public func claimMilestone(_ id: String) -> Result<Void, GameError> {
        update()
        guard let ms = content.milestone(id) else { return .failure(.unknownContent(id)) }
        if state.reachedMilestones.contains(id) { return .failure(.milestoneAlreadyReached) }
        let unmet = ms.requirements.filter { !isMet($0) }.map { describe($0) }
        if !unmet.isEmpty { return .failure(.requirementsNotMet(unmet)) }

        for t in ms.buildingTransforms {
            for i in state.buildings.indices where state.buildings[i].definitionId == t.from {
                state.buildings[i].definitionId = t.to
                state.buildings[i].history.append(t.to)
            }
        }
        state.stageId = ms.newStage
        state.reachedMilestones.append(id)
        normalizeAssignments()
        addLegacy(kind: "milestone", title: ms.name, detail: ms.legacyText, refId: id)
        notices.append(GameNotice(.milestone, "\(ms.name)! \(ms.description)"))
        evaluateProgress()
        return .success(())
    }
}
