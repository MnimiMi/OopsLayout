import Foundation

/// Core engine. Wires buffer + backend together. Platform-agnostic.
public final class SwitcherEngine {
    private let backend: KeyboardBackend
    private let buffer = WordBuffer()

    public var enabled = true

    public init(backend: KeyboardBackend) {
        self.backend = backend
        backend.onCharTyped = { [weak self] c in self?.onCharTyped(c) }
        backend.onWordBreakPressed = { [weak self] c in self?.flushAndReplace(c) }
        backend.onEnterPressed = { [weak self] in self?.flushAndReplace("\n") }
        backend.onBackspacePressed = { [weak self] in self?.onBackspace() }
    }

    public func start() { backend.start() }
    public func stop() { backend.stop() }

    private func onCharTyped(_ c: Character) {
        guard enabled else { return }
        // Just accumulate — word analysis happens on break.
        buffer.push(c)
    }

    private func onBackspace() {
        // Backspace typed — we can't know what got deleted exactly,
        // safest to clear our buffer to avoid desync.
        buffer.clear()
    }

    private func flushAndReplace(_ breakChar: Character) {
        guard enabled else { return }
        let (direction, backspaces, replacement) = buffer.flush(breakChar)
        if direction != .none && backspaces > 0 {
            backend.replaceWord(count: backspaces, newText: replacement, direction: direction)
        }
    }
}
