using OopsLayout.Core;
using Xunit;

namespace OopsLayout.Core.Tests;

public class SwitcherEngineTests
{
    private sealed class FakeBackend : IKeyboardBackend
    {
        public event Action<char>? CharTyped;
        public event Action? EnterPressed;
        public event Action? BackspacePressed;
        public event Action<char>? WordBreakPressed;

        public bool Running { get; private set; }
        public List<(int count, string text, string original, SwitchDirection dir)> Replacements { get; } = new();

        public void Start() => Running = true;
        public void Stop() => Running = false;
        public void Dispose() => Stop();

        public void ReplaceWord(int count, string newText, string originalText, SwitchDirection direction) =>
            Replacements.Add((count, newText, originalText, direction));

        public void Type(string s) { foreach (var c in s) CharTyped?.Invoke(c); }
        public void Break(char c) => WordBreakPressed?.Invoke(c);
        public void Enter() => EnterPressed?.Invoke();
        public void Backspace() => BackspacePressed?.Invoke();
    }

    [Fact]
    public void WrongLayoutWord_IsReplaced()
    {
        var backend = new FakeBackend();
        using var engine = new SwitcherEngine(backend);

        backend.Type("ghbdtn");
        backend.Break(' ');

        var r = Assert.Single(backend.Replacements);
        Assert.Equal((6, "привет", "ghbdtn", SwitchDirection.EnToRu), r);
    }

    [Fact]
    public void StartStop_AreForwardedToBackend()
    {
        var backend = new FakeBackend();
        using var engine = new SwitcherEngine(backend);

        engine.Start();
        Assert.True(backend.Running);
        engine.Stop();
        Assert.False(backend.Running);
    }

    // A word half-typed before pausing must not be glued to what is typed
    // after resuming: the backspace count would then reach into text the
    // engine never saw (e.g. typed in a game while paused) and delete it.
    [Fact]
    public void PauseResume_DropsWordTypedBeforePause()
    {
        var backend = new FakeBackend();
        using var engine = new SwitcherEngine(backend);
        engine.Start();

        backend.Type("ghbdtn");
        engine.Stop();
        engine.Start();
        backend.Break(' ');

        Assert.Empty(backend.Replacements);
    }

    [Fact]
    public void PauseResume_PartialWordIsNotMergedWithNewTyping()
    {
        var backend = new FakeBackend();
        using var engine = new SwitcherEngine(backend);
        engine.Start();

        backend.Type("ghb");
        engine.Stop();
        engine.Start();
        backend.Type("dtn");
        backend.Break(' ');

        // Whatever the engine decides about "dtn", it may only touch those 3 chars.
        Assert.All(backend.Replacements, r => Assert.True(r.count <= 3, $"backspaces={r.count}"));
    }

    // The previous-word re-fix deletes back across a space; that previous word
    // must not survive a pause either.
    [Fact]
    public void PauseResume_PreviousWordIsNotReFixed()
    {
        var backend = new FakeBackend();
        using var engine = new SwitcherEngine(backend);
        engine.Start();

        backend.Type("ns");          // "ты" typed in EN layout, left as-is (short)
        backend.Break(' ');
        engine.Stop();
        engine.Start();
        backend.Type("ghbdtn");
        backend.Break(' ');

        Assert.All(backend.Replacements, r => Assert.True(r.count <= 6, $"backspaces={r.count}"));
    }

    [Fact]
    public void Disabled_DoesNotReplace()
    {
        var backend = new FakeBackend();
        using var engine = new SwitcherEngine(backend) { Enabled = false };

        backend.Type("ghbdtn");
        backend.Break(' ');

        Assert.Empty(backend.Replacements);
    }

    [Fact]
    public void Backspace_ClearsBuffer()
    {
        var backend = new FakeBackend();
        using var engine = new SwitcherEngine(backend);

        backend.Type("ghbdtn");
        backend.Backspace();
        backend.Break(' ');

        Assert.Empty(backend.Replacements);
    }

    [Fact]
    public void Dispose_UnsubscribesAndStopsBackend()
    {
        var backend = new FakeBackend();
        var engine = new SwitcherEngine(backend);
        engine.Start();
        engine.Dispose();

        Assert.False(backend.Running);
        backend.Type("ghbdtn");
        backend.Break(' ');
        Assert.Empty(backend.Replacements);
    }
}
