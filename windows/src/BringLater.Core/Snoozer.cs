namespace BringLater.Core;

public enum SnoozeFailure
{
    /// <summary>The snooze couldn't be saved, so the window was left alone.</summary>
    Storage,
    Gone,
    Elevated,
    Refused,
}

public readonly record struct SnoozeResult(Snooze? Snooze, SnoozeFailure? Failure);

/// <summary>Windows that came back on their own schedule, and reminders for windows that closed.</summary>
public sealed record ReturnReport(IReadOnlyList<Snooze> Returned, IReadOnlyList<Snooze> ClosedReminders);

/// <summary>
/// Owns the list of snoozed windows. Every change goes through here, in an order that never
/// leaves a window hidden without a record: save first, then hide; restore first, then forget.
/// </summary>
public sealed class Snoozer(IWindowSystem windows, SnoozeStore store, TimeProvider time)
{
    private List<Snooze> _snoozes = [];

    public IReadOnlyList<Snooze> Snoozes => _snoozes;

    /// <summary>Raised after any change to <see cref="Snoozes"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised by <see cref="Tick"/> when windows come back or closed-window reminders fall due.</summary>
    public event EventHandler<ReturnReport>? Returned;

    /// <summary>Raised when the store can't be written. The in-memory list stays correct and is saved again on the next change.</summary>
    public event EventHandler<Exception>? StorageFailed;

    /// <summary>Loads what was saved last time. Call <see cref="Tick"/> afterwards to reconcile it with the windows that exist now.</summary>
    public StoreLoadResult Load()
    {
        var result = store.Load();
        _snoozes = [.. result.Snoozes];
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public bool Tracks(string windowRef) => _snoozes.Exists(s => s.Window.Ref == windowRef && s.State == SnoozeState.Hidden);

    public SnoozeResult Snooze(WindowTarget target, DateTimeOffset dueAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        var snooze = new Snooze
        {
            Id = Guid.NewGuid(),
            CreatedAt = time.GetUtcNow(),
            DueAt = dueAt,
            State = SnoozeState.Hidden,
            Method = HideMethod.Hide,
            App = target.App,
            Window = new SnoozeWindow(target.Ref, target.Title, target.Frame),
        };

        // The record goes to disk before the window disappears. If we crash in between, launch-time
        // reconcile finds the window still visible and drops the record. Nothing is lost.
        _snoozes.Add(snooze);
        if (!TrySave())
        {
            _snoozes.Remove(snooze);
            return new SnoozeResult(null, SnoozeFailure.Storage);
        }

        var hidden = windows.Hide(target);
        if (hidden.Method is not { } method)
        {
            _snoozes.Remove(snooze);
            TrySave();
            return new SnoozeResult(null, hidden.Failure switch
            {
                HideFailure.Gone => SnoozeFailure.Gone,
                HideFailure.Elevated => SnoozeFailure.Elevated,
                _ => SnoozeFailure.Refused,
            });
        }

        if (method != snooze.Method)
        {
            var index = _snoozes.IndexOf(snooze);
            snooze = snooze with { Method = method };
            _snoozes[index] = snooze;
            TrySave();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return new SnoozeResult(snooze, null);
    }

    public bool Reschedule(Guid id, DateTimeOffset dueAt)
    {
        var index = _snoozes.FindIndex(s => s.Id == id);
        if (index < 0)
            return false;
        _snoozes[index] = _snoozes[index] with { DueAt = dueAt };
        TrySave();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Brings one window back now, at the user's request. Returns the snooze if its window is on screen.</summary>
    public Snooze? BringBack(Guid id)
    {
        var snooze = _snoozes.Find(s => s.Id == id);
        if (snooze is null)
            return null;
        var restored = snooze.State == SnoozeState.Hidden && windows.Restore(snooze);
        _snoozes.Remove(snooze);
        TrySave();
        Changed?.Invoke(this, EventArgs.Empty);
        return restored ? snooze : null;
    }

    /// <summary>Brings every hidden window back and forgets all snoozes. Used for "Bring back all", quitting, signing out and crashes.</summary>
    public int BringBackAll()
    {
        var restored = 0;
        foreach (var snooze in _snoozes.Where(s => s.State == SnoozeState.Hidden))
        {
            if (windows.Restore(snooze))
                restored++;
        }
        _snoozes.Clear();
        TrySave();
        Changed?.Invoke(this, EventArgs.Empty);
        return restored;
    }

    /// <summary>Applies <see cref="Reconcile"/> to every snooze: restores what's due, forgets what the user already brought back, and keeps reminders for closed windows.</summary>
    public void Tick()
    {
        var now = time.GetUtcNow();
        var returned = new List<Snooze>();
        var closed = new List<Snooze>();
        var next = new List<Snooze>(_snoozes.Count);
        var changed = false;

        foreach (var snooze in _snoozes)
        {
            var live = snooze.State == SnoozeState.Closed ? LiveState.Gone : windows.Probe(snooze);
            switch (Reconcile.Action(snooze, live, now))
            {
                case ReconcileAction.Keep:
                    next.Add(snooze);
                    break;
                case ReconcileAction.Restore when windows.Restore(snooze):
                    returned.Add(snooze);
                    changed = true;
                    break;
                case ReconcileAction.Restore:
                case ReconcileAction.NotifyClosed:
                    closed.Add(snooze);
                    changed = true;
                    break;
                case ReconcileAction.Drop:
                    changed = true;
                    break;
                case ReconcileAction.MarkClosed:
                    next.Add(snooze with { State = SnoozeState.Closed });
                    changed = true;
                    break;
            }
        }

        if (!changed)
            return;
        _snoozes = next;
        TrySave();
        Changed?.Invoke(this, EventArgs.Empty);
        if (returned.Count > 0 || closed.Count > 0)
            Returned?.Invoke(this, new ReturnReport(returned, closed));
    }

    public DateTimeOffset? NextCheck() => Reconcile.NextCheck(_snoozes.Select(s => s.DueAt), time.GetUtcNow());

    private bool TrySave()
    {
        try
        {
            store.Save(_snoozes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StorageFailed?.Invoke(this, ex);
            return false;
        }
    }
}
