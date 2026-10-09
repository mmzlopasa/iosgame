import XCTest
@testable import EonwardCore

final class PlacementTests: XCTestCase {
    func testValidPlacementDeductsCostExactlyOnce() {
        let (game, _) = Fixtures.newGame()
        let woodBefore = game.state.resources.wood
        XCTAssertEqual(game.placementIssues(buildingId: "foraging_camp", x: 4, y: 5), [])
        XCTAssertSuccess(game.placeBuilding("foraging_camp", x: 4, y: 5))
        XCTAssertEqual(game.state.resources.wood, woodBefore - 10, accuracy: 1e-9)
        // Same spot again: rejected and nothing is charged.
        XCTAssertFailure(game.placeBuilding("foraging_camp", x: 4, y: 5), .placement(.overlaps))
        XCTAssertEqual(game.state.resources.wood, woodBefore - 10, accuracy: 1e-9)
        XCTAssertEqual(game.count(of: "foraging_camp", includeUnfinished: true), 1)
    }

    func testOverlapWithStartingBuildingRejected() {
        let (game, _) = Fixtures.newGame()
        XCTAssertTrue(game.placementIssues(buildingId: "shelter", x: 6, y: 7).contains(.overlaps))
    }

    func testMultiCellOverlapRejected() {
        let (game, _) = Fixtures.newGame()
        game.testGrant(ResourceBundle(wood: 50, stone: 50))
        // 2x2 footprint whose corner touches the campfire at (6,7).
        XCTAssertTrue(game.placementIssues(buildingId: "community_circle", x: 5, y: 7).contains(.overlaps))
    }

    func testInvalidTerrainRejected() {
        let (game, _) = Fixtures.newGame()
        // (10,5) is river water, (5,7) is rock.
        XCTAssertEqual(game.placementIssues(buildingId: "shelter", x: 10, y: 5), [.invalidTerrain(.water)])
        XCTAssertEqual(game.placementIssues(buildingId: "shelter", x: 5, y: 7), [.invalidTerrain(.rock)])
    }

    func testUnexploredAndOutOfBoundsRejected() {
        let (game, _) = Fixtures.newGame()
        XCTAssertTrue(game.placementIssues(buildingId: "shelter", x: 13, y: 8).contains(.unexplored))
        XCTAssertTrue(game.placementIssues(buildingId: "shelter", x: -1, y: 0).contains(.outOfBounds))
        XCTAssertTrue(game.placementIssues(buildingId: "shelter", x: 16, y: 3).contains(.outOfBounds))
    }

    func testAdjacencyRules() {
        let (game, _) = Fixtures.newGame()
        // (6,8) touches rock at (5,8); (8,10) has no rock nearby.
        XCTAssertEqual(game.placementIssues(buildingId: "stone_site", x: 6, y: 8), [])
        XCTAssertEqual(game.placementIssues(buildingId: "stone_site", x: 8, y: 10), [.needsAdjacent(.rock)])
        // (6,5) touches forest at (6,4).
        XCTAssertEqual(game.placementIssues(buildingId: "wood_camp", x: 6, y: 5), [])
    }

    func testUnaffordableRejectedWithoutCharge() {
        let (game, _) = Fixtures.newGame()
        let before = game.state.resources
        let result = game.placeBuilding("community_circle", x: 3, y: 9)
        guard case .failure(.placement(.unaffordable(let missing))) = result else {
            return XCTFail("Expected unaffordable, got \(result)")
        }
        XCTAssertEqual(missing.wood, 5, accuracy: 1e-9)
        XCTAssertEqual(missing.stone, 15, accuracy: 1e-9)
        XCTAssertEqual(game.state.resources, before)
    }

    func testLockedAndNonBuildable() {
        let (game, _) = Fixtures.newGame()
        XCTAssertTrue(game.placementIssues(buildingId: "campfire", x: 4, y: 4).contains(.notBuildable))
        XCTAssertTrue(game.placementIssues(buildingId: "woven_shelter", x: 4, y: 4).contains(.lockedByTech("Structural Weaving")))
    }

    func testConstructionQueueLimit() {
        let (game, _) = Fixtures.newGame()
        game.testGrant(ResourceBundle(wood: 100))
        XCTAssertSuccess(game.placeBuilding("shelter", x: 4, y: 4))
        XCTAssertSuccess(game.placeBuilding("shelter", x: 4, y: 5))
        let wood = game.state.resources.wood
        XCTAssertFailure(game.placeBuilding("shelter", x: 4, y: 6), .placement(.constructionQueueFull(2)))
        XCTAssertEqual(game.state.resources.wood, wood)
    }

    func testMaxCountLimit() {
        let (game, _) = Fixtures.newGame()
        game.testGrant(ResourceBundle(wood: 100, stone: 100))
        XCTAssertSuccess(game.placeBuilding("community_circle", x: 3, y: 9))
        XCTAssertTrue(game.placementIssues(buildingId: "community_circle", x: 3, y: 3).contains(.limitReached(1)))
    }
}
