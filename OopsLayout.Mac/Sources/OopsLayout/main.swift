import Cocoa

// Single-instance guard: if another copy with our bundle id is already running,
// bow out (the macOS analogue of the Windows named mutex).
let bundleID = Bundle.main.bundleIdentifier ?? "com.oopslayout.app"
let others = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID)
    .filter { $0 != NSRunningApplication.current }
if !others.isEmpty {
    exit(0)
}

let app = NSApplication.shared
// .accessory = menu-bar only, no Dock icon, no app menu (LSUIElement equivalent).
app.setActivationPolicy(.accessory)

let delegate = AppDelegate()
app.delegate = delegate
app.run()
