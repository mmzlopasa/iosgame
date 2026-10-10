import SwiftUI
import FoundersCore

// MARK: - Build

struct BuildPanel: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        let game = store.game
        List {
            Section {
                ForEach(game.content.buildings.filter(\.buildable)) { def in
                    BuildRow(def: def)
                }
            } footer: {
                Text("Tap a building, then tap the ground to place it. Resources are only spent when you confirm.")
            }
        }
    }
}

struct BuildRow: View {
    @EnvironmentObject var store: GameStore
    let def: BuildingDefinition

    var body: some View {
        let game = store.game
        let affordable = game.state.resources.covers(def.cost)
        let unlocked = game.isUnlocked(def)
        let limitReached = def.maxCount.map { game.count(of: def.id, includeUnfinished: true) >= $0 } ?? false
        Button {
            store.beginPlacement(def.id)
        } label: {
            HStack(alignment: .top, spacing: 12) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(def.name).font(.headline)
                    Text(def.description).font(.caption).foregroundStyle(.secondary)
                    HStack(spacing: 10) {
                        Text(def.cost.shortDescription)
                            .foregroundStyle(affordable ? Color.primary : Theme.bad)
                        Label(Fmt.duration(game.estimatedSeconds(work: def.buildSeconds)), systemImage: "clock")
                        if def.footprint.width > 1 {
                            Text("\(def.footprint.width)×\(def.footprint.height)")
                        }
                    }
                    .font(.caption.monospacedDigit())
                    if !unlocked {
                        Text("Requires \(game.content.technology(def.requiresTech ?? "")?.name ?? "research")")
                            .font(.caption.weight(.semibold)).foregroundStyle(Theme.bad)
                    } else if limitReached {
                        Text("Limit reached").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                    }
                }
                Spacer()
                Image(systemName: "chevron.right").foregroundStyle(.secondary)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!unlocked || limitReached)
        .opacity(!unlocked || limitReached ? 0.5 : 1)
    }
}

// MARK: - People

struct PeoplePanel: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        let game = store.game
        let economy = game.economy()
        let growthBlocked = game.state.population >= game.housingCapacity()
        List {
            Section {
                LabeledContent("Population", value: "\(game.state.population) / \(game.housingCapacity()) housed")
                LabeledContent("Idle", value: "\(game.idleWorkers)")
                LabeledContent("Food balance", value: Fmt.perMinute(economy.net.food))
                if game.state.foodShortage {
                    Label("Food has run out. All work is slower until food is restored.", systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(Theme.bad)
                } else if growthBlocked {
                    Label("Build or upgrade shelters so the community can grow.", systemImage: "house")
                        .foregroundStyle(Theme.accent)
                } else {
                    ProgressView(value: game.state.growthProgress) {
                        Text("Next newcomer").font(.caption)
                    }
                }
            }
            Section {
                ForEach(JobType.allCases, id: \.self) { job in
                    JobStepperRow(job: job)
                }
            } header: {
                Text("Jobs")
            } footer: {
                Text("Workplaces set how many people a job can take. Changes apply immediately.")
            }
            Section("Consumption") {
                LabeledContent("Eating", value: Fmt.perMinute(-Double(game.state.population) * game.content.balance.foodPerPersonPerSecond))
                LabeledContent("Food spoiling", value: Fmt.perMinute(-economy.spoilage))
            }
        }
    }
}

// MARK: - Research

struct ResearchPanel: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        let game = store.game
        List {
            Section {
                LabeledContent("Knowledge", value: "\(Fmt.amount(game.state.resources.knowledge)) 💡  (\(Fmt.perMinute(game.economy().net.knowledge)))")
                if game.economy().production.knowledge <= 0 {
                    Text("Assign people to Research (People tab) to produce knowledge.")
                        .font(.caption).foregroundStyle(Theme.accent)
                }
            }
            Section("Technologies") {
                ForEach(game.content.technologies) { tech in
                    TechRow(tech: tech)
                }
            }
        }
    }
}

struct TechRow: View {
    @EnvironmentObject var store: GameStore
    let tech: TechnologyDefinition

