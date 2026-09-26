using System.Globalization;
using BringLater.Core;

namespace BringLater;

/// <summary>Dates and times as the user's locale writes them, with relative days where they read better.</summary>
internal static class Format
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    private static DateTime Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local).DateTime;

    public static string Time(DateTimeOffset instant) => Local(instant).ToString("t", Culture);

    private static string MonthDay(DateTime date) =>
        date.ToString(Culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal), Culture);

    /// <summary>"Today at 7:00 PM", "Tomorrow at 9:00 AM", "Friday at 2:00 PM", "Oct 3 at 9:00 AM", in the user's language.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = Local(at);
        var days = (local.Date - Local(now).Date).Days;
        var text = days switch
        {
            0 => Loc.F("when.today", Time(at)),
            1 => Loc.F("when.tomorrow", Time(at)),
            > 1 and < 7 => Loc.F("when.day", Culture.DateTimeFormat.GetDayName(local.DayOfWeek), Time(at)),
            _ when local.Year == Local(now).Year => Loc.F("when.day", MonthDay(local), Time(at)),
            _ => Loc.F("when.day", local.ToString("d", Culture), Time(at)),
        };
        // Some languages write day names in lower case ("viernes"); a sentence still starts with a capital.
        return text.Length > 0 ? char.ToUpper(text[0], Culture) + text[1..] : text;
    }

    /// <summary>The compact form for the preset list: "7:00 PM", "Thu 9:00 AM", "Oct 3, 9:00 AM".</summary>
    public static string Short(DateTimeOffset at, DateTimeOffset now)
    {
        var local = Local(at);
        var days = (local.Date - Local(now).Date).Days;
        return days switch
        {
            0 => Time(at),
            > 0 and < 7 => $"{Culture.DateTimeFormat.GetAbbreviatedDayName(local.DayOfWeek)} {Time(at)}",
            _ => DateAndTime(at),
        };
    }

    /// <summary>"Oct 3, 9:00 AM".</summary>
    public static string DateAndTime(DateTimeOffset at) => $"{MonthDay(Local(at))}, {Time(at)}";

    /// <summary>A clock time for settings, e.g. "7:00 PM" or "19:00".</summary>
    public static string Clock(ClockTime time) => new DateTime(2000, 1, 1, time.Hour, time.Minute, 0).ToString("t", Culture);
}
