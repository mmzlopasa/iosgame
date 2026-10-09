import SpriteKit
import UIKit
import EonwardCore

/// Draws the settlement and turns touches into grid taps. It never changes game
/// state itself: it reads `store.game` and reports taps back to the store.
@MainActor
final class SettlementScene: SKScene {
    weak var store: GameStore?

    private let cameraNode = SKCameraNode()
    private let world = SKNode()
    private let groundLayer = SKNode()
    private let pathLayer = SKNode()
    private let objectLayer = SKNode()
    private let fogLayer = SKNode()
    private let overlayLayer = SKNode()

    private var builtTerrain = false
    private var fogNodes: [String: SKSpriteNode] = [:]
    private var buildingNodes: [Int: BuildingNode] = [:]
    private var pathsDrawnForStage: String?
    private var selectionNodes: [SKSpriteNode] = []
    private var ghostNodes: [SKNode] = []
    private var ghostKey = ""

    private var touchStart: CGPoint?
    private var isDragging = false
    private var pinchStartScale: CGFloat = 1
    private var pinchRecognizer: UIPinchGestureRecognizer?

    private let minZoom: CGFloat = 0.45
    private let maxZoom: CGFloat = 1.6

    override init(size: CGSize) {
        super.init(size: size)
        scaleMode = .resizeFill
        anchorPoint = CGPoint(x: 0.5, y: 0.5)
        backgroundColor = UIColor(red: 0.16, green: 0.19, blue: 0.17, alpha: 1)
        addChild(world)
        for (layer, z) in [(groundLayer, 0), (pathLayer, 1), (objectLayer, 10), (fogLayer, 1000), (overlayLayer, 2000)] {
            layer.zPosition = CGFloat(z)
            world.addChild(layer)
        }
        addChild(cameraNode)
        camera = cameraNode
        cameraNode.setScale(0.8)
    }

    required init?(coder aDecoder: NSCoder) { fatalError("init(coder:) is not supported") }

