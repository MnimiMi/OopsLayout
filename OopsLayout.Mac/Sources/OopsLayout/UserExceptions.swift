import Foundation
import OopsLayoutCore

private extension Character {
    var isCyrillic: Bool {
        unicodeScalars.contains { (0x0400...0x04FF).contains($0.value) }
    }
}

/// Persists the user's own keep-words to
/// ~/Library/Application Support/OopsLayout/exceptions.json and feeds them into
/// the Core keep-lists. Words are routed to the RU or EN list by script.
enum UserExceptions {
    private struct Store: Codable { var ru: [String]; var en: [String] }

    private static var dir: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("OopsLayout", isDirectory: true)
    }
    private static var fileURL: URL { dir.appendingPathComponent("exceptions.json") }

    private(set) static var ru: Set<String> = []
    private(set) static var en: Set<String> = []

    /// All user words across both scripts, sorted — for display/editing.
    static var all: [String] { ru.union(en).sorted() }

    static func load() {
        if let data = try? Data(contentsOf: fileURL),
           let s = try? JSONDecoder().decode(Store.self, from: data) {
            ru = Set(s.ru)
            en = Set(s.en)
        }
        apply()
    }

    /// Replace the whole set from a freeform list of words (one per line in the
    /// settings window), routing each by script.
    static func setWords(_ words: [String]) {
        ru = []
        en = []
        for raw in words {
            let w = raw.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            guard !w.isEmpty else { continue }
            if w.contains(where: { $0.isCyrillic }) {
                ru.insert(w)
            } else if w.contains(where: { $0.isLetter }) {
                en.insert(w)
            }
        }
        save()
        apply()
    }

    private static func apply() {
        WordBuffer.userKeepWordsRu = ru
        WordBuffer.userKeepWordsEn = en
    }

    private static func save() {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let store = Store(ru: Array(ru), en: Array(en))
        if let data = try? JSONEncoder().encode(store) {
            try? data.write(to: fileURL)
        }
    }
}
