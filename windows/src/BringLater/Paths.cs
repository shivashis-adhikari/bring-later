using System.IO;

namespace BringLater;

/// <summary>Everything Bring Later writes lives under %LOCALAPPDATA%\BringLater.</summary>
internal static class Paths
{
    public static string Data { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BringLater");
    public static string Settings => Path.Combine(Data, "settings.json");
    public static string Logs => Path.Combine(Data, "logs");
    public static string TrayIcon => Path.Combine(Data, "tray.ico");
}
