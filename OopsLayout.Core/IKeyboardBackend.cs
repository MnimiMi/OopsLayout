namespace OopsLayout.Core;

/// <summary>
/// Platform-specific keyboard backend.
/// Windows implements this via Win32 hooks; macOS via CGEventTap.
/// </summary>
public interface IKeyboardBackend : IDisposable
{
    /// <summary>Fired when a printable character is typed.</summary>
    event Action<char> CharTyped;

    /// <summary>Fired when Enter is pressed (flush buffer).</summary>
    event Action EnterPressed;

    /// <summary>Fired when Backspace is pressed (pop last char from buffer).</summary>
    event Action BackspacePressed;

    /// <summary>Fired when a word-breaking key is pressed (space, punctuation, etc).</summary>
    event Action<char> WordBreakPressed;

    void Start();
    void Stop();

    /// <summary>
    /// Replace the last `count` characters with `newText` and switch layout.
    /// Implementation: send Backspace×count, switch layout, type newText.
    /// `originalText` is what those `count` characters were — kept so the
    /// replacement can be undone (Backspace right after a correction).
    /// </summary>
    void ReplaceWord(int count, string newText, string originalText, SwitchDirection direction);
}
