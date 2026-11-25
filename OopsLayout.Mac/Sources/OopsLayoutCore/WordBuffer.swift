import Foundation

public final class WordBuffer {
    private var chars: [Character] = []

    private enum Lang { case unknown, en, ru }

    // Language of the surrounding text, inferred from recent confident (2+ letter)
    // words. Used to resolve cases bigrams can't judge on their own:
    //  • single letters (я, и, а, в, к, с, о, у / a, i),
    //  • short words that are valid in both layouts (e.g. "ру" vs "he").
    private var context: Lang = .unknown

    // The previous word, kept only while it's a re-fix candidate: we left it
    // unconverted, it's a single-script word, and it ended with a plain space.
    // When a following word reveals the language, we go back and fix this one too
    // (handles a phrase-leading word typed before any context existed).
    private var prev: (raw: String, breakChar: Character)?

    /// The active Cyrillic target (Russian or Ukrainian) — the "second" language
    /// the switcher converts to/from. Set by the app (menu) and persisted.
    public static var target: Cyrillic = .russian

    // Common one-letter words, in their *correct* (converted) form.
    private static let ruSingleWords: Set<String> = ["я", "и", "а", "в", "к", "с", "о", "у"]
    private static let ukSingleWords: Set<String> = ["я", "і", "у", "в", "з", "о", "а", "й"]
    private static let enSingleWords: Set<String> = ["a", "i"]

    private static var cyrSingleWords: Set<String> {
        target == .ukrainian ? ukSingleWords : ruSingleWords
    }

    // Real short words that must NEVER be auto-converted, even when the bigram
    // score narrowly favours the other layout. Stored lowercase. Add your own.
    private static let keepWordsRu: Set<String> = ["ща", "ру", "чё", "че", "оч", "хз"]
    private static let keepWordsUk: Set<String> = ["як", "це", "бо", "ну"]
    private static let keepWordsEn: Set<String> = [
        "xml", "html", "css", "json", "sql", "url", "api", "php", "http", "https",
    ]

    // User-added exceptions, loaded from disk at launch and merged with the
    // built-in lists. Populated by the app (see UserExceptions). Lowercase.
    // Read on the event-tap callback and written from the settings window — both
    // run on the main thread, so no locking is needed. Keep the tap on the main
    // run loop if you ever change that.
    public static var userKeepWordsRu: Set<String> = []
    public static var userKeepWordsEn: Set<String> = []

    // Optional diagnostic sink. The app wires this to DebugLog so we can see the
    // engine's actual decision (scores, context, allEn/allRu) — not just the
    // backend's "no replacement". OFF unless the app sets it. Core has no
    // dependency on the app's logger, so this stays an injectable closure.
    public static var log: ((String) -> Void)?

    // userKeepWordsRu holds the user's *Cyrillic* keep-words — they apply to
    // whichever Cyrillic target is active.
    private static func isKeepCyr(_ w: String) -> Bool {
        (target == .ukrainian ? keepWordsUk : keepWordsRu).contains(w) || userKeepWordsRu.contains(w)
    }
    private static func isKeepEn(_ w: String) -> Bool { keepWordsEn.contains(w) || userKeepWordsEn.contains(w) }

    private static func scoreCyr(_ word: String) -> Double {
        target == .ukrainian ? Bigrams.scoreUk(word) : Bigrams.scoreRu(word)
    }

    public init() {}

    /// Append a character to the current word (the backend owns word breaks).
    public func push(_ c: Character) { chars.append(c) }

    /// Analyse the current word and reset the buffer. Returns the replacement
    /// plan: how many characters to delete and what to type in their place (the
    /// trailing break char is re-emitted by the backend, not included here). The
    /// plan may span the previous word too — see `prev`.
    public func flush(_ breakChar: Character) -> (direction: SwitchDirection, backspaces: Int, replacement: String) {
        let result = analyze(breakChar)
        chars.removeAll()
        return result
    }

    public func clear() {
        chars.removeAll()
        prev = nil   // editing breaks the previous-word adjacency
    }

    public var length: Int { chars.count }

    private static let switchMargin = 1.0
    private static let shortWordMaxLen = 2
    private static let strongMargin = 2.5

    // When re-fixing the previous word the language is already CONFIRMED by the
    // adjacent word, so we only need it to look even slightly more like that
    // language. This catches borderline shorts (ну +0.07, вы +0.63) while still
    // leaving genuine English forms (of −1.06) alone.
    private static let reFixMargin = 0.0

