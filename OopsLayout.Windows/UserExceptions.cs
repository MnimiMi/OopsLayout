using System.Text.Json;
using OopsLayout.Core;

namespace OopsLayout.Windows;

/// <summary>
/// Persists the user's own keep-words to
/// %AppData%\OopsLayout\exceptions.json and feeds them into the Core keep-lists.
/// Words are routed to the RU or EN list by script. Mirrors UserExceptions.swift.
/// </summary>
internal static class UserExceptions
{
    private sealed class Store
    {
        public List<string> Ru { get; set; } = new();
        public List<string> En { get; set; } = new();
    }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OopsLayout");
    private static string FilePath => Path.Combine(Dir, "exceptions.json");

    private static HashSet<string> _ru = new();
    private static HashSet<string> _en = new();

    /// <summary>All user words across both scripts, sorted — for display/editing.</summary>
    public static IEnumerable<string> All => _ru.Concat(_en).OrderBy(w => w, StringComparer.Ordinal);

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) is { } s)
            {
                _ru = new HashSet<string>(s.Ru);
                _en = new HashSet<string>(s.En);
            }
        }
        catch
        {
            // Corrupt/unreadable file — start empty rather than crash.
        }
        Apply();
    }

    /// <summary>
    /// Replace the whole set from a freeform list of words (one per line in the
    /// settings window), routing each by script.
    /// </summary>
    public static void SetWords(IEnumerable<string> words)
    {
        _ru = new HashSet<string>();
        _en = new HashSet<string>();
        foreach (var raw in words)
        {
            var w = raw.Trim().ToLowerInvariant();
            if (w.Length == 0) continue;
            if (w.Any(IsCyrillic)) _ru.Add(w);
            else if (w.Any(char.IsLetter)) _en.Add(w);
        }
        Save();
        Apply();
    }

    private static bool IsCyrillic(char c) => c >= 0x0400 && c <= 0x04FF;

    private static void Apply()
    {
        WordBuffer.UserKeepWordsRu = _ru;
        WordBuffer.UserKeepWordsEn = _en;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(
                new Store { Ru = _ru.ToList(), En = _en.ToList() },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort persistence; never crash the app over it.
        }
    }
}