    var body: some View {
        let game = store.game
        let status = game.researchStatus(tech.id)
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(tech.name).font(.headline)
                Spacer()
                switch status {
                case .researched:
                    Label("Done", systemImage: "checkmark.circle.fill").foregroundStyle(Theme.good).font(.caption.weight(.bold))
                case .inProgress:
                    Text("Researching…").font(.caption.weight(.bold)).foregroundStyle(Theme.accent)
                default:
                    EmptyView()
                }
            }
            Text(tech.description).font(.caption).foregroundStyle(.secondary)
            switch status {
            case .inProgress(let progress):
                let remaining = game.state.activeResearch?.remainingSeconds ?? 0
                ProgressView(value: progress) {
                    Text("\(Fmt.duration(remaining)) left").font(.caption2)
                }
                .tint(Theme.accent)
            case .blocked(let missing):
                Text("Requires: \(missing.joined(separator: ", "))").font(.caption.weight(.semibold)).foregroundStyle(Theme.bad)
            case .available:
                let affordable = game.state.resources.covers(tech.cost)
                let busy = game.state.activeResearch != nil
                Button {
                    store.startResearch(tech.id)
                } label: {
                    Text("Research · \(tech.cost.shortDescription) · \(Fmt.duration(tech.researchSeconds))")
                        .frame(maxWidth: .infinity, minHeight: 36)
                }
                .buttonStyle(.borderedProminent)
                .tint(Theme.accent)
                .disabled(!affordable || busy)
                if busy {
                    Text("One project at a time.").font(.caption2).foregroundStyle(.secondary)
                }
            case .researched:
                EmptyView()
            }
        }
        .padding(.vertical, 4)
    }
}

// MARK: - Objectives

struct ObjectivesPanel: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        let game = store.game
        List {
            ForEach(game.content.milestones) { ms in
                MilestoneSection(milestone: ms)
            }
            Section("First steps") {
                ForEach(game.content.objectives) { obj in
                    ObjectiveRow(objective: obj, isCurrent: game.currentObjective?.id == obj.id)
                }
            }
        }
    }
}

struct ObjectiveRow: View {
    @EnvironmentObject var store: GameStore
    let objective: ObjectiveDefinition
    let isCurrent: Bool

    var body: some View {
        let game = store.game
        let done = game.state.completedObjectives.contains(objective.id)
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: done ? "checkmark.circle.fill" : (isCurrent ? "circle.inset.filled" : "circle"))
                .foregroundStyle(done ? Theme.good : (isCurrent ? Theme.accent : Color.secondary))
            VStack(alignment: .leading, spacing: 3) {
                Text(objective.title).font(.subheadline.weight(isCurrent ? .bold : .regular))
                    .strikethrough(done)
                if isCurrent {
                    Text(objective.hint).font(.caption).foregroundStyle(.secondary)
                    ForEach(Array(game.status(of: objective.conditions).enumerated()), id: \.offset) { _, req in
                        Label(req.text, systemImage: req.met ? "checkmark" : "smallcircle.filled.circle")
                            .font(.caption).foregroundStyle(req.met ? Theme.good : Color.primary)
                    }
                }
                if let reward = objective.reward, !done {
                    Text("Reward: \(reward.shortDescription)").font(.caption2).foregroundStyle(.secondary)
                }
            }
        }
    }
}

struct MilestoneSection: View {
    @EnvironmentObject var store: GameStore
    let milestone: MilestoneDefinition

    var body: some View {
        let game = store.game
        let reached = game.state.reachedMilestones.contains(milestone.id)
        Section("Milestone") {
            VStack(alignment: .leading, spacing: 6) {
                Text(milestone.name).font(.headline)
                if reached {
                    Label("Reached — your camp is now a permanent village.", systemImage: "star.fill")
                        .foregroundStyle(Theme.accent).font(.caption)
                } else {
                    ForEach(Array(game.milestoneRequirements(milestone.id).enumerated()), id: \.offset) { _, req in
                        Label(req.text, systemImage: req.met ? "checkmark.circle.fill" : "circle")
                            .font(.caption).foregroundStyle(req.met ? Theme.good : Color.secondary)
                    }
                    Button {
                        store.claimMilestone(milestone.id)
                        store.activePanel = nil
                    } label: {
                        Text("Found the village").bold().frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(Theme.accent)
                    .disabled(!game.canClaimMilestone(milestone.id))
                }
            }
            .padding(.vertical, 4)
        }
    }
}

// MARK: - Map & Legacy

struct MapPanel: View {
    @EnvironmentObject var store: GameStore

