namespace OopsLayout.Core;

public enum SwitchDirection
{
    None,
    EnToRu,  // typed EN chars that should be Cyrillic (the active target)
    RuToEn   // typed Cyrillic chars that should be EN
}

public class WordBuffer
{
    private readonly List<char> _chars = new();

    private enum Lang { Unknown, En, Ru }

    /// <summary>
    /// The active Cyrillic target (Russian or Ukrainian) — the "second" language
    /// the switcher converts to/from. Set by the app (tray menu), persisted.
    /// </summary>
    public static Cyrillic Target = Cyrillic.Russian;

    // Language of the surrounding text, inferred from recent confident (2+ letter)
    // words. (Ru here means "the active Cyrillic language", RU or UK.) Used to
    // resolve cases bigrams can't judge alone — single letters and short words.
    private Lang _context = Lang.Unknown;

    // The previous word, kept only while it's a re-fix candidate: we left it
    // unconverted, it's a single-script word, and it ended with a plain space.
    // When a following word reveals the language, we go back and fix this one too.
    private (string raw, char breakChar)? _prev;

    // Common one-letter words, in their *correct* (converted) form.
    private static readonly HashSet<string> RuSingleWords = new() { "я", "и", "а", "в", "к", "с", "о", "у" };
    private static readonly HashSet<string> UkSingleWords = new() { "я", "і", "у", "в", "з", "о", "а", "й" };
    private static readonly HashSet<string> EnSingleWords = new() { "a", "i" };

    private static HashSet<string> CyrSingleWords =>
        Target == Cyrillic.Ukrainian ? UkSingleWords : RuSingleWords;

    // Real short words that must NEVER be auto-converted, even when the bigram
    // score narrowly favours the other layout. Stored lowercase. Add your own.
    private static readonly HashSet<string> KeepWordsRu = new() { "ща", "ру", "чё", "че", "оч", "хз" };
    private static readonly HashSet<string> KeepWordsUk = new() { "як", "це", "бо", "ну" };
    private static readonly HashSet<string> KeepWordsEn = new()
    {
        "xml", "html", "css", "json", "sql", "url", "api", "php", "http", "https"
    };

    // User-added exceptions, loaded by the app (see UserExceptions) and merged
    // with the built-in lists. UserKeepWordsRu holds the user's *Cyrillic* words
    // (they apply to whichever Cyrillic target is active). Read on the hook/UI
    // thread and written from the settings form — same thread, so no locking.
    public static HashSet<string> UserKeepWordsRu = new();
    public static HashSet<string> UserKeepWordsEn = new();

    private static bool IsKeepCyr(string w) =>
        (Target == Cyrillic.Ukrainian ? KeepWordsUk : KeepWordsRu).Contains(w) || UserKeepWordsRu.Contains(w);
    private static bool IsKeepEn(string w) => KeepWordsEn.Contains(w) || UserKeepWordsEn.Contains(w);

    private static double ScoreCyr(string word) =>
        Target == Cyrillic.Ukrainian ? Bigrams.ScoreUk(word) : Bigrams.ScoreRu(word);

    /// <summary>Append a character to the current word (the backend owns word breaks).</summary>
    public void Push(char c) => _chars.Add(c);

    /// <summary>
    /// Analyse the current word and reset the buffer. Returns the replacement
    /// plan: how many characters to delete, what to type in their place, and
    /// what the deleted text originally was (for undo). The trailing break char
    /// is re-emitted by the backend, not included here. The plan may span the
    /// previous word too — see <see cref="_prev"/>.
    /// </summary>
    public (SwitchDirection direction, int backspaces, string replacement, string original) Flush(char breakChar)
    {
        var result = Analyze(breakChar);
        _chars.Clear();
        return result;
    }

    public void Clear()
    {
        _chars.Clear();
        _prev = null; // editing breaks the previous-word adjacency
    }

    public int Length => _chars.Count;

    private const double SwitchMargin = 1.0;
    private const int ShortWordMaxLen = 2;
    private const double StrongMargin = 2.5;

    // When re-fixing the previous word the language is already CONFIRMED by the
    // adjacent word, so we only need it to look even slightly more like that
    // language. Catches borderline shorts while leaving genuine English alone.
    private const double ReFixMargin = 0.0;

