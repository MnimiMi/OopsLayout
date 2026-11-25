using System.Text.Json;

namespace OopsLayout.Windows;

/// <summary>
/// The user's list of apps OopsLayout stays out of (games, etc.), persisted to
/// %AppData%\OopsLayout\apps.json and fed into the backend's exclusion check.
/// Entries are process names: "Wow", "Wow.exe" and "C:\…\Wow.exe" all mean the same.
/// </summary>
internal static class UserApps
{
    private sealed class Store
    {
        public List<string> Apps { get; set; } = new();
    }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OopsLayout");
    private static string FilePath => Path.Combine(Dir, "apps.json");

    /// <summary>The current list, sorted — for display/editing.</summary>
    public static IEnumerable<string> All =>
        WindowsKeyboardBackend.UserExcludedProcesses.OrderBy(a => a, StringComparer.Ordinal);

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) is { } s)
                WindowsKeyboardBackend.UserExcludedProcesses = Parse(s.Apps);
        }
        catch
        {
            // Corrupt/unreadable file — start empty rather than crash.
        }
    }

    /// <summary>Replace the whole list from freeform lines (one app per line).</summary>
    public static void SetApps(IEnumerable<string> lines)
    {
        var set = Parse(lines);
        WindowsKeyboardBackend.UserExcludedProcesses = set;
        ActivityLog.Write($"App list set: {(set.Count == 0 ? "(empty)" : string.Join(", ", set.Order()))}");
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(
                new Store { Apps = set.Order().ToList() },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort persistence; never crash the app over it.
        }
    }

    internal static HashSet<string> Parse(IEnumerable<string> lines)
    {
        var set = new HashSet<string>();
        foreach (var raw in lines)
        {
            var n = Normalize(raw);
            if (n.Length > 0) set.Add(n);
        }
        return set;
    }

    // "  \"C:\\Games\\Wow.EXE\"\r" → "wow" (lowercase process name, as the backend compares).
    internal static string Normalize(string raw)
    {
        var s = raw.Trim().Trim('"').Trim();
        if (s.Length == 0) return "";
        s = Path.GetFileName(s);
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s.Trim().ToLowerInvariant();
    }
}
