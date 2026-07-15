import Foundation
import Cocoa
import Carbon
import OopsLayoutCore

/// macOS implementation of KeyboardBackend.
/// Uses a CGEventTap for global key monitoring and CGEvent injection +
/// TISSelectInputSource for replacement and layout switching. Mirrors the
/// Windows backend (WindowsKeyboardBackend.cs) one-to-one.
public final class MacKeyboardBackend: KeyboardBackend {
    public var onCharTyped: ((Character) -> Void)?
    public var onEnterPressed: (() -> Void)?
    public var onBackspacePressed: (() -> Void)?
    public var onWordBreakPressed: ((Character) -> Void)?

    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?

    // A pending replacement set by replaceWord while the engine analyses a word.
    private var pending: (count: Int, text: String, dir: SwitchDirection)?

    // Events we inject ourselves carry this marker in their user-data field so
    // the tap can skip them (the equivalent of LLKHF_INJECTED on Windows).
    private static let injectedMarker: Int64 = 0x4F4F5053 // "OOPS"
    // .combinedSessionState shares the live session keyboard state. A
    // .privateState source has its own (US-leaning) layout state, which appeared
    // to fight our switch to Russian — the injected events kept pulling the
    // effective layout back toward English. (macOS; see debug-log investigation.)
    private let injectionSource = CGEventSource(stateID: .combinedSessionState)

    // Key injection is paced on this queue so target apps keep up.
    private let injectQueue = DispatchQueue(label: "com.oopslayout.inject")

    // Apps where auto-switching does more harm than good — terminals and code
    // editors are full of short English tokens and symbols, and the layout swap
    // races with fast typing. We skip those entirely (matched by bundle-id
    // prefix). Add your own here.
    private static let excludedBundlePrefixes = [
        "com.apple.Terminal",
        "com.googlecode.iterm2",
        "dev.warp.Warp-Stable",
        "com.microsoft.VSCode",
        "com.visualstudio.code.oss",
        "com.jetbrains.",          // Rider, IntelliJ, PyCharm, WebStorm, …
        "com.apple.dt.Xcode",
        "com.sublimetext.",
        "com.github.atom",
        "co.zeit.hyper",
    ]

    // Bundle id of the frontmost app, refreshed on app-activation rather than
    // queried on every keystroke. nil until the first activation/observation.
    private var frontBundleId: String?
    private var activationObserver: NSObjectProtocol?

    /// Fired (on the main thread) when the frontmost app becomes / stops being an
    /// excluded one, so the menu bar can reflect whether the switcher is live.
    /// `true` = the current app is excluded (switcher dormant).
    public var onExcludedChanged: ((Bool) -> Void)?

    public init() {}

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public func start() {
        DebugLog.reset()
        DebugLog.write("backend.start() called; AXIsProcessTrusted=\(AXIsProcessTrusted())")
        let mask = (1 << CGEventType.keyDown.rawValue)
        let selfPtr = Unmanaged.passUnretained(self).toOpaque()

        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: CGEventMask(mask),
            callback: { _, type, event, refcon in
                let backend = Unmanaged<MacKeyboardBackend>.fromOpaque(refcon!).takeUnretainedValue()
                return backend.handle(type: type, event: event)
            },
            userInfo: selfPtr
        ) else {
            // Almost always means Accessibility permission is missing.
            NSLog("OopsLayout: failed to create event tap (Accessibility permission?)")
            DebugLog.write("FAILED to create event tap (accessibility?)")
            return
        }

