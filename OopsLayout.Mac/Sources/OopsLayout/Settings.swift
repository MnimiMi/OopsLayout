import Foundation
import OopsLayoutCore

/// Persists user preferences. Currently just the Cyrillic target (Russian /
/// Ukrainian), stored in UserDefaults. The macOS counterpart of Settings.cs.
enum Settings {
    private static let targetKey = "cyrillicTarget"

    /// Restore the saved target into Core (call once at launch, before the
    /// engine starts). Defaults to Russian when nothing is stored.
    static func load() {
        let raw = UserDefaults.standard.string(forKey: targetKey)
        WordBuffer.target = (raw == "uk") ? .ukrainian : .russian
    }

    static func saveTarget(_ target: Cyrillic) {
        WordBuffer.target = target
        UserDefaults.standard.set(target == .ukrainian ? "uk" : "ru", forKey: targetKey)
    }
}
