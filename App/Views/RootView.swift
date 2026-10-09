import SwiftUI
import SpriteKit
import EonwardCore

enum Fmt {
    static func duration(_ seconds: Double) -> String {
        guard seconds.isFinite else { return "—" }
        let t = max(0, Int(seconds.rounded(.up)))
        return t >= 60 ? "\(t / 60)m \(t % 60)s" : "\(t)s"
    }

    static func amount(_ value: Double) -> String { "\(Int(value.rounded(.down)))" }

    static func perMinute(_ perSecond: Double) -> String { String(format: "%+.1f/min", perSecond * 60) }
}

enum Theme {
    static let accent = Color(red: 0.95, green: 0.72, blue: 0.35)
    static let good = Color(red: 0.45, green: 0.85, blue: 0.45)
    static let bad = Color(red: 1.0, green: 0.45, blue: 0.4)
    static let card = Color(red: 0.13, green: 0.12, blue: 0.11).opacity(0.92)
}

struct RootView: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        ZStack {
            SpriteView(scene: store.scene, options: [.ignoresSiblingOrder])
                .ignoresSafeArea()

            VStack(spacing: 8) {
                TopBar()
                ObjectiveChip()
                ToastStack()
                Spacer(minLength: 0)
                if store.game.pendingEvent != nil && store.eventMinimized {
                    EventBadge()
                }
                if store.placingBuildingId != nil {
                    PlacementBar()
                } else if store.selectedBuildingId != nil {
                    BuildingDetailCard()
                }
                BottomNav()
            }

            if store.game.pendingEvent != nil && !store.eventMinimized && store.activePanel == nil {
                EventPanel()
                    .transition(.opacity)
            }
            if let message = store.celebration {
                MilestoneOverlay(message: message)
                    .transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: 0.25), value: store.celebration)
        .sheet(item: $store.activePanel) { panel in
            PanelSheet(panel: panel)
                .environmentObject(store)
                .presentationDetents([.medium, .large])
                .presentationDragIndicator(.visible)
        }
    }
}

// MARK: - Top bar

struct TopBar: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        let game = store.game
        let economy = game.economy()
        let caps = game.storageCapacity()
        let epochIndex = game.content.epoch(game.state.epochId)?.index ?? 1
        VStack(spacing: 6) {
            HStack(alignment: .center) {
                VStack(alignment: .leading, spacing: 0) {
                    Text("EPOCH \(epochIndex) · \(game.epochName.uppercased())")
                        .font(.caption2.weight(.bold))
                        .foregroundStyle(Theme.accent)
                    Text(game.stageName)
                        .font(.headline)
                }
                Spacer()
                Button {
                    store.activePanel = .settings
                } label: {
                    Image(systemName: "gearshape.fill")
                        .font(.title3)
                        .frame(width: 44, height: 44)
                }
                .accessibilityLabel("Developer settings")
            }
            HStack(spacing: 6) {
                ForEach(ResourceType.allCases, id: \.self) { type in
                    ResourcePill(icon: type.icon, value: game.state.resources[type], cap: caps[type],
                                 rate: economy.net[type])
                }
                PopulationPill(population: game.state.population, housing: game.housingCapacity(),
                               idle: game.idleWorkers, shortage: game.state.foodShortage)
            }
        }
        .padding(.horizontal, 12)
        .padding(.top, 4)
        .padding(.bottom, 8)
        .background(.ultraThinMaterial)
    }
}

struct ResourcePill: View {
    let icon: String
    let value: Double
    let cap: Double
    let rate: Double

    var body: some View {
        let nearCap = value >= cap - 0.5
        VStack(spacing: 1) {
            Text("\(icon) \(Fmt.amount(value))")
                .font(.subheadline.weight(.semibold).monospacedDigit())
                .foregroundStyle(nearCap ? Theme.accent : .primary)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
            Text(abs(rate) < 0.0005 ? "—" : Fmt.perMinute(rate))
                .font(.system(size: 10, weight: .medium).monospacedDigit())
                .foregroundStyle(rate < -0.0005 ? Theme.bad : (rate > 0.0005 ? Theme.good : .secondary))
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 5)
        .background(Color.black.opacity(0.25), in: RoundedRectangle(cornerRadius: 8))
    }
}

struct PopulationPill: View {
    let population: Int
    let housing: Int
    let idle: Int
    let shortage: Bool

