import Foundation

// MARK: - Basic enums

public enum JobType: String, Codable, CaseIterable, Sendable {
    case foraging, woodcutting, stonecutting, crafting, construction, research
}

public enum TerrainType: String, Codable, CaseIterable, Sendable {
    case grass, forest, rock, water, sand

    /// Character used in the map rows of content.json.
    public init?(symbol: Character) {
        switch symbol {
        case ".": self = .grass
        case "T": self = .forest
        case "R": self = .rock
        case "~": self = .water
        case ",": self = .sand
        default: return nil
        }
    }

    public var displayName: String { rawValue.capitalized }
}

// MARK: - Shared building blocks

public struct Footprint: Codable, Equatable, Sendable {
    public var width: Int
    public var height: Int
}

public struct JobSlot: Codable, Equatable, Sendable {
    public var job: JobType
    public var count: Int
}

public enum EffectKind: String, Codable, Sendable {
    /// Multiplies the output of `job` by `value`.
    case jobRateMultiplier
    /// Multiplies the storage capacity of `resource` by `value`.
    case storageMultiplier
    /// Multiplies food spoilage by `value`.
    case spoilageMultiplier
    /// Multiplies construction speed by `value` when at least `minBuilders` work on construction.
    case constructionSpeedMultiplier
    /// Multiplies food consumption by `value`.
    case consumptionMultiplier
    /// Multiplies population growth speed by `value`.
    case growthMultiplier
    /// Makes `buildingId` available.
    case unlockBuilding
    /// Adds `resources` once (only valid for event outcomes and rewards).
    case grantResources
}

/// A data-driven gameplay effect. Which optional fields are used depends on `kind`.
public struct Effect: Codable, Equatable, Sendable {
    public var kind: EffectKind
    public var job: JobType?
    public var resource: ResourceType?
    public var value: Double?
    public var buildingId: String?
    public var minBuilders: Int?
    public var resources: ResourceBundle?
    /// For event effects: how long (in game seconds) the effect lasts. Nil = permanent.
    public var durationSeconds: Double?

    public init(kind: EffectKind, job: JobType? = nil, resource: ResourceType? = nil, value: Double? = nil,
                buildingId: String? = nil, minBuilders: Int? = nil, resources: ResourceBundle? = nil,
                durationSeconds: Double? = nil) {
        self.kind = kind
        self.job = job
        self.resource = resource
        self.value = value
        self.buildingId = buildingId
        self.minBuilders = minBuilders
        self.resources = resources
        self.durationSeconds = durationSeconds
    }
}

public enum ConditionKind: String, Codable, Sendable {
    case resourceAtLeast        // resource, amount
    case buildingCount          // id, amount (completed buildings)
    case housingAtLeast         // amount
    case populationAtLeast      // amount
    case techResearched         // id
    case techCountAtLeast       // amount
    case eventResolved          // id
    case objectiveCompleted     // id
    case milestoneReached       // id
    case regionExplored         // id
    case distinctJobsAtLeast    // amount (jobs with at least one worker)
    case foodBalancePositive    // net food rate > 0
}

public struct Condition: Codable, Equatable, Sendable {
    public var kind: ConditionKind
    public var id: String?
    public var resource: ResourceType?
    public var amount: Double?

    public init(kind: ConditionKind, id: String? = nil, resource: ResourceType? = nil, amount: Double? = nil) {
        self.kind = kind
        self.id = id
        self.resource = resource
        self.amount = amount
    }
}

// MARK: - Definitions

public struct BuildingDefinition: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var name: String
    public var description: String
    public var epoch: String
    public var footprint: Footprint
    public var cost: ResourceBundle
    public var buildSeconds: Double
    /// Key of the visual (placeholder sprite) used by the presentation layer.
    public var sprite: String
    public var allowedTerrain: [TerrainType]
    /// False for starting/upgrade-only buildings that never appear in the build menu.
    public var buildable: Bool
    public var housing: Int?
    public var storage: Double?
    public var protectedFood: Double?
    public var jobSlots: [JobSlot]?
    public var maxCount: Int?
    public var requiresTech: String?
    /// Placement requires one of the footprint's neighbouring cells to be this terrain.
    public var requiresAdjacentTerrain: TerrainType?
    /// In-place upgrade target (same footprint). Its cost/buildSeconds are used for the upgrade.
    public var upgradesTo: String?
    /// Building this structure becomes in a later era/milestone (keeps location and identity).
    public var successorId: String?

    public var housingValue: Int { housing ?? 0 }
    public var storageValue: Double { storage ?? 0 }
    public var protectedFoodValue: Double { protectedFood ?? 0 }

    public func slots(for job: JobType) -> Int {
        (jobSlots ?? []).filter { $0.job == job }.reduce(0) { $0 + $1.count }
    }
}

public struct TechnologyDefinition: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var name: String
    public var description: String
    public var cost: ResourceBundle
    public var researchSeconds: Double
    public var prerequisites: [String]
    public var effects: [Effect]
}

public struct EventOption: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var label: String
    /// Plain-language trade-off shown before the player confirms.
    public var tradeOff: String
    public var cost: ResourceBundle?
    public var requiresBuilding: String?
    public var effects: [Effect]
    public var legacyText: String
}

public struct EventDefinition: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var title: String
    public var text: String
    /// All must hold for the event to appear (it appears at most once).
    public var trigger: [Condition]
    /// Applied when the player decides, whichever option is chosen.
    public var baseEffects: [Effect]?
    public var options: [EventOption]
}

