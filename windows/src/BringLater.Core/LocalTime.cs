using System.Globalization;

namespace BringLater.Core;

/// <summary>A day on the proleptic Gregorian calendar, with no time zone attached.</summary>
public readonly record struct CivilDate(int Year, int Month, int Day) : IComparable<CivilDate>
{
    /// <summary>
    /// Days since 1970-01-01. Howard Hinnant's <c>days_from_civil</c>, which the macOS build uses
    /// too, so both platforms agree on every date without depending on their calendar APIs.
    /// </summary>
    public int DayNumber
    {
        get
        {
            var y = Month <= 2 ? Year - 1 : Year;
            var era = (y >= 0 ? y : y - 399) / 400;
            var yearOfEra = y - era * 400;
            var dayOfYear = (153 * (Month + (Month > 2 ? -3 : 9)) + 2) / 5 + Day - 1;
            var dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear;
            return era * 146_097 + dayOfEra - 719_468;
        }
    }

    public static CivilDate FromDayNumber(int dayNumber)
    {
        var z = dayNumber + 719_468;
        var era = (z >= 0 ? z : z - 146_096) / 146_097;
        var dayOfEra = z - era * 146_097;
        var yearOfEra = (dayOfEra - dayOfEra / 1460 + dayOfEra / 36524 - dayOfEra / 146_096) / 365;
        var dayOfYear = dayOfEra - (365 * yearOfEra + yearOfEra / 4 - yearOfEra / 100);
        var mp = (5 * dayOfYear + 2) / 153;
        var month = mp < 10 ? mp + 3 : mp - 9;
        return new CivilDate(yearOfEra + era * 400 + (month <= 2 ? 1 : 0), month, dayOfYear - (153 * mp + 2) / 5 + 1);
    }

    public CivilDate AddDays(int days) => FromDayNumber(DayNumber + days);

    /// <summary>ISO weekday: Monday is 1, Sunday is 7.</summary>
    public int Weekday => ((DayNumber + 3) % 7 + 7) % 7 + 1;

    public static int DaysInMonth(int year, int month) => month switch
    {
        2 => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0 ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    public int CompareTo(CivilDate other) => DayNumber.CompareTo(other.DayNumber);

    public static bool operator <(CivilDate a, CivilDate b) => a.CompareTo(b) < 0;
    public static bool operator >(CivilDate a, CivilDate b) => a.CompareTo(b) > 0;
    public static bool operator <=(CivilDate a, CivilDate b) => a.CompareTo(b) <= 0;
    public static bool operator >=(CivilDate a, CivilDate b) => a.CompareTo(b) >= 0;
}

/// <summary>A wall-clock time of day, to the minute.</summary>
public readonly record struct ClockTime(int Hour, int Minute) : IComparable<ClockTime>
{
    /// <summary>Parses "HH:MM" in 24-hour form.</summary>
    public static bool TryParse(string? text, out ClockTime time)
    {
        time = default;
        var parts = text?.Split(':');
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || h is < 0 or > 23 || m is < 0 or > 59)
            return false;
        time = new ClockTime(h, m);
        return true;
    }

    public static ClockTime Parse(string text) =>
        TryParse(text, out var time) ? time : throw new FormatException($"Not an HH:MM time: {text}");

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Hour:D2}:{Minute:D2}");

    public int CompareTo(ClockTime other) => (Hour * 60 + Minute).CompareTo(other.Hour * 60 + other.Minute);

    public static bool operator <(ClockTime a, ClockTime b) => a.CompareTo(b) < 0;
    public static bool operator >(ClockTime a, ClockTime b) => a.CompareTo(b) > 0;
    public static bool operator <=(ClockTime a, ClockTime b) => a.CompareTo(b) <= 0;
    public static bool operator >=(ClockTime a, ClockTime b) => a.CompareTo(b) >= 0;
}

public readonly record struct LocalParts(CivilDate Date, ClockTime Time, int Second);

/// <summary>
/// Converts between instants and wall-clock time in a zone, with explicit rules for the hour that
/// doesn't exist (spring forward) and the hour that happens twice (fall back).
/// </summary>
public static class LocalTime
{
    public static LocalParts Parts(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var t = instant.ToUnixTimeSeconds();
        var local = t + Offset(zone, t);
        var day = local >= 0 ? local / 86_400 : (local - 86_399) / 86_400;
        var secondOfDay = (int)(local - day * 86_400);
        return new LocalParts(
            CivilDate.FromDayNumber((int)day),
            new ClockTime(secondOfDay / 3600, secondOfDay % 3600 / 60),
            secondOfDay % 60);
    }

    /// <summary>
    /// The instant a wall-clock time names. A time that happens twice resolves to the first one.
    /// A time that doesn't exist resolves to the moment the clocks change, which is the first
    /// valid minute after it.
    /// </summary>
    public static DateTimeOffset Resolve(CivilDate date, ClockTime time, TimeZoneInfo zone, int second = 0)
    {
        ArgumentNullException.ThrowIfNull(zone);
        long wall = (long)date.DayNumber * 86_400 + time.Hour * 3600 + time.Minute * 60 + second;

        var before = Offset(zone, wall - 86_400);
        var after = Offset(zone, wall + 86_400);
        var valid = new[] { before, after }.Distinct().Select(o => wall - o).Where(t => Offset(zone, t) == wall - t).ToList();
        if (valid.Count > 0)
            return DateTimeOffset.FromUnixTimeSeconds(valid.Min());

        // In the gap: find the transition instant between the two readings.
        var low = wall - Math.Max(before, after);
        var high = wall - Math.Min(before, after);
        var lowOffset = Offset(zone, low);
        while (high - low > 1)
        {
            var mid = low + (high - low) / 2;
            if (Offset(zone, mid) == lowOffset) low = mid; else high = mid;
        }
        return DateTimeOffset.FromUnixTimeSeconds(high);
    }

    /// <summary><paramref name="instant"/> with seconds dropped.</summary>
    public static DateTimeOffset FloorToMinute(DateTimeOffset instant)
    {
        var t = instant.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(t - ((t % 60) + 60) % 60);
    }

    private static long Offset(TimeZoneInfo zone, long unixSeconds) =>
        (long)zone.GetUtcOffset(DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).TotalSeconds;
}