    var body: some View {
        let game = store.game
        List {
            Section {
                LabeledContent("Settlement", value: game.state.settlementName)
                LabeledContent("Geography", value: game.content.map.traits.joined(separator: ", "))
            }
            Section {
                ForEach(game.content.regions.filter { !$0.revealedAtStart }) { region in
                    RegionRow(region: region)
                }
            } header: {
                Text("Explore")
            } footer: {
                Text("Scouts reveal new land for building and may discover opportunities.")
            }
            Section("Legacy Timeline") {
                ForEach(game.state.legacy.reversed()) { entry in
                    VStack(alignment: .leading, spacing: 2) {
                        HStack {
                            Text(entry.title).font(.subheadline.weight(.semibold))
                            Spacer()
                            Text(Fmt.duration(entry.simTime)).font(.caption2.monospacedDigit()).foregroundStyle(.secondary)
                        }
                        if !entry.detail.isEmpty {
                            Text(entry.detail).font(.caption).foregroundStyle(.secondary)
                        }
                    }
                }
            }
        }
    }
}

struct RegionRow: View {
    @EnvironmentObject var store: GameStore
    let region: RegionDefinition

    var body: some View {
        let game = store.game
        let explored = game.state.exploredRegions.contains(region.id)
        let active = game.state.activeExpedition
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(region.name).font(.headline)
                Spacer()
                if explored {
                    Label("Explored", systemImage: "checkmark.circle.fill").font(.caption).foregroundStyle(Theme.good)
                }
            }
            Text(region.description).font(.caption).foregroundStyle(.secondary)
            if let active, active.regionId == region.id {
                ProgressView(value: 1 - active.remainingSeconds / max(1, active.totalSeconds)) {
                    Text("Scouts away · \(Fmt.duration(active.remainingSeconds))").font(.caption2)
                }
                .tint(Theme.accent)
            } else if !explored {
                Button {
                    store.explore(region.id)
                } label: {
                    Text("Send scouts · \(region.cost.shortDescription) · \(Fmt.duration(region.expeditionSeconds))")
                        .frame(maxWidth: .infinity, minHeight: 36)
                }
                .buttonStyle(.bordered)
                .disabled(active != nil || !game.state.resources.covers(region.cost))
            }
        }
        .padding(.vertical, 4)
    }
}

// MARK: - Developer

struct DevSettingsPanel: View {
    @EnvironmentObject var store: GameStore
    @State var confirmReset = false

    var body: some View {
        let game = store.game
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "?"
        let build = Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "?"
        List {
            Section("Diagnostics") {
                LabeledContent("App version", value: "\(version) (\(build))")
                LabeledContent("Content version", value: "\(game.content.contentVersion)")
                LabeledContent("Save version", value: "\(SaveEnvelope.currentVersion)")
                LabeledContent("Game time", value: Fmt.duration(game.state.simTime))
                LabeledContent("Buildings", value: "\(game.state.buildings.count)")
                LabeledContent("Save", value: store.saveStatus)
                Text(store.saveStore.fileURL.path).font(.caption2).foregroundStyle(.secondary).textSelection(.enabled)
                Text("Settlement \(game.state.settlementId)").font(.caption2).foregroundStyle(.secondary)
            }
            Section("Testing tools") {
                Button("Save now") { store.save() }
                Button("Fast-forward 1 minute") { store.debugFastForward(minutes: 1) }
                Button("Fast-forward 10 minutes") { store.debugFastForward(minutes: 10) }
                Button("Grant +50 of every resource") { store.debugGrant() }
            }
            Section {
                Button("Reset prototype", role: .destructive) { confirmReset = true }
            } footer: {
                Text("\(game.content.branding.title) — \(game.content.branding.subtitle). Prototype build with placeholder art.")
            }
        }
        .confirmationDialog("Erase this settlement and start over?", isPresented: $confirmReset, titleVisibility: .visible) {
            Button("Erase and restart", role: .destructive) { store.resetPrototype() }
        }
    }
}
