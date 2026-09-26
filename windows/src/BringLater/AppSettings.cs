using System.IO;
using System.Text.Json;
using BringLater.Core;
using BringLater.Win32;

namespace BringLater;

internal sealed record AppSettings
{
    public string Shortcut { get; init; } = Win32.Shortcut.Default.Serialize();
    public string Morning { get; init; } = "09:00";
    public string Evening { get; init; } = "19:00";
    /// <summary>When a window comes back, activate it instead of showing it quietly behind the current one.</summary>
    public bool BringToFront { get; init; }
    public bool WelcomeShown { get; init; }

    public Shortcut HotkeyOrDefault => Win32.Shortcut.Deserialize(Shortcut) ?? Win32.Shortcut.Default;

    public TimePrefs TimePrefs => new(
        ClockTime.TryParse(Morning, out var morning) ? morning : TimePrefs.Standard.Morning,
        ClockTime.TryParse(Evening, out var evening) ? evening : TimePrefs.Standard.Evening);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Paths.Settings))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Paths.Settings), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error("Settings unreadable, using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            var temporary = Paths.Settings + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
            File.Move(temporary, Paths.Settings, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Couldn't save settings", ex);
        }
    }
}
