import XCTest
@testable import FoundersCore

/// A simple scripted player that uses only public commands. It proves the first
/// session can be completed and reports how long it takes (balance smoke test).
final class PlaythroughTests: XCTestCase {
    private let buildOrder = ["foraging_camp", "stone_site", "shelter", "storage_pit", "wood_camp",
                              "community_circle", "shelter", "toolmaker", "shelter"]

    func testScriptedPlayerReachesStableVillage() {
        let (game, clock) = Fixtures.newGame()
        var nextBuild = 0
        var reachedAt: Double?

        for tick in 0..<(60 * 60 / 5) { // up to 60 minutes, decisions every 5 s
            if let ev = game.pendingEvent {
                let choice = ev.options.first { game.optionBlocker($0) == nil && $0.id == "open_stores" }
                    ?? ev.options.first { game.optionBlocker($0) == nil }!
                XCTAssertSuccess(game.resolveEvent(optionId: choice.id))
            }
            if game.canClaimMilestone("stable_village") {
                XCTAssertSuccess(game.claimMilestone("stable_village"))
                reachedAt = game.state.simTime
                break
            }
            if game.state.activeResearch == nil,
               let tech = game.content.technologies.first(where: { game.researchStatus($0.id) == .available
                   && game.state.resources.covers($0.cost) }) {
                XCTAssertSuccess(game.startResearch(tech.id))
            }
            if nextBuild < buildOrder.count, let cell = firstValidCell(game, buildOrder[nextBuild]) {
                XCTAssertSuccess(game.placeBuilding(buildOrder[nextBuild], x: cell.0, y: cell.1))
                nextBuild += 1
            }
            if game.isUnlocked(game.content.building("woven_shelter")!),
               let shelter = game.state.buildings.first(where: { $0.definitionId == "shelter" && $0.status == .complete }),
               game.state.resources.covers(game.content.building("woven_shelter")!.cost + ResourceBundle(wood: 30, stone: 20)) {
                _ = game.upgradeBuilding(id: shelter.id)
            }
            assignWorkers(game)
            _ = tick
            game.run(5, clock: clock)
        }

        guard let t = reachedAt else {
            let unmet = game.milestoneRequirements("stable_village").filter { !$0.met }.map(\.text)
            return XCTFail("Village not reached in 60 min. Unmet: \(unmet). State: \(game.state.resources) pop \(game.state.population)")
        }
        print("PLAYTHROUGH: stable village reached after \(Int(t / 60)) min \(Int(t) % 60) s of game time; objectives \(game.state.completedObjectives.count)/\(game.content.objectives.count)")
        XCTAssertEqual(game.state.stageId, "village")
        XCTAssertEqual(game.state.completedObjectives.count, game.content.objectives.count)
        XCTAssertLessThan(t, 45 * 60, "first session should not take longer than ~45 min")
        XCTAssertGreaterThan(t, 5 * 60, "first session should not be trivially short")
    }

    private func firstValidCell(_ game: Game, _ id: String) -> (Int, Int)? {
        for y in 0..<game.content.map.height {
            for x in 0..<game.content.map.width where game.placementIssues(buildingId: id, x: x, y: y).isEmpty {
                return (x, y)
            }
        }
        return nil
    }

    private func assignWorkers(_ game: Game) {
        for job in JobType.allCases { _ = game.setWorkers(job, to: 0) }
        func fill(_ job: JobType, _ wanted: Int) {
            let n = min(wanted, game.jobCapacity(job), game.idleWorkers + game.state.assignments[job])
            _ = game.setWorkers(job, to: n)
        }
        // Feed everyone first, then research, stone, wood, then help building.
        var foragers = 0
        while foragers < game.jobCapacity(.foraging), game.idleWorkers > 0, game.economy().net.food <= 0.05 {
            foragers += 1
            _ = game.setWorkers(.foraging, to: foragers)
        }
        fill(.research, 2)
        fill(.stonecutting, 2)
        fill(.woodcutting, 3)
        fill(.research, 4)
        fill(.construction, game.idleWorkers)
    }
}