        eventTap = tap
        runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetCurrent(), runLoopSource, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)

        // Track the frontmost app so we can stay out of terminals / code editors.
        frontBundleId = NSWorkspace.shared.frontmostApplication?.bundleIdentifier
        onExcludedChanged?(isExcludedAppFrontmost())
        activationObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification,
            object: nil, queue: .main
        ) { [weak self] note in
            guard let self else { return }
            let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
            self.frontBundleId = app?.bundleIdentifier
            self.onExcludedChanged?(self.isExcludedAppFrontmost())
        }

        DebugLog.write("event tap created and enabled")
    }

    public func stop() {
        if let tap = eventTap {
            CGEvent.tapEnable(tap: tap, enable: false)
        }
        if let src = runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetCurrent(), src, .commonModes)
        }
        if let obs = activationObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(obs)
            activationObserver = nil
        }
        eventTap = nil
        runLoopSource = nil
    }

    /// True when the frontmost app is one where auto-switching should stay off.
    private func isExcludedAppFrontmost() -> Bool {
        guard let id = frontBundleId else { return false }
        return MacKeyboardBackend.excludedBundlePrefixes.contains { id.hasPrefix($0) }
    }

    // ── Event handling ────────────────────────────────────────────────────────

    private func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        // The system disables a tap that blocks too long; just re-enable it.
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap = eventTap { CGEvent.tapEnable(tap: tap, enable: true) }
            return Unmanaged.passUnretained(event)
        }
        guard type == .keyDown else { return Unmanaged.passUnretained(event) }

        // Stay out of terminals / code editors entirely — don't even buffer.
        if isExcludedAppFrontmost() {
            return Unmanaged.passUnretained(event)
        }

        // Skip input we injected ourselves (our backspaces/retype).
        if event.getIntegerValueField(.eventSourceUserData) == MacKeyboardBackend.injectedMarker {
            return Unmanaged.passUnretained(event)
        }

        let keyCode = Int(event.getIntegerValueField(.keyboardEventKeycode))

        if handleKey(keyCode: keyCode, event: event) {
            return nil // swallow: a replacement happened, we re-emit the text ourselves
        }
        return Unmanaged.passUnretained(event)
    }

    /// Returns true if this key was consumed (a replacement happened).
    private func handleKey(keyCode: Int, event: CGEvent) -> Bool {
        // Backspace
        if keyCode == kVK_Delete {
            onBackspacePressed?()
            return false
        }

        // Enter / Return
        if keyCode == kVK_Return || keyCode == kVK_ANSI_KeypadEnter {
            return flushAndMaybeReplace(notify: { self.onEnterPressed?() }, trailing: "\r")
        }

        // Ctrl/Cmd/Option combinations are shortcuts, not text — don't buffer them.
        let flags = event.flags
        if flags.contains(.maskCommand) || flags.contains(.maskControl) || flags.contains(.maskAlternate) {
            return false
        }

        guard let c = character(from: event) else { return false }

        // A character belongs to the word if it maps between layouts. This
        // crucially includes the EN punctuation keys that produce Russian
        // letters (' → э, [ → х, ] → ъ, ; → ж, , → б, . → ю); treating them as
        // breakers would split words like "это" (typed ' n j) before conversion.
        if c.isLetter || c.isNumber || KeyMap.isEnChar(c) || KeyMap.isRuChar(c) {
            onCharTyped?(c)
            return false
        }

        // Anything else (space, !, ?, -, (, ), …) ends the word.
        return flushAndMaybeReplace(notify: { self.onWordBreakPressed?(c) }, trailing: c)
    }

    /// The character this key event produces under the current keyboard layout,
    /// honouring Shift/CapsLock — the macOS analogue of ToUnicodeEx.
    ///
    /// We do NOT use `event.keyboardGetUnicodeString`: right after we call
    /// TISSelectInputSource it lags, translating the keycode under the *old*
    /// layout while the system (and the target app) already type under the new
    /// one. That made our buffer disagree with the screen — we'd capture Russian
    /// "просто" while the app showed English "ghjcnj", so the engine left phantom
    /// input alone and the real gibberish never got fixed. Instead we translate
    /// the keycode ourselves against the *current* input source's layout, which
    /// tracks the switch in lockstep with what the app receives.
    private func character(from event: CGEvent) -> Character? {
        let keyCode = UInt16(event.getIntegerValueField(.keyboardEventKeycode))
        if let c = translateUnderCurrentLayout(keyCode: keyCode, flags: event.flags) {
            return c
        }
        // Fallback: some input sources expose no 'uchr' layout data (input modes,
        // etc.). Use the event's own translation rather than dropping the key.
        var length = 0
        var chars = [UniChar](repeating: 0, count: 4)
        event.keyboardGetUnicodeString(maxStringLength: 4, actualStringLength: &length, unicodeString: &chars)
        guard length >= 1 else { return nil }
        return String(utf16CodeUnits: chars, count: length).first
    }

    /// Translate a virtual keycode to its character under the *currently active*
    /// keyboard layout via UCKeyTranslate. Honours Shift / Caps Lock / Option.
    private func translateUnderCurrentLayout(keyCode: UInt16, flags: CGEventFlags) -> Character? {
        guard let src = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue(),
              let raw = TISGetInputSourceProperty(src, kTISPropertyUnicodeKeyLayoutData) else { return nil }
        let layoutData = Unmanaged<CFData>.fromOpaque(raw).takeUnretainedValue() as Data

        // UCKeyTranslate wants the modifier state as (Carbon modifiers >> 8) & 0xFF.
        var modifierState: UInt32 = 0
        if flags.contains(.maskShift)      { modifierState |= UInt32(shiftKey >> 8) }
        if flags.contains(.maskAlphaShift) { modifierState |= UInt32(alphaLock >> 8) }
        if flags.contains(.maskAlternate)  { modifierState |= UInt32(optionKey >> 8) }

        return layoutData.withUnsafeBytes { rawBuffer -> Character? in
            guard let ptr = rawBuffer.bindMemory(to: UCKeyboardLayout.self).baseAddress else { return nil }
            var deadKeyState: UInt32 = 0
            var length = 0
            var chars = [UniChar](repeating: 0, count: 4)
            let status = UCKeyTranslate(
                ptr, keyCode, UInt16(kUCKeyActionDown), modifierState,
                UInt32(LMGetKbdType()), OptionBits(kUCKeyTranslateNoDeadKeysBit),
                &deadKeyState, 4, &length, &chars
            )
            guard status == noErr, length >= 1 else { return nil }
            return String(utf16CodeUnits: chars, count: length).first
        }
    }

    /// Runs the word-end notification (which may set `pending` via replaceWord).
    /// If a replacement is pending, schedules it with `trailing` appended and
    /// reports that the original key should be swallowed.
    private func flushAndMaybeReplace(notify: () -> Void, trailing: Character) -> Bool {
        pending = nil
        notify()
        guard let p = pending else { return false }
        pending = nil
        DebugLog.write("word break: replacing \(p.count) chars with '\(p.text)' dir=\(p.dir)")

        let text = trailing == "\r" ? p.text : p.text + String(trailing)
        let sendEnter = trailing == "\r"

        injectQueue.async {
            self.executeReplace(count: p.count, newText: text, direction: p.dir, sendEnter: sendEnter)
        }
        return true
    }

    // ── ReplaceWord ───────────────────────────────────────────────────────────

    public func replaceWord(count: Int, newText: String, direction: SwitchDirection) {
        // Called by the engine from within the tap callback. We only *record*
        // the request here; flushAndMaybeReplace schedules the actual injection
        // once the triggering key has been swallowed.
        pending = (count, newText, direction)
    }

    // Runs off the tap thread. Keys are sent one at a time with small pauses so
    // apps that read input asynchronously don't drop any of the rapid backspaces.
    private func executeReplace(count: Int, newText: String, direction: SwitchDirection, sendEnter: Bool) {
        let gap: UInt32 = 6_000 // microseconds between keys

        // Switch the layout FIRST so it has the whole injection window to actually
        // take effect before the user types the next word (TISSelectInputSource
        // can lag by a noticeable amount). The retyped text is Unicode-injected,
        // so it lands correctly regardless of the active layout.
        switchLayout(direction)

        usleep(15_000) // let the swallowed break key settle in the target app

        for _ in 0..<count {
            sendKeyTap(kVK_Delete)
            usleep(gap)
        }

        usleep(12_000) // let deletions finish before typing

        for ch in newText {
            sendUnicode(ch)
            usleep(gap)
        }

        if sendEnter {
            usleep(gap)
            sendKeyTap(kVK_Return)
        }
    }

    private func sendKeyTap(_ keyCode: Int) {
        for down in [true, false] {
            guard let ev = CGEvent(keyboardEventSource: injectionSource,
                                   virtualKey: CGKeyCode(keyCode), keyDown: down) else { continue }
            ev.setIntegerValueField(.eventSourceUserData, value: MacKeyboardBackend.injectedMarker)
            ev.post(tap: .cgSessionEventTap)
        }
    }

    private func sendUnicode(_ c: Character) {
        let units = Array(String(c).utf16)
        for down in [true, false] {
            guard let ev = CGEvent(keyboardEventSource: injectionSource,
                                   virtualKey: 0, keyDown: down) else { continue }
            ev.keyboardSetUnicodeString(stringLength: units.count, unicodeString: units)
            ev.setIntegerValueField(.eventSourceUserData, value: MacKeyboardBackend.injectedMarker)
            ev.post(tap: .cgSessionEventTap)
        }
    }

    // ── Layout switching (TISSelectInputSource) ─────────────────────────────────

    private func switchLayout(_ direction: SwitchDirection) {
        // Text Input Source APIs are main-thread-affine. executeReplace runs on a
        // background queue, so hop to main — calling TIS off-main can crash,
        // especially when the user is also toggling layout by hand at the time.
        DispatchQueue.main.async {
            // EnToRu means "switch to the active Cyrillic target" — RU or UK.
            let wantLang: String
            switch direction {
            case .enToRu: wantLang = WordBuffer.target == .ukrainian ? "uk" : "ru"
            default:      wantLang = "en"
            }

            // Switch only to a layout the user already has installed. If none
            // matches we do NOTHING — never install or force a new one.
            guard let source = self.findInstalledLayout(language: wantLang) else {
                DebugLog.write("switchLayout: no installed layout for '\(wantLang)'")
                return
            }
            let status = TISSelectInputSource(source)
            DebugLog.write("switchLayout: selected '\(wantLang)' status=\(status)")
        }
    }

    private func findInstalledLayout(language: String) -> TISInputSource? {
        guard let cfList = TISCreateInputSourceList(nil, false)?.takeRetainedValue() else { return nil }
        let sources = cfList as! [TISInputSource]

        for source in sources {
            // Only enabled, selectable keyboard layouts — not input modes, palettes, etc.
            guard property(source, kTISPropertyInputSourceCategory) as String? == (kTISCategoryKeyboardInputSource as String),
                  boolProperty(source, kTISPropertyInputSourceIsSelectCapable),
                  boolProperty(source, kTISPropertyInputSourceIsEnabled) else { continue }

            guard let raw = TISGetInputSourceProperty(source, kTISPropertyInputSourceLanguages) else { continue }
            let langs = Unmanaged<CFArray>.fromOpaque(raw).takeUnretainedValue() as! [String]
            if let primary = langs.first, primary == language {
                return source
            }
        }
        return nil
    }

    private func property(_ source: TISInputSource, _ key: CFString) -> String? {
        guard let raw = TISGetInputSourceProperty(source, key) else { return nil }
        return Unmanaged<CFString>.fromOpaque(raw).takeUnretainedValue() as String
    }

    private func boolProperty(_ source: TISInputSource, _ key: CFString) -> Bool {
        guard let raw = TISGetInputSourceProperty(source, key) else { return false }
        return CFBooleanGetValue(Unmanaged<CFBoolean>.fromOpaque(raw).takeUnretainedValue())
    }

    deinit { stop() }
}
