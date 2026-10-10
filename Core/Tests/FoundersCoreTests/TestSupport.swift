import Foundation
import XCTest
@testable import FoundersCore

enum Fixtures {
    static let content: GameContent = {
        do { return try GameContent.loadBundled() } catch { fatalError("Bundled content failed: \(error)") }
    }()

    static func newGame(_ configure: ((inout GameContent) -> Void)? = nil) -> (Game, ManualClock) {
        var c = content
        configure?(&c)
        let clock = ManualClock()
        return (Game.newGame(content: c, clock: clock), clock)
    }
}

extension Game {
    /// Test helper: give resources directly.
    func testGrant(_ r: ResourceBundle) { state.resources = state.resources + r }
    /// Test helper: mark technologies researched.
    func testResearch(_ ids: [String]) { state.researched += ids }
    /// Test helper: run time forward through the clock (the real path the app uses).
    func run(_ seconds: Double, clock: ManualClock) {
        clock.advance(seconds)
        update()
    }
}

func XCTAssertSuccess<T>(_ result: Result<T, GameError>, file: StaticString = #filePath, line: UInt = #line) {
    if case .failure(let e) = result { XCTFail("Expected success, got \(e)", file: file, line: line) }
}

func XCTAssertFailure<T>(_ result: Result<T, GameError>, _ expected: GameError, file: StaticString = #filePath, line: UInt = #line) {
    switch result {
    case .success: XCTFail("Expected failure \(expected), got success", file: file, line: line)
    case .failure(let e): XCTAssertEqual(e, expected, file: file, line: line)
    }
}
