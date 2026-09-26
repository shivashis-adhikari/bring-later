using System.Windows;
using BringLater.UI;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BringLater;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "WPF owns the Application lifetime; fields are disposed in OnExit.")]
public partial class App : Application
{
    private const string InstanceName = @"Local\BringLater.Instance";
    private const string ActivateName = @"Local\BringLater.Activate";

    private Mutex? _instance;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activateWait;
    private TrayApp? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new Mutex(initiallyOwned: true, InstanceName, out var first);
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        if (!first)
        {
            // Already running: ask that copy to open its settings, then leave.
            _activate.Set();
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }

        var background = e.Args.Contains("--background") || ToastNotificationManagerCompat.WasCurrentProcessToastActivated();
        Theme.Apply(Theme.Detect());
        _tray = new TrayApp(this);
        _tray.Start(launchedByUser: !background);

        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.BeginInvoke(() => _tray?.OpenSettings()), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _tray?.Dispose();
        _activate?.Dispose();
        if (_instance is not null)
        {
            _instance.ReleaseMutex();
            _instance.Dispose();
        }
        base.OnExit(e);
    }
}
