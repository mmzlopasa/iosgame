import Foundation

extension Game {
    public var pendingEvent: EventDefinition? {
        state.pendingEventId.flatMap { content.event($0) }
    }

    /// Nil when the option can be chosen, otherwise the player-facing reason.
    public func optionBlocker(_ option: EventOption) -> String? {
        if let req = option.requiresBuilding, count(of: req) == 0 {
            return "Requires a \(content.building(req)?.name ?? req)."
        }
        if let cost = option.cost, !state.resources.covers(cost) {
            return "Not enough resources. Missing: \(state.resources.shortfall(for: cost).shortDescription)"
        }
        return nil
    }

    /// Opens the first event whose trigger is met (one event at a time, each at most once).
    func checkEventTriggers() {
        guard state.pendingEventId == nil else { return }
        for ev in content.events where state.resolvedEvents[ev.id] == nil {
            if ev.trigger.allSatisfy({ isMet($0) }) {
                state.pendingEventId = ev.id
                notices.append(GameNotice(.warning, "A decision awaits: \(ev.title)"))
                return
            }
        }
    }

    /// Applies the chosen option's consequences exactly once.
    @discardableResult
    public func resolveEvent(optionId: String) -> Result<Void, GameError> {
        update()
        guard let ev = pendingEvent else { return .failure(.noPendingEvent) }
        guard let option = ev.options.first(where: { $0.id == optionId }) else {
            return .failure(.unknownContent(optionId))
        }
        if let blocker = optionBlocker(option) { return .failure(.optionUnavailable(blocker)) }

        if let cost = option.cost { state.resources = state.resources - cost }
        for effect in (ev.baseEffects ?? []) + option.effects { apply(effect, sourceId: ev.id) }
        state.resolvedEvents[ev.id] = option.id
        state.pendingEventId = nil
        addLegacy(kind: "decision", title: "\(ev.title): \(option.label)", detail: option.legacyText, refId: ev.id)
        notices.append(GameNotice(.success, "Decision made: \(option.label)."))
        evaluateProgress()
        return .success(())
    }

    func apply(_ effect: Effect, sourceId: String) {
        switch effect.kind {
        case .grantResources:
            let caps = storageCapacity()
            var r = state.resources + (effect.resources ?? .zero)
            for type in ResourceType.allCases { r[type] = min(caps[type], max(0, r[type])) }
            state.resources = r
        default:
            let expires = effect.durationSeconds.map { state.simTime + $0 }
            state.modifiers.append(ActiveModifier(sourceId: sourceId, effect: effect, expiresAtSimTime: expires))
        }
    }
}
