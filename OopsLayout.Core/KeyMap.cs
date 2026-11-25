namespace OopsLayout.Core;

/// <summary>Which Cyrillic layout the switcher targets (the "second" language).</summary>
public enum Cyrillic { Russian, Ukrainian }

/// <summary>
/// Bidirectional mapping between EN (QWERTY) and the RU / UK (ЙЦУКЕН) layouts.
/// Key   = character as typed in EN layout
/// Value = character it should be in the Cyrillic layout (and vice versa)
/// </summary>
public static class KeyMap
{
    // EN -> RU
    public static readonly Dictionary<char, char> EnToRu = new()
    {
        { 'q', 'й' }, { 'w', 'ц' }, { 'e', 'у' }, { 'r', 'к' }, { 't', 'е' },
        { 'y', 'н' }, { 'u', 'г' }, { 'i', 'ш' }, { 'o', 'щ' }, { 'p', 'з' },
        { '[', 'х' }, { ']', 'ъ' },
        { 'a', 'ф' }, { 's', 'ы' }, { 'd', 'в' }, { 'f', 'а' }, { 'g', 'п' },
        { 'h', 'р' }, { 'j', 'о' }, { 'k', 'л' }, { 'l', 'д' }, { ';', 'ж' },
        { '\'', 'э' },
        { 'z', 'я' }, { 'x', 'ч' }, { 'c', 'с' }, { 'v', 'м' }, { 'b', 'и' },
        { 'n', 'т' }, { 'm', 'ь' }, { ',', 'б' }, { '.', 'ю' },
        // uppercase
        { 'Q', 'Й' }, { 'W', 'Ц' }, { 'E', 'У' }, { 'R', 'К' }, { 'T', 'Е' },
        { 'Y', 'Н' }, { 'U', 'Г' }, { 'I', 'Ш' }, { 'O', 'Щ' }, { 'P', 'З' },
        { '{', 'Х' }, { '}', 'Ъ' },
        { 'A', 'Ф' }, { 'S', 'Ы' }, { 'D', 'В' }, { 'F', 'А' }, { 'G', 'П' },
        { 'H', 'Р' }, { 'J', 'О' }, { 'K', 'Л' }, { 'L', 'Д' }, { ':', 'Ж' },
        { '"', 'Э' },
        { 'Z', 'Я' }, { 'X', 'Ч' }, { 'C', 'С' }, { 'V', 'М' }, { 'B', 'И' },
        { 'N', 'Т' }, { 'M', 'Ь' }, { '<', 'Б' }, { '>', 'Ю' },
    };

    // EN -> UK: identical to RU except a few keys — ы→і, ъ→ї, э→є — plus ґ on the
    // '\' key (the Ukrainian ЙЦУКЕН layout).
    public static readonly Dictionary<char, char> EnToUk = BuildUk();

    private static Dictionary<char, char> BuildUk() => new(EnToRu)
    {
        ['s']  = 'і', ['S'] = 'І',  // і / І   (Russian had ы)
        [']']  = 'ї', ['}'] = 'Ї',  // ї / Ї   (Russian had ъ)
        ['\''] = 'є', ['"'] = 'Є',  // є / Є   (Russian had э)
        ['\\'] = 'ґ', ['|'] = 'Ґ',  // ґ / Ґ   (new)
    };

    // RU -> EN and UK -> EN (reverse of the above)
    public static readonly Dictionary<char, char> RuToEn =
        EnToRu.ToDictionary(kv => kv.Value, kv => kv.Key);
    public static readonly Dictionary<char, char> UkToEn =
        EnToUk.ToDictionary(kv => kv.Value, kv => kv.Key);

    // A char is an EN key if it maps under either Cyrillic layout (EnToUk is a
    // superset — it adds the '\' = ґ key that only Ukrainian uses).
    public static bool IsEnChar(char c) => EnToUk.ContainsKey(c);
    public static bool IsRuChar(char c) => RuToEn.ContainsKey(c);
    public static bool IsUkChar(char c) => UkToEn.ContainsKey(c);
    public static bool IsCyrChar(char c, Cyrillic target) =>
        target == Cyrillic.Ukrainian ? IsUkChar(c) : IsRuChar(c);

    public static string ConvertEnToRu(string text) => Map(text, EnToRu);
    public static string ConvertRuToEn(string text) => Map(text, RuToEn);
    public static string ConvertEnToUk(string text) => Map(text, EnToUk);
    public static string ConvertUkToEn(string text) => Map(text, UkToEn);

    public static string ConvertEnToCyr(string text, Cyrillic target) =>
        Map(text, target == Cyrillic.Ukrainian ? EnToUk : EnToRu);
    public static string ConvertCyrToEn(string text, Cyrillic target) =>
        Map(text, target == Cyrillic.Ukrainian ? UkToEn : RuToEn);

    private static string Map(string text, Dictionary<char, char> m)
        => new(text.Select(c => m.TryGetValue(c, out var r) ? r : c).ToArray());
}
