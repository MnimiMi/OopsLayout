namespace OopsLayout.Windows;

/// <summary>
/// Plain-text log of what the app does with the system — hook installed or
/// removed, UIA watcher started or stopped, pause/resume and who asked for it,
/// game launched/exited — so a session can be checked afterwards ("was the
/// hook really off while the game ran?"). NEVER log typed text here.
/// File: %AppData%\OopsLayout\activity.log. Written by both the tray app and
/// the short-lived --run launcher, so the file is opened shared per line.
/// </summary>
internal static class ActivityLog
{
    // Settable only so tests can keep their hook installs out of the real log.
    public static string Path { get; internal set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OopsLayout", "activity.log");

    private const long MaxLogBytes = 1_000_000; // ~1 MB; start fresh past this

    private static readonly object Lock = new();
    private static readonly string Who = $"{Environment.ProcessId,6}";

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Who}] {message}{Environment.NewLine}";
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var info = new FileInfo(Path);
                if (info.Exists && info.Length > MaxLogBytes)
                    File.Delete(Path);

                // Shared open: the other process (app or launcher) may be
                // appending at the same moment.
                using var fs = new FileStream(Path, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                var bytes = System.Text.Encoding.UTF8.GetBytes(line);
                fs.Write(bytes, 0, bytes.Length);
            }
            catch
            {
                // Logging must never itself throw.
            }
        }
    }
}
