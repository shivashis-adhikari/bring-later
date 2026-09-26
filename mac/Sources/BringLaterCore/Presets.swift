import Foundation

/// What "this evening" and "tomorrow morning" mean to this user.
public struct TimePrefs: Hashable, Codable, Sendable {
    public var morning: ClockTime
    public var evening: ClockTime

    public init(morning: ClockTime, evening: ClockTime) {
        self.morning = morning
        self.evening = evening
    }

    public static let standard = TimePrefs(morning: ClockTime(hour: 9, minute: 0), evening: ClockTime(hour: 19, minute: 0))
}

public enum PresetKind: String, CaseIterable, Sendable {
    case inOneHour, thisMorning, thisEvening, tomorrowMorning, nextWeek
}

public struct Preset: Hashable, Sendable {
    public var kind: PresetKind
    public var date: Date
}

public enum Presets {
    /// Until this hour, the coming morning is "this morning" rather than "tomorrow morning".
    static let dayStartHour = 5

    /// The panel's presets in display order. A preset that lands on the same minute as an earlier
    /// one is left out, so the list never shows the same time twice.
    public static func compute(now: Date, zone: TimeZone, prefs: TimePrefs) -> [Preset] {
        let local = LocalTime.parts(of: now, zone: zone)
        let today = local.date
        let earlyHours = local.time.hour < dayStartHour
        var presets: [Preset] = []

        func add(_ kind: PresetKind, _ date: Date) {
            guard date > now, !presets.contains(where: { $0.date == date }) else { return }
            presets.append(Preset(kind: kind, date: date))
        }

        add(.inOneHour, LocalTime.floorToMinute(now).addingTimeInterval(3600))
        if earlyHours {
            add(.thisMorning, LocalTime.resolve(today, prefs.morning, zone: zone))
        }
        add(.thisEvening, LocalTime.resolve(today, prefs.evening, zone: zone))
        if !earlyHours {
            add(.tomorrowMorning, LocalTime.resolve(today.adding(days: 1), prefs.morning, zone: zone))
        }
        add(.nextWeek, LocalTime.resolve(nextMonday(after: today), prefs.morning, zone: zone))
        return presets
    }

    /// The first Monday strictly after `date`. Weeks start on Monday regardless of locale.
    static func nextMonday(after date: CivilDate) -> CivilDate {
        date.adding(days: 8 - date.weekday)
    }
}
