using System.Windows;
using BringLater.Core;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BringLater;

internal enum NotificationAction
{
    Show,
    SnoozeAgain,
    OpenSettings,
}

internal sealed record NotificationActivation(NotificationAction Action, Guid? SnoozeId);

/// <summary>Windows notifications. One per return, grouped when several windows come back at once.</summary>
internal sealed class Notifier
{
    public event EventHandler<NotificationActivation>? Activated;

    public Notifier()
    {
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            if (!args.TryGetValue("action", out var action) || !Enum.TryParse<NotificationAction>(action, out var parsed))
                return;
            Guid? id = args.TryGetValue("id", out var raw) && Guid.TryParse(raw, out var guid) ? guid : null;
            Application.Current?.Dispatcher.BeginInvoke(() => Activated?.Invoke(this, new NotificationActivation(parsed, id)));
        };
    }

    public static void Report(ReturnReport report, DateTimeOffset now)
    {
        if (report.Returned.Count == 1)
        {
            var snooze = report.Returned[0];
            Show(builder => builder
                .AddArgument("action", nameof(NotificationAction.Show))
                .AddArgument("id", snooze.Id.ToString())
                .AddText(snooze.Window.Title, hintMaxLines: 1)
                .AddText($"{snooze.App.Name} · snoozed {SnoozedAt(snooze, now)}")
                .AddButton(new ToastButton().SetContent("Show")
                    .AddArgument("action", nameof(NotificationAction.Show)).AddArgument("id", snooze.Id.ToString()))
                .AddButton(new ToastButton().SetContent("Snooze 1 hour")
                    .AddArgument("action", nameof(NotificationAction.SnoozeAgain)).AddArgument("id", snooze.Id.ToString())));
        }
        else if (report.Returned.Count > 1)
        {
            var titles = report.Returned.Select(s => Copy.Trim(s.Window.Title, 40)).ToList();
            var body = titles.Count == 2
                ? $"{titles[0]} and {titles[1]}"
                : $"{titles[0]}, {titles[1]} and {titles.Count - 2} more";
            Show(builder => builder
                .AddArgument("action", nameof(NotificationAction.Show))
                .AddText($"{report.Returned.Count} windows are back")
                .AddText(body));
        }

        foreach (var snooze in report.ClosedReminders)
        {
            Show(builder => builder
                .AddArgument("action", "none")
                .AddText(snooze.Window.Title, hintMaxLines: 1)
                .AddText($"The {snooze.App.Name} window was closed while it was snoozed."));
        }
    }

    public static void Message(string title, string body, string? settingsButton = null) =>
        Show(builder =>
        {
            builder.AddArgument("action", "none").AddText(title).AddText(body);
            if (settingsButton is not null)
                builder.AddButton(new ToastButton().SetContent(settingsButton).AddArgument("action", nameof(NotificationAction.OpenSettings)));
        });

    private static string SnoozedAt(Snooze snooze, DateTimeOffset now) =>
        (TimeZoneInfo.ConvertTime(snooze.CreatedAt, TimeZoneInfo.Local).Date == TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).Date)
            ? $"at {Format.Time(snooze.CreatedAt)}"
            : Format.When(snooze.CreatedAt, now).Replace(" at ", ", ", StringComparison.Ordinal);

    private static void Show(Action<ToastContentBuilder> build)
    {
        try
        {
            var builder = new ToastContentBuilder();
            build(builder);
            builder.Show();
        }
        catch (Exception ex)
        {
            // Notifications can be switched off or broken by policy. The window is back either way.
            Log.Error("Couldn't show a notification", ex);
        }
    }
}
