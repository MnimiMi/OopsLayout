import Cocoa
import Carbon
import OopsLayoutCore

/// Menu-bar application. No main window — lives entirely in the macOS menu bar.
/// The macOS counterpart of TrayApp.cs.
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var enabledItem: NSMenuItem!
    private var targetRuItem: NSMenuItem!
    private var targetUkItem: NSMenuItem!
    private let backend = MacKeyboardBackend()
    private var engine: SwitcherEngine!
    private var enabled = true
    private var started = false
    private var accessibilityTimer: Timer?
    private var inExcludedApp = false   // frontmost app is a terminal / IDE
    private var exceptionsWC: ExceptionsWindowController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        UserExceptions.load()   // feed user keep-words into Core before we start
        Settings.load()         // restore the chosen Cyrillic target (RU / UK)
        WordBuffer.log = { DebugLog.write($0) }   // route engine decisions to /tmp log
        engine = SwitcherEngine(backend: backend)

        // Reflect dormant state (excluded app) in the menu-bar glyph.
        backend.onExcludedChanged = { [weak self] excluded in
            self?.inExcludedApp = excluded
            self?.refreshIcon()
        }

        buildStatusItem()

        // CGEventTap needs Accessibility permission.
        if AXIsProcessTrusted() {
            startEngine()
        } else {
            // Ask once, then watch for the grant so the user never has to
            // relaunch (and we never re-spam the system prompt on each launch).
            promptForAccessibilityOnce()
            updateTitle(granted: false)
            accessibilityTimer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] timer in
                guard AXIsProcessTrusted() else { return }
                timer.invalidate()
                self?.accessibilityTimer = nil
                self?.startEngine()
            }
        }
    }

    private func startEngine() {
        guard !started else { return }
        started = true
        engine.start()
        updateTitle(granted: true)
    }

    func applicationWillTerminate(_ notification: Notification) {
        accessibilityTimer?.invalidate()
        engine.stop()
    }

    // ── Status item / menu ─────────────────────────────────────────────────

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.toolTip = "OopsLayout"
        refreshIcon()

        let menu = NSMenu()

        enabledItem = NSMenuItem(title: "Enabled", action: #selector(toggleEnabled), keyEquivalent: "")
        enabledItem.target = self
        enabledItem.state = .on
        menu.addItem(enabledItem)

        menu.addItem(.separator())

        // Cyrillic target submenu (which "second" language to switch to).
        let targetItem = NSMenuItem(title: "Cyrillic target", action: nil, keyEquivalent: "")
        let targetMenu = NSMenu()
        targetRuItem = NSMenuItem(title: "Russian", action: #selector(selectRussian), keyEquivalent: "")
        targetRuItem.target = self
        targetUkItem = NSMenuItem(title: "Ukrainian", action: #selector(selectUkrainian), keyEquivalent: "")
        targetUkItem.target = self
        targetMenu.addItem(targetRuItem)
        targetMenu.addItem(targetUkItem)
        targetItem.submenu = targetMenu
        menu.addItem(targetItem)
        refreshTargetChecks()

        let exceptions = NSMenuItem(title: "Exceptions…", action: #selector(openExceptions), keyEquivalent: "")
        exceptions.target = self
        menu.addItem(exceptions)

        let about = NSMenuItem(title: "About OopsLayout", action: #selector(showAbout), keyEquivalent: "")
        about.target = self
        menu.addItem(about)

        menu.addItem(.separator())

        let quit = NSMenuItem(title: "Quit", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        statusItem.menu = menu
    }

    /// Sets the menu-bar glyph: "Oo" when the switcher is live, "-.-" (sleeping)
    /// when it's off — either toggled off, or because we're in an excluded app.
    private func refreshIcon() {
        guard let button = statusItem?.button else { return }
        let active = enabled && !inExcludedApp
        let glyph = active ? "Oo" : "-.-"
        if let icon = makeIcon(glyph) {
            button.image = icon
            button.title = ""
        } else {
            button.image = nil
            button.title = glyph      // last-resort fallback
        }
    }

    /// Renders a short string as a monochrome template image (adapts to
    /// light/dark menu bars). Drawn eagerly with lockFocus (a lazy
    /// drawingHandler image can come up blank in a status item).
    private func makeIcon(_ string: String) -> NSImage? {
        let text = string as NSString
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 13, weight: .semibold),
            .foregroundColor: NSColor.black
        ]
        let textSize = text.size(withAttributes: attrs)
        let size = NSSize(width: ceil(textSize.width) + 2, height: 18)

        let icon = NSImage(size: size)
        icon.lockFocus()
        text.draw(at: NSPoint(x: 1, y: (size.height - textSize.height) / 2), withAttributes: attrs)
        icon.unlockFocus()
        icon.isTemplate = true   // monochrome, theme-adaptive
        return icon
    }

    private func updateTitle(granted: Bool) {
        guard let button = statusItem.button else { return }
        if !granted {
            button.toolTip = "OopsLayout — needs Accessibility permission"
        } else {
            button.toolTip = enabled ? "OopsLayout (active)" : "OopsLayout (paused)"
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    @objc private func toggleEnabled() {
        enabled.toggle()
        engine.enabled = enabled
        enabledItem.state = enabled ? .on : .off
        updateTitle(granted: true)
        refreshIcon()
    }

    @objc private func selectRussian() {
        Settings.saveTarget(.russian)
        refreshTargetChecks()
    }

    @objc private func selectUkrainian() {
        Settings.saveTarget(.ukrainian)
        refreshTargetChecks()
    }

    private func refreshTargetChecks() {
        targetRuItem?.state = WordBuffer.target == .russian ? .on : .off
        targetUkItem?.state = WordBuffer.target == .ukrainian ? .on : .off
    }

    @objc private func openExceptions() {
        if exceptionsWC == nil {
            exceptionsWC = ExceptionsWindowController()
        }
        exceptionsWC?.show()
    }

    @objc private func showAbout() {
        let alert = NSAlert()
        alert.messageText = "OopsLayout"
        alert.informativeText = "Automatic keyboard layout switcher\n\ngithub.com/MnimiMi/OopsLayout"
        alert.alertStyle = .informational
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }

    @objc private func quit() {
        NSApplication.shared.terminate(nil)
    }

    // ── Accessibility permission ───────────────────────────────────────────────

    /// Shown only when not yet trusted. Registers the app in the Accessibility
    /// list and offers to open System Settings. Once the user flips the toggle,
    /// the poll timer in applicationDidFinishLaunching starts the engine — no
    /// relaunch needed, and we never re-spam this on later launches.
    private func promptForAccessibilityOnce() {
        // Ask macOS to show its own "would like to control this computer" request
        // AND register OopsLayout in the Accessibility list. We're an .accessory
        // (LSUIElement) app, so our NSAlert below can fail to come to the front —
        // the system prompt is the reliable nudge. (Was false, which showed no
        // system request at all; partners reported nothing appearing.)
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(opts)

        let alert = NSAlert()
        alert.messageText = "Accessibility permission needed"
        alert.informativeText = """
        OopsLayout watches your typing through a global keyboard tap, which \
        macOS gates behind Accessibility access.

        Open System Settings → Privacy & Security → Accessibility and enable \
        OopsLayout. It starts working the moment you flip the switch — no need \
        to restart the app.
        """
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Open System Settings")
        alert.addButton(withTitle: "Later")
        if alert.runModal() == .alertFirstButtonReturn,
           let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }
}
