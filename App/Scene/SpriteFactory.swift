import UIKit
import SpriteKit
import EonwardCore

// PLACEHOLDER ART: every texture here is drawn procedurally at runtime.
// Replace with licensed/final art later (see README "Art placeholders").

enum Iso {
    static let tileWidth: CGFloat = 64
    static let tileHeight: CGFloat = 32

    /// Scene position of the centre of grid cell (x, y). Fractional values allowed.
    static func point(x: CGFloat, y: CGFloat) -> CGPoint {
        CGPoint(x: (x - y) * tileWidth / 2, y: -(x + y) * tileHeight / 2)
    }

    /// Grid cell under a scene position.
    static func cell(at p: CGPoint) -> (Int, Int) {
        let a = p.x / (tileWidth / 2)
        let b = -p.y / (tileHeight / 2)
        let gx = (a + b) / 2
        let gy = (b - a) / 2
        return (Int(floor(gx + 0.5)), Int(floor(gy + 0.5)))
    }

    /// Draw order: cells further down the screen are drawn on top.
    static func depth(x: Int, y: Int, footprint: Footprint) -> CGFloat {
        CGFloat(x + footprint.width - 1 + y + footprint.height - 1)
    }
}

@MainActor
enum SpriteFactory {
    private static var cache: [String: SKTexture] = [:]

    private static func cached(_ key: String, size: CGSize, draw: (CGContext, CGSize) -> Void) -> SKTexture {
        if let t = cache[key] { return t }
        let format = UIGraphicsImageRendererFormat()
        format.scale = 2
        let image = UIGraphicsImageRenderer(size: size, format: format).image { ctx in draw(ctx.cgContext, size) }
        let texture = SKTexture(image: image)
        cache[key] = texture
        return texture
    }

    static func color(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat, _ a: CGFloat = 1) -> UIColor {
        UIColor(red: r / 255, green: g / 255, blue: b / 255, alpha: a)
    }

    // MARK: - Terrain

    /// Tile image is 64×44: a 64×32 diamond on top plus a 12pt earth side for depth.
    /// The diamond centre sits at anchor (0.5, 1 - 16/44).
    static let tileSize = CGSize(width: 64, height: 44)
    static let tileAnchor = CGPoint(x: 0.5, y: 1 - 16.0 / 44.0)

    static func diamond(in rect: CGRect) -> UIBezierPath {
        let p = UIBezierPath()
        p.move(to: CGPoint(x: rect.midX, y: rect.minY))
        p.addLine(to: CGPoint(x: rect.maxX, y: rect.midY))
        p.addLine(to: CGPoint(x: rect.midX, y: rect.maxY))
        p.addLine(to: CGPoint(x: rect.minX, y: rect.midY))
        p.close()
        return p
    }

    static func terrainColors(_ t: TerrainType) -> (top: UIColor, side: UIColor) {
        switch t {
        case .grass: return (color(122, 158, 84), color(110, 84, 58))
        case .forest: return (color(78, 118, 66), color(98, 76, 52))
        case .rock: return (color(146, 140, 128), color(104, 98, 90))
        case .water: return (color(76, 132, 170), color(58, 100, 132))
        case .sand: return (color(206, 186, 136), color(150, 122, 88))
        }
    }

