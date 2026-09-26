// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "BringLater",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "BringLater", targets: ["BringLater"]),
    ],
    dependencies: [
        .package(url: "https://github.com/sindresorhus/KeyboardShortcuts", exact: "2.4.0"),
    ],
    targets: [
        .target(name: "BringLaterCore"),
        .executableTarget(
            name: "BringLater",
            dependencies: ["BringLaterCore", "KeyboardShortcuts"]
        ),
        .testTarget(name: "BringLaterCoreTests", dependencies: ["BringLaterCore"]),
    ]
)
