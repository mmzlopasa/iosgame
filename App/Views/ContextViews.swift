import SwiftUI
import FoundersCore

// MARK: - Placement

struct PlacementBar: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        if let id = store.placingBuildingId, let def = store.game.content.building(id) {
            let issues = store.placementIssues
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text(def.name).font(.headline)
                    Spacer()
                    Text(def.cost.shortDescription).font(.subheadline.monospacedDigit())
                    Text("· \(Fmt.duration(store.game.estimatedSeconds(work: def.buildSeconds)))")
                        .font(.subheadline).foregroundStyle(.secondary)
                }
                if let issue = issues.first {
                    Label(issue.message, systemImage: "xmark.octagon.fill")
                        .font(.footnote.weight(.semibold))
                        .foregroundStyle(Theme.bad)
                } else {
                    Label("Valid spot. Tap the ground to move, then confirm.", systemImage: "checkmark.seal.fill")
                        .font(.footnote.weight(.semibold))
                        .foregroundStyle(Theme.good)
                }
                HStack(spacing: 10) {
                    Button(role: .cancel) {
                        store.cancelPlacement()
                    } label: {
                        Text("Cancel").frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.bordered)
                    Button {
                        store.confirmPlacement()
                    } label: {
                        Text("Build here").bold().frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(Theme.accent)
                    .disabled(!issues.isEmpty)
                }
            }
            .padding(14)
            .background(Theme.card, in: RoundedRectangle(cornerRadius: 16))
            .padding(.horizontal, 12)
        }
    }
}

// MARK: - Selected building

struct BuildingDetailCard: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        if let id = store.selectedBuildingId, let b = store.game.state.building(withId: id),
           let def = store.game.content.building(b.definitionId) {
            let game = store.game
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text(def.name).font(.headline)
                    Spacer()
                    Button {
                        store.selectedBuildingId = nil
                    } label: {
                        Image(systemName: "xmark.circle.fill").font(.title3).foregroundStyle(.secondary)
                    }
                    .frame(width: 44, height: 44)
                }
                Text(def.description).font(.footnote).foregroundStyle(.secondary)

                if b.status != .complete {
                    let label = b.status == .upgrading ? "Upgrading" : "Under construction"
                    ProgressView(value: b.progress) {
                        Text("\(label) · \(Fmt.duration(game.estimatedSeconds(work: b.remainingWork))) left")
                            .font(.caption)
                    }
                    .tint(Theme.accent)
                }

                if def.housingValue > 0 { InfoRow(icon: "house.fill", text: "Houses \(def.housingValue) people") }
                if def.storageValue > 0 { InfoRow(icon: "shippingbox.fill", text: "+\(Int(def.storageValue)) storage") }
                if def.protectedFoodValue > 0 { InfoRow(icon: "lock.fill", text: "Protects \(Int(def.protectedFoodValue)) food from spoiling") }

                ForEach(def.jobSlots ?? [], id: \.job) { slot in
                    JobStepperRow(job: slot.job, compact: true)
                }

                if let upId = def.upgradesTo, let target = game.content.building(upId), b.status == .complete {
                    let locked = !game.isUnlocked(target)
                    Button {
                        store.upgrade(b.id)
                    } label: {
                        VStack(spacing: 2) {
                            Text("Upgrade to \(target.name)").bold()
                            Text(locked
                                 ? "Requires \(game.content.technology(target.requiresTech ?? "")?.name ?? "research")"
                                 : "\(target.cost.shortDescription) · \(Fmt.duration(game.estimatedSeconds(work: target.buildSeconds))) · houses \(target.housingValue)")
                                .font(.caption)
                        }
                        .frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(Theme.accent)
                    .disabled(locked || !game.state.resources.covers(target.cost))
                }
                if let successor = def.successorId, let next = game.content.building(successor) {
                    InfoRow(icon: "clock.arrow.circlepath", text: "Will become the \(next.name) when the settlement grows.")
                }
                if b.history.count > 1 {
                    InfoRow(icon: "scroll.fill", text: "History: " + b.history.compactMap { game.content.building($0)?.name }.joined(separator: " → "))
                }
            }
            .padding(14)
            .background(Theme.card, in: RoundedRectangle(cornerRadius: 16))
            .padding(.horizontal, 12)
        }
    }
}

struct InfoRow: View {
    let icon: String
    let text: String

    var body: some View {
        Label(text, systemImage: icon).font(.caption).foregroundStyle(.secondary)
    }
}

/// Shows workers for a job with −/+ buttons and the effect on production.
struct JobStepperRow: View {
    @EnvironmentObject var store: GameStore
    let job: JobType
    var compact = false

