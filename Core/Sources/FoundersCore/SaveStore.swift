import Foundation

/// Versioned wrapper written to disk.
public struct SaveEnvelope: Codable, Equatable {
    public static let currentVersion = 1

    public var saveVersion: Int
    public var contentVersion: Int
    public var savedAtWallTime: Double
    public var state: GameState
}

public enum LoadOutcome: Equatable {
    case loaded(GameState)
    case noSave
    /// Unreadable or invalid data. The bad file was moved aside (if possible) so it is not lost.
    case corrupt(reason: String)
    /// Written by a newer version of the game; left untouched.
    case unsupportedVersion(Int)
}

/// Persistence boundary. A cloud/server implementation can replace the local one later.
public protocol SaveStore {
    func load(content: GameContent) -> LoadOutcome
    func save(_ state: GameState, content: GameContent, wallTime: Double) throws
    func reset() throws
}

public enum SaveCodec {
    public static func encode(_ state: GameState, content: GameContent, wallTime: Double) throws -> Data {
        let envelope = SaveEnvelope(saveVersion: SaveEnvelope.currentVersion, contentVersion: content.contentVersion,
                                    savedAtWallTime: wallTime, state: state)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        return try encoder.encode(envelope)
    }

    /// Decodes and validates save data. Never throws: problems become a `LoadOutcome`.
    public static func decode(_ data: Data, content: GameContent) -> LoadOutcome {
        struct VersionProbe: Decodable { var saveVersion: Int }
        guard let probe = try? JSONDecoder().decode(VersionProbe.self, from: data) else {
            return .corrupt(reason: "Save file is not readable JSON.")
        }
        if probe.saveVersion > SaveEnvelope.currentVersion { return .unsupportedVersion(probe.saveVersion) }
        if probe.saveVersion < 1 { return .corrupt(reason: "Invalid save version \(probe.saveVersion).") }
        // Future: migrate older save versions here before decoding.
        let envelope: SaveEnvelope
        do {
            envelope = try JSONDecoder().decode(SaveEnvelope.self, from: data)
        } catch {
            return .corrupt(reason: "Save data is incomplete: \(error)")
        }
        let problems = validate(envelope.state, content: content)
        if !problems.isEmpty { return .corrupt(reason: problems.joined(separator: "; ")) }
        return .loaded(envelope.state)
    }

    /// Structural checks so a tampered or damaged save can never break the rules.
    public static func validate(_ s: GameState, content: GameContent) -> [String] {
        var problems: [String] = []
        if !s.resources.isValid { problems.append("resources must be finite and non-negative") }
        if s.population < 0 { problems.append("negative population") }
        if s.assignments.total > s.population { problems.append("more workers assigned than people") }
        if s.assignments.counts.values.contains(where: { $0 < 0 }) { problems.append("negative assignment") }
        if !s.simTime.isFinite || s.simTime < 0 { problems.append("invalid simTime") }
        if !s.lastUpdateWallTime.isFinite { problems.append("invalid lastUpdateWallTime") }
        if content.epoch(s.epochId) == nil { problems.append("unknown epoch \(s.epochId)") }
        var ids = Set<Int>()
        for b in s.buildings {
            if content.building(b.definitionId) == nil { problems.append("unknown building \(b.definitionId)") }
            if !ids.insert(b.id).inserted { problems.append("duplicate building id \(b.id)") }
            if b.id >= s.nextBuildingId { problems.append("building id \(b.id) >= nextBuildingId") }
            if !b.remainingWork.isFinite { problems.append("invalid construction progress") }
        }
        for t in s.researched where content.technology(t) == nil { problems.append("unknown technology \(t)") }
        if let r = s.activeResearch, content.technology(r.techId) == nil { problems.append("unknown research \(r.techId)") }
        for r in s.exploredRegions where content.region(r) == nil { problems.append("unknown region \(r)") }
        if let e = s.pendingEventId, content.event(e) == nil { problems.append("unknown event \(e)") }
        return problems
    }
}

/// Stores the save as JSON in a directory (the app passes its Application Support folder).
/// Writes atomically so a crash mid-save never leaves a half-written file.
public final class FileSaveStore: SaveStore {
    public let fileURL: URL

    public init(directory: URL, fileName: String = "founders_save.json") {
        self.fileURL = directory.appendingPathComponent(fileName)
    }

    public func load(content: GameContent) -> LoadOutcome {
        guard FileManager.default.fileExists(atPath: fileURL.path) else { return .noSave }
        guard let data = try? Data(contentsOf: fileURL) else { return .corrupt(reason: "Save file cannot be read.") }
        let outcome = SaveCodec.decode(data, content: content)
        if case .corrupt = outcome { quarantine() }
        return outcome
    }

    public func save(_ state: GameState, content: GameContent, wallTime: Double) throws {
        let data = try SaveCodec.encode(state, content: content, wallTime: wallTime)
        try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: fileURL, options: .atomic)
    }

    public func reset() throws {
        if FileManager.default.fileExists(atPath: fileURL.path) {
            try FileManager.default.removeItem(at: fileURL)
        }
    }

    /// Keeps a copy of a corrupt save next to the original for diagnostics.
    private func quarantine() {
        let backup = fileURL.appendingPathExtension("corrupt")
        try? FileManager.default.removeItem(at: backup)
        try? FileManager.default.moveItem(at: fileURL, to: backup)
    }
}

/// In-memory store for tests.
public final class MemorySaveStore: SaveStore {
    public var data: Data?
    public init(data: Data? = nil) { self.data = data }

    public func load(content: GameContent) -> LoadOutcome {
        guard let data else { return .noSave }
        return SaveCodec.decode(data, content: content)
    }

    public func save(_ state: GameState, content: GameContent, wallTime: Double) throws {
        data = try SaveCodec.encode(state, content: content, wallTime: wallTime)
    }

    public func reset() throws { data = nil }
}
