import Foundation

public enum ResearchStatus: Equatable, Sendable {
    case researched
    case inProgress(progress: Double)
    case available
    case blocked(missing: [String])
}

extension Game {
    public func researchStatus(_ techId: String) -> ResearchStatus {
        if state.researched.contains(techId) { return .researched }
        if let active = state.activeResearch, active.techId == techId {
            let p = active.totalSeconds > 0 ? 1 - active.remainingSeconds / active.totalSeconds : 1
            return .inProgress(progress: min(1, max(0, p)))
        }
        let missing = missingPrerequisites(techId)
        return missing.isEmpty ? .available : .blocked(missing: missing)
    }

    /// Names of prerequisites that are not researched yet.
    public func missingPrerequisites(_ techId: String) -> [String] {
        guard let tech = content.technology(techId) else { return [] }
        return tech.prerequisites
            .filter { !state.researched.contains($0) }
            .map { content.technology($0)?.name ?? $0 }
    }

    /// Starts research. Validates prerequisites, duplicates and cost before spending.
    @discardableResult
    public func startResearch(_ techId: String) -> Result<Void, GameError> {
        update()
        guard let tech = content.technology(techId) else { return .failure(.unknownContent(techId)) }
        if state.researched.contains(techId) { return .failure(.researchAlreadyDone) }
        if state.activeResearch != nil { return .failure(.researchInProgress) }
        let missing = missingPrerequisites(techId)
        if !missing.isEmpty { return .failure(.missingPrerequisites(missing)) }
        guard state.resources.covers(tech.cost) else {
            return .failure(.unaffordable(state.resources.shortfall(for: tech.cost)))
        }
        state.resources = state.resources - tech.cost
        state.activeResearch = ActiveResearch(techId: techId, remainingSeconds: tech.researchSeconds,
                                              totalSeconds: tech.researchSeconds)
        notices.append(GameNotice(.info, "Research started: \(tech.name)."))
        return .success(())
    }

    func advanceResearch(_ dt: Double) {
        guard var active = state.activeResearch else { return }
        active.remainingSeconds -= dt
        if active.remainingSeconds > 1e-9 {
            state.activeResearch = active
            return
        }
        state.activeResearch = nil
        guard !state.researched.contains(active.techId) else { return }
        state.researched.append(active.techId)
        let tech = content.technology(active.techId)
        let name = tech?.name ?? active.techId
        notices.append(GameNotice(.success, "Research complete: \(name)!"))
        addLegacy(kind: "technology", title: "Discovered \(name)", detail: tech?.description ?? "", refId: active.techId)
    }
}