    override func didMove(to view: SKView) {
        view.isMultipleTouchEnabled = true
        if pinchRecognizer == nil {
            let pinch = UIPinchGestureRecognizer(target: self, action: #selector(handlePinch(_:)))
            pinch.cancelsTouchesInView = false
            view.addGestureRecognizer(pinch)
            pinchRecognizer = pinch
        }
        if !builtTerrain { rebuildAll() }
    }

    // MARK: - Building the world

    /// Full redraw (used at start and after a reset).
    func rebuildAll() {
        groundLayer.removeAllChildren()
        objectLayer.removeAllChildren()
        fogLayer.removeAllChildren()
        pathLayer.removeAllChildren()
        overlayLayer.removeAllChildren()
        fogNodes.removeAll()
        buildingNodes.removeAll()
        selectionNodes.removeAll()
        ghostNodes.removeAll()
        ghostKey = ""
        pathsDrawnForStage = nil
        builtTerrain = false
        buildTerrain()
        if let game = store?.game, let center = game.state.buildings.first {
            cameraNode.position = Iso.point(x: CGFloat(center.x), y: CGFloat(center.y))
        }
        sync()
    }

    private func buildTerrain() {
        guard let game = store?.game, !builtTerrain else { return }
        builtTerrain = true
        let map = game.content.map
        for y in 0..<map.height {
            for x in 0..<map.width {
                guard let terrain = map.terrain(x: x, y: y) else { continue }
                let variant = (x * 7 + y * 13) % 3
                let pos = Iso.point(x: CGFloat(x), y: CGFloat(y))
                let tile = SKSpriteNode(texture: SpriteFactory.tile(terrain, variant: variant))
                tile.anchorPoint = SpriteFactory.tileAnchor
                tile.position = pos
                tile.zPosition = CGFloat(x + y) * 0.001
                groundLayer.addChild(tile)

                let depth = CGFloat(x + y)
                if terrain == .forest {
                    let tree = SKSpriteNode(texture: SpriteFactory.tree(variant: variant))
                    tree.anchorPoint = CGPoint(x: 0.5, y: 0.1)
                    tree.position = CGPoint(x: pos.x + CGFloat(variant - 1) * 5, y: pos.y - 2)
                    tree.zPosition = depth
                    objectLayer.addChild(tree)
                } else if terrain == .rock {
                    let rock = SKSpriteNode(texture: SpriteFactory.boulder(variant: variant))
                    rock.anchorPoint = CGPoint(x: 0.5, y: 0.2)
                    rock.position = pos
                    rock.zPosition = depth
                    objectLayer.addChild(rock)
                }

                let fog = SKSpriteNode(texture: SpriteFactory.fog())
                fog.anchorPoint = SpriteFactory.tileAnchor
                fog.position = pos
                fog.zPosition = CGFloat(x + y) * 0.001
                fogLayer.addChild(fog)
                fogNodes["\(x),\(y)"] = fog
            }
        }
    }

    // MARK: - Sync with game state

    /// Updates sprites to match the game state. Cheap enough to call several times per second.
    func sync() {
        guard let store, builtTerrain else { return }
        let game = store.game

        // Fog of war
        for (key, node) in fogNodes {
            let parts = key.split(separator: ",")
            guard parts.count == 2, let x = Int(parts[0]), let y = Int(parts[1]) else { continue }
            if game.isRevealed(x: x, y: y) {
                fogNodes[key] = nil
                node.run(.sequence([.fadeOut(withDuration: 0.8), .removeFromParent()]))
            }
        }

        // Buildings
        var seen = Set<Int>()
        for b in game.state.buildings {
            guard let def = game.content.building(b.definitionId) else { continue }
            seen.insert(b.id)
            let key = "\(b.definitionId)|\(b.status.rawValue)"
            if let node = buildingNodes[b.id], node.key == key {
                node.setProgress(b.progress, active: b.status != .complete)
            } else {
                let wasThere = buildingNodes[b.id] != nil
                buildingNodes[b.id]?.removeFromParent()
                let node = BuildingNode(building: b, definition: def, key: key)
                node.position = Iso.point(x: CGFloat(b.x) + CGFloat(def.footprint.width - 1) / 2,
                                          y: CGFloat(b.y) + CGFloat(def.footprint.height - 1) / 2)
                node.zPosition = Iso.depth(x: b.x, y: b.y, footprint: def.footprint) + 0.5
                objectLayer.addChild(node)
                buildingNodes[b.id] = node
                if wasThere || b.status == .constructing { node.popIn() }
            }
        }
        for (id, node) in buildingNodes where !seen.contains(id) {
            node.removeFromParent()
            buildingNodes[id] = nil
        }

        drawVillagePathsIfNeeded(game)
        updateSelection(store)
        updateGhost(store)
    }

    /// After the village milestone, worn paths connect every building to the hearth.
    private func drawVillagePathsIfNeeded(_ game: Game) {
        let stage = game.state.stageId
        let buildingCount = game.state.buildings.count
        let key = "\(stage)#\(buildingCount)"
        guard stage != game.content.startStage, pathsDrawnForStage != key else { return }
        let firstTime = pathsDrawnForStage == nil
        pathsDrawnForStage = key
        pathLayer.removeAllChildren()
        guard let hearth = game.state.buildings.first(where: { $0.definitionId == "village_hearth" }) else { return }
        let start = Iso.point(x: CGFloat(hearth.x), y: CGFloat(hearth.y))
        for b in game.state.buildings where b.id != hearth.id {
            guard let def = game.content.building(b.definitionId) else { continue }
            let end = Iso.point(x: CGFloat(b.x) + CGFloat(def.footprint.width - 1) / 2,
                                y: CGFloat(b.y) + CGFloat(def.footprint.height - 1) / 2)
            let path = CGMutablePath()
            path.move(to: start)
            path.addQuadCurve(to: end, control: CGPoint(x: (start.x + end.x) / 2 + 10, y: (start.y + end.y) / 2 - 6))
            let shape = SKShapeNode(path: path)
            shape.strokeColor = UIColor(red: 0.74, green: 0.62, blue: 0.42, alpha: 0.85)
            shape.lineWidth = 7
            shape.lineCap = .round
            shape.alpha = firstTime ? 0 : 1
            pathLayer.addChild(shape)
            if firstTime { shape.run(.fadeIn(withDuration: 1.5)) }
        }
        if firstTime { focus(on: start, zoom: 0.7) }
    }

    private func updateSelection(_ store: GameStore) {
        selectionNodes.forEach { $0.removeFromParent() }
        selectionNodes.removeAll()
        guard store.placingBuildingId == nil, let id = store.selectedBuildingId,
              let b = store.game.state.building(withId: id),
              let def = store.game.content.building(b.definitionId) else { return }
        let texture = SpriteFactory.cellMarker("select", fill: UIColor(white: 1, alpha: 0.18),
                                               stroke: UIColor(red: 1, green: 0.85, blue: 0.3, alpha: 1))
        for dy in 0..<def.footprint.height {
            for dx in 0..<def.footprint.width {
                let marker = SKSpriteNode(texture: texture)
                marker.position = Iso.point(x: CGFloat(b.x + dx), y: CGFloat(b.y + dy))
                pathLayer.parent?.addChild(marker)
                marker.zPosition = 5
                selectionNodes.append(marker)
            }
        }
    }

    private func updateGhost(_ store: GameStore) {
        guard let id = store.placingBuildingId, let cell = store.ghostCell,
              let def = store.game.content.building(id) else {
            ghostNodes.forEach { $0.removeFromParent() }
            ghostNodes.removeAll()
            ghostKey = ""
            return
        }
        let valid = store.placementIssues.isEmpty
        let key = "\(id)|\(cell.x),\(cell.y)|\(valid)"
        guard key != ghostKey else { return }
        ghostKey = key
        ghostNodes.forEach { $0.removeFromParent() }
        ghostNodes.removeAll()

        let marker = valid
            ? SpriteFactory.cellMarker("ok", fill: UIColor(red: 0.3, green: 0.9, blue: 0.4, alpha: 0.35),
                                       stroke: UIColor(red: 0.3, green: 0.95, blue: 0.4, alpha: 1))
            : SpriteFactory.cellMarker("bad", fill: UIColor(red: 0.95, green: 0.25, blue: 0.2, alpha: 0.35),
                                       stroke: UIColor(red: 1, green: 0.3, blue: 0.25, alpha: 1))
        for dy in 0..<def.footprint.height {
            for dx in 0..<def.footprint.width {
                let m = SKSpriteNode(texture: marker)
                m.position = Iso.point(x: CGFloat(cell.x + dx), y: CGFloat(cell.y + dy))
                m.zPosition = 0
                overlayLayer.addChild(m)
                ghostNodes.append(m)
            }
        }
        let sprite = SKSpriteNode(texture: SpriteFactory.building(def.sprite, footprint: def.footprint))
        sprite.anchorPoint = SpriteFactory.buildingAnchor
        sprite.position = Iso.point(x: CGFloat(cell.x) + CGFloat(def.footprint.width - 1) / 2,
                                    y: CGFloat(cell.y) + CGFloat(def.footprint.height - 1) / 2)
        sprite.alpha = 0.7
        sprite.color = valid ? .green : .red
        sprite.colorBlendFactor = valid ? 0.15 : 0.45
        sprite.zPosition = 1
        overlayLayer.addChild(sprite)
        ghostNodes.append(sprite)
    }

    func focus(on point: CGPoint, zoom: CGFloat? = nil) {
        var actions: [SKAction] = [.move(to: point, duration: 0.6)]
        if let zoom { actions.append(.scale(to: zoom, duration: 0.6)) }
        let group = SKAction.group(actions)
        group.timingMode = .easeInEaseOut
        cameraNode.run(group)
    }

    /// Centre of a grid cell in scene coordinates (for the store/UI).
    func focus(onCellX x: Int, y: Int) {
        focus(on: Iso.point(x: CGFloat(x), y: CGFloat(y)))
    }

    // MARK: - Input

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard let touch = touches.first, let view else { return }
        touchStart = touch.location(in: view)
        isDragging = false
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard let touch = touches.first, let view else { return }
        if (event?.allTouches?.count ?? 1) > 1 {
            isDragging = true
            return
        }
        let current = touch.location(in: view)
        let previous = touch.previousLocation(in: view)
        if let start = touchStart, hypot(current.x - start.x, current.y - start.y) > 8 { isDragging = true }
        guard isDragging else { return }
        let scale = cameraNode.xScale
        cameraNode.position.x -= (current.x - previous.x) * scale
        cameraNode.position.y += (current.y - previous.y) * scale
        clampCamera()
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
        defer { touchStart = nil }
        guard !isDragging, let touch = touches.first else { return }
        let p = touch.location(in: world)
        let (x, y) = Iso.cell(at: p)
        store?.handleTap(x: x, y: y)
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) {
        touchStart = nil
        isDragging = false
    }

