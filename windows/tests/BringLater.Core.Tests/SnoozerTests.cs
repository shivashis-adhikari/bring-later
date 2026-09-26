using BringLater.Core;
using Xunit;

namespace BringLater.Core.Tests;

public sealed class SnoozerTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly FakeWindows _windows = new();
    private readonly ManualTime _time = new(Start);
    private readonly SnoozeStore _store;
    private readonly Snoozer _snoozer;

    public SnoozerTests()
    {
        _store = new SnoozeStore(_directory);
        _snoozer = new Snoozer(_windows, _store, _time);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static WindowTarget Target(string reference = "hwnd:1") =>
        new(reference, "Flights", new SnoozeApp("chrome.exe", "Google Chrome", 7, null), null);

    [Fact]
    public void The_record_is_on_disk_before_the_window_is_hidden()
    {
        _windows.OnHide = target => Assert.Contains(_store.Load().Snoozes, s => s.Window.Ref == target.Ref);

        var result = _snoozer.Snooze(Target(), Start.AddHours(1));

        Assert.NotNull(result.Snooze);
        Assert.Equal(LiveState.Hidden, _windows.State["hwnd:1"]);
    }

    [Fact]
    public void A_failed_hide_leaves_nothing_behind()
    {
        _windows.HideFailure = HideFailure.Elevated;

        var result = _snoozer.Snooze(Target(), Start.AddHours(1));

        Assert.Equal(SnoozeFailure.Elevated, result.Failure);
        Assert.Empty(_snoozer.Snoozes);
        Assert.Empty(_store.Load().Snoozes);
    }

    [Fact]
    public void A_window_is_never_hidden_if_the_record_cannot_be_saved()
    {
        File.WriteAllText(_directory, "a file where the directory should be");
        try
        {
            var result = _snoozer.Snooze(Target(), Start.AddHours(1));

            Assert.Equal(SnoozeFailure.Storage, result.Failure);
            Assert.False(_windows.State.ContainsKey("hwnd:1"));
        }
        finally
        {
            File.Delete(_directory);
        }
    }

    [Fact]
    public void The_method_that_actually_worked_is_recorded()
    {
        _windows.Method = HideMethod.Minimize;
        _snoozer.Snooze(Target(), Start.AddHours(1));
        Assert.Equal(HideMethod.Minimize, Assert.Single(_store.Load().Snoozes).Method);
    }

    [Fact]
    public void Due_windows_come_back_together_in_one_report()
    {
        _snoozer.Snooze(Target("hwnd:1"), Start.AddHours(1));
        _snoozer.Snooze(Target("hwnd:2"), Start.AddHours(1));
        _snoozer.Snooze(Target("hwnd:3"), Start.AddHours(5));
        var reports = new List<ReturnReport>();
        _snoozer.Returned += (_, report) => reports.Add(report);

        _time.Now = Start.AddHours(1);
        _snoozer.Tick();

        var report = Assert.Single(reports);
        Assert.Equal(2, report.Returned.Count);
        Assert.Equal(LiveState.Visible, _windows.State["hwnd:1"]);
        Assert.Equal(LiveState.Hidden, _windows.State["hwnd:3"]);
        Assert.Single(_store.Load().Snoozes);
    }

    [Fact]
    public void A_window_the_user_brought_back_is_forgotten_quietly()
    {
        _snoozer.Snooze(Target(), Start.AddHours(1));
        var reports = 0;
        _snoozer.Returned += (_, _) => reports++;

        _windows.State["hwnd:1"] = LiveState.Visible;
        _snoozer.Tick();

        Assert.Empty(_snoozer.Snoozes);
        Assert.Equal(0, reports);
    }

    [Fact]
    public void A_closed_window_still_gets_its_reminder_when_due()
    {
        _snoozer.Snooze(Target(), Start.AddHours(1));
        _windows.State["hwnd:1"] = LiveState.Gone;
        ReturnReport? report = null;
        _snoozer.Returned += (_, r) => report = r;

        _snoozer.Tick();
        Assert.Equal(SnoozeState.Closed, Assert.Single(_snoozer.Snoozes).State);
        Assert.Null(report);

        _time.Now = Start.AddHours(1);
        _snoozer.Tick();
        Assert.Empty(_snoozer.Snoozes);
        Assert.Single(report!.ClosedReminders);
    }

    [Fact]
    public void Bring_back_all_restores_every_hidden_window()
    {
        _snoozer.Snooze(Target("hwnd:1"), Start.AddHours(1));
        _snoozer.Snooze(Target("hwnd:2"), Start.AddDays(2));

        Assert.Equal(2, _snoozer.BringBackAll());
        Assert.All(_windows.State.Values, state => Assert.Equal(LiveState.Visible, state));
        Assert.Empty(_store.Load().Snoozes);
    }

    [Fact]
    public void After_a_crash_overdue_windows_come_back_on_the_next_launch()
    {
        _snoozer.Snooze(Target(), Start.AddHours(1));

        _time.Now = Start.AddHours(3);
        var relaunched = new Snoozer(_windows, new SnoozeStore(_directory), _time);
        relaunched.Load();
        relaunched.Tick();

        Assert.Equal(LiveState.Visible, _windows.State["hwnd:1"]);
        Assert.Empty(relaunched.Snoozes);
    }

    [Fact]
    public void Rescheduling_is_saved()
    {
        var snooze = _snoozer.Snooze(Target(), Start.AddHours(1)).Snooze!;
        _snoozer.Reschedule(snooze.Id, Start.AddDays(1));
        Assert.Equal(Start.AddDays(1), Assert.Single(_store.Load().Snoozes).DueAt);
    }

    [Fact]
    public void Bringing_one_back_restores_it_and_forgets_it()
    {
        var snooze = _snoozer.Snooze(Target(), Start.AddHours(1)).Snooze!;
        Assert.NotNull(_snoozer.BringBack(snooze.Id));
        Assert.Equal(LiveState.Visible, _windows.State["hwnd:1"]);
        Assert.Empty(_snoozer.Snoozes);
    }

    private sealed class FakeWindows : IWindowSystem
    {
        public Dictionary<string, LiveState> State { get; } = [];
        public HideFailure? HideFailure { get; set; }
        public HideMethod Method { get; set; } = HideMethod.Hide;
        public Action<WindowTarget>? OnHide { get; set; }

        public HideResult Hide(WindowTarget target)
        {
            OnHide?.Invoke(target);
            if (HideFailure is { } failure)
                return HideResult.Failed(failure);
            State[target.Ref] = LiveState.Hidden;
            return HideResult.Hidden(Method);
        }

        public bool Restore(Snooze snooze)
        {
            if (State.GetValueOrDefault(snooze.Window.Ref) == LiveState.Gone)
                return false;
            State[snooze.Window.Ref] = LiveState.Visible;
            return true;
        }

        public LiveState Probe(Snooze snooze) => State.GetValueOrDefault(snooze.Window.Ref, LiveState.Gone);
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