public struct ObjectiveDefinition: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var title: String
    public var hint: String
    public var conditions: [Condition]
    public var reward: ResourceBundle?
}

public struct BuildingTransform: Codable, Equatable, Sendable {
    public var from: String
    public var to: String
}

public struct MilestoneDefinition: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var name: String
    public var description: String
    public var requirements: [Condition]
    public var newStage: String
    public var buildingTransforms: [BuildingTransform]
    public var legacyText: String
}

public struct RegionDefinition: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var name: String
    public var description: String
    public var x: Int
    public var y: Int
    public var width: Int
    public var height: Int
    public var revealedAtStart: Bool
    public var cost: ResourceBundle
    public var expeditionSeconds: Double
    public var legacyText: String

    public func contains(x cx: Int, y cy: Int) -> Bool {
        cx >= x && cx < x + width && cy >= y && cy < y + height
    }
}

public struct StartBuilding: Codable, Equatable, Sendable {
    public var buildingId: String
    public var x: Int
    public var y: Int
}

public struct MapDefinition: Codable, Equatable, Sendable {
    public var name: String
    public var width: Int
    public var height: Int
    public var rows: [String]
    public var startBuildings: [StartBuilding]
    /// Persistent geographic traits recorded in the Legacy Timeline (e.g. riverside).
    public var traits: [String]

    public func terrain(x: Int, y: Int) -> TerrainType? {
        guard x >= 0, y >= 0, x < width, y < height, y < rows.count else { return nil }
        let row = Array(rows[y])
        guard x < row.count else { return nil }
        return TerrainType(symbol: row[x])
    }
}

public struct JobConfig: Codable, Equatable, Sendable {
    public var job: JobType
    public var name: String
    public var description: String
    public var output: ResourceType?
    public var ratePerWorkerPerSecond: Double
    /// Jobs like construction have no building slots.
    public var unlimitedSlots: Bool
}

public struct StageDefinition: Codable, Equatable, Sendable {
    public var id: String
    public var name: String
}

public struct EpochDefinition: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var index: Int
    public var name: String
    public var stages: [StageDefinition]
    public var playable: Bool
}

public struct Branding: Codable, Equatable, Sendable {
    public var title: String
    public var subtitle: String
    public var tagline: String
}

/// All tunable numbers live here (content.json → "balance").
public struct BalanceConfig: Codable, Equatable, Sendable {
    public var startResources: ResourceBundle
    public var startPopulation: Int
    public var startAssignments: [JobSlot]
    public var baseStorage: Double
    public var knowledgeCap: Double
    public var foodPerPersonPerSecond: Double
    public var growthSecondsPerPerson: Double
    public var growthMinFood: Double
    public var shortageProductivityMultiplier: Double
    public var spoilageRatePerSecond: Double
    public var baseConstructionSpeed: Double
    public var constructionSpeedPerBuilder: Double
    public var maxConcurrentConstructions: Int
    public var craftingBonusPerCrafter: Double
    public var maxCraftingBonus: Double
    public var craftingInputPerCrafterPerSecond: ResourceBundle
    public var maxOfflineSeconds: Double
    public var simulationStepSeconds: Double
    public var jobs: [JobConfig]
}

// MARK: - Content root

public enum ContentError: Error, CustomStringConvertible {
    case missingBundledFile
    case invalid([String])

    public var description: String {
        switch self {
        case .missingBundledFile: return "content.json was not found in the EonwardCore bundle."
        case .invalid(let problems): return "Content is invalid:\n- " + problems.joined(separator: "\n- ")
        }
    }
}

public struct GameContent: Codable, Equatable, Sendable {
    public var contentVersion: Int
    public var branding: Branding
    public var startEpoch: String
    public var startStage: String
    public var epochs: [EpochDefinition]
    public var balance: BalanceConfig
    public var buildings: [BuildingDefinition]
    public var technologies: [TechnologyDefinition]
    public var events: [EventDefinition]
    public var objectives: [ObjectiveDefinition]
    public var milestones: [MilestoneDefinition]
    public var regions: [RegionDefinition]
    public var map: MapDefinition

    public func building(_ id: String) -> BuildingDefinition? { buildings.first { $0.id == id } }
    public func technology(_ id: String) -> TechnologyDefinition? { technologies.first { $0.id == id } }
    public func event(_ id: String) -> EventDefinition? { events.first { $0.id == id } }
    public func objective(_ id: String) -> ObjectiveDefinition? { objectives.first { $0.id == id } }
    public func milestone(_ id: String) -> MilestoneDefinition? { milestones.first { $0.id == id } }
    public func region(_ id: String) -> RegionDefinition? { regions.first { $0.id == id } }
    public func epoch(_ id: String) -> EpochDefinition? { epochs.first { $0.id == id } }
    public func jobConfig(_ job: JobType) -> JobConfig? { balance.jobs.first { $0.job == job } }

    /// Decodes and validates content. Throws if anything is inconsistent.
    public static func load(from data: Data) throws -> GameContent {
        let content = try JSONDecoder().decode(GameContent.self, from: data)
        let problems = ContentValidator.validate(content)
        if !problems.isEmpty { throw ContentError.invalid(problems) }
        return content
    }

    /// Loads the content.json shipped inside the package.
    public static func loadBundled() throws -> GameContent {
        guard let url = Bundle.module.url(forResource: "content", withExtension: "json") else {
            throw ContentError.missingBundledFile
        }
        return try load(from: Data(contentsOf: url))
    }
}
