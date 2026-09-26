using BringLater.Core;
using BringLater.Win32;

namespace BringLater;

/// <summary>
/// The sentences the app shows, looked up in the user's language (see <see cref="Loc"/> and
/// Locales/*.json). English uses sentence case, as Windows does.
/// </summary>
internal static class Copy
{
    public const string AppName = "Bring Later";

    public static string Preset(PresetKind kind) => kind switch
    {
        PresetKind.InOneHour => Loc.T("preset.inOneHour"),
        PresetKind.ThisMorning => Loc.T("preset.thisMorning"),
        PresetKind.ThisEvening => Loc.T("preset.thisEvening"),
        PresetKind.TomorrowMorning => Loc.T("preset.tomorrowMorning"),
        PresetKind.NextWeek => Loc.T("preset.nextWeek"),
        _ => kind.ToString(),
    };

    public static string PickDateAndTime => Loc.T("panel.pick");
    public static string InputPlaceholder => Loc.T("panel.placeholder");

    public static string ParseError(TimeParseError error) => error switch
    {
        TimeParseError.Past => Loc.T("parse.past"),
        TimeParseError.TooFar => Loc.T("parse.tooFar"),
        _ => Loc.T("parse.unrecognized"),
    };

    public static string Refusal(CaptureRefusal refusal, Shortcut shortcut) => refusal switch
    {
        CaptureRefusal.Dialog => Loc.T("refusal.dialog"),
        CaptureRefusal.ToolWindow => Loc.T("refusal.toolWindow"),
        CaptureRefusal.Elevated => Loc.T("refusal.elevated"),
        _ => Loc.F("refusal.noWindow", shortcut),
    };

    public static string RefusalTitle(CaptureRefusal refusal) =>
        Loc.T(refusal == CaptureRefusal.NoWindow ? "refusal.titleNothing" : "refusal.title");

    public static string Failure(SnoozeFailure failure) => failure switch
    {
        SnoozeFailure.Storage => Loc.T("failure.storage"),
        SnoozeFailure.Gone => Loc.T("failure.gone"),
        SnoozeFailure.Elevated => Loc.T("refusal.elevated"),
        _ => Loc.T("failure.refused"),
    };

    public static string Tooltip(int count) => count switch
    {
        0 => AppName,
        1 => $"{AppName}\n{Loc.T("tray.tooltipOne")}",
        _ => $"{AppName}\n{Loc.F("tray.tooltipMany", count)}",
    };

    public static string SnoozeTarget(string? title) =>
        title is null ? Loc.T("tray.snoozeAny") : Loc.F("tray.snoozeTarget", Trim(title, 40));

    public static string Trim(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");
}
