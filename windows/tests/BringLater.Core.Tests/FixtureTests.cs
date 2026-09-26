using System.Globalization;
using System.Text.Json;
using BringLater.Core;
using Xunit;

namespace BringLater.Core.Tests;

/// <summary>
/// Runs the shared behavior contract in <c>fixtures/</c>. The macOS test suite loads the same
/// files, so a change on one platform without the other fails CI.
/// </summary>
public class FixtureTests
{
    [Fact]
    public void Time_grammar_matches_fixtures()
    {
        using var file = Fixture.Load("time-grammar.json");
        var defaults = file.RootElement.GetProperty("defaults");
        var failures = new List<string>();

        foreach (var item in file.RootElement.GetProperty("cases").EnumerateArray())
        {
            var zone = Fixture.Zone(Fixture.Get(item, defaults, "zone"));
            var now = Fixture.Instant(Fixture.Get(item, defaults, "now"), zone);
            var prefs = new TimePrefs(
                ClockTime.Parse(Fixture.Get(item, defaults, "morning")),
                ClockTime.Parse(Fixture.Get(item, defaults, "evening")));
            var input = item.GetProperty("input").GetString()!;
            var result = TimeGrammar.Parse(input, now, zone, prefs);

            if (item.TryGetProperty("expect", out var expect))
            {
                if (result.Date is not { } date)
                    failures.Add($"\"{input}\" failed with {result.Error}, expected {expect}");
                else if (!Fixture.Matches(date, expect.GetString()!, zone))
                    failures.Add($"\"{input}\" gave {Fixture.Describe(date, zone)}, expected {expect}");
            }
            else
            {
                var expected = item.GetProperty("error").GetString();
                if (result.Error is not { } error)
                    failures.Add($"\"{input}\" gave {Fixture.Describe(result.Date!.Value, zone)}, expected error {expected}");
                else if (Fixture.Camel(error.ToString()) != expected)
                    failures.Add($"\"{input}\" failed with {error}, expected {expected}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Presets_match_fixtures()
    {
        using var file = Fixture.Load("presets.json");
        var defaults = file.RootElement.GetProperty("defaults");
        var failures = new List<string>();

        foreach (var item in file.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            var zone = Fixture.Zone(Fixture.Get(item, defaults, "zone"));
            var now = Fixture.Instant(item.GetProperty("now").GetString()!, zone);
            var prefs = new TimePrefs(
                ClockTime.Parse(Fixture.Get(item, defaults, "morning")),
                ClockTime.Parse(Fixture.Get(item, defaults, "evening")));
            var expected = item.GetProperty("expect").EnumerateArray().ToList();
            var actual = Presets.Compute(now, zone, prefs);

            var actualKinds = string.Join(",", actual.Select(p => Fixture.Camel(p.Kind.ToString())));
            var expectedKinds = string.Join(",", expected.Select(e => e.GetProperty("kind").GetString()));
            if (actualKinds != expectedKinds)
            {
                failures.Add($"{name}: kinds {actualKinds}, expected {expectedKinds}");
                continue;
            }
            foreach (var (preset, want) in actual.Zip(expected))
            {
                var at = want.GetProperty("at").GetString()!;
                if (!Fixture.Matches(preset.Date, at, zone))
                    failures.Add($"{name}: {preset.Kind} gave {Fixture.Describe(preset.Date, zone)}, expected {at}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Reconcile_matches_fixtures()
    {
        using var file = Fixture.Load("reconcile.json");
        var now = Fixture.Utc(file.RootElement.GetProperty("now").GetString()!);

        foreach (var item in file.RootElement.GetProperty("cases").EnumerateArray())
        {
            var state = Enum.Parse<SnoozeState>(item.GetProperty("state").GetString()!, ignoreCase: true);
            var live = Enum.Parse<LiveState>(item.GetProperty("live").GetString()!, ignoreCase: true);
            var snooze = Sample.Snooze(Fixture.Utc(item.GetProperty("due").GetString()!), state);
            var action = Reconcile.Action(snooze, live, now);
            Assert.True(
                Fixture.Camel(action.ToString()) == item.GetProperty("expect").GetString(),
                $"{item.GetProperty("name").GetString()}: got {action}");
        }

        foreach (var item in file.RootElement.GetProperty("nextCheck").EnumerateArray())
        {
            var dues = item.GetProperty("dues").EnumerateArray().Select(d => Fixture.Utc(d.GetString()!)).ToList();
            var expect = item.GetProperty("expect");
            DateTimeOffset? expected = expect.ValueKind == JsonValueKind.Null ? null : Fixture.Utc(expect.GetString()!);
            Assert.Equal(expected, Reconcile.NextCheck(dues, now));
        }
    }
}

internal static class Fixture
{
    public static JsonDocument Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name)));

    public static string Get(JsonElement item, JsonElement defaults, string key) =>
        (item.TryGetProperty(key, out var value) ? value : defaults.GetProperty(key)).GetString()!;

    public static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

    /// <summary>"2026-09-23T15:14:37" or "2026-09-23T15:14" as wall-clock time in <paramref name="zone"/>.</summary>
    public static DateTimeOffset Instant(string text, TimeZoneInfo zone)
    {
        var n = text.Split('-', 'T', ':').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        return LocalTime.Resolve(new CivilDate(n[0], n[1], n[2]), new ClockTime(n[3], n[4]), zone, n.Length > 5 ? n[5] : 0);
    }

    public static DateTimeOffset Utc(string text) => Instant(text.TrimEnd('Z'), TimeZoneInfo.Utc);

    /// <summary>Expectations without an offset compare wall-clock minutes; with one, they compare instants.</summary>
    public static bool Matches(DateTimeOffset date, string expect, TimeZoneInfo zone)
    {
        if (expect.Length <= 16)
            return Describe(date, zone) == expect;
        var expected = DateTimeOffset.ParseExact(expect, "yyyy-MM-dd'T'HH:mmzzz", CultureInfo.InvariantCulture);
        return date == expected;
    }

    public static string Describe(DateTimeOffset date, TimeZoneInfo zone)
    {
        var p = LocalTime.Parts(date, zone);
        return string.Create(CultureInfo.InvariantCulture,
            $"{p.Date.Year:D4}-{p.Date.Month:D2}-{p.Date.Day:D2}T{p.Time.Hour:D2}:{p.Time.Minute:D2}");
    }

    public static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}

internal static class Sample
{
    public static Snooze Snooze(DateTimeOffset due, SnoozeState state) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = due.AddHours(-1),
        DueAt = due,
        State = state,
        Method = HideMethod.Hide,
        App = new SnoozeApp("C:\\Program Files\\Example\\example.exe", "Example", 42, null),
        Window = new SnoozeWindow("hwnd:1A2B", "Example", null),
    };
}
