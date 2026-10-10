import XCTest
@testable import FoundersCore

final class ContentTests: XCTestCase {
    func testBundledContentIsValid() {
        XCTAssertEqual(ContentValidator.validate(Fixtures.content), [])
        XCTAssertEqual(Fixtures.content.buildings.filter(\.buildable).count, 7)
        XCTAssertEqual(Fixtures.content.technologies.count, 5)
    }

    func testDetectsPrerequisiteCycle() {
        var c = Fixtures.content
        if let i = c.technologies.firstIndex(where: { $0.id == "improved_stone_tools" }) {
            c.technologies[i].prerequisites = ["permanent_settlement"]
        }
        let problems = ContentValidator.validate(c)
        XCTAssertTrue(problems.contains { $0.contains("cycle") }, "\(problems)")
    }

    func testDetectsUnknownReferencesAndBadMap() {
        var c = Fixtures.content
        c.technologies[0].prerequisites = ["does_not_exist"]
        c.map.rows[0] = "TTT"
        let problems = ContentValidator.validate(c)
        XCTAssertTrue(problems.contains { $0.contains("does_not_exist") })
        XCTAssertTrue(problems.contains { $0.contains("map row 0") })
    }

    func testLoadRejectsInvalidContent() throws {
        var c = Fixtures.content
        c.buildings.append(c.buildings[0]) // duplicate id
        let data = try JSONEncoder().encode(c)
        XCTAssertThrowsError(try GameContent.load(from: data))
    }
}
