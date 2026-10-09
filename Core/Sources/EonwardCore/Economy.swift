import Foundation

/// Per-second production/consumption at one moment. Used by the simulation and shown by the UI.
public struct EconomySnapshot: Equatable, Sendable {
    public var production = ResourceBundle()
    public var consumption = ResourceBundle()
    public var spoilage: Double = 0
    public var craftingActive = false
    public var craftingBonus: Double = 0
    public var net: ResourceBundle { production - consumption }
}

extension Game {
    /// Effects from researched technologies plus active event modifiers.
    func activeEffects() -> [Effect] {
        var effects: [Effect] = []
        for id in state.researched {
            if let tech = content.technology(id) { effects += tech.effects }
        }
        effects += state.modifiers.map(\.effect)
        return effects
    }

    /// Product of all multipliers of `kind` (optionally filtered by job/resource).
    func multiplier(_ kind: EffectKind, job: JobType? = nil, resource: ResourceType? = nil) -> Double {
        activeEffects()
            .filter { $0.kind == kind && $0.job == job && $0.resource == resource }
            .reduce(1.0) { $0 * ($1.value ?? 1) }
    }

    public func operationalBuildings() -> [(PlacedBuilding, BuildingDefinition)] {
        state.buildings.compactMap { b in
            guard b.isOperational, let def = content.building(b.definitionId) else { return nil }
            return (b, def)
        }
    }

    public func housingCapacity() -> Int {
        operationalBuildings().reduce(0) { $0 + $1.1.housingValue }
    }

    public func storageCapacity() -> ResourceBundle {
        let extra = operationalBuildings().reduce(0.0) { $0 + $1.1.storageValue }
        let base = content.balance.baseStorage + extra
        var caps = ResourceBundle()
        for type in [ResourceType.food, .wood, .stone] {
            caps[type] = base * multiplier(.storageMultiplier, resource: type)
        }
        caps.knowledge = content.balance.knowledgeCap * multiplier(.storageMultiplier, resource: .knowledge)
        return caps
    }

    public func protectedFood() -> Double {
        operationalBuildings().reduce(0.0) { $0 + $1.1.protectedFoodValue }
    }

    /// How many workers a job can take. Jobs with unlimited slots are limited only by population.
    public func jobCapacity(_ job: JobType) -> Int {
        if content.jobConfig(job)?.unlimitedSlots == true { return state.population }
        return operationalBuildings().reduce(0) { $0 + $1.1.slots(for: job) }
    }

    /// Output per worker per second for a job, including all current bonuses and penalties.
    public func ratePerWorker(_ job: JobType) -> Double {
        guard let cfg = content.jobConfig(job), cfg.output != nil else { return 0 }
        var rate = cfg.ratePerWorkerPerSecond * multiplier(.jobRateMultiplier, job: job)
        if state.foodShortage { rate *= content.balance.shortageProductivityMultiplier }
        if isGathering(job) { rate *= 1 + craftingStatus().bonus }
        return rate
    }

    func isGathering(_ job: JobType) -> Bool {
        job == .foraging || job == .woodcutting || job == .stonecutting
    }

    func craftingStatus() -> (active: Bool, bonus: Double, input: ResourceBundle) {
        let b = content.balance
        let crafters = state.assignments[.crafting]
        guard crafters > 0 else { return (false, 0, .zero) }
        let input = b.craftingInputPerCrafterPerSecond * Double(crafters)
        // Crafting only works while there is enough material for at least one step.
        let active = state.resources.covers(input * b.simulationStepSeconds)
        let bonus = active ? min(b.maxCraftingBonus, b.craftingBonusPerCrafter * Double(crafters)) : 0
        return (active, bonus, active ? input : .zero)
    }

    /// Current per-second economy.
    public func economy() -> EconomySnapshot {
        let b = content.balance
        var snap = EconomySnapshot()
        let crafting = craftingStatus()
        snap.craftingActive = crafting.active
        snap.craftingBonus = crafting.bonus

        for cfg in b.jobs {
            guard let output = cfg.output else { continue }
            snap.production[output] += Double(state.assignments[cfg.job]) * ratePerWorker(cfg.job)
        }

        snap.consumption.food = Double(state.population) * b.foodPerPersonPerSecond * multiplier(.consumptionMultiplier)
        snap.spoilage = max(0, state.resources.food - protectedFood()) * b.spoilageRatePerSecond * multiplier(.spoilageMultiplier)
        snap.consumption.food += snap.spoilage
        snap.consumption = snap.consumption + crafting.input
        return snap
    }

    // MARK: - Jobs

    /// Sets the number of workers for a job. Validates capacity and idle population.
    public func setWorkers(_ job: JobType, to count: Int) -> Result<Void, GameError> {
        update()
        let target = max(0, count)
        let current = state.assignments[job]
        if target > current {
            let available = idleWorkers
            if target - current > available { return .failure(.tooManyWorkers(available: available)) }
            let capacity = jobCapacity(job)
            if target > capacity { return .failure(.noJobSlots(capacity: capacity)) }
        }
        state.assignments[job] = target
        evaluateProgress()
        return .success(())
    }

    /// Keeps assignments within capacity after buildings change (e.g. a transform).
    func normalizeAssignments() {
        for job in JobType.allCases {
            let cap = jobCapacity(job)
            if state.assignments[job] > cap { state.assignments[job] = cap }
        }
        while state.assignments.total > state.population {
            guard let job = JobType.allCases.last(where: { state.assignments[$0] > 0 }) else { break }
            state.assignments[job] -= 1
        }
    }
}
