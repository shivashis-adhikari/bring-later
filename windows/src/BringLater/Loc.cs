using System.Globalization;
using System.Text.Json;
using System.Windows.Markup;

namespace BringLater;

/// <summary>
/// The app's text in the user's Windows display language: English, Simplified Chinese, Spanish or
/// Hindi, falling back to English for anything else. Strings live in Locales/*.json.
/// </summary>
internal static class Loc
{
    public static readonly IReadOnlyList<string> Languages = ["en", "zh-Hans", "es", "hi"];

    private static readonly Dictionary<string, string> English = Load("en");
    private static Dictionary<string, string> s_current = Load(Detect(CultureInfo.CurrentUICulture));

    /// <summary>Switches language, for the screenshot tool. The app itself follows Windows.</summary>
    public static void Use(string language) => s_current = Load(language);

    public static string T(string key) =>
        s_current.TryGetValue(key, out var text) ? text
        : English.TryGetValue(key, out var fallback) ? fallback
        : key;

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    public static string Detect(CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            switch (c.Name)
            {
                case "zh-Hant" or "zh-TW" or "zh-HK" or "zh-MO":
                    return "en";
                case "zh-Hans" or "zh" or "zh-CN" or "zh-SG":
                    return "zh-Hans";
                case "es":
                    return "es";
                case "hi":
                    return "hi";
            }
        }
        return "en";
    }

    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"locale.{language}.json");
        return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}

/// <summary>Looks up a string in XAML: <c>Text="{l:T settings.updates}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T()
    {
    }

    public T(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