    var body: some View {
        VStack(spacing: 1) {
            Text("👥 \(population)/\(housing)")
                .font(.subheadline.weight(.semibold).monospacedDigit())
                .lineLimit(1)
                .minimumScaleFactor(0.7)
            Text(shortage ? "HUNGRY" : (idle > 0 ? "\(idle) idle" : "all busy"))
                .font(.system(size: 10, weight: .bold))
                .foregroundStyle(shortage ? Theme.bad : (idle > 0 ? Theme.accent : .secondary))
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 5)
        .background(Color.black.opacity(0.25), in: RoundedRectangle(cornerRadius: 8))
    }
}

// MARK: - Objective chip & toasts

struct ObjectiveChip: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        if let objective = store.game.currentObjective {
            let firstCondition = objective.conditions.first.map { store.game.describe($0) } ?? ""
            Button {
                store.activePanel = .objectives
            } label: {
                HStack(spacing: 8) {
                    Text("🎯")
                    VStack(alignment: .leading, spacing: 1) {
                        Text(objective.title).font(.footnote.weight(.semibold))
                        Text(firstCondition).font(.caption2).foregroundStyle(.secondary)
                    }
                    Spacer(minLength: 0)
                    Image(systemName: "chevron.right").font(.caption)
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .background(Theme.card, in: RoundedRectangle(cornerRadius: 12))
            }
            .buttonStyle(.plain)
            .padding(.horizontal, 12)
        }
    }
}

struct ToastStack: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        VStack(spacing: 6) {
            ForEach(store.toasts) { toast in
                Text(toast.notice.message)
                    .font(.footnote.weight(.medium))
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 8)
                    .frame(maxWidth: .infinity)
                    .background(color(for: toast.notice.kind), in: RoundedRectangle(cornerRadius: 10))
                    .transition(.move(edge: .top).combined(with: .opacity))
            }
        }
        .padding(.horizontal, 16)
        .animation(.easeOut(duration: 0.2), value: store.toasts)
        .allowsHitTesting(false)
    }

    private func color(for kind: GameNotice.Kind) -> Color {
        switch kind {
        case .info: return Color(white: 0.2).opacity(0.92)
        case .success: return Color(red: 0.16, green: 0.36, blue: 0.2).opacity(0.95)
        case .warning: return Color(red: 0.5, green: 0.22, blue: 0.14).opacity(0.95)
        case .milestone: return Color(red: 0.55, green: 0.4, blue: 0.1).opacity(0.95)
        }
    }
}

// MARK: - Bottom navigation

struct BottomNav: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        let game = store.game
        let researchIdle = game.state.activeResearch == nil
            && game.content.technologies.contains { game.researchStatus($0.id) == .available && game.state.resources.covers($0.cost) }
        let milestoneReady = game.content.milestones.contains { game.canClaimMilestone($0.id) }
        HStack(spacing: 0) {
            NavButton(title: "Build", icon: "hammer.fill", badge: false) { store.activePanel = .build }
            NavButton(title: "People", icon: "person.3.fill", badge: game.idleWorkers > 0) { store.activePanel = .people }
            NavButton(title: "Research", icon: "lightbulb.fill", badge: researchIdle) { store.activePanel = .research }
            NavButton(title: "Objectives", icon: "checklist", badge: milestoneReady) { store.activePanel = .objectives }
            NavButton(title: "Map", icon: "map.fill", badge: false) { store.activePanel = .map }
        }
        .padding(.top, 6)
        .background(.ultraThinMaterial)
    }
}

struct NavButton: View {
    let title: String
    let icon: String
    let badge: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: 3) {
                ZStack(alignment: .topTrailing) {
                    Image(systemName: icon).font(.system(size: 20))
                    if badge {
                        Circle().fill(Theme.accent).frame(width: 9, height: 9).offset(x: 6, y: -3)
                    }
                }
                Text(title).font(.system(size: 11, weight: .medium))
            }
            .frame(maxWidth: .infinity, minHeight: 48)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Sheets

struct PanelSheet: View {
    let panel: Panel
    @EnvironmentObject var store: GameStore

    var body: some View {
        NavigationStack {
            Group {
                switch panel {
                case .build: BuildPanel()
                case .people: PeoplePanel()
                case .research: ResearchPanel()
                case .objectives: ObjectivesPanel()
                case .map: MapPanel()
                case .settings: DevSettingsPanel()
                }
            }
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { store.activePanel = nil }
                }
            }
        }
    }

    private var title: String {
        switch panel {
        case .build: return "Build"
        case .people: return "People & Jobs"
        case .research: return "Research"
        case .objectives: return "Objectives"
        case .map: return "Map & Legacy"
        case .settings: return "Developer"
        }
    }
}
