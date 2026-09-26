namespace BringLater.Core;

/// <summary>What "this evening" and "tomorrow morning" mean to this user.</summary>
public sealed record TimePrefs(ClockTime Morning, ClockTime Evening)
{
    public static TimePrefs Standard { get; } = new(new ClockTime(9, 0), new ClockTime(19, 0));
}

public enum PresetKind
{
    InOneHour,
    ThisMorning,
    ThisEvening,
    TomorrowMorning,
    NextWeek,
}

public readonly record struct Preset(PresetKind Kind, DateTimeOffset Date);

public static class Presets
{
    /// <summary>Until this hour, the coming morning is "this morning" rather than "tomorrow morning".</summary>
    private const int DayStartHour = 5;

    /// <summary>
    /// The panel's presets in display order. A preset that lands on the same minute as an earlier
    /// one is left out, so the list never shows the same time twice.
    /// </summary>
    public static IReadOnlyList<Preset> Compute(DateTimeOffset now, TimeZoneInfo zone, TimePrefs prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var local = LocalTime.Parts(now, zone);
        var today = local.Date;
        var earlyHours = local.Time.Hour < DayStartHour;
        var presets = new List<Preset>();

        void Add(PresetKind kind, DateTimeOffset date)
        {
            if (date > now && presets.TrueForAll(p => p.Date != date))
                presets.Add(new Preset(kind, date));
        }

        Add(PresetKind.InOneHour, LocalTime.FloorToMinute(now).AddHours(1));
        if (earlyHours)
            Add(PresetKind.ThisMorning, LocalTime.Resolve(today, prefs.Morning, zone));
        Add(PresetKind.ThisEvening, LocalTime.Resolve(today, prefs.Evening, zone));
        if (!earlyHours)
            Add(PresetKind.TomorrowMorning, LocalTime.Resolve(today.AddDays(1), prefs.Morning, zone));
        Add(PresetKind.NextWeek, LocalTime.Resolve(NextMonday(today), prefs.Morning, zone));
        return presets;
    }

    /// <summary>The first Monday strictly after <paramref name="date"/>. Weeks start on Monday regardless of locale.</summary>
    internal static CivilDate NextMonday(CivilDate date) => date.AddDays(8 - date.Weekday);
}
