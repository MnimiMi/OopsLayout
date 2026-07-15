import Foundation

/// Which Cyrillic layout the switcher targets (the "second" language).
public enum Cyrillic { case russian, ukrainian }

/// Bidirectional mapping between EN (QWERTY) and the RU / UK (ЙЦУКЕН) layouts.
/// Key   = character as typed in EN layout
/// Value = character it should be in the Cyrillic layout (and vice versa)
public enum KeyMap {
    // EN -> RU
    public static let enToRu: [Character: Character] = [
        "q": "й", "w": "ц", "e": "у", "r": "к", "t": "е",
        "y": "н", "u": "г", "i": "ш", "o": "щ", "p": "з",
        "[": "х", "]": "ъ",
        "a": "ф", "s": "ы", "d": "в", "f": "а", "g": "п",
        "h": "р", "j": "о", "k": "л", "l": "д", ";": "ж",
        "'": "э",
        "z": "я", "x": "ч", "c": "с", "v": "м", "b": "и",
        "n": "т", "m": "ь", ",": "б", ".": "ю",
        // uppercase
        "Q": "Й", "W": "Ц", "E": "У", "R": "К", "T": "Е",
        "Y": "Н", "U": "Г", "I": "Ш", "O": "Щ", "P": "З",
        "{": "Х", "}": "Ъ",
        "A": "Ф", "S": "Ы", "D": "В", "F": "А", "G": "П",
        "H": "Р", "J": "О", "K": "Л", "L": "Д", ":": "Ж",
        "\"": "Э",
        "Z": "Я", "X": "Ч", "C": "С", "V": "М", "B": "И",
        "N": "Т", "M": "Ь", "<": "Б", ">": "Ю",
    ]

    // EN -> UK: identical to RU except a few keys — ы→і, ъ→ї, э→є — plus ґ on the
    // '\' key (the Ukrainian ЙЦУКЕН layout).
    public static let enToUk: [Character: Character] = {
        var d = enToRu
        d["s"] = "і";  d["S"] = "І"   // і / І   (Russian had ы)
        d["]"] = "ї";  d["}"] = "Ї"   // ї / Ї   (Russian had ъ)
        d["'"] = "є";  d["\""] = "Є"  // є / Є   (Russian had э)
        d["\\"] = "ґ"; d["|"] = "Ґ"   // ґ / Ґ   (new)
        return d
    }()

    // RU -> EN and UK -> EN (reverse of the above)
    public static let ruToEn: [Character: Character] = {
        var d = [Character: Character]()
        for (k, v) in enToRu { d[v] = k }
        return d
    }()
    public static let ukToEn: [Character: Character] = {
        var d = [Character: Character]()
        for (k, v) in enToUk { d[v] = k }
        return d
    }()

    // A char is an EN key if it maps under either Cyrillic layout (enToUk is a
    // superset — it adds the '\' = ґ key that only Ukrainian uses).
    public static func isEnChar(_ c: Character) -> Bool { enToUk[c] != nil }
    public static func isRuChar(_ c: Character) -> Bool { ruToEn[c] != nil }
    public static func isUkChar(_ c: Character) -> Bool { ukToEn[c] != nil }
    public static func isCyrChar(_ c: Character, _ target: Cyrillic) -> Bool {
        target == .ukrainian ? isUkChar(c) : isRuChar(c)
    }

    public static func convertEnToRu(_ text: String) -> String { String(text.map { enToRu[$0] ?? $0 }) }
    public static func convertRuToEn(_ text: String) -> String { String(text.map { ruToEn[$0] ?? $0 }) }
    public static func convertEnToUk(_ text: String) -> String { String(text.map { enToUk[$0] ?? $0 }) }
    public static func convertUkToEn(_ text: String) -> String { String(text.map { ukToEn[$0] ?? $0 }) }

    public static func convertEnToCyr(_ text: String, _ target: Cyrillic) -> String {
        target == .ukrainian ? convertEnToUk(text) : convertEnToRu(text)
    }
    public static func convertCyrToEn(_ text: String, _ target: Cyrillic) -> String {
        target == .ukrainian ? convertUkToEn(text) : convertRuToEn(text)
    }
}
