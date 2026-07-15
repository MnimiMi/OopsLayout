namespace OopsLayout.Windows;

/// <summary>
/// Minimal crash/error logger. Anything thrown inside the global keyboard hook
/// would otherwise terminate the whole process, so we catch and record it here.
/// </summary>
internal static class CrashLog
{
    public static readonly string Path =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oopslayout-error.log");

    private const long MaxLogBytes = 1_000_000; // ~1 MB; start fresh past this

    public static void Write(Exception? ex)
    {
        try
        {
            if (System.IO.File.Exists(Path) && new System.IO.FileInfo(Path).Length > MaxLogBytes)
                System.IO.File.Delete(Path);

            System.IO.File.AppendAllText(Path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never itself throw.
        }
    }
}
