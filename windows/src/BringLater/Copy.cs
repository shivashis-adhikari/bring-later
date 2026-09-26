using BringLater.Core;
using BringLater.Win32;

namespace BringLater;

/// <summary>
/// Every sentence the Windows app shows, in one place. Windows uses sentence case. Keep in step
/// with docs/copy.md.
/// </summary>
internal static class Copy
{
    public const string AppName = "Bring Later";

    public static string Preset(PresetKind kind) => kind switch
    {
        PresetKind.InOneHour => "In 1 hour",
        PresetKind.ThisMorning => "This morning",
        PresetKind.ThisEvening => "This evening",
        PresetKind.TomorrowMorning => "Tomorrow morning",
        PresetKind.NextWeek => "Next week",
        _ => kind.ToString(),
    };

    public const string PickDateAndTime = "Pick a date and time…";
    public const string InputPlaceholder = "Type a time, like 7pm or tomorrow 9";

    public static string ParseError(TimeParseError error) => error switch
    {
        TimeParseError.Past => "That time has already passed",
        TimeParseError.TooFar => "Pick a time within the next year",
        _ => "Try 7pm, tomorrow 9, or 2h",
    };

    public static string Refusal(CaptureRefusal refusal, Shortcut shortcut) => refusal switch
    {
        CaptureRefusal.Dialog => "This is a dialog. Snooze the window it belongs to instead.",
        CaptureRefusal.ToolWindow => "This kind of window can't be snoozed.",
        CaptureRefusal.Elevated => "This app is running as administrator, so Windows won't let Bring Later hide it.",
        _ => $"Click the window you want to snooze, then press {shortcut}.",
    };

    public static string RefusalTitle(CaptureRefusal refusal) =>
        refusal == CaptureRefusal.NoWindow ? "Nothing to snooze" : "Can't snooze this window";

    public static string Failure(SnoozeFailure failure) => failure switch
    {
        SnoozeFailure.Storage => "The snooze couldn't be saved, so the window was left open. Check that your user folder isn't full.",
        SnoozeFailure.Gone => "That window has closed.",
        SnoozeFailure.Elevated => "This app is running as administrator, so Windows won't let Bring Later hide it.",
        _ => "The app put this window straight back, so it can't be snoozed.",
    };

    public static string Tooltip(int count) => count switch
    {
        0 => AppName,
        1 => $"{AppName}\n1 snoozed window",
        _ => $"{AppName}\n{count} snoozed windows",
    };

    public static string SnoozeTarget(string? title) => title is null ? "Snooze a window…" : $"Snooze “{Trim(title, 40)}”…";

    public static string Trim(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");
}
