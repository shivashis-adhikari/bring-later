using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace BringLater.Core.Tests;

/// <summary>Every translation has every string, with the same placeholders as English.</summary>
public partial class LocaleTests
{
    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    private static Dictionary<string, string> Load(string name) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "locales", name)))!;

    [Theory]
    [InlineData("zh-Hans.json")]
    [InlineData("es.json")]
    [InlineData("hi.json")]
    public void Translation_matches_english(string file)
    {
        var english = Load("en.json");
        var translation = Load(file);

        Assert.Empty(english.Keys.Except(translation.Keys));
        Assert.Empty(translation.Keys.Except(english.Keys));
        foreach (var (key, text) in english)
        {
            var expected = Placeholder().Matches(text).Select(m => m.Value).Order();
            var actual = Placeholder().Matches(translation[key]).Select(m => m.Value).Order();
            Assert.True(expected.SequenceEqual(actual), $"{file}: {key} has different placeholders");
            Assert.False(string.IsNullOrWhiteSpace(translation[key]), $"{file}: {key} is empty");
        }
    }
}