    static func tile(_ t: TerrainType, variant: Int) -> SKTexture {
        cached("tile_\(t.rawValue)_\(variant)", size: tileSize) { ctx, size in
            let colors = terrainColors(t)
            let top = CGRect(x: 0, y: 0, width: 64, height: 32)
            // Earth side (left and right faces)
            let side = UIBezierPath()
            side.move(to: CGPoint(x: 0, y: 16))
            side.addLine(to: CGPoint(x: 32, y: 32))
            side.addLine(to: CGPoint(x: 64, y: 16))
            side.addLine(to: CGPoint(x: 64, y: 28))
            side.addLine(to: CGPoint(x: 32, y: 44))
            side.addLine(to: CGPoint(x: 0, y: 28))
            side.close()
            colors.side.setFill()
            side.fill()
            // Top face with a tiny brightness variation per tile
            let shift: CGFloat = [0, 0.04, -0.03][variant % 3]
            var h: CGFloat = 0, s: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
            _ = colors.top.getHue(&h, saturation: &s, brightness: &b, alpha: &a)
            UIColor(hue: h, saturation: s, brightness: min(1, b + shift), alpha: 1).setFill()
            diamond(in: top).fill()
            UIColor(white: 0, alpha: 0.08).setStroke()
            let outline = diamond(in: top.insetBy(dx: 0.5, dy: 0.5))
            outline.lineWidth = 1
            outline.stroke()
            if t == .water {
                UIColor(white: 1, alpha: 0.35).setStroke()
                let wave = UIBezierPath()
                wave.move(to: CGPoint(x: 20, y: 14 + CGFloat(variant % 3)))
                wave.addQuadCurve(to: CGPoint(x: 40, y: 14 + CGFloat(variant % 3)), controlPoint: CGPoint(x: 30, y: 10))
                wave.lineWidth = 1.5
                wave.stroke()
            } else if t == .grass && variant % 3 == 1 {
                color(98, 140, 70).setFill()
                for (x, y) in [(22, 12), (40, 18), (30, 22)] {
                    UIBezierPath(ovalIn: CGRect(x: x, y: y, width: 3, height: 2)).fill()
                }
            }
        }
    }

    static func fog() -> SKTexture {
        cached("fog", size: tileSize) { _, _ in
            color(28, 32, 30, 0.82).setFill()
            let p = UIBezierPath()
            p.move(to: CGPoint(x: 32, y: -1))
            p.addLine(to: CGPoint(x: 65, y: 16))
            p.addLine(to: CGPoint(x: 65, y: 28))
            p.addLine(to: CGPoint(x: 32, y: 45))
            p.addLine(to: CGPoint(x: -1, y: 28))
            p.addLine(to: CGPoint(x: -1, y: 16))
            p.close()
            p.fill()
        }
    }

    /// Flat diamond used for selection and placement feedback.
    static func cellMarker(_ key: String, fill: UIColor, stroke: UIColor) -> SKTexture {
        cached("marker_\(key)", size: CGSize(width: 64, height: 32)) { _, size in
            let d = diamond(in: CGRect(origin: .zero, size: size).insetBy(dx: 2, dy: 1))
            fill.setFill()
            d.fill()
            stroke.setStroke()
            d.lineWidth = 2
            d.stroke()
        }
    }

    static func tree(variant: Int) -> SKTexture {
        cached("tree_\(variant % 3)", size: CGSize(width: 40, height: 56)) { _, _ in
            color(92, 66, 44).setFill()
            UIBezierPath(rect: CGRect(x: 18, y: 40, width: 4, height: 12)).fill()
            let greens = [color(54, 96, 52), color(64, 108, 58), color(48, 88, 60)]
            greens[variant % 3].setFill()
            for (i, w) in [CGFloat(30), 24, 16].enumerated() {
                let top = CGFloat(6 + i * 9)
                let p = UIBezierPath()
                p.move(to: CGPoint(x: 20, y: top))
                p.addLine(to: CGPoint(x: 20 + w / 2, y: top + 22))
                p.addLine(to: CGPoint(x: 20 - w / 2, y: top + 22))
                p.close()
                p.fill()
            }
        }
    }

    static func boulder(variant: Int) -> SKTexture {
        cached("boulder_\(variant % 2)", size: CGSize(width: 44, height: 34)) { _, _ in
            color(120, 116, 108).setFill()
            UIBezierPath(ovalIn: CGRect(x: 6, y: 10, width: 26, height: 20)).fill()
            color(150, 146, 136).setFill()
            UIBezierPath(ovalIn: CGRect(x: 18 + CGFloat(variant % 2) * 2, y: 4, width: 20, height: 18)).fill()
            color(176, 172, 160).setFill()
            UIBezierPath(ovalIn: CGRect(x: 22, y: 7, width: 8, height: 5)).fill()
        }
    }

