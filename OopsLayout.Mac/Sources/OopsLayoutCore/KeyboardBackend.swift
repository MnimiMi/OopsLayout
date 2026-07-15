import Foundation

public enum SwitchDirection {
    case none
    case enToRu  // typed EN chars that should be RU
    case ruToEn  // typed RU chars that should be EN
}

/// Platform-specific keyboard backend.
/// Windows implements this via Win32 hooks; macOS via CGEventTap.
public protocol KeyboardBackend: AnyObject {
    /// Fired when a printable character is typed.
    var onCharTyped: ((Character) -> Void)? { get set }

    /// Fired when Enter is pressed (flush buffer).
    var onEnterPressed: (() -> Void)? { get set }

    /// Fired when Backspace is pressed (pop last char from buffer).
    var onBackspacePressed: (() -> Void)? { get set }

    /// Fired when a word-breaking key is pressed (space, punctuation, etc).
    var onWordBreakPressed: ((Character) -> Void)? { get set }

    func start()
    func stop()

    /// Replace the last `count` characters with `newText` and switch layout.
    /// Implementation: send Backspace×count, switch layout, type newText.
    func replaceWord(count: Int, newText: String, direction: SwitchDirection)
}
