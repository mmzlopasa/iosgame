import Foundation

/// Development-only commands used by the in-app developer panel.
extension Game {
    /// Simulates `seconds` of game time immediately (as if the app had been away).
    public func debugFastForward(seconds: Double) {
        update()
        advance(seconds: min(max(0, seconds), content.balance.maxOfflineSeconds))
    }

    /// Adds resources (clamped to storage) and re-checks objectives.
    public func debugGrant(_ resources: ResourceBundle) {
        update()
        apply(Effect(kind: .grantResources, resources: resources), sourceId: "debug")
        evaluateProgress()
    }
}
