import BringLaterCore
import Foundation

/// Looks up `key` in Localizable.strings for the user's language. English is the fallback.
func L(_ key: String) -> String {
    Bundle.main.localizedString(forKey: key, value: nil, table: nil)
}

func L(_ key: String, _ arguments: CVarArg...) -> String {
    String(format: L(key), locale: .current, arguments: arguments)
}

/// The sentences the Mac app shows, from mac/Localization. English uses title case for menus and
/// buttons, as macOS does.
enum Copy {
    static let appName = "Bring Later"

    static func preset(_ kind: PresetKind) -> String {
        switch kind {
        case .inOneHour: L("preset.inOneHour")
        case .thisMorning: L("preset.thisMorning")
        case .thisEvening: L("preset.thisEvening")
        case .tomorrowMorning: L("preset.tomorrowMorning")
        case .nextWeek: L("preset.nextWeek")
        }
    }

    static var pickDateAndTime: String { L("panel.pick") }
    static var inputPlaceholder: String { L("panel.placeholder") }

    static func parseError(_ error: TimeParseError) -> String {
        switch error {
        case .past: L("parse.past")
        case .tooFar: L("parse.tooFar")
        case .empty, .unrecognized: L("parse.unrecognized")
        }
    }

    static func refusal(_ refusal: CaptureRefusal, shortcut: Shortcut) -> (title: String, message: String) {
        switch refusal {
        case .fullScreen: (L("refusal.fullScreenTitle"), L("refusal.fullScreen", shortcut.display))
        case .notStandard: (L("refusal.notStandardTitle"), L("refusal.notStandard"))
        case .noWindow: (L("refusal.noWindowTitle"), L("refusal.noWindow", shortcut.display))
        }
    }

    static func failure(_ failure: SnoozeFailure) -> String {
        switch failure {
        case .storage: L("failure.storage")
        case .gone: L("failure.gone")
        case .refused: L("failure.refused")
        }
    }

    static func snoozeTarget(_ title: String?) -> String {
        guard let title else { return L("menu.snoozeAny") }
        return L("menu.snoozeTarget", trim(title, 40))
    }

    static func trim(_ text: String, _ max: Int) -> String {
        text.count <= max ? text : String(text.prefix(max - 1)) + "…"
    }
}

/// Dates and times as the user's locale writes them, with relative days where they read better.
enum Format {
    private static var calendar: Calendar { .current }

    static func time(_ date: Date) -> String {
        date.formatted(date: .omitted, time: .shortened)
    }

    private static func days(from now: Date, to date: Date) -> Int {
        calendar.dateComponents([.day], from: calendar.startOfDay(for: now), to: calendar.startOfDay(for: date)).day ?? 0
    }

    /// "Today at 7:00 PM", "Tomorrow at 9:00 AM", "Friday at 2:00 PM", "Oct 3 at 9:00 AM", in the user's language.
    static func when(_ date: Date, now: Date) -> String {
        let text: String
        switch days(from: now, to: date) {
        case 0: text = L("when.today", time(date))
        case 1: text = L("when.tomorrow", time(date))
        case 2..<7: text = L("when.day", date.formatted(.dateTime.weekday(.wide)), time(date))
        default:
            let day = calendar.isDate(date, equalTo: now, toGranularity: .year)
                ? date.formatted(.dateTime.month(.abbreviated).day())
                : date.formatted(date: .abbreviated, time: .omitted)
            text = L("when.day", day, time(date))
        }
        // Some languages write day names in lower case ("viernes"); a sentence still starts with a capital.
        return text.prefix(1).uppercased() + text.dropFirst()
    }

    /// "Oct 3, 9:00 AM".
    static func dateAndTime(_ date: Date) -> String {
        "\(date.formatted(.dateTime.month(.abbreviated).day())), \(time(date))"
    }

    /// The compact form for the preset list: "7:00 PM", "Thu 9:00 AM", "Oct 3, 9:00 AM".
    static func short(_ date: Date, now: Date) -> String {
        switch days(from: now, to: date) {
        case 0: time(date)
        case 1..<7: "\(date.formatted(.dateTime.weekday(.abbreviated))) \(time(date))"
        default: "\(date.formatted(.dateTime.month(.abbreviated).day())), \(time(date))"
        }
    }

    static func clock(_ time: ClockTime) -> String {
        var components = DateComponents()
        components.hour = time.hour
        components.minute = time.minute
        return (calendar.date(from: components) ?? Date()).formatted(date: .omitted, time: .shortened)
    }
}
