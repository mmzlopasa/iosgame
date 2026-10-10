import Foundation

/// Owns the settlement state and applies the rules to it.
/// Rules are split by topic into extensions (Economy, Placement, Construction,
/// Research, Events, Progression, Exploration). The UI only calls these commands
/// and reads `state`; it never changes state directly.
public final class Game {
    public let content: GameContent
    public internal(set) var state: GameState
    let clock: GameClock
    /// Feedback produced since the UI last called `drainNotices()`.
    var notices: [GameNotice] = []

    public init(content: GameContent, state: GameState, clock: GameClock) {
        self.content = content
        self.state = state
        self.clock = clock
    }

    /// Creates a brand new settlement from content.
    public static func newGame(content: GameContent, clock: GameClock, settlementName: String = "Riverside Camp") -> Game {
        let b = content.balance
        var assignments = JobAssignments()
        for slot in b.startAssignments { assignments[slot.job] += slot.count }

        let state = GameState(
            settlementId: UUID().uuidString,
            settlementName: settlementName,
            mapName: content.map.name,
            epochId: content.startEpoch,
            stageId: content.startStage,
            lastUpdateWallTime: clock.now(),
            simTime: 0,
            resources: b.startResources,
            population: b.startPopulation,
            growthProgress: 0,
            assignments: assignments,
            foodShortage: false,
            buildings: [],
            nextBuildingId: 1,
            researched: [],
            activeResearch: nil,
            exploredRegions: content.regions.filter(\.revealedAtStart).map(\.id),
            activeExpedition: nil,
            pendingEventId: nil,
            resolvedEvents: [:],
            modifiers: [],
            completedObjectives: [],
            reachedMilestones: [],
            legacy: [],
            nextLegacyId: 1
        )
        let game = Game(content: content, state: state, clock: clock)
        for sb in content.map.startBuildings {
            guard let def = content.building(sb.buildingId) else { continue }
            game.state.buildings.append(PlacedBuilding(
                id: game.state.nextBuildingId, definitionId: def.id, x: sb.x, y: sb.y,
                status: .complete, remainingWork: 0, totalWork: def.buildSeconds,
                upgradeTargetId: nil, history: [def.id], foundedAtSimTime: 0))
            game.state.nextBuildingId += 1
        }
        let traits = content.map.traits.joined(separator: ", ")
        game.addLegacy(kind: "founding", title: "\(settlementName) is founded",
                       detail: "A small band settles in a clearing (\(traits)). This place will shape everything built here.",
                       refId: content.map.name)
        return game
    }

    /// Returns and clears pending feedback for the UI.
    public func drainNotices() -> [GameNotice] {
        let out = notices
        notices.removeAll()
        return out
    }

    // MARK: - Time

    /// Brings the simulation up to the current wall-clock time.
    /// Every wall-clock interval is simulated exactly once, so calling this on
    /// resume, after a restart or repeatedly can never grant duplicate production.
    public func update() {
        let now = clock.now()
        let elapsed = now - state.lastUpdateWallTime
        guard elapsed > 0 else {
            // Clock moved backwards (or no time passed): re-anchor without simulating.
            state.lastUpdateWallTime = now
            return
        }
        let simulated = min(elapsed, content.balance.maxOfflineSeconds)
        state.lastUpdateWallTime = now
        advance(seconds: simulated)
    }

    /// Simulates `seconds` of game time in fixed steps (deterministic for identical input).
    func advance(seconds: Double) {
        guard seconds > 0 else { return }
        let step = content.balance.simulationStepSeconds
        var remaining = seconds
        while remaining > 1e-9 {
            let dt = min(step, remaining)
            simulateStep(dt)
            remaining -= dt
        }
        evaluateProgress()
    }

    private func simulateStep(_ dt: Double) {
        let snapshot = economy()

        // Resources
        let caps = storageCapacity()
        for type in ResourceType.allCases {
            let value = state.resources[type] + snapshot.net[type] * dt
            state.resources[type] = min(caps[type], max(0, value))
        }

        // Food shortage: warning + productivity penalty, never deaths.
        let shortage = state.resources.food <= 1e-6 && snapshot.net.food < 0
        if shortage != state.foodShortage {
            state.foodShortage = shortage
            notices.append(shortage
                ? GameNotice(.warning, "Food has run out! Work slows down until food is restored.")
                : GameNotice(.success, "Food supply restored."))
        }

        advanceConstruction(dt)
        advanceResearch(dt)
        advanceExpedition(dt)
        advanceGrowth(dt)

        state.simTime += dt
        let simTime = state.simTime
        state.modifiers.removeAll { m in
            if let end = m.expiresAtSimTime { return end <= simTime }
            return false
        }
    }

    private func advanceGrowth(_ dt: Double) {
        let b = content.balance
        guard !state.foodShortage,
              state.population < housingCapacity(),
              state.resources.food >= b.growthMinFood,
              b.growthSecondsPerPerson > 0 else { return }
        state.growthProgress += dt / b.growthSecondsPerPerson * multiplier(.growthMultiplier)
        if state.growthProgress >= 1 {
            state.growthProgress = 0
            state.population += 1
            notices.append(GameNotice(.success, "A new member joined the community (population \(state.population))."))
        }
    }

    // MARK: - Legacy

    func addLegacy(kind: String, title: String, detail: String, refId: String?) {
        state.legacy.append(LegacyEntry(id: state.nextLegacyId, simTime: state.simTime, epochId: state.epochId,
                                        kind: kind, title: title, detail: detail, refId: refId))
        state.nextLegacyId += 1
    }

    // MARK: - Display helpers

    public var epochName: String { content.epoch(state.epochId)?.name ?? state.epochId }

    public var stageName: String {
        content.epoch(state.epochId)?.stages.first { $0.id == state.stageId }?.name ?? state.stageId
    }

    public var idleWorkers: Int { max(0, state.population - state.assignments.total) }
}
