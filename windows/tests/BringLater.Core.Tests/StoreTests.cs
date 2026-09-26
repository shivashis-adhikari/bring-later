using BringLater.Core;
using Xunit;

namespace BringLater.Core.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly SnoozeStore _store;

    public StoreTests() => _store = new SnoozeStore(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Missing_file_loads_empty()
    {
        var result = _store.Load();
        Assert.Empty(result.Snoozes);
        Assert.Null(result.Quarantined);
    }

    [Fact]
    public void Round_trips()
    {
        var snooze = Sample.Snooze(DateTimeOffset.FromUnixTimeSeconds(1_790_000_000), SnoozeState.Hidden) with
        {
            Window = new SnoozeWindow("hwnd:1A2B", "Flights – Google Flights", new Frame(10, 20, 1280, 900)),
        };
        _store.Save([snooze]);
        Assert.Equal(snooze, Assert.Single(_store.Load().Snoozes));
    }

    [Fact]
    public void Unreadable_file_is_set_aside_not_lost()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_store.FilePath, "{ not json");

        var result = _store.Load();

        Assert.Empty(result.Snoozes);
        Assert.NotNull(result.Quarantined);
        Assert.True(File.Exists(result.Quarantined));
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public void Save_replaces_the_previous_file()
    {
        _store.Save([Sample.Snooze(DateTimeOffset.UnixEpoch.AddYears(60), SnoozeState.Hidden)]);
        _store.Save([]);
        Assert.Empty(_store.Load().Snoozes);
        Assert.Single(Directory.GetFiles(_directory));
    }
}
