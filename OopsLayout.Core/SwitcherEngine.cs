namespace OopsLayout.Core;

/// <summary>
/// Core engine. Wires buffer + backend together.
/// Platform-agnostic.
/// </summary>
public class SwitcherEngine : IDisposable
{
    private readonly IKeyboardBackend _backend;
    private readonly WordBuffer _buffer = new();
    private bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public SwitcherEngine(IKeyboardBackend backend)
    {
        _backend = backend;
        _backend.CharTyped += OnCharTyped;
        _backend.WordBreakPressed += OnWordBreak;
        _backend.EnterPressed += OnEnter;
        _backend.BackspacePressed += OnBackspace;
    }

    public void Start() => _backend.Start();
    public void Stop() => _backend.Stop();

    private void OnCharTyped(char c)
    {
        if (!_enabled) return;
        // Just accumulate — word analysis happens on break
        _buffer.Push(c);
    }

    private void OnWordBreak(char breakChar) => FlushAndReplace(breakChar);

    private void OnEnter() => FlushAndReplace('\n');

    private void OnBackspace()
    {
        // Backspace typed — we can't know what got deleted exactly,
        // safest to clear our buffer to avoid desync
        _buffer.Clear();
    }

    private void FlushAndReplace(char breakChar)
    {
        if (!_enabled) return;
        var (direction, backspaces, replacement, original) = _buffer.Flush(breakChar);
        if (direction != SwitchDirection.None && backspaces > 0)
            _backend.ReplaceWord(backspaces, replacement, original, direction);
    }

    public void Dispose()
    {
        _backend.CharTyped -= OnCharTyped;
        _backend.WordBreakPressed -= OnWordBreak;
        _backend.EnterPressed -= OnEnter;
        _backend.BackspacePressed -= OnBackspace;
        _backend.Dispose();
    }
}