    var body: some View {
        let game = store.game
        let config = game.content.jobConfig(job)
        let workers = game.state.assignments[job]
        let capacity = game.jobCapacity(job)
        let unlimited = config?.unlimitedSlots ?? false
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text(config?.name ?? job.rawValue).font(compact ? .footnote.weight(.semibold) : .body.weight(.semibold))
                Text(effectText(game: game, workers: workers)).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button {
                store.setWorkers(job, to: workers - 1)
            } label: {
                Image(systemName: "minus").frame(width: 36, height: 36)
            }
            .buttonStyle(.bordered)
            .disabled(workers == 0)
            Text(unlimited ? "\(workers)" : "\(workers)/\(capacity)")
                .font(.body.monospacedDigit().weight(.semibold))
                .frame(minWidth: 40)
            Button {
                store.setWorkers(job, to: workers + 1)
            } label: {
                Image(systemName: "plus").frame(width: 36, height: 36)
            }
            .buttonStyle(.bordered)
            .disabled(game.idleWorkers == 0 || workers >= capacity)
        }
    }

    private func effectText(game: Game, workers: Int) -> String {
        guard let config = game.content.jobConfig(job) else { return "" }
        if let output = config.output {
            let each = game.ratePerWorker(job) * 60
            return String(format: "%@ %.1f/min each · total %.1f/min", output.icon, each, each * Double(workers))
        }
        switch job {
        case .construction:
            return String(format: "Build speed ×%.1f", game.constructionSpeed())
        case .crafting:
            let status = game.economy()
            if workers == 0 { return "Tools boost all gathering" }
            return status.craftingActive
                ? String(format: "Gathering +%.0f%% · uses wood & stone", status.craftingBonus * 100)
                : "Needs wood and stone to work"
        default:
            return config.description
        }
    }
}

// MARK: - Events

struct EventBadge: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        Button {
            store.eventMinimized = false
        } label: {
            Label("Decision waiting: \(store.game.pendingEvent?.title ?? "")", systemImage: "exclamationmark.bubble.fill")
                .font(.footnote.weight(.semibold))
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                .background(Theme.accent.opacity(0.9), in: Capsule())
                .foregroundStyle(.black)
        }
        .buttonStyle(.plain)
    }
}

struct EventPanel: View {
    @EnvironmentObject var store: GameStore
    @State var chosen: String?

    var body: some View {
        if let event = store.game.pendingEvent {
            ZStack {
                Color.black.opacity(0.55).ignoresSafeArea()
                VStack(alignment: .leading, spacing: 12) {
                    Text("LOCAL EVENT").font(.caption.weight(.bold)).foregroundStyle(Theme.accent)
                    Text(event.title).font(.title3.weight(.bold))
                    Text(event.text).font(.subheadline).foregroundStyle(.secondary)
                    ScrollView {
                        VStack(spacing: 8) {
                            ForEach(event.options) { option in
                                EventOptionCard(option: option, selected: chosen == option.id,
                                                blocker: store.game.optionBlocker(option)) {
                                    chosen = option.id
                                }
                            }
                        }
                    }
                    .frame(maxHeight: 320)
                    HStack(spacing: 10) {
                        Button {
                            store.eventMinimized = true
                        } label: {
                            Text("Decide later").frame(maxWidth: .infinity, minHeight: 44)
                        }
                        .buttonStyle(.bordered)
                        Button {
                            if let chosen { store.resolveEvent(chosen) }
                            chosen = nil
                        } label: {
                            Text("Confirm choice").bold().frame(maxWidth: .infinity, minHeight: 44)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(Theme.accent)
                        .disabled(chosen == nil)
                    }
                }
                .padding(18)
                .background(Theme.card, in: RoundedRectangle(cornerRadius: 20))
                .padding(16)
            }
        }
    }
}

struct EventOptionCard: View {
    let option: EventOption
    let selected: Bool
    let blocker: String?
    let onSelect: () -> Void

    var body: some View {
        Button(action: onSelect) {
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text(option.label).font(.headline)
                    Spacer()
                    if let cost = option.cost, !cost.isZero {
                        Text(cost.shortDescription).font(.caption.monospacedDigit())
                    }
                }
                Text(option.tradeOff).font(.footnote).foregroundStyle(.secondary)
                if let blocker {
                    Text(blocker).font(.caption.weight(.semibold)).foregroundStyle(Theme.bad)
                }
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(selected ? Theme.accent.opacity(0.25) : Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(selected ? Theme.accent : .clear, lineWidth: 2))
        }
        .buttonStyle(.plain)
        .disabled(blocker != nil)
        .opacity(blocker != nil ? 0.5 : 1)
    }
}

// MARK: - Milestone

struct MilestoneOverlay: View {
    @EnvironmentObject var store: GameStore
    let message: String
    @State var appeared = false

    var body: some View {
        ZStack {
            Color.black.opacity(0.6).ignoresSafeArea()
            VStack(spacing: 14) {
                Text("MILESTONE").font(.caption.weight(.heavy)).foregroundStyle(Theme.accent)
                HStack(spacing: 10) {
                    Text("🔥").font(.system(size: 40))
                    Image(systemName: "arrow.right").font(.title2)
                    Text("🏘️").font(.system(size: 40))
                }
                .scaleEffect(appeared ? 1 : 0.6)
                Text("Temporary Camp → Stable Village").font(.headline)
                Text(message).font(.subheadline).multilineTextAlignment(.center).foregroundStyle(.secondary)
                Text("Recorded in your Legacy Timeline.").font(.caption).foregroundStyle(.secondary)
                Button {
                    store.celebration = nil
                } label: {
                    Text("Continue").bold().frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.borderedProminent)
                .tint(Theme.accent)
            }
            .padding(22)
            .background(Theme.card, in: RoundedRectangle(cornerRadius: 22))
            .padding(24)
        }
        .onAppear {
            withAnimation(.spring(response: 0.5, dampingFraction: 0.6)) { appeared = true }
        }
    }
}
