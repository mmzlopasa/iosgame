import XCTest
@testable import EonwardCore

final class EconomyTests: XCTestCase {
    func testStartingState() {
        let (game, _) = Fixtures.newGame()
        XCTAssertEqual(game.state.population, 6)
        XCTAssertEqual(game.housingCapacity(), 6)
        XCTAssertEqual(game.state.buildings.count, 4)
        XCTAssertEqual(game.idleWorkers, 3)
        XCTAssertEqual(game.state.legacy.first?.kind, "founding")
    }

    func testWoodProductionIsExactOverControlledDuration() {
        let (game, clock) = Fixtures.newGame()
        // 1 wood gatherer at 0.10 wood/s, wood has no consumption.
        game.run(60, clock: clock)
        XCTAssertEqual(game.state.resources.wood, 25 + 6, accuracy: 1e-6)
    }

    func testKnowledgeFromResearchers() {
        let (game, clock) = Fixtures.newGame()
        XCTAssertSuccess(game.setWorkers(.research, to: 1))
        // Using a third job completes "obj_jobs" (+4 knowledge reward).
        XCTAssertEqual(game.state.resources.knowledge, 4, accuracy: 1e-6)
        game.run(100, clock: clock)
        XCTAssertEqual(game.state.resources.knowledge, 4 + 6, accuracy: 1e-6)
    }

    func testJobReassignmentChangesRatesImmediately() {
        let (game, _) = Fixtures.newGame()
        let before = game.economy().production.wood
        XCTAssertSuccess(game.setWorkers(.woodcutting, to: 0))
        XCTAssertEqual(game.economy().production.wood, 0)
        XCTAssertEqual(before, 0.1, accuracy: 1e-9)
        XCTAssertSuccess(game.setWorkers(.foraging, to: 2))
        XCTAssertEqual(game.economy().production.food, 0.24, accuracy: 1e-9)
    }

    func testJobCapacityAndIdleLimits() {
        let (game, _) = Fixtures.newGame()
        // Campfire offers only 2 foraging slots.
        XCTAssertFailure(game.setWorkers(.foraging, to: 3), .noJobSlots(capacity: 2))
        // Construction has unlimited slots but only 3 people are idle.
        XCTAssertFailure(game.setWorkers(.construction, to: 4), .tooManyWorkers(available: 3))
        XCTAssertSuccess(game.setWorkers(.construction, to: 3))
        XCTAssertEqual(game.idleWorkers, 0)
    }

    func testStorageCapsProduction() {
        let (game, clock) = Fixtures.newGame()
        game.run(10_000, clock: clock)
        XCTAssertLessThanOrEqual(game.state.resources.wood, game.storageCapacity().wood + 1e-9)
        XCTAssertEqual(game.state.resources.wood, 120, accuracy: 1e-6)
    }

    func testFoodShortageWarnsButNobodyDies() {
        let (game, clock) = Fixtures.newGame()
        XCTAssertSuccess(game.setWorkers(.foraging, to: 0))
        game.run(4 * 3600, clock: clock)
        XCTAssertEqual(game.state.resources.food, 0, accuracy: 1e-9)
        XCTAssertTrue(game.state.foodShortage)
        XCTAssertEqual(game.state.population, 6)
        XCTAssertTrue(game.drainNotices().contains { $0.kind == .warning })
        // Shortage slows work.
        XCTAssertEqual(game.ratePerWorker(.woodcutting), 0.1 * 0.6, accuracy: 1e-9)
    }

    func testPopulationGrowsWhenHousedAndFed() {
        let (game, clock) = Fixtures.newGame()
        game.testGrant(ResourceBundle(food: 80, wood: 20))
        XCTAssertSuccess(game.placeBuilding("shelter", x: 4, y: 5))
        game.run(200, clock: clock)
        XCTAssertEqual(game.housingCapacity(), 8)
        XCTAssertGreaterThan(game.state.population, 6)
        XCTAssertLessThanOrEqual(game.state.population, 8)
    }

    func testCraftingBoostsGatheringAndConsumesMaterials() {
        let (game, _) = Fixtures.newGame()
        game.testGrant(ResourceBundle(wood: 50, stone: 50))
        game.state.buildings.append(PlacedBuilding(id: 99, definitionId: "toolmaker", x: 4, y: 4, status: .complete,
                                                   remainingWork: 0, totalWork: 1, upgradeTargetId: nil,
                                                   history: ["toolmaker"], foundedAtSimTime: 0))
        XCTAssertSuccess(game.setWorkers(.crafting, to: 2))
        let snap = game.economy()
        XCTAssertTrue(snap.craftingActive)
        XCTAssertEqual(snap.craftingBonus, 0.3, accuracy: 1e-9)
        XCTAssertEqual(snap.production.wood, 0.1 * 1.3, accuracy: 1e-9)
        XCTAssertEqual(snap.consumption.stone, 0.04, accuracy: 1e-9)
    }
}
