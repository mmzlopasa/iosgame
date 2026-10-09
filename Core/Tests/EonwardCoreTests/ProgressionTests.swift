import XCTest
@testable import EonwardCore

final class ConstructionTests: XCTestCase {
    func testConstructionCompletesOnceWithFeedback() {
        let (game, clock) = Fixtures.newGame()
        guard case .success(let id) = game.placeBuilding("foraging_camp", x: 4, y: 5) else { return XCTFail() }
        _ = game.drainNotices()
        game.run(14, clock: clock)
        XCTAssertEqual(game.state.building(withId: id)?.status, .constructing)
        game.run(1, clock: clock)
        XCTAssertEqual(game.state.building(withId: id)?.status, .complete)
        XCTAssertEqual(game.drainNotices().filter { $0.message.contains("Construction complete") }.count, 1)
        game.run(100, clock: clock)
        XCTAssertTrue(game.drainNotices().filter { $0.message.contains("Construction complete") }.isEmpty)
        XCTAssertEqual(game.jobCapacity(.foraging), 5)
    }

    func testBuildersSpeedUpConstruction() {
        let (game, clock) = Fixtures.newGame()
        XCTAssertSuccess(game.setWorkers(.construction, to: 2)) // speed 1 + 2*0.5 = 2
        guard case .success(let id) = game.placeBuilding("shelter", x: 4, y: 5) else { return XCTFail() }
        game.run(10, clock: clock)
        XCTAssertEqual(game.state.building(withId: id)?.status, .complete)
    }

    func testUpgradeKeepsIdentity() {
        let (game, clock) = Fixtures.newGame()
        game.testGrant(ResourceBundle(wood: 50, stone: 50))
        let shelter = game.state.buildings.first { $0.definitionId == "shelter" }!
        XCTAssertFailure(game.upgradeBuilding(id: shelter.id), .lockedByTech("Structural Weaving"))
        game.testResearch(["improved_stone_tools", "structural_weaving"])
        XCTAssertSuccess(game.upgradeBuilding(id: shelter.id))
        game.run(30, clock: clock)
        let upgraded = game.state.building(withId: shelter.id)!
        XCTAssertEqual(upgraded.definitionId, "woven_shelter")
        XCTAssertEqual(upgraded.x, shelter.x)
        XCTAssertEqual(upgraded.history, ["shelter", "woven_shelter"])
        XCTAssertEqual(game.housingCapacity(), 8)
    }
}

final class ResumeTests: XCTestCase {
    func testRepeatedUpdatesNeverDuplicateProduction() {
        let (game, clock) = Fixtures.newGame()
        clock.advance(60)
        game.update()
        let wood = game.state.resources.wood
        // Simulated foreground/background cycles without time passing.
        game.update(); game.update(); game.update()
        XCTAssertEqual(game.state.resources.wood, wood)
    }

    func testClockGoingBackwardsGrantsNothing() {
        let (game, clock) = Fixtures.newGame()
        clock.advance(-3600)
        game.update()
        XCTAssertEqual(game.state.resources.wood, 25)
        clock.advance(3600) // back to the original time: only 1h of real progress since re-anchoring
        game.update()
        XCTAssertEqual(game.state.resources.wood, min(120, 25 + 360), accuracy: 1e-6)
    }

    func testOfflineProgressIsCapped() {
        let (game, clock) = Fixtures.newGame()
        clock.advance(10 * 24 * 3600)
        game.update()
        XCTAssertEqual(game.state.simTime, Fixtures.content.balance.maxOfflineSeconds, accuracy: 1e-6)
    }
}

final class ResearchTests: XCTestCase {
    func testPrerequisitesEnforced() {
        let (game, _) = Fixtures.newGame()
        game.testGrant(ResourceBundle(knowledge: 100))
        XCTAssertFailure(game.startResearch("structural_weaving"), .missingPrerequisites(["Improved Stone Tools"]))
        XCTAssertEqual(game.state.resources.knowledge, 100)
    }

    func testResearchCostAndCannotBuyTwice() {
        let (game, clock) = Fixtures.newGame()
        guard case .failure(.unaffordable) = game.startResearch("improved_stone_tools") else { return XCTFail() }
        game.testGrant(ResourceBundle(knowledge: 50))
        XCTAssertSuccess(game.startResearch("improved_stone_tools"))
        XCTAssertEqual(game.state.resources.knowledge, 38, accuracy: 1e-9)
        XCTAssertFailure(game.startResearch("improved_stone_tools"), .researchInProgress)
        XCTAssertFailure(game.startResearch("food_preservation"), .researchInProgress)
        game.run(30, clock: clock)
        XCTAssertEqual(game.researchStatus("improved_stone_tools"), .researched)
        XCTAssertFailure(game.startResearch("improved_stone_tools"), .researchAlreadyDone)
        XCTAssertEqual(game.ratePerWorker(.woodcutting), 0.125, accuracy: 1e-9)
        // Second tech now possible.
        XCTAssertSuccess(game.startResearch("structural_weaving"))
    }
}

final class EventAndMilestoneTests: XCTestCase {
    /// Makes the first research objective complete so the poor season event opens.
    private func gameWithEvent() -> (Game, ManualClock) {
        let (game, clock) = Fixtures.newGame()
        game.testGrant(ResourceBundle(knowledge: 12))
        XCTAssertSuccess(game.startResearch("food_preservation"))
        game.run(30, clock: clock)
        return (game, clock)
    }

