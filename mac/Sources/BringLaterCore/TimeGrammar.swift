import Foundation

public enum TimeParseError: String, Error, Sendable {
    case empty
    case unrecognized
    case past
    case tooFar
}

/// Reads times the way people type them: "30m", "7pm", "tomorrow 9", "fri 2pm", "oct 3".
/// Every rule here is pinned by `fixtures/time-grammar.json`.
public enum TimeGrammar {
    /// Nothing further out than this. Snoozes don't survive a restart, so a year is already generous.
    public static let horizon: TimeInterval = 366 * 86_400
    static let afternoon = ClockTime(hour: 14, minute: 0)

    public static func parse(_ text: String, now: Date, zone: TimeZone, prefs: TimePrefs) -> Result<Date, TimeParseError> {
        let tokens = tokenize(text)
        guard !tokens.isEmpty else { return .failure(.empty) }

        let result: Result<Date, TimeParseError>
        switch duration(tokens) {
        case .some(.success(let length)):
            result = .success(apply(length, to: now, zone: zone))
        case .some(.failure(let error)):
            return .failure(error)
        case nil:
            guard let phrase = Phrase(tokens) else { return .failure(.unrecognized) }
            result = phrase.resolve(now: now, zone: zone, prefs: prefs)
        }

        return result.flatMap { date in
            if date <= now { return .failure(.past) }
            if date.timeIntervalSince(now) > horizon { return .failure(.tooFar) }
            return .success(date)
        }
    }

    // MARK: Tokens

    static func tokenize(_ text: String) -> [String] {
        let normalized = text.lowercased()
            .replacingOccurrences(of: "a.m.", with: "am")
            .replacingOccurrences(of: "p.m.", with: "pm")
            .replacingOccurrences(of: "a.m", with: "am")
            .replacingOccurrences(of: "p.m", with: "pm")
        return normalized.matches(of: /[0-9]+(?:[.:][0-9]+)?|[a-z]+/).map { String($0.output) }
    }

    static func isNumber(_ token: String) -> Bool {
        token.first?.isNumber == true
    }

    // MARK: Durations: "2h", "1h30m", "in an hour", "90 minutes from now"

    struct Length {
        var days = 0
        var seconds = 0.0
    }

    /// `nil` when the tokens aren't a duration at all, so the phrase grammar gets a turn.
    static func duration(_ tokens: [String]) -> Result<Length, TimeParseError>? {
        var body = tokens[...]
        if body.first == "in" { body = body.dropFirst() }
        if body.suffix(2) == ["from", "now"] { body = body.dropLast(2) }
        guard !body.isEmpty, body.count % 2 == 0 else { return nil }

        var length = Length()
        var index = body.startIndex
        while index < body.endIndex {
            let amountToken = body[index]
            let unitToken = body[index + 1]
            index += 2

            let amount: Double
            if amountToken == "a" || amountToken == "an" {
                amount = 1
            } else if isNumber(amountToken), !amountToken.contains(":"), let value = Double(amountToken) {
                amount = value
            } else {
                return nil
            }

            switch unitToken {
            case "m", "min", "mins", "minute", "minutes":
                guard amount == amount.rounded() else { return .failure(.unrecognized) }
                length.seconds += amount * 60
            case "h", "hr", "hrs", "hour", "hours":
                length.seconds += amount * 3600
            case "d", "day", "days":
                guard amount == amount.rounded() else { return .failure(.unrecognized) }
                guard amount <= 1000 else { return .failure(.tooFar) }
                length.days += Int(amount)
            case "w", "wk", "wks", "week", "weeks":
                guard amount == amount.rounded() else { return .failure(.unrecognized) }
                guard amount <= 1000 else { return .failure(.tooFar) }
                length.days += Int(amount) * 7
            default:
                return nil
            }
            if length.seconds > horizon * 2 { return .failure(.tooFar) }
        }
        return .success(length)
    }

    /// Days move the calendar date and keep the wall-clock time, so "1d" across a DST change is
    /// still the same time tomorrow. Hours and minutes are exact elapsed time.
    static func apply(_ length: Length, to now: Date, zone: TimeZone) -> Date {
        var base = LocalTime.floorToMinute(now)
        if length.days > 0 {
            let local = LocalTime.parts(of: base, zone: zone)
            base = LocalTime.resolve(local.date.adding(days: length.days), local.time, zone: zone)
        }
        return base.addingTimeInterval(length.seconds)
    }

    // MARK: Phrases: "tomorrow 9", "fri 2pm", "tonight", "oct 3 5pm"

    enum Day: Equatable {
        case today
        case tomorrow
        case nextWeek
        case weekday(Int, strictlyAfterToday: Bool)
        case date(month: Int, day: Int)
    }

    enum PartOfDay {
        case morning, afternoon, evening
    }

