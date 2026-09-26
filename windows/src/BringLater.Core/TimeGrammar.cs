using System.Globalization;
using System.Text.RegularExpressions;

namespace BringLater.Core;

public enum TimeParseError
{
    Empty,
    Unrecognized,
    Past,
    TooFar,
}

public readonly record struct TimeParseResult(DateTimeOffset? Date, TimeParseError? Error)
{
    public static TimeParseResult Success(DateTimeOffset date) => new(date, null);
    public static TimeParseResult Failure(TimeParseError error) => new(null, error);
}

/// <summary>
/// Reads times the way people type them: "30m", "7pm", "tomorrow 9", "fri 2pm", "oct 3".
/// Every rule here is pinned by <c>fixtures/time-grammar.json</c>, shared with the macOS build.
/// </summary>
public static partial class TimeGrammar
{
    /// <summary>Nothing further out than this. Snoozes don't survive a restart, so a year is already generous.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(366);
    private static readonly ClockTime Afternoon = new(14, 0);

    public static TimeParseResult Parse(string? text, DateTimeOffset now, TimeZoneInfo zone, TimePrefs prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var tokens = Tokenize(text ?? "");
        if (tokens.Count == 0)
            return TimeParseResult.Failure(TimeParseError.Empty);

        DateTimeOffset date;
        var duration = ParseDuration(tokens);
        if (duration is { Error: { } durationError })
            return TimeParseResult.Failure(durationError);
        if (duration is { Length: { } length })
        {
            date = Apply(length, now, zone);
        }
        else
        {
            var phrase = Phrase.TryParse(tokens);
            if (phrase is null)
                return TimeParseResult.Failure(TimeParseError.Unrecognized);
            var resolved = phrase.Resolve(now, zone, prefs);
            if (resolved.Error is { } phraseError)
                return TimeParseResult.Failure(phraseError);
            date = resolved.Date!.Value;
        }

        if (date <= now)
            return TimeParseResult.Failure(TimeParseError.Past);
        if (date - now > Horizon)
            return TimeParseResult.Failure(TimeParseError.TooFar);
        return TimeParseResult.Success(date);
    }

    // Tokens

