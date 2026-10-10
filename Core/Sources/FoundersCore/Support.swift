import Foundation

// MARK: - Time

/// Injectable time source. All timed systems read time only through this.
public protocol GameClock: AnyObject {
    /// Wall-clock seconds since 1970.
    func now() -> Double
}

public final class SystemClock: GameClock {
    public init() {}
    public func now() -> Double { Date().timeIntervalSince1970 }
}

/// Deterministic clock for tests and previews.
public final class ManualClock: GameClock {
    public var time: Double
    public init(time: Double = 1_000_000) { self.time = time }
    public func now() -> Double { time }
    public func advance(_ seconds: Double) { time += seconds }
}

// MARK: - Feedback

/// Something the player should be told about (shown as a toast by the UI).
public struct GameNotice: Equatable, Sendable {
    public enum Kind: String, Sendable { case info, success, warning, milestone }
    public var kind: Kind
    public var message: String

    public init(_ kind: Kind, _ message: String) {
        self.kind = kind
        self.message = message
    }
}

/// Why a command was refused. Messages are player facing.
public enum GameError: Error, Equatable, CustomStringConvertible {
    case unknownContent(String)
    case placement(PlacementIssue)
    case unaffordable(ResourceBundle)
    case constructionQueueFull(Int)
    case notUpgradeable
    case buildingBusy
    case lockedByTech(String)
    case tooManyWorkers(available: Int)
    case noJobSlots(capacity: Int)
    case researchAlreadyDone
    case researchInProgress
    case missingPrerequisites([String])
    case noPendingEvent
    case optionUnavailable(String)
    case expeditionInProgress
    case regionAlreadyExplored
    case regionNotAdjacent
    case milestoneAlreadyReached
    case requirementsNotMet([String])

    public var description: String {
        switch self {
        case .unknownContent(let id): return "Unknown content '\(id)'."
        case .placement(let issue): return issue.message
        case .unaffordable(let missing): return "Not enough resources. Missing: \(missing.shortDescription)"
        case .constructionQueueFull(let max): return "Your people can only build \(max) things at once."
        case .notUpgradeable: return "This building has no upgrade available."
        case .buildingBusy: return "This building is still under construction."
        case .lockedByTech(let name): return "Requires the technology \(name)."
        case .tooManyWorkers(let available): return "Only \(available) idle people available."
        case .noJobSlots(let capacity): return "No room for more workers (capacity \(capacity)). Build more workplaces."
        case .researchAlreadyDone: return "Already researched."
        case .researchInProgress: return "Another research project is in progress."
        case .missingPrerequisites(let names): return "Research first: \(names.joined(separator: ", "))."
        case .noPendingEvent: return "There is no decision waiting."
        case .optionUnavailable(let reason): return reason
        case .expeditionInProgress: return "Scouts are already out exploring."
        case .regionAlreadyExplored: return "This area is already explored."
        case .regionNotAdjacent: return "This area cannot be reached yet."
        case .milestoneAlreadyReached: return "Milestone already reached."
        case .requirementsNotMet(let list): return "Not ready yet: \(list.joined(separator: ", "))."
        }
    }
}