    @objc private func handlePinch(_ recognizer: UIPinchGestureRecognizer) {
        switch recognizer.state {
        case .began:
            pinchStartScale = cameraNode.xScale
            isDragging = true
        case .changed:
            let target = pinchStartScale / max(0.01, recognizer.scale)
            cameraNode.setScale(min(maxZoom, max(minZoom, target)))
        default:
            break
        }
    }

    private func clampCamera() {
        guard let map = store?.game.content.map else { return }
        let corners = [Iso.point(x: 0, y: 0), Iso.point(x: CGFloat(map.width), y: 0),
                       Iso.point(x: 0, y: CGFloat(map.height)), Iso.point(x: CGFloat(map.width), y: CGFloat(map.height))]
        let minX = corners.map(\.x).min() ?? 0, maxX = corners.map(\.x).max() ?? 0
        let minY = corners.map(\.y).min() ?? 0, maxY = corners.map(\.y).max() ?? 0
        cameraNode.position.x = min(maxX, max(minX, cameraNode.position.x))
        cameraNode.position.y = min(maxY, max(minY, cameraNode.position.y))
    }
}

/// Visual for one placed building: sprite + construction progress bar.
@MainActor
final class BuildingNode: SKNode {
    let key: String
    private let sprite: SKSpriteNode
    private let barBack: SKShapeNode
    private let barFill: SKShapeNode
    private let barWidth: CGFloat = 40

