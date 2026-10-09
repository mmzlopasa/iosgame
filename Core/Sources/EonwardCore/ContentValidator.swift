import Foundation

/// Checks content for broken references, duplicate IDs and impossible chains.
/// Returns human readable problems; an empty list means the content is usable.
public enum ContentValidator {
    public static func validate(_ c: GameContent) -> [String] {
        var problems: [String] = []

        func checkUnique(_ ids: [String], _ label: String) {
            var seen = Set<String>()
            for id in ids {
                if id.isEmpty { problems.append("\(label) has an empty id") }
                if !seen.insert(id).inserted { problems.append("Duplicate \(label) id '\(id)'") }
            }
        }
        checkUnique(c.buildings.map(\.id), "building")
        checkUnique(c.technologies.map(\.id), "technology")
        checkUnique(c.events.map(\.id), "event")
        checkUnique(c.objectives.map(\.id), "objective")
        checkUnique(c.milestones.map(\.id), "milestone")
        checkUnique(c.regions.map(\.id), "region")
        checkUnique(c.epochs.map(\.id), "epoch")

        let buildingIds = Set(c.buildings.map(\.id))
        let techIds = Set(c.technologies.map(\.id))
        let eventIds = Set(c.events.map(\.id))
        let objectiveIds = Set(c.objectives.map(\.id))
        let milestoneIds = Set(c.milestones.map(\.id))
        let regionIds = Set(c.regions.map(\.id))

        // Epochs
        guard let startEpoch = c.epoch(c.startEpoch) else {
            problems.append("startEpoch '\(c.startEpoch)' does not exist")
            return problems
        }
        let stageIds = Set(startEpoch.stages.map(\.id))
        if !stageIds.contains(c.startStage) { problems.append("startStage '\(c.startStage)' is not a stage of \(c.startEpoch)") }

        // Balance
        let b = c.balance
        if b.simulationStepSeconds <= 0 { problems.append("simulationStepSeconds must be > 0") }
        if b.maxOfflineSeconds < 0 { problems.append("maxOfflineSeconds must be >= 0") }
        if !b.startResources.isValid { problems.append("startResources must be non-negative") }
        for job in JobType.allCases where c.jobConfig(job) == nil {
            problems.append("balance.jobs is missing job '\(job.rawValue)'")
        }
        if b.startAssignments.reduce(0, { $0 + $1.count }) > b.startPopulation {
            problems.append("startAssignments exceed startPopulation")
        }

        // Map
        let map = c.map
        if map.rows.count != map.height { problems.append("map has \(map.rows.count) rows, expected \(map.height)") }
        for (y, row) in map.rows.enumerated() {
            if row.count != map.width { problems.append("map row \(y) has \(row.count) cells, expected \(map.width)") }
            for ch in row where TerrainType(symbol: ch) == nil {
                problems.append("map row \(y) has unknown terrain symbol '\(ch)'")
            }
        }
        for sb in map.startBuildings where !buildingIds.contains(sb.buildingId) {
            problems.append("start building '\(sb.buildingId)' does not exist")
        }

        // Buildings
        for bld in c.buildings {
            if c.epoch(bld.epoch) == nil { problems.append("building '\(bld.id)' uses unknown epoch '\(bld.epoch)'") }
            if bld.footprint.width < 1 || bld.footprint.height < 1 { problems.append("building '\(bld.id)' has an empty footprint") }
            if bld.buildSeconds <= 0 { problems.append("building '\(bld.id)' needs buildSeconds > 0") }
            if !bld.cost.isValid { problems.append("building '\(bld.id)' has a negative cost") }
            if bld.allowedTerrain.isEmpty { problems.append("building '\(bld.id)' has no allowed terrain") }
            if let t = bld.requiresTech, !techIds.contains(t) { problems.append("building '\(bld.id)' requires unknown tech '\(t)'") }
            if let up = bld.upgradesTo {
                if let target = c.building(up) {
                    if target.footprint != bld.footprint { problems.append("upgrade '\(bld.id)' → '\(up)' changes footprint") }
                } else {
                    problems.append("building '\(bld.id)' upgrades to unknown '\(up)'")
                }
            }
            if let succ = bld.successorId, let target = c.building(succ), target.footprint != bld.footprint {
                problems.append("successor '\(bld.id)' → '\(succ)' changes footprint")
            }
        }

        // Technologies: references and cycles
        for tech in c.technologies {
            for p in tech.prerequisites where !techIds.contains(p) {
                problems.append("tech '\(tech.id)' has unknown prerequisite '\(p)'")
            }
            if tech.researchSeconds <= 0 { problems.append("tech '\(tech.id)' needs researchSeconds > 0") }
            problems += validateEffects(tech.effects, owner: "tech '\(tech.id)'", buildingIds: buildingIds)
        }
        if let cycle = findPrerequisiteCycle(c.technologies) {
            problems.append("technology prerequisites form a cycle: " + cycle.joined(separator: " → "))
        }

        // Conditions
        func checkConditions(_ conds: [Condition], owner: String) {
            for cond in conds {
                switch cond.kind {
                case .buildingCount:
                    if !buildingIds.contains(cond.id ?? "") { problems.append("\(owner): unknown building '\(cond.id ?? "nil")'") }
                case .techResearched:
                    if !techIds.contains(cond.id ?? "") { problems.append("\(owner): unknown tech '\(cond.id ?? "nil")'") }
                case .eventResolved:
                    if !eventIds.contains(cond.id ?? "") { problems.append("\(owner): unknown event '\(cond.id ?? "nil")'") }
                case .objectiveCompleted:
                    if !objectiveIds.contains(cond.id ?? "") { problems.append("\(owner): unknown objective '\(cond.id ?? "nil")'") }
                case .milestoneReached:
                    if !milestoneIds.contains(cond.id ?? "") { problems.append("\(owner): unknown milestone '\(cond.id ?? "nil")'") }
                case .regionExplored:
                    if !regionIds.contains(cond.id ?? "") { problems.append("\(owner): unknown region '\(cond.id ?? "nil")'") }
                case .resourceAtLeast:
                    if cond.resource == nil { problems.append("\(owner): resourceAtLeast needs a resource") }
                default:
                    break
                }
            }
        }

        for ev in c.events {
            checkConditions(ev.trigger, owner: "event '\(ev.id)'")
            if ev.options.count < 2 || ev.options.count > 3 { problems.append("event '\(ev.id)' must have 2–3 options") }
            problems += validateEffects(ev.baseEffects ?? [], owner: "event '\(ev.id)'", buildingIds: buildingIds)
            for opt in ev.options {
                if let req = opt.requiresBuilding, !buildingIds.contains(req) {
                    problems.append("event option '\(opt.id)' requires unknown building '\(req)'")
                }
                problems += validateEffects(opt.effects, owner: "event option '\(opt.id)'", buildingIds: buildingIds)
            }
            if ev.options.allSatisfy({ $0.requiresBuilding != nil }) {
                problems.append("event '\(ev.id)' needs at least one option without requirements")
            }
        }
        for obj in c.objectives {
            if obj.conditions.isEmpty { problems.append("objective '\(obj.id)' has no conditions") }
            checkConditions(obj.conditions, owner: "objective '\(obj.id)'")
        }
        for ms in c.milestones {
            checkConditions(ms.requirements, owner: "milestone '\(ms.id)'")
            if !stageIds.contains(ms.newStage) { problems.append("milestone '\(ms.id)' targets unknown stage '\(ms.newStage)'") }
            for t in ms.buildingTransforms {
                guard let from = c.building(t.from), let to = c.building(t.to) else {
                    problems.append("milestone '\(ms.id)' transforms unknown buildings \(t.from) → \(t.to)")
                    continue
                }
                if from.footprint != to.footprint { problems.append("milestone transform \(t.from) → \(t.to) changes footprint") }
            }
        }
        for r in c.regions where r.x < 0 || r.y < 0 || r.x + r.width > map.width || r.y + r.height > map.height {
            problems.append("region '\(r.id)' lies outside the map")
        }
        if !c.regions.contains(where: \.revealedAtStart) { problems.append("at least one region must be revealed at start") }

        return problems
    }