    struct Clock {
        var hour: Int
        var minute: Int
        /// A bare 1–12 with no am/pm: which half of the day depends on context.
        var ambiguous: Bool
    }

    struct Phrase {
        var day: Day?
        var part: PartOfDay?
        var clock: Clock?
        var midnight = false

        static let fillers: Set<String> = ["at", "on", "this", "the", "by"]
        static let ordinals: Set<String> = ["st", "nd", "rd", "th"]
        static let weekdays: [String: Int] = [
            "mon": 1, "monday": 1, "tue": 2, "tues": 2, "tuesday": 2, "wed": 3, "weds": 3, "wednesday": 3,
            "thu": 4, "thur": 4, "thurs": 4, "thursday": 4, "fri": 5, "friday": 5,
            "sat": 6, "saturday": 6, "sun": 7, "sunday": 7,
        ]
        static let months: [String: Int] = [
            "jan": 1, "january": 1, "feb": 2, "february": 2, "mar": 3, "march": 3, "apr": 4, "april": 4,
            "may": 5, "jun": 6, "june": 6, "jul": 7, "july": 7, "aug": 8, "august": 8,
            "sep": 9, "sept": 9, "september": 9, "oct": 10, "october": 10, "nov": 11, "november": 11,
            "dec": 12, "december": 12,
        ]

        init?(_ tokens: [String]) {
            var i = 0
            func peek(_ offset: Int = 0) -> String? {
                i + offset < tokens.count ? tokens[i + offset] : nil
            }

            while let token = peek() {
                if Self.fillers.contains(token) {
                    i += 1
                } else if token == "today" {
                    guard set(day: .today) else { return nil }
                    i += 1
                } else if token == "tonight" {
                    guard set(day: .today), set(part: .evening) else { return nil }
                    i += 1
                } else if ["tomorrow", "tmrw", "tmr", "tomorow"].contains(token) {
                    guard set(day: .tomorrow) else { return nil }
                    i += 1
                } else if token == "next" {
                    if peek(1) == "week" {
                        guard set(day: .nextWeek) else { return nil }
                    } else if let weekday = peek(1).flatMap({ Self.weekdays[$0] }) {
                        guard set(day: .weekday(weekday, strictlyAfterToday: true)) else { return nil }
                    } else {
                        return nil
                    }
                    i += 2
                } else if let weekday = Self.weekdays[token] {
                    guard set(day: .weekday(weekday, strictlyAfterToday: false)) else { return nil }
                    i += 1
                } else if let month = Self.months[token] {
                    guard let dayToken = peek(1), isNumber(dayToken), let dayOfMonth = Int(dayToken),
                          set(day: .date(month: month, day: dayOfMonth)) else { return nil }
                    i += 2
                    if let suffix = peek(), Self.ordinals.contains(suffix) { i += 1 }
                } else if token == "morning" {
                    guard set(part: .morning) else { return nil }
                    i += 1
                } else if token == "afternoon" {
                    guard set(part: .afternoon) else { return nil }
                    i += 1
                } else if token == "evening" || token == "night" {
                    guard set(part: .evening) else { return nil }
                    i += 1
                } else if token == "noon" {
                    guard set(clock: Clock(hour: 12, minute: 0, ambiguous: false)) else { return nil }
                    i += 1
                } else if token == "midnight" {
                    guard clock == nil, !midnight else { return nil }
                    midnight = true
                    i += 1
                } else if isNumber(token) {
                    // "3 oct" / "3rd oct"
                    var next = 1
                    if let suffix = peek(next), Self.ordinals.contains(suffix) { next += 1 }
                    if let month = peek(next).flatMap({ Self.months[$0] }) {
                        guard let dayOfMonth = Int(token), set(day: .date(month: month, day: dayOfMonth)) else { return nil }
                        i += next + 1
                        continue
                    }
                    // A time: "7", "7:30", "19:00", "7pm", "730pm"
                    let meridiem = peek(1).flatMap { $0 == "am" || $0 == "pm" ? $0 : nil }
                    guard let parsed = Self.clock(token, meridiem: meridiem), set(clock: parsed) else { return nil }
                    i += meridiem == nil ? 1 : 2
                } else {
                    return nil
                }
            }
            if day == nil, part == nil, clock == nil, !midnight { return nil }
            if midnight, part == .morning || part == .afternoon { return nil }
        }

        private mutating func set(day value: Day) -> Bool {
            guard day == nil else { return false }
            day = value
            return true
        }

        private mutating func set(part value: PartOfDay) -> Bool {
            guard part == nil else { return false }
            part = value
            return true
        }

        private mutating func set(clock value: Clock) -> Bool {
            guard clock == nil, !midnight else { return false }
            clock = value
            return true
        }