    private (SwitchDirection, int, string, string) Analyze(char breakChar)
    {
        if (_chars.Count == 0)
        {
            _prev = null; // multi-break gap (e.g. double space) — drop adjacency
            return (SwitchDirection.None, 0, string.Empty, string.Empty);
        }

        var word   = new string(_chars.ToArray());
        bool allEn  = _chars.All(KeyMap.IsEnChar);
        bool allCyr = !allEn && _chars.All(c => KeyMap.IsCyrChar(c, Target));

        var dir = Decide(word, allEn, allCyr, _context);

        var planDir = SwitchDirection.None;
        int backspaces = 0;
        var replacement = string.Empty;
        var original = string.Empty;

        if (dir != SwitchDirection.None)
        {
            var converted = Convert(word, dir);
            planDir = dir;
            backspaces = word.Length;
            replacement = converted;
            original = word;

            // If the now-established context would flip the previous word the
            // same way, re-fix it too as one combined replacement across the space.
            var newContext = dir == SwitchDirection.EnToRu ? Lang.Ru : Lang.En;
            if (_prev is { } prev && prev.breakChar == ' ')
            {
                bool pEn  = prev.raw.All(KeyMap.IsEnChar);
                bool pCyr = !pEn && prev.raw.All(c => KeyMap.IsCyrChar(c, Target));
                if (Decide(prev.raw, pEn, pCyr, newContext, ReFixMargin) == dir)
                {
                    backspaces = prev.raw.Length + 1 + word.Length;
                    replacement = Convert(prev.raw, dir) + " " + converted;
                    original = prev.raw + " " + word;
                }
            }
        }

        // Update context from this word.
        if (dir != SwitchDirection.None)
            _context = dir == SwitchDirection.EnToRu ? Lang.Ru : Lang.En;
        else if (_chars.Count >= 2 && (allEn || allCyr))
            _context = allEn ? Lang.En : Lang.Ru;

        // Remember this word as a re-fix candidate only if we left it unconverted
        // and it's a single-script word; otherwise the chain is broken.
        _prev = dir == SwitchDirection.None && (allEn || allCyr)
            ? (word, breakChar)
            : null;

        return (planDir, backspaces, replacement, original);
    }

    private static string Convert(string word, SwitchDirection dir) =>
        dir == SwitchDirection.EnToRu
            ? KeyMap.ConvertEnToCyr(word, Target)
            : KeyMap.ConvertCyrToEn(word, Target);

    /// <summary>
    /// A word is "all caps" if it has a cased letter and every letter is
    /// uppercase (XML, API) — almost always an acronym/constant typed on purpose,
    /// so we leave it alone. Title-case ("Ghbdtn") is not.
    /// </summary>
    private static bool IsAllCaps(string word)
    {
        bool hasLetter = false;
        foreach (var c in word)
        {
            if (!char.IsLetter(c)) continue;
            hasLetter = true;
            if (!char.IsUpper(c)) return false;
        }
        return hasLetter;
    }

    /// <summary>Pure decision for one word under a given context (no side effects).</summary>
    private static SwitchDirection Decide(string word, bool allEn, bool allCyr, Lang context,
        double? marginOverride = null)
    {
        // ALL-CAPS words (2+ letters) are left untouched — acronyms, constants.
        if (word.Length >= 2 && IsAllCaps(word)) return SwitchDirection.None;

        if (allEn)
        {
            var asCyr = KeyMap.ConvertEnToCyr(word, Target);
            if (word.Length == 1)
                return context != Lang.En && CyrSingleWords.Contains(asCyr)
                    ? SwitchDirection.EnToRu : SwitchDirection.None;
            // Confidently Cyrillic and this maps to a known Cyrillic word
            // (e.g. "of" → "ща"): force it, even though "of" looks English.
            if (context == Lang.Ru && IsKeepCyr(asCyr))
                return SwitchDirection.EnToRu;
            // Keep-list protects only in forward analysis; during a re-fix the
            // adjacent word has already confirmed the language.
            if (marginOverride is null && IsKeepEn(word.ToLowerInvariant()))
                return SwitchDirection.None;
            var margin = marginOverride
                ?? (word.Length <= ShortWordMaxLen && context == Lang.En ? StrongMargin : SwitchMargin);
            return ScoreCyr(asCyr) - Bigrams.ScoreEn(word) > margin
                ? SwitchDirection.EnToRu : SwitchDirection.None;
        }
        if (allCyr)
        {
            var asEn = KeyMap.ConvertCyrToEn(word, Target);
            if (word.Length == 1)
                return context != Lang.Ru && EnSingleWords.Contains(asEn)
                    ? SwitchDirection.RuToEn : SwitchDirection.None;
            // Confidently English and this maps to a known English word
            // (e.g. "чьд" → "xml"): force it, even against bigrams.
            if (context == Lang.En && IsKeepEn(asEn))
                return SwitchDirection.RuToEn;
            if (marginOverride is null && IsKeepCyr(word.ToLowerInvariant()))
                return SwitchDirection.None;
            var margin = marginOverride
                ?? (word.Length <= ShortWordMaxLen && context == Lang.Ru ? StrongMargin : SwitchMargin);
            return Bigrams.ScoreEn(asEn) - ScoreCyr(word) > margin
                ? SwitchDirection.RuToEn : SwitchDirection.None;
        }
        return SwitchDirection.None;
    }
}