    private func analyze(_ breakChar: Character) -> (SwitchDirection, Int, String) {
        if chars.isEmpty {
            prev = nil   // multi-break gap (e.g. double space) — drop adjacency
            return (.none, 0, "")
        }

        let word = String(chars)
        let allEn = chars.allSatisfy(KeyMap.isEnChar)
        let allCyr = !allEn && chars.allSatisfy { KeyMap.isCyrChar($0, WordBuffer.target) }

        let dir = WordBuffer.decide(word, allEn, allCyr, context)

        // Diagnostic: show exactly why we did (or didn't) switch. The MIXED case
        // is the silent "doesn't switch" culprit — a word that straddled an async
        // layout change is neither allEn nor allCyr, so decide() bails to .none.
        if let log = WordBuffer.log {
            let detail: String
            if allEn {
                let asCyr = KeyMap.convertEnToCyr(word, WordBuffer.target)
                detail = "asCyr='\(asCyr)' scoreCyr=\(WordBuffer.scoreCyr(asCyr)) scoreEn=\(Bigrams.scoreEn(word))"
            } else if allCyr {
                let asEn = KeyMap.convertCyrToEn(word, WordBuffer.target)
                detail = "asEn='\(asEn)' scoreEn=\(Bigrams.scoreEn(asEn)) scoreCyr=\(WordBuffer.scoreCyr(word))"
            } else {
                detail = "MIXED-SCRIPT — neither allEn nor allCyr, forced .none"
            }
            log("analyze word='\(word)' target=\(WordBuffer.target) allEn=\(allEn) allCyr=\(allCyr) context=\(context) -> \(dir) [\(detail)]")
        }

        var planDir = SwitchDirection.none
        var backspaces = 0
        var replacement = ""

        if dir != .none {
            let converted = WordBuffer.convert(word, dir)
            planDir = dir
            backspaces = word.count
            replacement = converted

            // If the now-established context would flip the previous word the
            // same way, re-fix it too as one combined replacement across the space.
            let newContext: Lang = dir == .enToRu ? .ru : .en
            if let p = prev, p.breakChar == " " {
                let pEn = p.raw.allSatisfy(KeyMap.isEnChar)
                let pCyr = !pEn && p.raw.allSatisfy { KeyMap.isCyrChar($0, WordBuffer.target) }
                if WordBuffer.decide(p.raw, pEn, pCyr, newContext, WordBuffer.reFixMargin) == dir {
                    backspaces = p.raw.count + 1 + word.count
                    replacement = WordBuffer.convert(p.raw, dir) + " " + converted
                }
            }
        }

        // Update context from this word.
        if dir != .none {
            context = dir == .enToRu ? .ru : .en
        } else if chars.count >= 2 && (allEn || allCyr) {
            context = allEn ? .en : .ru
        }

        // Remember this word as a re-fix candidate only if we left it unconverted
        // and it's a single-script word; otherwise the chain is broken.
        prev = (dir == .none && (allEn || allCyr)) ? (word, breakChar) : nil

        return (planDir, backspaces, replacement)
    }

    private static func convert(_ word: String, _ dir: SwitchDirection) -> String {
        dir == .enToRu
            ? KeyMap.convertEnToCyr(word, target)
            : KeyMap.convertCyrToEn(word, target)
    }

    /// A word is "all caps" if it has a cased letter and every letter is
    /// uppercase (XML, API, MAX_SIZE) — almost always an acronym / constant
    /// typed on purpose, so we leave it alone. Title-case ("Ghbdtn") is not.
    private static func isAllCaps(_ word: String) -> Bool {
        var hasLetter = false
        for c in word where c.isLetter {
            hasLetter = true
            if !c.isUppercase { return false }
        }
        return hasLetter
    }

    /// Pure decision for one word under a given context (no side effects).
    private static func decide(_ word: String, _ allEn: Bool, _ allCyr: Bool,
                              _ context: Lang, _ marginOverride: Double? = nil) -> SwitchDirection {
        // ALL-CAPS words (2+ letters) are left untouched — acronyms, constants,
        // keywords. A single capital (sentence start) is not affected.
        if word.count >= 2 && isAllCaps(word) { return .none }

        if allEn {
            let asCyr = KeyMap.convertEnToCyr(word, target)
            if word.count == 1 {
                return context != .en && cyrSingleWords.contains(asCyr) ? .enToRu : .none
            }
            // We're confidently in the Cyrillic language and this maps to a known
            // word (e.g. "of" → "ща"): force it, even though "of" looks English.
            if context == .ru && isKeepCyr(asCyr) {
                return .enToRu
            }
            // Keep-list protection applies only in forward analysis. During a
            // re-fix (marginOverride set) the adjacent word has already confirmed
            // the language, so a keep-word follows it instead of being protected.
            if marginOverride == nil && isKeepEn(word.lowercased()) {
                return .none
            }
            let margin = marginOverride
                ?? (word.count <= shortWordMaxLen && context == .en ? strongMargin : switchMargin)
            return scoreCyr(asCyr) - Bigrams.scoreEn(word) > margin ? .enToRu : .none
        }
        if allCyr {
            let asEn = KeyMap.convertCyrToEn(word, target)
            if word.count == 1 {
                return context != .ru && enSingleWords.contains(asEn) ? .ruToEn : .none
            }
            // Confidently in English and this maps to a known English word
            // (e.g. "чьд" → "xml"): force it, even against bigrams.
            if context == .en && isKeepEn(asEn) {
                return .ruToEn
            }
            // Keep-list protection applies only in forward analysis; during a
            // re-fix the adjacent word confirmed the language, so "ща" before an
            // English word becomes "of" (ща hello -> of hello).
            if marginOverride == nil && isKeepCyr(word.lowercased()) {
                return .none
            }
            let margin = marginOverride
                ?? (word.count <= shortWordMaxLen && context == .ru ? strongMargin : switchMargin)
            return Bigrams.scoreEn(asEn) - scoreCyr(word) > margin ? .ruToEn : .none
        }
        return .none
    }
}
