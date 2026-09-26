import Foundation

/// A day on the proleptic Gregorian calendar, with no time zone attached.
public struct CivilDate: Hashable, Comparable, Sendable {
    public var year: Int
    public var month: Int
    public var day: Int

    public init(year: Int, month: Int, day: Int) {
        self.year = year
        self.month = month
        self.day = day
    }

    /// Days since 1970-01-01. Howard Hinnant's `days_from_civil`, which the Windows build uses too,
    /// so both platforms agree on every date without depending on their calendar APIs.
    var dayNumber: Int {
        let y = month <= 2 ? year - 1 : year
        let era = (y >= 0 ? y : y - 399) / 400
        let yearOfEra = y - era * 400
        let dayOfYear = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1
        let dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear
        return era * 146_097 + dayOfEra - 719_468
    }

    init(dayNumber: Int) {
        let z = dayNumber + 719_468
        let era = (z >= 0 ? z : z - 146_096) / 146_097
        let dayOfEra = z - era * 146_097
        let yearOfEra = (dayOfEra - dayOfEra / 1460 + dayOfEra / 36524 - dayOfEra / 146_096) / 365
        let dayOfYear = dayOfEra - (365 * yearOfEra + yearOfEra / 4 - yearOfEra / 100)
        let mp = (5 * dayOfYear + 2) / 153
        let month = mp < 10 ? mp + 3 : mp - 9
        self.init(year: yearOfEra + era * 400 + (month <= 2 ? 1 : 0), month: month, day: dayOfYear - (153 * mp + 2) / 5 + 1)
    }

    public func adding(days: Int) -> CivilDate {
        CivilDate(dayNumber: dayNumber + days)
    }

    /// ISO weekday: Monday is 1, Sunday is 7.
    public var weekday: Int {
        ((dayNumber + 3) % 7 + 7) % 7 + 1
    }

    public static func daysInMonth(year: Int, month: Int) -> Int {
        switch month {
        case 2: (year % 4 == 0 && year % 100 != 0) || year % 400 == 0 ? 29 : 28
        case 4, 6, 9, 11: 30
        default: 31
        }
    }

    public static func < (a: CivilDate, b: CivilDate) -> Bool {
        (a.year, a.month, a.day) < (b.year, b.month, b.day)
    }
}

/// A wall-clock time of day, to the minute.
public struct ClockTime: Hashable, Comparable, Codable, Sendable {
    public var hour: Int
    public var minute: Int

    public init(hour: Int, minute: Int) {
        self.hour = hour
        self.minute = minute
    }

    /// Parses "HH:MM" in 24-hour form.
    public init?(_ text: String) {
        let parts = text.split(separator: ":")
        guard parts.count == 2, let h = Int(parts[0]), let m = Int(parts[1]),
              (0...23).contains(h), (0...59).contains(m) else { return nil }
        self.init(hour: h, minute: m)
    }

    public var description: String { String(format: "%02d:%02d", hour, minute) }

    public static func < (a: ClockTime, b: ClockTime) -> Bool {
        (a.hour, a.minute) < (b.hour, b.minute)
    }
}

/// Converts between instants and wall-clock time in a zone, with explicit rules for the
/// hour that doesn't exist (spring forward) and the hour that happens twice (fall back).
public enum LocalTime {
    public struct Parts: Sendable {
        public var date: CivilDate
        public var time: ClockTime
        public var second: Int
    }

    public static func parts(of instant: Date, zone: TimeZone) -> Parts {
        let t = Int(instant.timeIntervalSince1970.rounded(.down))
        let local = t + zone.secondsFromGMT(for: instant)
        let day = local >= 0 ? local / 86_400 : (local - 86_399) / 86_400
        let secondOfDay = local - day * 86_400
        return Parts(
            date: CivilDate(dayNumber: day),
            time: ClockTime(hour: secondOfDay / 3600, minute: secondOfDay % 3600 / 60),
            second: secondOfDay % 60
        )
    }

    /// The instant a wall-clock time names. A time that happens twice resolves to the first one.
    /// A time that doesn't exist resolves to the moment the clocks change, which is the first
    /// valid minute after it.
    public static func resolve(_ date: CivilDate, _ time: ClockTime, second: Int = 0, zone: TimeZone) -> Date {
        let wall = date.dayNumber * 86_400 + time.hour * 3600 + time.minute * 60 + second
        func offset(_ t: Int) -> Int { zone.secondsFromGMT(for: Date(timeIntervalSince1970: TimeInterval(t))) }

        let before = offset(wall - 86_400)
        let after = offset(wall + 86_400)
        let valid = Set([before, after]).map { wall - $0 }.filter { offset($0) == wall - $0 }
        if let first = valid.min() {
            return Date(timeIntervalSince1970: TimeInterval(first))
        }

        // In the gap: find the transition instant between the two readings.
        var low = wall - max(before, after)
        var high = wall - min(before, after)
        let lowOffset = offset(low)
        while high - low > 1 {
            let mid = low + (high - low) / 2
            if offset(mid) == lowOffset { low = mid } else { high = mid }
        }
        return Date(timeIntervalSince1970: TimeInterval(high))
    }

    /// `instant` with seconds dropped.
    public static func floorToMinute(_ instant: Date) -> Date {
        let t = instant.timeIntervalSince1970
        return Date(timeIntervalSince1970: (t / 60).rounded(.down) * 60)
    }
}
