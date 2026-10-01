// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "KawaiiIsland",
    platforms: [.macOS(.v13)],
    targets: [
        // Pure logic (notch geometry, layout, formatting, config): unit-tested.
        .target(name: "KawaiiCore"),
        .executableTarget(
            name: "KawaiiIsland",
            dependencies: ["KawaiiCore"],
            linkerSettings: [
                .linkedFramework("IOBluetooth"), .linkedFramework("CoreMediaIO"),
                .linkedFramework("CoreAudio"), .linkedFramework("IOKit"), .linkedFramework("ServiceManagement"),
            ]),
        .testTarget(name: "KawaiiCoreTests", dependencies: ["KawaiiCore"]),
    ])
