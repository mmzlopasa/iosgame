// swift-tools-version:5.9
import PackageDescription

// FoundersCore: pure gameplay domain (no UIKit/SpriteKit) so it can be unit tested
// on macOS, Linux and Windows. The iOS app depends on this package.
let package = Package(
    name: "FoundersCore",
    platforms: [.iOS(.v16), .macOS(.v13)],
    products: [
        .library(name: "FoundersCore", targets: ["FoundersCore"])
    ],
    targets: [
        .target(
            name: "FoundersCore",
            resources: [.copy("Resources/content.json")]
        ),
        .testTarget(
            name: "FoundersCoreTests",
            dependencies: ["FoundersCore"]
        )
    ]
)
