namespace BringLater.Core;

/// <summary>What the OS says about a snoozed window right now.</summary>
public enum LiveState
{
    /// <summary>Still hidden where we left it.</summary>
    Hidden,
    /// <summary>Back on screen, but not because of us: the user or the app brought it back.</summary>
    Visible,
    /// <summary>The window no longer exists.</summary>
    Gone,
}

public enum ReconcileAction
{
    Keep,
    Restore,
    /// <summary>Forget the snooze without a notification. The window is already back.</summary>
    Drop,
    MarkClosed,
    /// <summary>Tell the user the reminder is due even though the window is gone, then forget it.</summary>
    NotifyClosed,
}

/// <summary>
/// Decides what to do with each stored snooze. The same rules run at launch (crash recovery) and
/// on every tick, so there is one code path for "time's up" and "we were away".
/// </summary>
public static class Reconcile
{
    public static ReconcileAction Action(Snooze snooze, LiveState live, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snooze);
        var due = snooze.DueAt <= now;
        return (snooze.State, live) switch
        {
            (SnoozeState.Closed, _) => due ? ReconcileAction.NotifyClosed : ReconcileAction.Keep,
            (_, LiveState.Hidden) => due ? ReconcileAction.Restore : ReconcileAction.Keep,
            (_, LiveState.Visible) => ReconcileAction.Drop,
            _ => due ? ReconcileAction.NotifyClosed : ReconcileAction.MarkClosed,
        };
    }

    /// <summary>When to look again. Capped so a missed wake or clock change costs at most <paramref name="maxInterval"/>.</summary>
    public static DateTimeOffset? NextCheck(IEnumerable<DateTimeOffset> dues, DateTimeOffset now, TimeSpan? maxInterval = null)
    {
        var list = dues.ToList();
        if (list.Count == 0)
            return null;
        var cap = now + (maxInterval ?? TimeSpan.FromSeconds(60));
        var soonest = list.Min();
        return soonest < now ? now : soonest < cap ? soonest : cap;
    }
}
