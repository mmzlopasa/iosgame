import Foundation

extension Game {
    /// Work units per second applied to each active construction.
    public func constructionSpeed() -> Double {
        let b = content.balance
        let builders = state.assignments[.construction]
        var speed = b.baseConstructionSpeed + b.constructionSpeedPerBuilder * Double(builders)
        for e in activeEffects() where e.kind == .constructionSpeedMultiplier {
            if builders >= (e.minBuilders ?? 0) { speed *= e.value ?? 1 }
        }
        return speed
    }

    /// Estimated seconds to finish `work` at the current speed.
    public func estimatedSeconds(work: Double) -> Double {
        let speed = constructionSpeed()
        return speed > 0 ? work / speed : .infinity
    }

    func advanceConstruction(_ dt: Double) {
        let speed = constructionSpeed()
        var completed: [Int] = []
        for i in state.buildings.indices where state.buildings[i].status != .complete {
            state.buildings[i].remainingWork -= speed * dt
            if state.buildings[i].remainingWork <= 1e-9 { completed.append(i) }
        }
        for i in completed { finishConstruction(at: i) }
    }

    /// Completes a construction or upgrade. Only reachable once per building per job,
    /// because the status leaves `.constructing`/`.upgrading` here.
    private func finishConstruction(at index: Int) {
        var b = state.buildings[index]
        let wasUpgrade = b.status == .upgrading
        if wasUpgrade, let target = b.upgradeTargetId {
            b.definitionId = target
            b.history.append(target)
        }
        b.status = .complete
        b.remainingWork = 0
        b.upgradeTargetId = nil
        state.buildings[index] = b

        let name = content.building(b.definitionId)?.name ?? b.definitionId
        notices.append(GameNotice(.success, wasUpgrade ? "Upgrade complete: \(name)." : "Construction complete: \(name)."))
        if count(of: b.definitionId) == 1 {
            addLegacy(kind: "building", title: "First \(name)",
                      detail: content.building(b.definitionId)?.description ?? "", refId: b.definitionId)
        }
    }

    /// Starts an in-place upgrade (keeps the building id, position and history).
    @discardableResult
    public func upgradeBuilding(id: Int) -> Result<Void, GameError> {
        update()
        guard let index = state.buildings.firstIndex(where: { $0.id == id }),
              let def = content.building(state.buildings[index].definitionId) else {
            return .failure(.unknownContent("building #\(id)"))
        }
        guard state.buildings[index].status == .complete else { return .failure(.buildingBusy) }
        guard let targetId = def.upgradesTo, let target = content.building(targetId) else { return .failure(.notUpgradeable) }
        if !isUnlocked(target) {
            return .failure(.lockedByTech(content.technology(target.requiresTech ?? "")?.name ?? "?"))
        }
        let maxQueue = content.balance.maxConcurrentConstructions
        if activeConstructionCount() >= maxQueue { return .failure(.constructionQueueFull(maxQueue)) }
        guard state.resources.covers(target.cost) else {
            return .failure(.unaffordable(state.resources.shortfall(for: target.cost)))
        }
        state.resources = state.resources - target.cost
        state.buildings[index].status = .upgrading
        state.buildings[index].upgradeTargetId = target.id
        state.buildings[index].remainingWork = target.buildSeconds
        state.buildings[index].totalWork = target.buildSeconds
        notices.append(GameNotice(.info, "Upgrade started: \(def.name) → \(target.name)."))
        return .success(())
    }
}
