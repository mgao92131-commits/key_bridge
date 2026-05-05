// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "BlueTypeMac",
    platforms: [
        .macOS(.v14),
    ],
    products: [
        .library(name: "BlueTypeMacCore", targets: ["BlueTypeMacCore"]),
        .executable(name: "BlueTypeMac", targets: ["BlueTypeMac"]),
        .executable(name: "BlueTypeMacCLI", targets: ["BlueTypeMacCLI"]),
    ],
    targets: [
        .target(
            name: "BlueTypeMacCore",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("ApplicationServices"),
                .linkedFramework("CryptoKit"),
                .linkedFramework("IOBluetooth"),
                .linkedFramework("Network"),
            ]
        ),
        .executableTarget(
            name: "BlueTypeMac",
            dependencies: ["BlueTypeMacCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
            ]
        ),
        .executableTarget(
            name: "BlueTypeMacCLI",
            dependencies: ["BlueTypeMacCore"]
        ),
        .testTarget(
            name: "BlueTypeMacCoreTests",
            dependencies: ["BlueTypeMacCore"]
        ),
    ],
    swiftLanguageModes: [.v5]
)
