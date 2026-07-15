using System.Text.Json;
using OopsLayout.Core;

namespace OopsLayout.Windows;

/// <summary>
/// Persists user preferences to %AppData%\OopsLayout\settings.json. Currently
/// just the Cyrillic target (Russian / Ukrainian).
/// </summary>
internal static class Settings
{
    private sealed class Store
    {
        public string Target { get; set; } = "ru";
    }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OopsLayout", "settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) is { } s)
            {
                WordBuffer.Target = s.Target.Equals("uk", StringComparison.OrdinalIgnoreCase)
                    ? Cyrillic.Ukrainian
                    : Cyrillic.Russian;
            }
        }
        catch
        {
            // Unreadable settings — keep the default.
        }
    }

    public static void SaveTarget(Cyrillic target)
    {
        WordBuffer.Target = target;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(
                new Store { Target = target == Cyrillic.Ukrainian ? "uk" : "ru" },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}
