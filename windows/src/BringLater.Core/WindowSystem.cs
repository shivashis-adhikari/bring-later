namespace BringLater.Core;

/// <summary>A window the user picked to snooze, as captured when they pressed the shortcut.</summary>
public sealed record WindowTarget(string Ref, string Title, SnoozeApp App, Frame? Frame);

public enum HideFailure
{
    /// <summary>The window closed before we could hide it.</summary>
    Gone,
    /// <summary>It belongs to an app running as administrator, which a normal app can't touch.</summary>
    Elevated,
    /// <summary>The app put the window straight back, or ignored the request.</summary>
    Refused,
}

public readonly record struct HideResult(HideMethod? Method, HideFailure? Failure)
{
    public static HideResult Hidden(HideMethod method) => new(method, null);
    public static HideResult Failed(HideFailure failure) => new(null, failure);
}

/// <summary>
/// The only OS boundary <see cref="Snoozer"/> depends on. The app implements it with Win32 calls;
/// tests implement it in memory.
/// </summary>
public interface IWindowSystem
{
    /// <summary>Hides the window and confirms it is gone. On failure the window is left visible.</summary>
    HideResult Hide(WindowTarget target);

    /// <summary>Shows the window where it was, without activating it. True if it is back on screen.</summary>
    bool Restore(Snooze snooze);

    LiveState Probe(Snooze snooze);
}
