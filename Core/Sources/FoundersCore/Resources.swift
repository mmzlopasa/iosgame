import Foundation

/// The four primary resources of the first epoch.
public enum ResourceType: String, Codable, CaseIterable, Sendable {
    case food, wood, stone, knowledge

    public var displayName: String {
        switch self {
        case .food: return "Food"
        case .wood: return "Wood"
        case .stone: return "Stone"
        case .knowledge: return "Knowledge"
        }
    }

    public var icon: String {
        switch self {
        case .food: return "🫐"
        case .wood: return "🪵"
        case .stone: return "🪨"
        case .knowledge: return "💡"
        }
    }
}

/// An amount of each resource. Used for stockpiles, costs, rewards and rates.
/// Missing keys decode as zero so content JSON stays short.
public struct ResourceBundle: Codable, Equatable, Sendable {
    public var food: Double
    public var wood: Double
    public var stone: Double
    public var knowledge: Double

    public static let zero = ResourceBundle()
    static let epsilon = 1e-9

    public init(food: Double = 0, wood: Double = 0, stone: Double = 0, knowledge: Double = 0) {
        self.food = food
        self.wood = wood
        self.stone = stone
        self.knowledge = knowledge
    }

    enum CodingKeys: String, CodingKey { case food, wood, stone, knowledge }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        food = try c.decodeIfPresent(Double.self, forKey: .food) ?? 0
        wood = try c.decodeIfPresent(Double.self, forKey: .wood) ?? 0
        stone = try c.decodeIfPresent(Double.self, forKey: .stone) ?? 0
        knowledge = try c.decodeIfPresent(Double.self, forKey: .knowledge) ?? 0
    }

    public subscript(type: ResourceType) -> Double {
        get {
            switch type {
            case .food: return food
            case .wood: return wood
            case .stone: return stone
            case .knowledge: return knowledge
            }
        }
        set {
            switch type {
            case .food: food = newValue
            case .wood: wood = newValue
            case .stone: stone = newValue
            case .knowledge: knowledge = newValue
            }
        }
    }

    public var isZero: Bool {
        ResourceType.allCases.allSatisfy { abs(self[$0]) < ResourceBundle.epsilon }
    }

    /// Non-zero entries, in canonical order.
    public var entries: [(ResourceType, Double)] {
        ResourceType.allCases.compactMap { abs(self[$0]) < ResourceBundle.epsilon ? nil : ($0, self[$0]) }
    }

    public var isValid: Bool {
        ResourceType.allCases.allSatisfy { self[$0].isFinite && self[$0] >= 0 }
    }

    /// True when every resource in `cost` is available.
    public func covers(_ cost: ResourceBundle) -> Bool {
        ResourceType.allCases.allSatisfy { self[$0] + ResourceBundle.epsilon >= cost[$0] }
    }

    /// How much is missing to pay `cost` (zero entries when affordable).
    public func shortfall(for cost: ResourceBundle) -> ResourceBundle {
        var missing = ResourceBundle()
        for type in ResourceType.allCases {
            missing[type] = max(0, cost[type] - self[type])
        }
        return missing
    }

    public static func + (lhs: ResourceBundle, rhs: ResourceBundle) -> ResourceBundle {
        var result = lhs
        for type in ResourceType.allCases { result[type] += rhs[type] }
        return result
    }

    public static func - (lhs: ResourceBundle, rhs: ResourceBundle) -> ResourceBundle {
        var result = lhs
        for type in ResourceType.allCases { result[type] -= rhs[type] }
        return result
    }

    public static func * (lhs: ResourceBundle, rhs: Double) -> ResourceBundle {
        var result = lhs
        for type in ResourceType.allCases { result[type] *= rhs }
        return result
    }

    /// Human readable, e.g. "12 🪵  5 🪨".
    public var shortDescription: String {
        let parts = entries.map { "\(Int($0.1.rounded())) \($0.0.icon)" }
        return parts.isEmpty ? "Free" : parts.joined(separator: "  ")
    }
}
