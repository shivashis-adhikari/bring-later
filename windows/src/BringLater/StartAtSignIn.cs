using Microsoft.Win32;

namespace BringLater;

/// <summary>
/// "Start with Windows", through the per-user Run key. Task Manager's Startup page can switch the
/// entry off without deleting it, so that switch is read and cleared too.
/// </summary>
internal static class StartAtSignIn
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Bring Later";

    private static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string)
                return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            // The first byte is 2 (or absent) when enabled, 3 when switched off in Task Manager.
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || state[0] % 2 == 0;
        }
    }

    public static void Set(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        if (enabled)
            run.SetValue(ValueName, Command);
        else
            run.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Bring Later ships as a portable exe. If the user moved it, point the entry at the new location.</summary>
    public static void RefreshPath()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (run?.GetValue(ValueName) is string current && current != Command)
            run.SetValue(ValueName, Command);
    }
}
