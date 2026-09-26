using System.Collections.ObjectModel;
using System.Windows.Media;
using BringLater.Core;

namespace BringLater.UI;

internal sealed record SnoozeRow(Guid Id, string Title, string Subtitle, ImageSource? Icon, bool IsClosed)
{
    public bool HasIcon => Icon is not null;
    public string BringBackLabel => $"Bring back {Title}";
    public string ChangeTimeLabel => $"Change time for {Title}";
}

internal sealed class TrayFlyoutModel : Observable
{
    private string? _snoozeTarget;

    public TrayFlyoutModel(string shortcut) => Shortcut = shortcut;

    public ObservableCollection<SnoozeRow> Rows { get; } = [];
    public string Shortcut { get; }
    public string EmptyHint => $"Press {Shortcut} on any window to snooze it.";
    public bool HasRows => Rows.Count > 0;
    public bool IsEmpty => Rows.Count == 0;
    public bool CanBringBackAll => Rows.Count(r => !r.IsClosed) > 1;

    /// <summary>Title of the window "Snooze …" would act on, or null when there isn't one.</summary>
    public string? SnoozeTarget
    {
        get => _snoozeTarget;
        set
        {
            Set(ref _snoozeTarget, value);
            Raise(nameof(SnoozeLabel));
            Raise(nameof(CanSnooze));
        }
    }

    public string SnoozeLabel => Copy.SnoozeTarget(_snoozeTarget);
    public bool CanSnooze => _snoozeTarget is not null;

    public void Update(IEnumerable<Snooze> snoozes, DateTimeOffset now)
    {
        Rows.Clear();
        foreach (var snooze in snoozes.OrderBy(s => s.DueAt))
        {
            var subtitle = snooze.State == SnoozeState.Closed
                ? $"Window closed · reminder {Format.When(snooze.DueAt, now).ToLowerInvariantFirst()}"
                : $"{snooze.App.Name} · {Format.When(snooze.DueAt, now)}";
            Rows.Add(new SnoozeRow(snooze.Id, snooze.Window.Title, subtitle, IconCache.For(snooze.App, snooze.Window.Ref), snooze.State == SnoozeState.Closed));
        }
        Raise(nameof(HasRows));
        Raise(nameof(IsEmpty));
        Raise(nameof(CanBringBackAll));
    }
}

internal static class StringExtensions
{
    /// <summary>"Today at 7:00 PM" → "today at 7:00 PM", for use mid-sentence.</summary>
    public static string ToLowerInvariantFirst(this string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
