using System.Text.Json;
using System.Text.Json.Serialization;

namespace BringLater.Core;

public enum SnoozeState
{
    Hidden,
    /// <summary>The window was closed while snoozed. Kept so the reminder still fires.</summary>
    Closed,
}

public enum HideMethod
{
    /// <summary>Moved past a screen corner (macOS).</summary>
    Park,
    Minimize,
    /// <summary><c>SW_HIDE</c> (Windows).</summary>
    Hide,
}

/// <param name="Id">Executable path on Windows, bundle identifier on macOS.</param>
/// <param name="Started">Process start time. Guards against the OS reusing the pid for another process.</param>
public sealed record SnoozeApp(string Id, string Name, int Pid, DateTimeOffset? Started);

public sealed record Frame(double X, double Y, double Width, double Height);

/// <param name="Ref"><c>hwnd:&lt;hex&gt;</c> on Windows, <c>cg:&lt;CGWindowID&gt;</c> on macOS.</param>
/// <param name="Frame">Where the window was before it was hidden, in screen coordinates.</param>
public sealed record SnoozeWindow(string Ref, string Title, Frame? Frame);

public sealed record Snooze
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset DueAt { get; init; }
    public required SnoozeState State { get; init; }
    public required HideMethod Method { get; init; }
    public required SnoozeApp App { get; init; }
    public required SnoozeWindow Window { get; init; }
}

public sealed record StoreLoadResult(IReadOnlyList<Snooze> Snoozes, string? Quarantined);

/// <summary>Snoozes on disk, as one JSON file replaced atomically on every save.</summary>
public sealed class SnoozeStore(string directory)
{
    private const int Version = 1;

    private sealed record StoreFile(int Version, IReadOnlyList<Snooze> Snoozes);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string FilePath { get; } = Path.Combine(directory, "snoozes.json");

    public StoreLoadResult Load()
    {
        if (!File.Exists(FilePath))
            return new StoreLoadResult([], null);

        try
        {
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(FilePath), Options);
            if (file is { Version: Version, Snoozes: not null })
                return new StoreLoadResult(file.Snoozes, null);
        }
        catch (JsonException)
        {
            // Falls through to quarantine below.
        }

        // Unreadable, or written by a newer version. Set it aside rather than overwrite it.
        var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH-mm-ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var aside = Path.Combine(directory, $"snoozes.unreadable-{stamp}.json");
        File.Move(FilePath, aside);
        return new StoreLoadResult([], aside);
    }

    public void Save(IReadOnlyList<Snooze> snoozes)
    {
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, new StoreFile(Version, snoozes), Options);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, FilePath, overwrite: true);
    }
}
