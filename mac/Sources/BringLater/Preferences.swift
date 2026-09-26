import BringLaterCore
import Foundation

/// Settings, stored in UserDefaults.
@MainActor
enum Preferences {
    private static let defaults = UserDefaults.standard

    static var shortcut: Shortcut {
        get {
            guard let data = defaults.data(forKey: "shortcut"), let shortcut = try? JSONDecoder().decode(Shortcut.self, from: data), shortcut.isValid else {
                return .standard
            }
            return shortcut
        }
        set { defaults.set(try? JSONEncoder().encode(newValue), forKey: "shortcut") }
    }

    static var morning: ClockTime {
        get { defaults.string(forKey: "morning").flatMap(ClockTime.init) ?? TimePrefs.standard.morning }
        set { defaults.set(newValue.description, forKey: "morning") }
    }

    static var evening: ClockTime {
        get { defaults.string(forKey: "evening").flatMap(ClockTime.init) ?? TimePrefs.standard.evening }
        set { defaults.set(newValue.description, forKey: "evening") }
    }

    static var timePrefs: TimePrefs { TimePrefs(morning: morning, evening: evening) }

    /// When a window comes back, activate it instead of showing it quietly behind the current one.
    static var bringToFront: Bool {
        get { defaults.bool(forKey: "bringToFront") }
        set { defaults.set(newValue, forKey: "bringToFront") }
    }

    /// The version that last ran. An unsigned app's Accessibility grant doesn't survive an update,
    /// so a change here means the grant may need renewing.
    static var lastVersion: String? {
        get { defaults.string(forKey: "lastVersion") }
        set { defaults.set(newValue, forKey: "lastVersion") }
    }
}
