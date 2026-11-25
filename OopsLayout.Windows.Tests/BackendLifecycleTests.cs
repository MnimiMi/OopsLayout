using Xunit;

namespace OopsLayout.Windows.Tests;

// These install a REAL global low-level keyboard hook for a few milliseconds.
// The test thread pumps no messages, so keystrokes typed while they run may
// lag briefly — harmless, but don't type during the run.
public class BackendLifecycleTests
{
    // Generous: a UIA call into a busy app (e.g. during a build) can take
    // several seconds before the watcher thread gets to exit.
    private static bool WaitUntil(Func<bool> cond, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!cond())
        {
            if (Environment.TickCount64 > deadline) return false;
            Thread.Sleep(20);
        }
        return true;
    }

    [Fact]
    public void Start_InstallsHook_Stop_RemovesHookAndWatcher()
    {
        using var backend = new WindowsKeyboardBackend();

        backend.Start();
        Assert.True(backend.IsHookInstalled);
        Assert.True(backend.IsPasswordWatcherAlive);

        backend.Stop();
        Assert.False(backend.IsHookInstalled);
        Assert.True(WaitUntil(() => !backend.IsPasswordWatcherAlive),
            "UIA watcher thread still running after Stop");
    }

    // A resume event posted just before the user paused must not bring the
    // hook back while paused (anti-cheat would see it).
    [Fact]
    public void RearmAfterStop_DoesNotReinstallHook()
    {
        using var backend = new WindowsKeyboardBackend();
        backend.Start();
        backend.Stop();

        backend.RearmHook();

        Assert.False(backend.IsHookInstalled);
    }

    [Fact]
    public void RearmWhileRunning_KeepsHookInstalled()
    {
        using var backend = new WindowsKeyboardBackend();
        backend.Start();

        backend.RearmHook();

        Assert.True(backend.IsHookInstalled);
        backend.Stop();
    }

    [Fact]
    public void RepeatedPauseResume_LeavesNoHookOrThreadBehind()
    {
        using var backend = new WindowsKeyboardBackend();
        for (int i = 0; i < 5; i++)
        {
            backend.Start();
            Assert.True(backend.IsHookInstalled);
            backend.Stop();
            Assert.False(backend.IsHookInstalled);
        }
        Assert.True(WaitUntil(() => !backend.IsPasswordWatcherAlive));
    }

    [Fact]
    public void StopTwice_AndDisposeAfterStop_AreSafe()
    {
        var backend = new WindowsKeyboardBackend();
        backend.Start();
        backend.Stop();
        backend.Stop();
        backend.Dispose();
        Assert.False(backend.IsHookInstalled);
    }
}
