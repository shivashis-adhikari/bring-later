// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "BringLater",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "BringLater", targets: ["BringLater"]),
    ],
    targets: [
        .target(name: "BringLaterCore"),
        .executableTarget(name: "BringLater", dependencies: ["BringLaterCore"]),
        .testTarget(name: "BringLaterCoreTests", dependencies: ["BringLaterCore"]),
    ]
)
