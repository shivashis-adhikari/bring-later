using Microsoft.Win32;
using Windows.ApplicationModel;

namespace BringLater;

/// <summary>
/// "Start with Windows". The GitHub build uses the per-user Run key; Task Manager's Startup page can
/// switch that entry off without deleting it, so that switch is read and cleared too. The Store build
/// can't use the Run key and declares a startup task in its package instead.
/// </summary>
internal static class StartAtSignIn
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Bring Later";
    private const string TaskId = "BringLater";

    private static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static bool IsEnabled
    {
        get
        {
            if (Paths.IsStore)
                return StoreTask().State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

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
        if (Paths.IsStore)
        {
            var task = StoreTask();
            if (!enabled)
                task.Disable();
            else if (task.State == StartupTaskState.Disabled)
                task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
            return;
        }

        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        if (enabled)
            run.SetValue(ValueName, Command);
        else
            run.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>The GitHub build is a portable exe. If the user moved it, point the entry at the new location.</summary>
    public static void RefreshPath()
    {
        if (Paths.IsStore)
            return;
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (run?.GetValue(ValueName) is string current && current != Command)
            run.SetValue(ValueName, Command);
    }

    /// <summary>True when the Store build was started by its startup task rather than by the user.</summary>
    public static bool LaunchedAtSignIn() =>
        Paths.IsStore && AppInstance.GetActivatedEventArgs()?.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;

    private static StartupTask StoreTask() => StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();
}
