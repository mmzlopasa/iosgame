import Foundation

extension Game {
    public func startExpedition(regionId: String) -> Result<Void, GameError> {
        update()
        guard let region = content.region(regionId) else { return .failure(.unknownContent(regionId)) }
        if state.exploredRegions.contains(regionId) { return .failure(.regionAlreadyExplored) }
        if state.activeExpedition != nil { return .failure(.expeditionInProgress) }
        guard state.resources.covers(region.cost) else {
            return .failure(.unaffordable(state.resources.shortfall(for: region.cost)))
        }
        state.resources = state.resources - region.cost
        state.activeExpedition = ActiveExpedition(regionId: regionId, remainingSeconds: region.expeditionSeconds,
                                                  totalSeconds: region.expeditionSeconds)
        notices.append(GameNotice(.info, "Scouts set out for \(region.name)."))
        return .success(())
    }

    func advanceExpedition(_ dt: Double) {
        guard var exp = state.activeExpedition else { return }
        exp.remainingSeconds -= dt
        if exp.remainingSeconds > 1e-9 {
            state.activeExpedition = exp
            return
        }
        state.activeExpedition = nil
        guard !state.exploredRegions.contains(exp.regionId), let region = content.region(exp.regionId) else { return }
        state.exploredRegions.append(exp.regionId)
        notices.append(GameNotice(.success, "Scouts returned: \(region.name) explored."))
        addLegacy(kind: "exploration", title: "Explored \(region.name)", detail: region.legacyText, refId: region.id)
    }
}