    [GeneratedRegex("[0-9]+(?:[.:][0-9]+)?|[a-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    internal static List<string> Tokenize(string text)
    {
        var normalized = text.ToLowerInvariant()
            .Replace("a.m.", "am", StringComparison.Ordinal)
            .Replace("p.m.", "pm", StringComparison.Ordinal)
            .Replace("a.m", "am", StringComparison.Ordinal)
            .Replace("p.m", "pm", StringComparison.Ordinal);
        return TokenPattern().Matches(normalized).Select(m => m.Value).ToList();
    }

    private static bool IsNumber(string token) => token.Length > 0 && char.IsAsciiDigit(token[0]);

    private static int? ToInt(string token) =>
        int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    // Durations: "2h", "1h30m", "in an hour", "90 minutes from now"

    private readonly record struct Length(int Days, double Seconds);

    private readonly record struct DurationResult(Length? Length, TimeParseError? Error);

    /// <summary><c>null</c> when the tokens aren't a duration at all, so the phrase grammar gets a turn.</summary>
    private static DurationResult? ParseDuration(List<string> tokens)
    {
        var start = tokens[0] == "in" ? 1 : 0;
        var end = tokens.Count;
        if (end - start >= 2 && tokens[end - 2] == "from" && tokens[end - 1] == "now")
            end -= 2;
        if (end <= start || (end - start) % 2 != 0)
            return null;

        var days = 0;
        var seconds = 0.0;
        for (var i = start; i < end; i += 2)
        {
            var amountToken = tokens[i];
            var unitToken = tokens[i + 1];

            double amount;
            if (amountToken is "a" or "an")
                amount = 1;
            else if (IsNumber(amountToken) && !amountToken.Contains(':', StringComparison.Ordinal)
                     && double.TryParse(amountToken, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                amount = value;
            else
                return null;

            var whole = amount == Math.Round(amount);
            switch (unitToken)
            {
                case "m" or "min" or "mins" or "minute" or "minutes":
                    if (!whole) return new DurationResult(null, TimeParseError.Unrecognized);
                    seconds += amount * 60;
                    break;
                case "h" or "hr" or "hrs" or "hour" or "hours":
                    seconds += amount * 3600;
                    break;
                case "d" or "day" or "days":
                    if (!whole) return new DurationResult(null, TimeParseError.Unrecognized);
                    if (amount > 1000) return new DurationResult(null, TimeParseError.TooFar);
                    days += (int)amount;
                    break;
                case "w" or "wk" or "wks" or "week" or "weeks":
                    if (!whole) return new DurationResult(null, TimeParseError.Unrecognized);
                    if (amount > 1000) return new DurationResult(null, TimeParseError.TooFar);
                    days += (int)amount * 7;
                    break;
                default:
                    return null;
            }
            if (seconds > Horizon.TotalSeconds * 2)
                return new DurationResult(null, TimeParseError.TooFar);
        }
        return new DurationResult(new Length(days, seconds), null);
    }

    /// <summary>
    /// Days move the calendar date and keep the wall-clock time, so "1d" across a DST change is
    /// still the same time tomorrow. Hours and minutes are exact elapsed time.
    /// </summary>
    private static DateTimeOffset Apply(Length length, DateTimeOffset now, TimeZoneInfo zone)
    {
        var start = LocalTime.FloorToMinute(now);
        if (length.Days > 0)
        {
            var local = LocalTime.Parts(start, zone);
            start = LocalTime.Resolve(local.Date.AddDays(length.Days), local.Time, zone);
        }
        return start.AddSeconds(length.Seconds);
    }

    // Phrases: "tomorrow 9", "fri 2pm", "tonight", "oct 3 5pm"

    private enum DayKind
    {
        Today,
        Tomorrow,
        NextWeek,
        Weekday,
        Date,
    }

    private readonly record struct Day(DayKind Kind, int Weekday = 0, bool StrictlyAfterToday = false, int Month = 0, int DayOfMonth = 0);

    private enum PartOfDay
    {
        Morning,
        Afternoon,
        Evening,
    }

    /// <param name="Ambiguous">A bare 1–12 with no am/pm: which half of the day depends on context.</param>
    private readonly record struct Clock(int Hour, int Minute, bool Ambiguous);

    private sealed class Phrase
    {
        private static readonly HashSet<string> Fillers = ["at", "on", "this", "the", "by"];
        private static readonly HashSet<string> Ordinals = ["st", "nd", "rd", "th"];
        private static readonly HashSet<string> TomorrowWords = ["tomorrow", "tmrw", "tmr", "tomorow"];

        private static readonly Dictionary<string, int> Weekdays = new()
        {
            ["mon"] = 1, ["monday"] = 1, ["tue"] = 2, ["tues"] = 2, ["tuesday"] = 2,
            ["wed"] = 3, ["weds"] = 3, ["wednesday"] = 3, ["thu"] = 4, ["thur"] = 4, ["thurs"] = 4, ["thursday"] = 4,
            ["fri"] = 5, ["friday"] = 5, ["sat"] = 6, ["saturday"] = 6, ["sun"] = 7, ["sunday"] = 7,
        };

        private static readonly Dictionary<string, int> Months = new()
        {
            ["jan"] = 1, ["january"] = 1, ["feb"] = 2, ["february"] = 2, ["mar"] = 3, ["march"] = 3,
            ["apr"] = 4, ["april"] = 4, ["may"] = 5, ["jun"] = 6, ["june"] = 6, ["jul"] = 7, ["july"] = 7,
            ["aug"] = 8, ["august"] = 8, ["sep"] = 9, ["sept"] = 9, ["september"] = 9,
            ["oct"] = 10, ["october"] = 10, ["nov"] = 11, ["november"] = 11, ["dec"] = 12, ["december"] = 12,
        };

        private Day? _day;
        private PartOfDay? _part;
        private Clock? _clock;
        private bool _midnight;

        public static Phrase? TryParse(List<string> tokens)
        {
            var phrase = new Phrase();
            var i = 0;
            string? Peek(int offset = 0) => i + offset < tokens.Count ? tokens[i + offset] : null;

            while (Peek() is { } token)
            {
                if (Fillers.Contains(token))
                {
                    i += 1;
                }
                else if (token == "today")
                {
                    if (!phrase.SetDay(new Day(DayKind.Today))) return null;
                    i += 1;
                }
                else if (token == "tonight")
                {
                    if (!phrase.SetDay(new Day(DayKind.Today)) || !phrase.SetPart(PartOfDay.Evening)) return null;
                    i += 1;
                }
                else if (TomorrowWords.Contains(token))
                {
                    if (!phrase.SetDay(new Day(DayKind.Tomorrow))) return null;
                    i += 1;
                }
                else if (token == "next")
                {
                    if (Peek(1) == "week")
                    {
                        if (!phrase.SetDay(new Day(DayKind.NextWeek))) return null;
                    }
                    else if (Peek(1) is { } next && Weekdays.TryGetValue(next, out var nextWeekday))
                    {
                        if (!phrase.SetDay(new Day(DayKind.Weekday, nextWeekday, StrictlyAfterToday: true))) return null;
                    }
                    else
                    {
                        return null;
                    }
                    i += 2;
                }
                else if (Weekdays.TryGetValue(token, out var weekday))
                {
                    if (!phrase.SetDay(new Day(DayKind.Weekday, weekday))) return null;
                    i += 1;
                }
                else if (Months.TryGetValue(token, out var month))
                {
                    if (Peek(1) is not { } dayToken || !IsNumber(dayToken) || ToInt(dayToken) is not { } dayOfMonth
                        || !phrase.SetDay(new Day(DayKind.Date, Month: month, DayOfMonth: dayOfMonth)))
                        return null;
                    i += 2;
                    if (Peek() is { } suffix && Ordinals.Contains(suffix)) i += 1;
                }
                else if (token == "morning")
                {
                    if (!phrase.SetPart(PartOfDay.Morning)) return null;
                    i += 1;
                }
                else if (token == "afternoon")
                {
                    if (!phrase.SetPart(PartOfDay.Afternoon)) return null;
                    i += 1;
                }
                else if (token is "evening" or "night")
                {
                    if (!phrase.SetPart(PartOfDay.Evening)) return null;
                    i += 1;
                }
                else if (token == "noon")
                {
                    if (!phrase.SetClock(new Clock(12, 0, false))) return null;
                    i += 1;
                }
                else if (token == "midnight")
                {
                    if (phrase._clock is not null || phrase._midnight) return null;
                    phrase._midnight = true;
                    i += 1;
                }
                else if (IsNumber(token))
                {
                    // "3 oct" / "3rd oct"
                    var next = 1;
                    if (Peek(next) is { } suffix && Ordinals.Contains(suffix)) next += 1;
                    if (Peek(next) is { } monthToken && Months.TryGetValue(monthToken, out var dateMonth))
                    {
                        if (ToInt(token) is not { } dayOfMonth
                            || !phrase.SetDay(new Day(DayKind.Date, Month: dateMonth, DayOfMonth: dayOfMonth)))
                            return null;
                        i += next + 1;
                        continue;
                    }
                    // A time: "7", "7:30", "19:00", "7pm", "730pm"
                    var meridiem = Peek(1) is "am" or "pm" ? Peek(1) : null;
                    if (ParseClock(token, meridiem) is not { } clock || !phrase.SetClock(clock)) return null;
                    i += meridiem is null ? 1 : 2;
                }
                else
                {
                    return null;
                }
            }

            if (phrase._day is null && phrase._part is null && phrase._clock is null && !phrase._midnight) return null;
            if (phrase._midnight && phrase._part is PartOfDay.Morning or PartOfDay.Afternoon) return null;
            return phrase;
        }

        private bool SetDay(Day value)
        {
            if (_day is not null) return false;
            _day = value;
            return true;
        }

        private bool SetPart(PartOfDay value)
        {
            if (_part is not null) return false;
            _part = value;
            return true;
        }

        private bool SetClock(Clock value)
        {
            if (_clock is not null || _midnight) return false;
            _clock = value;
            return true;
        }

        private static Clock? ParseClock(string token, string? meridiem)
        {
            int hour;
            var minute = 0;
            if (token.Contains(':', StringComparison.Ordinal))
            {
                var parts = token.Split(':');
                if (parts.Length != 2 || parts[1].Length != 2 || ToInt(parts[0]) is not { } h || ToInt(parts[1]) is not { } m)
                    return null;
                hour = h;
                minute = m;
            }
            else if (token.Length <= 2 && ToInt(token) is { } h)
            {
                hour = h;
            }
            else if (token.Length is 3 or 4 && meridiem is not null && ToInt(token) is { } n)
            {
                hour = n / 100;
                minute = n % 100;
            }
            else
            {
                return null;
            }
            if (minute is < 0 or > 59) return null;

            if (meridiem is not null)
            {
                if (hour is < 1 or > 12) return null;
                return new Clock(hour % 12 + (meridiem == "pm" ? 12 : 0), minute, false);
            }
            if (hour is < 0 or > 23) return null;
            var leadingZero = token.StartsWith('0') && token.Length > 1;
            return new Clock(hour, minute, hour is >= 1 and <= 12 && !leadingZero);
        }

        public TimeParseResult Resolve(DateTimeOffset now, TimeZoneInfo zone, TimePrefs prefs)
        {
            var today = LocalTime.Parts(now, zone).Date;
            DateTimeOffset At(CivilDate date, ClockTime time) => LocalTime.Resolve(date, time, zone);

            if (_midnight)
            {
                // Midnight belongs to the end of the named day.
                var date = DayDate(today, now, d => At(d.AddDays(1), new ClockTime(0, 0))) ?? today;
                return TimeParseResult.Success(At(date.AddDays(1), new ClockTime(0, 0)));
            }

            if (_day is not { } day)
            {
                // No day named: the next time it will be this o'clock.
                ClockTime[] times = _clock is { Ambiguous: true } ambiguous && _part is null
                    ? [new ClockTime(ambiguous.Hour % 12, ambiguous.Minute), new ClockTime(ambiguous.Hour % 12 + 12, ambiguous.Minute)]
                    : [Time(null, prefs)];
                var candidates = new[] { today, today.AddDays(1) }.SelectMany(d => times.Select(t => At(d, t))).Where(t => t > now).ToList();
                return candidates.Count > 0 ? TimeParseResult.Success(candidates.Min()) : TimeParseResult.Failure(TimeParseError.Past);
            }

            if (day.Kind == DayKind.Today && _clock is { Ambiguous: true } clock && _part is null)
            {
                // "today 5": whichever 5 o'clock is still ahead.
                var candidates = new[] { clock.Hour % 12, clock.Hour % 12 + 12 }
                    .Select(h => At(today, new ClockTime(h, clock.Minute))).Where(t => t > now).ToList();
                return candidates.Count > 0 ? TimeParseResult.Success(candidates.Min()) : TimeParseResult.Failure(TimeParseError.Past);
            }

            var time = Time(day, prefs);
            return DayDate(today, now, d => At(d, time)) is { } resolved
                ? TimeParseResult.Success(At(resolved, time))
                : TimeParseResult.Failure(TimeParseError.Unrecognized);
        }

        /// <summary>
        /// The time of day, once the day is known. A bare hour on another day follows how people
        /// schedule: 7 to 11 mean morning, 1 to 6 mean afternoon.
        /// </summary>
        private ClockTime Time(Day? day, TimePrefs prefs)
        {
            if (_clock is { } clock)
            {
                if (!clock.Ambiguous) return new ClockTime(clock.Hour, clock.Minute);
                var hour = _part switch
                {
                    PartOfDay.Morning => clock.Hour % 12,
                    PartOfDay.Afternoon or PartOfDay.Evening => clock.Hour % 12 + 12,
                    _ => clock.Hour is >= 1 and <= 6 ? clock.Hour + 12 : clock.Hour,
                };
                return new ClockTime(hour, clock.Minute);
            }
            return _part switch
            {
                PartOfDay.Morning => prefs.Morning,
                PartOfDay.Afternoon => Afternoon,
                PartOfDay.Evening => prefs.Evening,
                _ => day?.Kind == DayKind.Today ? prefs.Evening : prefs.Morning,
            };
        }

        /// <summary>
        /// The calendar date the phrase names. Weekdays pick the nearest matching day whose
        /// resulting time is still ahead, so "wed 5pm" on a Wednesday afternoon means today.
        /// </summary>
        private CivilDate? DayDate(CivilDate today, DateTimeOffset now, Func<CivilDate, DateTimeOffset> instant)
        {
            switch (_day)
            {
                case null or { Kind: DayKind.Today }:
                    return today;
                case { Kind: DayKind.Tomorrow }:
                    return today.AddDays(1);
                case { Kind: DayKind.NextWeek }:
                    return Presets.NextMonday(today);
                case { Kind: DayKind.Weekday } day:
                    for (var k = day.StrictlyAfterToday ? 1 : 0; k <= 7; k++)
                    {
                        var candidate = today.AddDays(k);
                        if (candidate.Weekday == day.Weekday && (day.StrictlyAfterToday || instant(candidate) > now))
                            return candidate;
                    }
                    return null;
                case { Kind: DayKind.Date } day:
                    if (day.Month is < 1 or > 12) return null;
                    var year = today.Year;
                    if (new CivilDate(year, day.Month, day.DayOfMonth) < today) year += 1;
                    if (day.DayOfMonth < 1 || day.DayOfMonth > CivilDate.DaysInMonth(year, day.Month)) return null;
                    return new CivilDate(year, day.Month, day.DayOfMonth);
                default:
                    return null;
            }
        }
    }
}
