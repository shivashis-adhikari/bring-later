import BringLaterCore
import Foundation

/// Every sentence the Mac app shows, in one place. macOS uses title case for menus and buttons.
/// Keep in step with docs/copy.md.
enum Copy {
    static let appName = "Bring Later"

    static func preset(_ kind: PresetKind) -> String {
        switch kind {
        case .inOneHour: "In 1 Hour"
        case .thisMorning: "This Morning"
        case .thisEvening: "This Evening"
        case .tomorrowMorning: "Tomorrow Morning"
        case .nextWeek: "Next Week"
        }
    }

    static let pickDateAndTime = "Pick a Date & Time…"
    static let inputPlaceholder = "Type a time, like 7pm or tomorrow 9"

    static func parseError(_ error: TimeParseError) -> String {
        switch error {
        case .past: "That time has already passed"
        case .tooFar: "Pick a time within the next year"
        case .empty, .unrecognized: "Try 7pm, tomorrow 9, or 2h"
        }
    }

    static func refusal(_ refusal: CaptureRefusal, shortcut: Shortcut) -> (title: String, message: String) {
        switch refusal {
        case .fullScreen: ("Can't Snooze a Full-Screen Window", "Exit full screen first, then press \(shortcut.display) again.")
        case .notStandard: ("Can't Snooze This Window", "Only regular app windows can be snoozed, not panels or dialogs.")
        case .noWindow: ("Nothing to Snooze", "Click the window you want to snooze, then press \(shortcut.display).")
        }
    }

    static func failure(_ failure: SnoozeFailure) -> String {
        switch failure {
        case .storage: "The snooze couldn't be saved, so the window was left open."
        case .gone: "That window has closed."
        case .refused: "The app put this window straight back, so it can't be snoozed."
        }
    }

    static func snoozeTarget(_ title: String?) -> String {
        guard let title else { return "Snooze a Window…" }
        return "Snooze “\(trim(title, 40))”…"
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

    /// "Today at 7:00 PM", "Tomorrow at 9:00 AM", "Friday at 2:00 PM", "Oct 3 at 9:00 AM".
    static func when(_ date: Date, now: Date) -> String {
        let days = days(from: now, to: date)
        let day: String
        switch days {
        case 0: day = "Today"
        case 1: day = "Tomorrow"
        case 2..<7: day = date.formatted(.dateTime.weekday(.wide))
        default:
            day = calendar.isDate(date, equalTo: now, toGranularity: .year)
                ? date.formatted(.dateTime.month(.abbreviated).day())
                : date.formatted(date: .abbreviated, time: .omitted)
        }
        return "\(day) at \(time(date))"
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
