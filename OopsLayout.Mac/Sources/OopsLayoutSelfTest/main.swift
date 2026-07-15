import Foundation
import OopsLayoutCore

// Minimal headless test harness (XCTest needs full Xcode; this runs under
// Command Line Tools and in CI). Exits non-zero if any check fails.

var failures = 0

func check(_ cond: Bool, _ msg: String) {
    if cond {
        print("  ok: \(msg)")
    } else {
        print("FAIL: \(msg)")
        failures += 1
    }
}

func eq<T: Equatable>(_ a: T, _ b: T, _ msg: String) {
    check(a == b, "\(msg)  (got \(a), want \(b))")
}

// A fake backend that records replacement requests instead of touching the OS.
final class FakeBackend: KeyboardBackend {
    var onCharTyped: ((Character) -> Void)?
    var onEnterPressed: (() -> Void)?
    var onBackspacePressed: (() -> Void)?
    var onWordBreakPressed: ((Character) -> Void)?

    var lastReplacement: (count: Int, text: String, dir: SwitchDirection)?

    func start() {}
    func stop() {}
    func replaceWord(count: Int, newText: String, direction: SwitchDirection) {
        lastReplacement = (count, newText, direction)
    }

    func type(_ word: String, breakChar: Character = " ") {
        lastReplacement = nil
        for c in word { onCharTyped?(c) }
        onWordBreakPressed?(breakChar)
    }
}

print("KeyMap:")
eq(KeyMap.convertEnToRu("ghbdtn"), "привет", "ghbdtn -> привет")
eq(KeyMap.convertEnToRu("hello"), "руддщ", "hello -> руддщ")
eq(KeyMap.convertRuToEn("руддщ"), "hello", "руддщ -> hello")
eq(KeyMap.convertRuToEn("привет"), "ghbdtn", "привет -> ghbdtn")
eq(KeyMap.convertRuToEn(KeyMap.convertEnToRu("ghbdtn")), "ghbdtn", "round trip")

print("Bigrams:")
check(Bigrams.scoreRu("привет") - Bigrams.scoreEn("ghbdtn") > 1.0, "привет beats ghbdtn under RU")
check(Bigrams.scoreEn("hello") - Bigrams.scoreRu("руддщ") > 1.0, "hello beats руддщ under EN")

print("Engine:")
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("ghbdtn")
    eq(b.lastReplacement?.text, "привет", "fixes EN-typed Russian")
    eq(b.lastReplacement?.dir, .enToRu, "direction enToRu")
    eq(b.lastReplacement?.count, 6, "count = 6")
}
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("руддщ")
    eq(b.lastReplacement?.text, "hello", "fixes RU-typed English")
    eq(b.lastReplacement?.dir, .ruToEn, "direction ruToEn")
}
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("hello")
    check(b.lastReplacement == nil, "leaves real English alone")
}
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start(); e.enabled = false
    b.type("ghbdtn")
    check(b.lastReplacement == nil, "disabled does nothing")
}

print("Single letters & short words (no prior context):")
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("z")   // EN 'z' -> RU 'я'
    eq(b.lastReplacement?.text, "я", "lone 'z' -> я")
}
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("f")   // EN 'f' -> RU 'а'
    eq(b.lastReplacement?.text, "а", "lone 'f' -> а")
}
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("я")   // already correct Russian — should NOT switch
    check(b.lastReplacement == nil, "real 'я' stays")
}

print("Keep-list (protected short words):")
do {
    // "ща" and "ру" are real Russian slang — never converted, even with no context.
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("ща")
    check(b.lastReplacement == nil, "'ща' stays (keep-list, no context)")
}
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("ру")
    check(b.lastReplacement == nil, "'ру' stays (keep-list, not 'he')")
}
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("xml")   // would otherwise become "чьд"
    check(b.lastReplacement == nil, "'xml' stays (keep-list)")
}
do {
    // User-added exception protects a word that would otherwise convert.
    WordBuffer.userKeepWordsRu = ["шт"]
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("шт")    // normally -> "in"; user exception keeps it
    check(b.lastReplacement == nil, "user exception 'шт' kept")
    WordBuffer.userKeepWordsRu = []
}

