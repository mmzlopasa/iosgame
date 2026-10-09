// swift-tools-version:5.9
import PackageDescription

// EonwardCore: pure gameplay domain (no UIKit/SpriteKit) so it can be unit tested
// on macOS, Linux and Windows. The iOS app depends on this package.
let package = Package(
    name: "EonwardCore",
    platforms: [.iOS(.v16), .macOS(.v13)],
    products: [
        .library(name: "EonwardCore", targets: ["EonwardCore"])
    ],
    targets: [
        .target(
            name: "EonwardCore",
            resources: [.copy("Resources/content.json")]
        ),
        .testTarget(
            name: "EonwardCoreTests",
            dependencies: ["EonwardCore"]
        )
    ]
)
