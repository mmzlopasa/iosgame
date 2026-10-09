import Foundation

public enum BuildingStatus: String, Codable, Sendable {
    case constructing, complete, upgrading
}

/// A building placed in the settlement. `id` is a stable instance id that survives
/// upgrades and era transitions, so the same structure keeps its identity and history.
public struct PlacedBuilding: Codable, Equatable, Sendable, Identifiable {
    public var id: Int
    public var definitionId: String
    public var x: Int
    public var y: Int
    public var status: BuildingStatus
    public var remainingWork: Double
    public var totalWork: Double
    public var upgradeTargetId: String?
    /// Every definition this building has been, oldest first (identity across eras).
    public var history: [String]
    public var foundedAtSimTime: Double

    public var isOperational: Bool { status != .constructing }

    public var progress: Double {
        guard status != .complete, totalWork > 0 else { return 1 }
        return min(1, max(0, 1 - remainingWork / totalWork))
    }
}

/// Simple job → worker count storage keyed by the stable job id.
public struct JobAssignments: Codable, Equatable, Sendable {
    public var counts: [String: Int] = [:]

    public init() {}

    public subscript(job: JobType) -> Int {
        get { counts[job.rawValue] ?? 0 }
        set { counts[job.rawValue] = max(0, newValue) }
    }

    public var total: Int { JobType.allCases.reduce(0) { $0 + self[$1] } }
}

public struct ActiveResearch: Codable, Equatable, Sendable {
    public var techId: String
    public var remainingSeconds: Double
    public var totalSeconds: Double
}

public struct ActiveExpedition: Codable, Equatable, Sendable {
    public var regionId: String
    public var remainingSeconds: Double
    public var totalSeconds: Double
}

/// A temporary or permanent effect from an event decision.
public struct ActiveModifier: Codable, Equatable, Sendable {
    public var sourceId: String
    public var effect: Effect
    /// Game (simulation) time at which it ends. Nil = permanent.
    public var expiresAtSimTime: Double?
}

/// One entry of the Legacy Timeline: a decision or event and its lasting meaning.
public struct LegacyEntry: Codable, Equatable, Sendable, Identifiable {
    public var id: Int
    public var simTime: Double
    public var epochId: String
    public var kind: String
    public var title: String
    public var detail: String
    public var refId: String?
}

/// The complete, saveable state of one settlement. Plain data only — rules live in `Game`.
public struct GameState: Codable, Equatable, Sendable {
    public var settlementId: String
    public var settlementName: String
    public var mapName: String
    public var epochId: String
    public var stageId: String

    /// Wall-clock time (seconds since 1970) up to which the simulation has been advanced.
    public var lastUpdateWallTime: Double
    /// Total simulated game time in seconds. Drives modifier expiry deterministically.
    public var simTime: Double

    public var resources: ResourceBundle
    public var population: Int
    public var growthProgress: Double
    public var assignments: JobAssignments
    public var foodShortage: Bool

    public var buildings: [PlacedBuilding]
    public var nextBuildingId: Int

    public var researched: [String]
    public var activeResearch: ActiveResearch?

    public var exploredRegions: [String]
    public var activeExpedition: ActiveExpedition?

    public var pendingEventId: String?
    public var resolvedEvents: [String: String] // eventId → optionId
    public var modifiers: [ActiveModifier]

    public var completedObjectives: [String]
    public var reachedMilestones: [String]

    public var legacy: [LegacyEntry]
    public var nextLegacyId: Int

    public func building(withId id: Int) -> PlacedBuilding? { buildings.first { $0.id == id } }
}