print("Uppercase handling:")
do {
    // ALL-CAPS words are left alone (acronyms/constants), even gibberish ones.
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("GHBDTN")
    check(b.lastReplacement == nil, "'GHBDTN' (all caps) left untouched")
}
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("API")
    check(b.lastReplacement == nil, "'API' acronym left untouched")
}
do {
    // Title case (sentence start) still converts — only the first letter is caps.
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("Ghbdtn")
    eq(b.lastReplacement?.text, "Привет", "'Ghbdtn' -> Привет (title case still converts)")
}

print("Short-word context guard:")
do {
    // A short word NOT on the keep-list still respects context.
    // "шт" -> "in" scores 2.249: converts with no context, but the strong
    // margin (2.5) holds it back inside a Russian context.
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("привет")           // sets Russian context
    b.type("шт")
    check(b.lastReplacement == nil, "'шт' stays in Russian context (guard)")
}
do {
    // Same "шт" with no prior context DOES convert to "in".
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("шт")
    eq(b.lastReplacement?.text, "in", "'шт' -> in with no context")
}
do {
    // A strong-signal short flip still happens even against context.
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("привет")           // Russian context
    b.type("rj")               // strong: -> 'ко'
    eq(b.lastReplacement?.text, "ко", "strong 'rj' -> ко still flips in RU context")
}

print("Keep-list forcing & retroactive re-fix:")
do {
    // Confirmed Russian context + "of" maps to keep-word "ща" -> force the flip,
    // even though "of" looks English to bigrams.
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("привет")           // Russian context
    b.type("of")
    eq(b.lastReplacement?.text, "ща", "forced 'of' -> ща in Russian context")
    eq(b.lastReplacement?.count, 2, "forced flip deletes just 'of'")
}
do {
    // Phrase-leading "of" left alone (no context yet); the next word converts
    // and re-fixes "of" too, as one combined replacement across the space.
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("of")               // stays for now (no context)
    check(b.lastReplacement == nil, "leading 'of' left alone first")
    b.type("ghbdtn")           // -> привет, establishes RU, re-fixes 'of'
    eq(b.lastReplacement?.text, "ща привет", "retroactive: 'of ghbdtn' -> 'ща привет'")
    eq(b.lastReplacement?.count, 9, "re-fix spans prev word + space + current")
}
do {
    // Mirror case: keep-word "ща" before an English word — the English second
    // word confirms English, so "ща" re-fixes to "of" (ща hello -> of hello).
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("ща")               // stays for now (keep-list, no opposing context)
    check(b.lastReplacement == nil, "leading 'ща' left alone first")
    b.type("руддщ")            // -> hello, confirms EN, re-fixes 'ща' -> 'of'
    eq(b.lastReplacement?.text, "of hello", "retroactive: 'ща руддщ' -> 'of hello'")
}

print("Ukrainian target:")
WordBuffer.target = .ukrainian
eq(KeyMap.convertEnToUk("ghbdsn"), "привіт", "ghbdsn -> привіт (UK; s->і)")
eq(KeyMap.convertUkToEn("привіт"), "ghbdsn", "привіт -> ghbdsn (UK)")
eq(KeyMap.convertEnToUk("'"), "є", "apostrophe -> є (UK)")
eq(KeyMap.convertEnToUk("]"), "ї", "] -> ї (UK)")
check(KeyMap.isUkChar("і"), "і is a UK char")
check(!KeyMap.isRuChar("і"), "і is NOT a RU char")
do {
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("ghbdsn")          // -> привіт under the Ukrainian target
    eq(b.lastReplacement?.text, "привіт", "UK: ghbdsn -> привіт")
    eq(b.lastReplacement?.dir, SwitchDirection.enToRu, "UK: direction is enToRu")
}
do {
    // Cyrillic→EN still works under the UK target (руддщ shares keys with RU/UK).
    let b = FakeBackend(); let e = SwitcherEngine(backend: b); e.start()
    b.type("руддщ")
    eq(b.lastReplacement?.text, "hello", "UK target: руддщ -> hello")
}
WordBuffer.target = .russian   // restore default

print("")
if failures == 0 {
    print("ALL PASSED")
} else {
    print("\(failures) FAILURE(S)")
    exit(1)
}
