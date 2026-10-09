import XCTest
@testable import EonwardCore

final class SaveTests: XCTestCase {
    func testRoundTripPreservesState() throws {
        let (game, clock) = Fixtures.newGame()
        XCTAssertSuccess(game.setWorkers(.research, to: 1))
        XCTAssertSuccess(game.placeBuilding("foraging_camp", x: 4, y: 5))
        game.run(5, clock: clock)
        let store = MemorySaveStore()
        try store.save(game.state, content: game.content, wallTime: clock.now())
        guard case .loaded(let loaded) = store.load(content: game.content) else { return XCTFail() }
        XCTAssertEqual(loaded, game.state)
    }

    func testRestartSimulatesOfflineTimeOnce() throws {
        let (game, clock) = Fixtures.newGame()
        let store = MemorySaveStore()
        try store.save(game.state, content: game.content, wallTime: clock.now())

        // App is killed; 60 s later it is relaunched from the save.
        clock.advance(60)
        guard case .loaded(let state) = store.load(content: game.content) else { return XCTFail() }
        let relaunched = Game(content: game.content, state: state, clock: clock)
        relaunched.update()
        XCTAssertEqual(relaunched.state.resources.wood, 31, accuracy: 1e-6)
        relaunched.update()
        XCTAssertEqual(relaunched.state.resources.wood, 31, accuracy: 1e-6)
    }

    func testFileStoreWritesAndReadsAndRecoversFromCorruption() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("eonward-tests-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir) }
        let store = FileSaveStore(directory: dir)
        let (game, clock) = Fixtures.newGame()
        XCTAssertEqual(store.load(content: game.content), .noSave)

        try store.save(game.state, content: game.content, wallTime: clock.now())
        guard case .loaded = store.load(content: game.content) else { return XCTFail() }

        try Data("{ not json".utf8).write(to: store.fileURL)
        guard case .corrupt = store.load(content: game.content) else { return XCTFail() }
        // Corrupt file was moved aside; next load starts clean.
        XCTAssertEqual(store.load(content: game.content), .noSave)
        XCTAssertTrue(FileManager.default.fileExists(atPath: store.fileURL.path + ".corrupt"))
    }

    func testNewerSaveVersionIsRefused() throws {
        let (game, clock) = Fixtures.newGame()
        var envelope = SaveEnvelope(saveVersion: 99, contentVersion: 1, savedAtWallTime: clock.now(), state: game.state)
        envelope.saveVersion = 99
        let data = try JSONEncoder().encode(envelope)
        XCTAssertEqual(SaveCodec.decode(data, content: game.content), .unsupportedVersion(99))
    }

    func testTamperedSaveIsRejected() throws {
        let (game, clock) = Fixtures.newGame()
        var bad = game.state
        bad.resources.wood = -50
        bad.assignments[.foraging] = 40
        let data = try SaveCodec.encode(bad, content: game.content, wallTime: clock.now())
        guard case .corrupt(let reason) = SaveCodec.decode(data, content: game.content) else { return XCTFail() }
        XCTAssertTrue(reason.contains("resources"))

        var unknown = game.state
        unknown.buildings[0].definitionId = "laser_tower"
        let data2 = try SaveCodec.encode(unknown, content: game.content, wallTime: clock.now())
        guard case .corrupt = SaveCodec.decode(data2, content: game.content) else { return XCTFail() }
    }

    func testMissingFieldsAreCorruptNotCrash() {
        let data = Data(#"{"saveVersion":1,"state":{}}"#.utf8)
        guard case .corrupt = SaveCodec.decode(data, content: Fixtures.content) else { return XCTFail() }
    }
}