    // MARK: - Buildings

    /// Canvas for a building of footprint `s` (square): width 64·s, height 72·s.
    /// The ground centre is 16·s above the bottom edge → anchor (0.5, 16/72).
    static let buildingAnchor = CGPoint(x: 0.5, y: 16.0 / 72.0)

    static func building(_ sprite: String, footprint: Footprint) -> SKTexture {
        let s = CGFloat(max(footprint.width, footprint.height))
        let size = CGSize(width: 64 * s, height: 72 * s)
        return cached("bld_\(sprite)_\(Int(s))", size: size) { _, size in
            let base = CGPoint(x: size.width / 2, y: size.height - 16 * s)
            // Soft ground shadow
            UIColor(white: 0, alpha: 0.18).setFill()
            UIBezierPath(ovalIn: CGRect(x: base.x - 24 * s, y: base.y - 9 * s, width: 48 * s, height: 18 * s)).fill()
            drawBuilding(sprite, base: base, s: s)
        }
    }

    private static func poly(_ points: [CGPoint], _ fill: UIColor, stroke: UIColor? = nil) {
        guard let first = points.first else { return }
        let p = UIBezierPath()
        p.move(to: first)
        for pt in points.dropFirst() { p.addLine(to: pt) }
        p.close()
        fill.setFill()
        p.fill()
        if let stroke {
            stroke.setStroke()
            p.lineWidth = 1
            p.stroke()
        }
    }

    private static func oval(_ c: CGPoint, _ w: CGFloat, _ h: CGFloat, _ fill: UIColor) {
        fill.setFill()
        UIBezierPath(ovalIn: CGRect(x: c.x - w / 2, y: c.y - h / 2, width: w, height: h)).fill()
    }

    private static func line(_ a: CGPoint, _ b: CGPoint, _ color: UIColor, _ width: CGFloat) {
        let p = UIBezierPath()
        p.move(to: a)
        p.addLine(to: b)
        p.lineWidth = width
        p.lineCapStyle = .round
        color.setStroke()
        p.stroke()
    }

    private static func flame(at b: CGPoint, scale k: CGFloat) {
        poly([CGPoint(x: b.x - 7 * k, y: b.y), CGPoint(x: b.x, y: b.y - 22 * k), CGPoint(x: b.x + 7 * k, y: b.y)],
             color(232, 120, 40))
        poly([CGPoint(x: b.x - 4 * k, y: b.y), CGPoint(x: b.x + 1 * k, y: b.y - 13 * k), CGPoint(x: b.x + 4 * k, y: b.y)],
             color(250, 206, 90))
    }

    private static func stoneRing(center c: CGPoint, rx: CGFloat, ry: CGFloat, count: Int, size: CGFloat) {
        for i in 0..<count {
            let a = CGFloat(i) / CGFloat(count) * .pi * 2
            oval(CGPoint(x: c.x + cos(a) * rx, y: c.y + sin(a) * ry), size, size * 0.7, color(134 + CGFloat(i % 3) * 12, 128, 118))
        }
    }

