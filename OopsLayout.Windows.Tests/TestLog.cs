using System.Runtime.CompilerServices;

namespace OopsLayout.Windows.Tests;

internal static class TestLog
{
    // Runs before any test: keep test hook installs out of the user's real
    // activity log, which they read to check a game session.
    [ModuleInitializer]
    internal static void Redirect() =>
        ActivityLog.Path = Path.Combine(Path.GetTempPath(), "oopslayout-tests-activity.log");
}
