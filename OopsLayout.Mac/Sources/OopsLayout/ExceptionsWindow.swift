import Cocoa

/// Small settings window for managing user exceptions: one word per line in a
/// text view. Saved when the window closes or "Save" is pressed; words are
/// routed to the RU/EN keep-list by script.
final class ExceptionsWindowController: NSWindowController, NSWindowDelegate {
    private var textView: NSTextView!

    convenience init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 360, height: 380),
            styleMask: [.titled, .closable],
            backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false  // the NSWindowController owns the lifetime
        window.title = "OopsLayout — Exceptions"
        window.center()
        self.init(window: window)
        window.delegate = self
        buildContent()
    }

    private func buildContent() {
        guard let content = window?.contentView else { return }

        let label = NSTextField(wrappingLabelWithString:
            "Words OopsLayout should never auto-switch — one per line. " +
            "Add anything it keeps changing on you (Cyrillic and Latin both work).")
        label.frame = NSRect(x: 16, y: 332, width: 328, height: 36)
        label.font = .systemFont(ofSize: 11)
        label.textColor = .secondaryLabelColor
        content.addSubview(label)

        let scroll = NSScrollView(frame: NSRect(x: 16, y: 52, width: 328, height: 268))
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        let tv = NSTextView(frame: scroll.bounds)
        tv.autoresizingMask = [.width]
        tv.isRichText = false
        tv.font = .monospacedSystemFont(ofSize: 13, weight: .regular)
        tv.string = UserExceptions.all.joined(separator: "\n")
        scroll.documentView = tv
        content.addSubview(scroll)
        textView = tv

        let save = NSButton(title: "Save", target: self, action: #selector(saveAndClose))
        save.frame = NSRect(x: 252, y: 12, width: 92, height: 32)
        save.bezelStyle = .rounded
        save.keyEquivalent = "\r"
        content.addSubview(save)
    }

    @objc private func saveAndClose() {
        persist()
        window?.close()
    }

    private func persist() {
        let lines = (textView?.string ?? "").components(separatedBy: .newlines)
        UserExceptions.setWords(lines)
    }

    func windowWillClose(_ notification: Notification) {
        persist()
    }

    func show() {
        textView?.string = UserExceptions.all.joined(separator: "\n")
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}
