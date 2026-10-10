import SwiftUI
import UIKit
import FoundersCore

enum Panel: String, Identifiable {
    case build, people, research, objectives, map, settings
    var id: String { rawValue }
}

struct GridCell: Equatable {
    var x: Int
    var y: Int
}

struct Toast: Identifiable, Equatable {
    let id = UUID()
    let notice: GameNotice
    let createdAt: Date
}

/// Presentation-side owner of the running game: drives the clock, saves,
/// translates notices into toasts/haptics and forwards UI commands to `Game`.
/// It holds no game rules of its own.
@MainActor
final class GameStore: ObservableObject {
    @Published private(set) var tick = 0
    @Published var activePanel: Panel?
    @Published var selectedBuildingId: Int?
    @Published private(set) var placingBuildingId: String?
    @Published private(set) var ghostCell: GridCell?
    @Published private(set) var toasts: [Toast] = []
    @Published var eventMinimized = false
    @Published var celebration: String?

    let content: GameContent
    private(set) var game: Game
    let saveStore: FileSaveStore
    let scene: SettlementScene
    private let clock: SystemClock
    private var timer: Timer?
    private var lastSaveWallTime: Double = 0
    private(set) var saveStatus = "Not saved yet"
    private(set) var startupMessage: String?

    init() {
        let loadedContent: GameContent
        do {
            loadedContent = try GameContent.loadBundled()
        } catch {
            fatalError("FOUNDERS content failed to load: \(error)")
        }
        let clock = SystemClock()
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
        let saveStore = FileSaveStore(directory: support.appendingPathComponent("Founders", isDirectory: true))

        var message: String?
        let game: Game
        switch saveStore.load(content: loadedContent) {
        case .loaded(let state):
            game = Game(content: loadedContent, state: state, clock: clock)
        case .noSave:
            game = Game.newGame(content: loadedContent, clock: clock)
        case .corrupt(let reason):
            print("[FOUNDERS] Corrupt save moved aside: \(reason)")
            message = "Your previous save could not be read, so a new camp was started. A backup was kept."
            game = Game.newGame(content: loadedContent, clock: clock)
        case .unsupportedVersion(let version):
            print("[FOUNDERS] Save version \(version) is newer than this build supports.")
            message = "Your save is from a newer version of FOUNDERS (v\(version)). Started a temporary new camp; the save was not changed."
            game = Game.newGame(content: loadedContent, clock: clock)
        }

        self.content = loadedContent
        self.clock = clock
        self.saveStore = saveStore
        self.game = game
        self.scene = SettlementScene(size: CGSize(width: 390, height: 844))
        self.startupMessage = message

        scene.store = self
        game.update() // simulate time spent away since the last save (capped, never duplicated)
        if let message { toasts.append(Toast(notice: GameNotice(.warning, message), createdAt: Date())) }
        absorbNotices()
        save()
        startTimer()
    }

    // MARK: - Clock