    static func validateEffects(_ effects: [Effect], owner: String, buildingIds: Set<String>) -> [String] {
        var problems: [String] = []
        for e in effects {
            switch e.kind {
            case .jobRateMultiplier:
                if e.job == nil || e.value == nil { problems.append("\(owner): jobRateMultiplier needs job and value") }
            case .storageMultiplier:
                if e.resource == nil || e.value == nil { problems.append("\(owner): storageMultiplier needs resource and value") }
            case .spoilageMultiplier, .consumptionMultiplier, .growthMultiplier, .constructionSpeedMultiplier:
                if e.value == nil { problems.append("\(owner): \(e.kind.rawValue) needs value") }
            case .unlockBuilding:
                if !buildingIds.contains(e.buildingId ?? "") { problems.append("\(owner): unlocks unknown building '\(e.buildingId ?? "nil")'") }
            case .grantResources:
                if e.resources == nil { problems.append("\(owner): grantResources needs resources") }
            }
            if let v = e.value, !v.isFinite || v < 0 { problems.append("\(owner): effect value must be finite and >= 0") }
        }
        return problems
    }

    /// Returns one cycle (as a list of ids) if the prerequisite graph is not acyclic.
    public static func findPrerequisiteCycle(_ techs: [TechnologyDefinition]) -> [String]? {
        let byId = Dictionary(techs.map { ($0.id, $0) }, uniquingKeysWith: { a, _ in a })
        var state: [String: Int] = [:] // 1 = visiting, 2 = done
        var stack: [String] = []

        func visit(_ id: String) -> [String]? {
            if state[id] == 2 { return nil }
            if state[id] == 1 {
                if let start = stack.firstIndex(of: id) { return Array(stack[start...]) + [id] }
                return [id]
            }
            state[id] = 1
            stack.append(id)
            for p in byId[id]?.prerequisites ?? [] {
                if let cycle = visit(p) { return cycle }
            }
            stack.removeLast()
            state[id] = 2
            return nil
        }

        for t in techs {
            if let cycle = visit(t.id) { return cycle }
        }
        return nil
    }
}
