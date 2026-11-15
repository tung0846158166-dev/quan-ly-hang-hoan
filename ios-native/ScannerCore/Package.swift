// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "ScannerCore",
    platforms: [.iOS(.v16), .macOS(.v13)],
    products: [.library(name: "ScannerCore", targets: ["ScannerCore"])],
    targets: [
        .target(name: "ScannerCore"),
        .testTarget(name: "ScannerCoreTests", dependencies: ["ScannerCore"])
    ]
)