    private func startTimer() {
        guard timer == nil else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.step() }
        }
    }

    private func stopTimer() {
        timer?.invalidate()
        timer = nil
    }

    private func step() {
        game.update()
        refresh()
        if clock.now() - lastSaveWallTime > 15 { save() }
    }

    /// Pulls notices, expires toasts, redraws.
    private func refresh() {
        absorbNotices()
        let now = Date()
        if toasts.contains(where: { now.timeIntervalSince($0.createdAt) > 4 }) {
            toasts.removeAll { now.timeIntervalSince($0.createdAt) > 4 }
        }
        tick &+= 1
        scene.sync()
    }

    private func absorbNotices() {
        let notices = game.drainNotices()
        guard !notices.isEmpty else { return }
        for n in notices {
            toasts.append(Toast(notice: n, createdAt: Date()))
            switch n.kind {
            case .success: Haptics.success()
            case .warning: Haptics.warning()
            case .milestone:
                Haptics.success()
                celebration = n.message
            case .info: break
            }
            if n.message.hasPrefix("A decision awaits") { eventMinimized = false }
        }
        if toasts.count > 3 { toasts = Array(toasts.suffix(3)) }
    }

    func handleScenePhase(_ phase: ScenePhase) {
        switch phase {
        case .active:
            game.update()
            startTimer()
            refresh()
        case .inactive, .background:
            game.update()
            save()
            stopTimer()
        @unknown default:
            break
        }
    }

    // MARK: - Saving

    func save() {
        do {
            try saveStore.save(game.state, content: content, wallTime: clock.now())
            lastSaveWallTime = clock.now()
            saveStatus = "Saved \(Date().formatted(date: .omitted, time: .standard))"
        } catch {
            saveStatus = "Save failed: \(error.localizedDescription)"
            print("[FOUNDERS] Save failed: \(error)")
        }
    }

    func resetPrototype() {
        do { try saveStore.reset() } catch { print("[FOUNDERS] Reset failed: \(error)") }
        game = Game.newGame(content: content, clock: clock)
        placingBuildingId = nil
        ghostCell = nil
        selectedBuildingId = nil
        celebration = nil
        toasts.removeAll()
        activePanel = nil
        scene.rebuildAll()
        save()
        refresh()
    }

    // MARK: - Commands

    /// Runs a game command; failures become a warning toast. Returns true on success.
    @discardableResult
    func perform<T>(_ command: () -> Result<T, GameError>) -> Bool {
        let result = command()
        var ok = true
        if case .failure(let error) = result {
            ok = false
            toasts.append(Toast(notice: GameNotice(.warning, error.description), createdAt: Date()))
            if toasts.count > 3 { toasts = Array(toasts.suffix(3)) }
            Haptics.error()
        }
        refresh()
        save()
        return ok
    }

    func setWorkers(_ job: JobType, to count: Int) {
        perform { game.setWorkers(job, to: count) }
    }

    func startResearch(_ id: String) { perform { game.startResearch(id) } }

    func upgrade(_ buildingId: Int) {
        if perform({ game.upgradeBuilding(id: buildingId) }) { Haptics.light() }
    }

    func resolveEvent(_ optionId: String) { perform { game.resolveEvent(optionId: optionId) } }

    func explore(_ regionId: String) { perform { game.startExpedition(regionId: regionId) } }

    func claimMilestone(_ id: String) { perform { game.claimMilestone(id) } }

    func debugFastForward(minutes: Double) {
        game.debugFastForward(seconds: minutes * 60)
        refresh()
        save()
    }

    func debugGrant() {
        game.debugGrant(ResourceBundle(food: 50, wood: 50, stone: 50, knowledge: 50))
        refresh()
        save()
    }

    // MARK: - Placement

    var placementIssues: [PlacementIssue] {
        guard let id = placingBuildingId, let cell = ghostCell else { return [] }
        return game.placementIssues(buildingId: id, x: cell.x, y: cell.y)
    }

    func beginPlacement(_ buildingId: String) {
        activePanel = nil
        selectedBuildingId = nil
        placingBuildingId = buildingId
        ghostCell = suggestedCell(for: buildingId)
        if let cell = ghostCell { scene.focus(onCellX: cell.x, y: cell.y) }
        refresh()
    }

    func cancelPlacement() {
        placingBuildingId = nil
        ghostCell = nil
        refresh()
    }

    func confirmPlacement() {
        guard let id = placingBuildingId, let cell = ghostCell else { return }
        let placed = perform { game.placeBuilding(id, x: cell.x, y: cell.y) }
        if placed {
            Haptics.light()
            placingBuildingId = nil
            ghostCell = nil
            refresh()
        }
    }

    /// Nearest valid cell to the camp centre, so the ghost starts somewhere sensible.
    private func suggestedCell(for buildingId: String) -> GridCell? {
        let map = content.map
        let center = game.state.buildings.first.map { GridCell(x: $0.x, y: $0.y) } ?? GridCell(x: map.width / 2, y: map.height / 2)
        var best: GridCell?
        var bestDistance = Int.max
        for y in 0..<map.height {
            for x in 0..<map.width {
                let d = abs(x - center.x) + abs(y - center.y)
                guard d < bestDistance else { continue }
                let issues = game.placementIssues(buildingId: buildingId, x: x, y: y)
                    .filter { if case .unaffordable = $0 { return false }; return true }
                if issues.isEmpty {
                    best = GridCell(x: x, y: y)
                    bestDistance = d
                }
            }
        }
        return best ?? GridCell(x: center.x + 1, y: center.y + 1)
    }

    func handleTap(x: Int, y: Int) {
        if placingBuildingId != nil {
            ghostCell = GridCell(x: x, y: y)
        } else {
            let hit = game.buildingId(atX: x, y: y)
            selectedBuildingId = (hit == selectedBuildingId) ? nil : hit
            if hit != nil { Haptics.light() }
        }
        refresh()
    }
}

@MainActor
enum Haptics {
    static func light() { UIImpactFeedbackGenerator(style: .light).impactOccurred() }
    static func success() { UINotificationFeedbackGenerator().notificationOccurred(.success) }
    static func warning() { UINotificationFeedbackGenerator().notificationOccurred(.warning) }
    static func error() { UINotificationFeedbackGenerator().notificationOccurred(.error) }
}