    func testEventTriggersAfterFirstResearch() {
        let (game, _) = gameWithEvent()
        XCTAssertEqual(game.pendingEvent?.id, "poor_foraging_season")
    }

    func testStorageOptionRequiresPreparation() {
        let (game, _) = gameWithEvent()
        let option = game.pendingEvent!.options.first { $0.id == "open_stores" }!
        XCTAssertNotNil(game.optionBlocker(option))
        guard case .failure(.optionUnavailable) = game.resolveEvent(optionId: "open_stores") else { return XCTFail() }
        XCTAssertNotNil(game.pendingEvent)
    }

    func testForageHarderHasRealConsequences() {
        let (game, clock) = gameWithEvent()
        let woodRate = game.ratePerWorker(.woodcutting)
        XCTAssertSuccess(game.resolveEvent(optionId: "forage_harder"))
        XCTAssertNil(game.pendingEvent)
        XCTAssertEqual(game.ratePerWorker(.woodcutting), woodRate * 0.5, accuracy: 1e-9)
        XCTAssertEqual(game.ratePerWorker(.foraging), 0.12 * 0.7 * 1.6, accuracy: 1e-9)
        XCTAssertEqual(game.state.resolvedEvents["poor_foraging_season"], "forage_harder")
        // Cannot be resolved twice.
        XCTAssertFailure(game.resolveEvent(optionId: "ration"), .noPendingEvent)
        // Temporary effects expire.
        game.run(241, clock: clock)
        XCTAssertEqual(game.ratePerWorker(.woodcutting), woodRate, accuracy: 1e-9)
        XCTAssertTrue(game.state.legacy.contains { $0.kind == "decision" })
    }

    func testRationStopsGrowth() {
        let (game, _) = gameWithEvent()
        XCTAssertSuccess(game.resolveEvent(optionId: "ration"))
        XCTAssertEqual(game.multiplier(.growthMultiplier), 0)
        XCTAssertEqual(game.multiplier(.consumptionMultiplier), 0.75, accuracy: 1e-9)
    }

    func testMilestoneGatingAndTransform() {
        let (game, _) = gameWithEvent()
        XCTAssertFalse(game.canClaimMilestone("stable_village"))
        guard case .failure(.requirementsNotMet) = game.claimMilestone("stable_village") else { return XCTFail() }
        XCTAssertEqual(game.state.stageId, "camp")

        XCTAssertSuccess(game.resolveEvent(optionId: "ration"))
        game.testResearch(["improved_stone_tools", "structural_weaving", "permanent_settlement"])
        for (def, x, y) in [("community_circle", 3, 9), ("storage_pit", 4, 4), ("shelter", 4, 5)] {
            game.state.buildings.append(PlacedBuilding(id: game.state.nextBuildingId, definitionId: def, x: x, y: y,
                                                       status: .complete, remainingWork: 0, totalWork: 1,
                                                       upgradeTargetId: nil, history: [def], foundedAtSimTime: 0))
            game.state.nextBuildingId += 1
        }
        XCTAssertFalse(game.canClaimMilestone("stable_village"), "population is still 6")
        game.state.population = 8
        XCTAssertTrue(game.canClaimMilestone("stable_village"))

        let campfire = game.state.buildings.first { $0.definitionId == "campfire" }!
        XCTAssertSuccess(game.claimMilestone("stable_village"))
        XCTAssertEqual(game.state.stageId, "village")
        let hearth = game.state.building(withId: campfire.id)!
        XCTAssertEqual(hearth.definitionId, "village_hearth")
        XCTAssertEqual(hearth.history, ["campfire", "village_hearth"])
        XCTAssertTrue(game.state.completedObjectives.contains("obj_village"))
        XCTAssertFailure(game.claimMilestone("stable_village"), .milestoneAlreadyReached)
    }

    func testObjectiveRewardGrantedOnce() {
        let (game, clock) = Fixtures.newGame()
        XCTAssertSuccess(game.setWorkers(.research, to: 1))
        XCTAssertTrue(game.state.completedObjectives.contains("obj_jobs"))
        let knowledge = game.state.resources.knowledge
        XCTAssertEqual(knowledge, 4, accuracy: 1e-9)
        game.run(1, clock: clock)
        XCTAssertEqual(game.state.completedObjectives.filter { $0 == "obj_jobs" }.count, 1)
        XCTAssertEqual(game.state.resources.knowledge, 4.06, accuracy: 1e-9)
    }

    func testExplorationRevealsLandAndTriggersEvent() {
        let (game, clock) = Fixtures.newGame()
        XCTAssertFalse(game.isRevealed(x: 13, y: 8))
        XCTAssertSuccess(game.startExpedition(regionId: "east_ridge"))
        XCTAssertFailure(game.startExpedition(regionId: "north_forest"), .expeditionInProgress)
        game.run(45, clock: clock)
        XCTAssertTrue(game.isRevealed(x: 13, y: 8))
        XCTAssertEqual(game.pendingEvent?.id, "ridge_deposit")
        XCTAssertFailure(game.startExpedition(regionId: "east_ridge"), .regionAlreadyExplored)
    }
}
