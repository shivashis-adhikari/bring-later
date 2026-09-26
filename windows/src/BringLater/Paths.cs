using System.IO;

namespace BringLater;

/// <summary>Everything Bring Later writes lives under %LOCALAPPDATA%\BringLater.</summary>
internal static class Paths
{
    public static string Data { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BringLater");
    public static string Settings => Path.Combine(Data, "settings.json");
    public static string Logs => Path.Combine(Data, "logs");
    public static string TrayIcon => Path.Combine(Data, "tray.ico");

    /// <summary>Installed from the Microsoft Store. The Store then owns updates and starting at sign-in.</summary>
    public static bool IsStore { get; } = IsPackaged();

    private static bool IsPackaged()
    {
        uint length = 0;
        return Win32.Native.GetCurrentPackageFullName(ref length, null) != Win32.Native.APPMODEL_ERROR_NO_PACKAGE;
    }
}
