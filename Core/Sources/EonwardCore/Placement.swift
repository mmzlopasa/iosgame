import Foundation

/// Reasons a placement is rejected. Ordered from most to least fundamental.
public enum PlacementIssue: Equatable, Sendable {
    case notBuildable
    case lockedByTech(String)
    case limitReached(Int)
    case outOfBounds
    case unexplored
    case invalidTerrain(TerrainType)
    case overlaps
    case needsAdjacent(TerrainType)
    case constructionQueueFull(Int)
    case unaffordable(ResourceBundle)

    public var message: String {
        switch self {
        case .notBuildable: return "This structure cannot be built directly."
        case .lockedByTech(let name): return "Requires the technology \(name)."
        case .limitReached(let max): return "Limit reached: only \(max) allowed."
        case .outOfBounds: return "Outside the settlement area."
        case .unexplored: return "This land is unexplored. Send scouts from the Map first."
        case .invalidTerrain(let t): return "Cannot build on \(t.displayName.lowercased())."
        case .overlaps: return "Something is already built here."
        case .needsAdjacent(let t): return "Must be placed next to \(t.displayName.lowercased())."
        case .constructionQueueFull(let max): return "Your people can only build \(max) things at once."
        case .unaffordable(let missing): return "Not enough resources. Missing: \(missing.shortDescription)"
        }
    }
}

extension Game {
    public func isRevealed(x: Int, y: Int) -> Bool {
        state.exploredRegions.contains { id in content.region(id)?.contains(x: x, y: y) ?? false }
    }

    func cells(x: Int, y: Int, footprint: Footprint) -> [(Int, Int)] {
        var result: [(Int, Int)] = []
        for dy in 0..<footprint.height {
            for dx in 0..<footprint.width { result.append((x + dx, y + dy)) }
        }
        return result
    }

    /// Id of the building occupying a cell, if any.
    public func buildingId(atX x: Int, y: Int) -> Int? {
        for b in state.buildings {
            guard let def = content.building(b.definitionId) else { continue }
            if x >= b.x, x < b.x + def.footprint.width, y >= b.y, y < b.y + def.footprint.height { return b.id }
        }
        return nil
    }

    public func count(of definitionId: String, includeUnfinished: Bool = false) -> Int {
        state.buildings.filter { $0.definitionId == definitionId && (includeUnfinished || $0.isOperational) }.count
    }

    public func activeConstructionCount() -> Int {
        state.buildings.filter { $0.status != .complete }.count
    }

    public func isUnlocked(_ def: BuildingDefinition) -> Bool {
        guard let tech = def.requiresTech else { return true }
        return state.researched.contains(tech)
    }

    /// All reasons the building cannot be placed at (x, y). Empty = valid.
    /// Does not change any state.
    public func placementIssues(buildingId: String, x: Int, y: Int) -> [PlacementIssue] {
        guard let def = content.building(buildingId) else { return [.notBuildable] }
        var issues: [PlacementIssue] = []
        if !def.buildable { issues.append(.notBuildable) }
        if !isUnlocked(def) {
            issues.append(.lockedByTech(content.technology(def.requiresTech ?? "")?.name ?? "?"))
        }
        if let max = def.maxCount, count(of: def.id, includeUnfinished: true) >= max {
            issues.append(.limitReached(max))
        }

        let footprintCells = cells(x: x, y: y, footprint: def.footprint)
        let map = content.map
        if footprintCells.contains(where: { $0.0 < 0 || $0.1 < 0 || $0.0 >= map.width || $0.1 >= map.height }) {
            issues.append(.outOfBounds)
        } else {
            if footprintCells.contains(where: { !isRevealed(x: $0.0, y: $0.1) }) { issues.append(.unexplored) }
            for (cx, cy) in footprintCells {
                if let t = map.terrain(x: cx, y: cy), !def.allowedTerrain.contains(t) {
                    issues.append(.invalidTerrain(t))
                    break
                }
            }
            if footprintCells.contains(where: { self.buildingId(atX: $0.0, y: $0.1) != nil }) { issues.append(.overlaps) }
            if let needed = def.requiresAdjacentTerrain, !hasAdjacent(needed, cells: footprintCells) {
                issues.append(.needsAdjacent(needed))
            }
        }

        let maxQueue = content.balance.maxConcurrentConstructions
        if activeConstructionCount() >= maxQueue { issues.append(.constructionQueueFull(maxQueue)) }
        if !state.resources.covers(def.cost) { issues.append(.unaffordable(state.resources.shortfall(for: def.cost))) }
        return issues
    }

    func hasAdjacent(_ terrain: TerrainType, cells footprintCells: [(Int, Int)]) -> Bool {
        let occupied = Set(footprintCells.map { "\($0.0),\($0.1)" })
        for (cx, cy) in footprintCells {
            for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
                let nx = cx + dx, ny = cy + dy
                if occupied.contains("\(nx),\(ny)") { continue }
                if content.map.terrain(x: nx, y: ny) == terrain, isRevealed(x: nx, y: ny) { return true }
            }
        }
        return false
    }

    /// Places a building and starts construction. Resources are spent only here,
    /// only after every check passed, and exactly once per placed building.
    @discardableResult
    public func placeBuilding(_ buildingId: String, x: Int, y: Int) -> Result<Int, GameError> {
        update()
        guard let def = content.building(buildingId) else { return .failure(.unknownContent(buildingId)) }
        if let issue = placementIssues(buildingId: buildingId, x: x, y: y).first {
            return .failure(.placement(issue))
        }
        state.resources = state.resources - def.cost
        let id = state.nextBuildingId
        state.nextBuildingId += 1
        state.buildings.append(PlacedBuilding(
            id: id, definitionId: def.id, x: x, y: y, status: .constructing,
            remainingWork: def.buildSeconds, totalWork: def.buildSeconds,
            upgradeTargetId: nil, history: [def.id], foundedAtSimTime: state.simTime))
        notices.append(GameNotice(.info, "Construction started: \(def.name)."))
        return .success(id)
    }
}