    private static func drawBuilding(_ sprite: String, base b: CGPoint, s: CGFloat) {
        let wood = color(122, 86, 54)
        let darkWood = color(86, 60, 38)
        let hide = color(178, 140, 98)
        switch sprite {
        case "campfire":
            stoneRing(center: b, rx: 12, ry: 6, count: 8, size: 6)
            line(CGPoint(x: b.x - 8, y: b.y + 2), CGPoint(x: b.x + 8, y: b.y - 3), darkWood, 3)
            line(CGPoint(x: b.x - 8, y: b.y - 3), CGPoint(x: b.x + 8, y: b.y + 2), darkWood, 3)
            flame(at: CGPoint(x: b.x, y: b.y), scale: 1)
        case "village_hearth":
            poly([CGPoint(x: b.x - 16, y: b.y), CGPoint(x: b.x, y: b.y + 8), CGPoint(x: b.x + 16, y: b.y),
                  CGPoint(x: b.x + 16, y: b.y - 10), CGPoint(x: b.x, y: b.y - 2), CGPoint(x: b.x - 16, y: b.y - 10)],
                 color(120, 114, 106))
            poly([CGPoint(x: b.x - 16, y: b.y - 10), CGPoint(x: b.x, y: b.y - 18), CGPoint(x: b.x + 16, y: b.y - 10),
                  CGPoint(x: b.x, y: b.y - 2)], color(158, 150, 138))
            flame(at: CGPoint(x: b.x, y: b.y - 9), scale: 1.3)
        case "shelter":
            poly([CGPoint(x: b.x - 18, y: b.y + 4), CGPoint(x: b.x - 4, y: b.y - 34), CGPoint(x: b.x + 16, y: b.y + 2)], hide,
                 stroke: darkWood)
            line(CGPoint(x: b.x - 4, y: b.y - 38), CGPoint(x: b.x - 20, y: b.y + 6), darkWood, 2.5)
            line(CGPoint(x: b.x - 4, y: b.y - 38), CGPoint(x: b.x + 18, y: b.y + 4), darkWood, 2.5)
            poly([CGPoint(x: b.x - 6, y: b.y + 2), CGPoint(x: b.x - 2, y: b.y - 12), CGPoint(x: b.x + 3, y: b.y + 2)],
                 color(60, 44, 30))
        case "woven_shelter":
            color(196, 166, 112).setFill()
            let dome = UIBezierPath()
            dome.move(to: CGPoint(x: b.x - 20, y: b.y + 3))
            dome.addQuadCurve(to: CGPoint(x: b.x + 20, y: b.y + 3), controlPoint: CGPoint(x: b.x, y: b.y - 52))
            dome.close()
            dome.fill()
            for i in 1...4 {
                let y = b.y + 3 - CGFloat(i) * 7
                let half = 20 - CGFloat(i) * 3.6
                line(CGPoint(x: b.x - half, y: y), CGPoint(x: b.x + half, y: y), color(150, 120, 76), 1.5)
            }
            oval(CGPoint(x: b.x + 2, y: b.y - 3), 9, 13, color(64, 46, 30))
            stoneRing(center: CGPoint(x: b.x, y: b.y + 3), rx: 20, ry: 4, count: 7, size: 5)
        case "foraging_camp":
            oval(CGPoint(x: b.x - 10, y: b.y - 8), 18, 16, color(70, 120, 58))
            oval(CGPoint(x: b.x + 9, y: b.y - 10), 20, 18, color(80, 132, 62))
            for (dx, dy) in [(-13, -10), (-7, -5), (6, -13), (12, -7), (9, -15)] {
                oval(CGPoint(x: b.x + CGFloat(dx), y: b.y + CGFloat(dy)), 3.5, 3.5, color(176, 46, 70))
            }
            poly([CGPoint(x: b.x - 7, y: b.y - 2), CGPoint(x: b.x + 7, y: b.y - 2), CGPoint(x: b.x + 5, y: b.y + 6),
                  CGPoint(x: b.x - 5, y: b.y + 6)], color(170, 128, 70), stroke: darkWood)
        case "wood_camp":
            for i in 0..<3 {
                let y = b.y - CGFloat(i) * 6
                let off = CGFloat(i) * 2
                line(CGPoint(x: b.x - 16 + off, y: y), CGPoint(x: b.x + 12 - off, y: y - 6), wood, 6)
                oval(CGPoint(x: b.x + 12 - off, y: y - 6), 6, 6, color(196, 160, 112))
            }
            oval(CGPoint(x: b.x + 14, y: b.y + 4), 10, 6, darkWood)
            line(CGPoint(x: b.x + 14, y: b.y + 1), CGPoint(x: b.x + 20, y: b.y - 10), darkWood, 2)
            poly([CGPoint(x: b.x + 18, y: b.y - 12), CGPoint(x: b.x + 24, y: b.y - 10), CGPoint(x: b.x + 21, y: b.y - 6)],
                 color(150, 150, 150))
        case "stone_site":
            poly([CGPoint(x: b.x - 16, y: b.y), CGPoint(x: b.x - 4, y: b.y + 5), CGPoint(x: b.x - 2, y: b.y - 6),
                  CGPoint(x: b.x - 12, y: b.y - 12)], color(150, 146, 136))
            poly([CGPoint(x: b.x, y: b.y + 2), CGPoint(x: b.x + 14, y: b.y - 2), CGPoint(x: b.x + 10, y: b.y - 14),
                  CGPoint(x: b.x + 2, y: b.y - 10)], color(128, 124, 116))
            poly([CGPoint(x: b.x - 6, y: b.y - 8), CGPoint(x: b.x + 4, y: b.y - 10), CGPoint(x: b.x, y: b.y - 20)],
                 color(170, 166, 156))
            line(CGPoint(x: b.x + 14, y: b.y + 6), CGPoint(x: b.x + 22, y: b.y - 6), darkWood, 2)
        case "storage_pit":
            oval(b, 34, 16, color(150, 120, 86))
            oval(b, 26, 11, color(52, 38, 26))
            line(CGPoint(x: b.x - 16, y: b.y - 1), CGPoint(x: b.x + 4, y: b.y - 8), wood, 3)
            line(CGPoint(x: b.x - 6, y: b.y + 5), CGPoint(x: b.x + 15, y: b.y - 3), wood, 3)
            oval(CGPoint(x: b.x + 11, y: b.y - 10), 10, 8, color(180, 150, 100))
        case "toolmaker":
            poly([CGPoint(x: b.x - 18, y: b.y + 2), CGPoint(x: b.x - 8, y: b.y - 30), CGPoint(x: b.x + 8, y: b.y)], color(160, 128, 92),
                 stroke: darkWood)
            line(CGPoint(x: b.x - 8, y: b.y - 34), CGPoint(x: b.x - 20, y: b.y + 4), darkWood, 2.5)
            oval(CGPoint(x: b.x + 13, y: b.y - 2), 16, 10, color(126, 122, 114))
            oval(CGPoint(x: b.x + 13, y: b.y - 5), 12, 6, color(156, 152, 142))
            poly([CGPoint(x: b.x + 9, y: b.y - 9), CGPoint(x: b.x + 15, y: b.y - 14), CGPoint(x: b.x + 17, y: b.y - 9)],
                 color(70, 70, 76))
        case "community_circle":
            oval(b, 100, 50, color(150, 130, 92, 0.55))
            for i in 0..<10 {
                let a = CGFloat(i) / 10 * .pi * 2
                let p = CGPoint(x: b.x + cos(a) * 44, y: b.y + sin(a) * 22)
                let h: CGFloat = 16 + CGFloat(i % 3) * 3
                poly([CGPoint(x: p.x - 4, y: p.y), CGPoint(x: p.x + 4, y: p.y), CGPoint(x: p.x + 3, y: p.y - h),
                      CGPoint(x: p.x - 3, y: p.y - h)], color(140 + CGFloat(i % 3) * 10, 136, 126), stroke: color(96, 92, 86))
            }
            stoneRing(center: b, rx: 8, ry: 4, count: 6, size: 5)
            flame(at: b, scale: 0.7)
        default:
            poly([CGPoint(x: b.x - 14, y: b.y), CGPoint(x: b.x, y: b.y - 24), CGPoint(x: b.x + 14, y: b.y)], color(200, 60, 200))
        }
    }
}