        static func clock(_ token: String, meridiem: String?) -> Clock? {
            var hour: Int
            var minute = 0
            if token.contains(":") {
                let parts = token.split(separator: ":")
                guard parts.count == 2, parts[1].count == 2, let h = Int(parts[0]), let m = Int(parts[1]) else { return nil }
                hour = h
                minute = m
            } else if token.count <= 2, let h = Int(token) {
                hour = h
            } else if (3...4).contains(token.count), meridiem != nil, let n = Int(token) {
                hour = n / 100
                minute = n % 100
            } else {
                return nil
            }
            guard (0...59).contains(minute) else { return nil }

            if let meridiem {
                guard (1...12).contains(hour) else { return nil }
                hour = hour % 12 + (meridiem == "pm" ? 12 : 0)
                return Clock(hour: hour, minute: minute, ambiguous: false)
            }
            guard (0...23).contains(hour) else { return nil }
            let leadingZero = token.hasPrefix("0") && token.count > 1
            return Clock(hour: hour, minute: minute, ambiguous: hour >= 1 && hour <= 12 && !leadingZero)
        }

        func resolve(now: Date, zone: TimeZone, prefs: TimePrefs) -> Result<Date, TimeParseError> {
            let today = LocalTime.parts(of: now, zone: zone).date
            func at(_ date: CivilDate, _ time: ClockTime) -> Date { LocalTime.resolve(date, time, zone: zone) }

            if midnight {
                // Midnight belongs to the end of the named day.
                let date = dayDate(today: today, now: now) { at($0.adding(days: 1), ClockTime(hour: 0, minute: 0)) } ?? today
                return .success(at(date.adding(days: 1), ClockTime(hour: 0, minute: 0)))
            }

            guard let day else {
                // No day named: the next time it will be this o'clock.
                let times: [ClockTime]
                if let clock, clock.ambiguous, part == nil {
                    times = [ClockTime(hour: clock.hour % 12, minute: clock.minute), ClockTime(hour: clock.hour % 12 + 12, minute: clock.minute)]
                } else {
                    times = [time(on: nil, prefs: prefs)]
                }
                let candidates = [today, today.adding(days: 1)].flatMap { date in times.map { at(date, $0) } }
                return candidates.filter { $0 > now }.min().map { .success($0) } ?? .failure(.past)
            }

            if day == .today, let clock, clock.ambiguous, part == nil {
                // "today 5": whichever 5 o'clock is still ahead.
                let candidates = [clock.hour % 12, clock.hour % 12 + 12].map { at(today, ClockTime(hour: $0, minute: clock.minute)) }
                return candidates.filter { $0 > now }.min().map { .success($0) } ?? .failure(.past)
            }

            guard let date = dayDate(today: today, now: now, { at($0, time(on: day, prefs: prefs)) }) else {
                return .failure(.unrecognized)
            }
            return .success(at(date, time(on: day, prefs: prefs)))
        }

        /// The time of day, once the day is known. A bare hour on another day follows how people
        /// schedule: 7 to 11 mean morning, 1 to 6 mean afternoon.
        func time(on day: Day?, prefs: TimePrefs) -> ClockTime {
            if let clock {
                guard clock.ambiguous else { return ClockTime(hour: clock.hour, minute: clock.minute) }
                let hour: Int
                switch part {
                case .morning: hour = clock.hour % 12
                case .afternoon, .evening: hour = clock.hour % 12 + 12
                case nil: hour = (1...6).contains(clock.hour) ? clock.hour + 12 : clock.hour
                }
                return ClockTime(hour: hour, minute: clock.minute)
            }
            switch part {
            case .morning: return prefs.morning
            case .afternoon: return TimeGrammar.afternoon
            case .evening: return prefs.evening
            case nil: return day == .today ? prefs.evening : prefs.morning
            }
        }

        /// The calendar date the phrase names. Weekdays pick the nearest matching day whose
        /// resulting time is still ahead, so "wed 5pm" on a Wednesday afternoon means today.
        func dayDate(today: CivilDate, now: Date, _ instant: (CivilDate) -> Date) -> CivilDate? {
            switch day {
            case nil, .today:
                return today
            case .tomorrow:
                return today.adding(days: 1)
            case .nextWeek:
                return Presets.nextMonday(after: today)
            case .weekday(let weekday, let strictlyAfterToday):
                let first = strictlyAfterToday ? 1 : 0
                return (first...7).map { today.adding(days: $0) }
                    .first { $0.weekday == weekday && (strictlyAfterToday || instant($0) > now) }
            case .date(let month, let dayOfMonth):
                guard (1...12).contains(month) else { return nil }
                var year = today.year
                if CivilDate(year: year, month: month, day: dayOfMonth) < today { year += 1 }
                guard (1...CivilDate.daysInMonth(year: year, month: month)).contains(dayOfMonth) else { return nil }
                return CivilDate(year: year, month: month, day: dayOfMonth)
            }
        }
    }
}
