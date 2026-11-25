// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "OopsLayout",
    platforms: [
        .macOS(.v12)
    ],
    targets: [
        // Platform-agnostic logic, mirrors the C# OopsLayout.Core project.
        .target(
            name: "OopsLayoutCore"
        ),
        // macOS menu-bar app: CGEventTap + TISSelectInputSource + NSStatusItem.
        .executableTarget(
            name: "OopsLayout",
            dependencies: ["OopsLayoutCore"],
            linkerSettings: [
                .linkedFramework("Cocoa"),
                .linkedFramework("Carbon"),
                .linkedFramework("ApplicationServices")
            ]
        ),
        // Self-test runner. XCTest isn't available with Command Line Tools only,
        // so verification is a plain executable that asserts and exits non-zero
        // on failure. Run: swift run OopsLayoutSelfTest
        .executableTarget(
            name: "OopsLayoutSelfTest",
            dependencies: ["OopsLayoutCore"]
        )
    ]
)