    init(building: PlacedBuilding, definition: BuildingDefinition, key: String) {
        self.key = key
        sprite = SKSpriteNode(texture: SpriteFactory.building(definition.sprite, footprint: definition.footprint))
        sprite.anchorPoint = SpriteFactory.buildingAnchor
        barBack = SKShapeNode(rect: CGRect(x: -20, y: 0, width: 40, height: 6), cornerRadius: 3)
        barFill = SKShapeNode(rect: CGRect(x: -19, y: 1, width: 38, height: 4), cornerRadius: 2)
        super.init()
        addChild(sprite)
        barBack.fillColor = UIColor(white: 0, alpha: 0.6)
        barBack.strokeColor = .clear
        barFill.fillColor = UIColor(red: 1, green: 0.8, blue: 0.3, alpha: 1)
        barFill.strokeColor = .clear
        let barY = sprite.size.height * (1 - SpriteFactory.buildingAnchor.y) - 6
        barBack.position = CGPoint(x: 0, y: barY)
        barFill.position = CGPoint(x: 0, y: barY)
        addChild(barBack)
        addChild(barFill)
        setProgress(building.progress, active: building.status != .complete)
    }

    required init?(coder aDecoder: NSCoder) { fatalError("init(coder:) is not supported") }

    func setProgress(_ progress: Double, active: Bool) {
        barBack.isHidden = !active
        barFill.isHidden = !active
        sprite.alpha = active ? 0.5 : 1
        barFill.xScale = CGFloat(max(0.02, min(1, progress)))
        barFill.position.x = -19 * (1 - barFill.xScale)
    }

    func popIn() {
        setScale(0.6)
        run(.scale(to: 1, duration: 0.25))
    }
}
