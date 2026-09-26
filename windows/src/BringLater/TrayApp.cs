using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BringLater.Core;
using BringLater.UI;
using BringLater.Win32;

namespace BringLater;

/// <summary>
/// Wires the pieces together: shortcut, tray icon, panels, the snoozer, its timer, notifications,
/// and the handlers that bring every window back if the app is quitting, signing out or crashing.
/// </summary>
internal sealed class TrayApp : IDisposable
{
    private readonly Application _app;
    private readonly HostWindow _host = new();
    private readonly Win32Windows _windows = new();
    private readonly Notifier _notifier = new();
    private readonly Snoozer _snoozer;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private readonly Dictionary<Guid, Snooze> _recentlyReturned = [];
    private TrayIcon? _tray;
    private AppSettings _settings = new();
    private SnoozePanel? _panel;
    private TrayFlyout? _flyout;
    private DateTime _flyoutClosedAt;
    private SettingsWindow? _settingsWindow;
    private bool _tickQueued;

    /// <summary>Windows hidden right now, readable from any thread, for the crash handlers.</summary>
    private static volatile (IntPtr Handle, bool Minimized)[] s_hidden = [];
    private static Native.ApplicationRecoveryCallback? s_recoveryCallback;

    public TrayApp(Application app)
    {
        _app = app;
        _snoozer = new Snoozer(_windows, new Core.SnoozeStore(Paths.Data), TimeProvider.System);
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _snoozer.Tick();
            Schedule();
        };
    }

    private Shortcut Hotkey => _settings.HotkeyOrDefault;

    public void Start(bool launchedByUser)
    {
        _settings = AppSettings.Load();
        Log.Info($"Starting Bring Later {UpdateChecker.CurrentText}");
        InstallCrashHandlers();

        WriteTrayIcon();
        _tray = new TrayIcon(_host, Paths.TrayIcon);

        _host.HotkeyPressed += (_, _) => OnHotkey();
        _host.TrayEvent += (_, e) => OnTrayEvent(e);
        _host.Resumed += (_, _) => OnClockMayHaveJumped();
        _host.TimeChanged += (_, _) => OnClockMayHaveJumped();
        _host.ThemeChanged += (_, _) => Theme.Apply(Theme.Detect());
        _host.SessionEnding += (_, _) => BringEverythingBack("session ending");
        _windows.WatchedWindowChanged += (_, _) => QueueTick();
        _snoozer.Changed += (_, _) => OnSnoozesChanged();
        _snoozer.Returned += (_, report) => OnReturned(report);
        _snoozer.StorageFailed += (_, ex) => Log.Error("Couldn't save snoozes", ex);
        _notifier.Activated += (_, activation) => OnNotification(activation);

        // Anything still hidden from last time is reconciled now: overdue windows come back,
        // windows the user already brought back are forgotten.
        var loaded = _snoozer.Load();
        if (loaded.Quarantined is not null)
            Log.Error($"Snooze file was unreadable and was moved to {loaded.Quarantined}");
        _snoozer.Tick();
        Schedule();

        if (!_host.RegisterHotkey(Hotkey))
        {
            Log.Info($"Shortcut {Hotkey} is taken");
            Notifier.Message(Loc.T("notify.shortcutTakenTitle"), Loc.F("notify.shortcutTakenBody", Hotkey), Loc.T("notify.openSettings"));
        }

        StartAtSignIn.RefreshPath();
        if (!_settings.WelcomeShown)
            ShowWelcome();
        else if (launchedByUser)
            Notifier.Message(Loc.T("notify.runningTitle"), Loc.F("notify.runningBody", Hotkey));
    }

    // Snoozing

    private void OnHotkey()
    {
        if (_panel is not null)
        {
            _panel.Dismiss();
            return;
        }
        OpenPanel(Native.GetForegroundWindow());
    }

    private void OpenPanel(IntPtr hwnd)
    {
        _flyout?.Dismiss();
        var now = DateTimeOffset.UtcNow;
        var capture = _windows.Capture(hwnd);

        if (capture.Window is not { } window)
        {
            var refusal = capture.Refusal ?? CaptureRefusal.NoWindow;
            ShowPanel(SnoozePanelModel.Refused(now, Copy.RefusalTitle(refusal), Copy.Refusal(refusal, Hotkey)), AnchorFor(hwnd), null);
            return;
        }

        var target = window.Target;
        var model = new SnoozePanelModel(now, _settings.TimePrefs, target.Title, target.App.Name, IconCache.For(target.App, target.Ref));
        ShowPanel(model, window.Bounds, (panel, due) =>
        {
            var result = _snoozer.Snooze(target, due);
            if (result.Failure is { } failure)
            {
                Log.Info($"Snooze failed: {failure} ({Path.GetFileName(target.App.Id)})");
                panel.ShowError(Copy.Failure(failure));
                return;
            }
            Log.Info($"Snoozed {target.Ref} ({Path.GetFileName(target.App.Id)}) with {result.Snooze!.Method}");
            panel.Dismiss();
        });
    }

    private void ChangeTime(Guid id)
    {
        if (_snoozer.Snoozes.FirstOrDefault(s => s.Id == id) is not { } snooze)
            return;
        var now = DateTimeOffset.UtcNow;
        var subtitle = Loc.F("panel.changeSubtitle", Format.When(snooze.DueAt, now).ToLowerInvariantFirst());
        var model = new SnoozePanelModel(now, _settings.TimePrefs, snooze.Window.Title, subtitle, IconCache.For(snooze.App, snooze.Window.Ref));
        ShowPanel(model, AnchorFor(IntPtr.Zero), (panel, due) =>
        {
            _snoozer.Reschedule(id, due);
            panel.Dismiss();
        });
    }

    private void ShowPanel(SnoozePanelModel model, Native.RECT anchor, Action<SnoozePanel, DateTimeOffset>? onChosen)
    {
        _panel?.Dismiss();
        var panel = new SnoozePanel(model);
        _panel = panel;
        panel.Closed += (_, _) =>
        {
            if (_panel == panel)
                _panel = null;
        };
        if (onChosen is not null)
            panel.Chosen += (_, due) => onChosen(panel, due);
        panel.Present(anchor);
    }

    /// <summary>The window's own bounds when there is one, otherwise the display under the cursor.</summary>
    private static Native.RECT AnchorFor(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && Native.IsWindowVisible(hwnd))
            return Win32Windows.Bounds(hwnd);
        Native.GetCursorPos(out var cursor);
        var monitor = Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        return Native.GetMonitorInfoW(monitor, ref info) ? info.rcWork : new Native.RECT { Right = 1280, Bottom = 720 };
    }

    // Coming back

    private void Schedule()
    {
        _timer.Stop();
        if (_snoozer.NextCheck() is not { } next)
            return;
        var wait = next - DateTimeOffset.UtcNow;
        _timer.Interval = wait > TimeSpan.FromMilliseconds(250) ? wait : TimeSpan.FromMilliseconds(250);
        _timer.Start();
    }

    private void QueueTick()
    {
        if (_tickQueued)
            return;
        _tickQueued = true;
        _app.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _tickQueued = false;
            _snoozer.Tick();
        });
    }

    private void OnClockMayHaveJumped()
    {
        TimeZoneInfo.ClearCachedData();
        _snoozer.Tick();
        Schedule();
    }

    private void OnSnoozesChanged()
    {
        var snoozes = _snoozer.Snoozes;
        _windows.Watch(snoozes);
        s_hidden = [.. snoozes.Where(s => s.State == SnoozeState.Hidden)
            .Select(s => (Win32Windows.HandleOf(s.Window.Ref), s.Method == HideMethod.Minimize))];
        _tray?.SetTooltip(Copy.Tooltip(snoozes.Count));
        if (_flyout?.DataContext is TrayFlyoutModel model)
            model.Update(snoozes, DateTimeOffset.UtcNow);
        Schedule();
    }

    private void OnReturned(ReturnReport report)
    {
        foreach (var snooze in report.Returned)
        {
            _recentlyReturned[snooze.Id] = snooze;
            Log.Info($"Returned {snooze.Window.Ref}");
        }
        while (_recentlyReturned.Count > 50)
            _recentlyReturned.Remove(_recentlyReturned.Keys.First());

        Notifier.Report(report, DateTimeOffset.UtcNow);
        if (_settings.BringToFront && report.Returned.Count > 0)
            Win32Windows.Activate(Win32Windows.HandleOf(report.Returned[^1].Window.Ref));
    }

    private void OnNotification(NotificationActivation activation)
    {
        switch (activation.Action)
        {
            case NotificationAction.Show when activation.SnoozeId is { } id && _recentlyReturned.TryGetValue(id, out var shown):
                Win32Windows.Activate(Win32Windows.HandleOf(shown.Window.Ref));
                break;
            case NotificationAction.SnoozeAgain when activation.SnoozeId is { } id && _recentlyReturned.TryGetValue(id, out var again):
                var target = new WindowTarget(again.Window.Ref, again.Window.Title, again.App, again.Window.Frame);
                var result = _snoozer.Snooze(target, LocalTime.FloorToMinute(DateTimeOffset.UtcNow).AddHours(1));
                if (result.Failure is { } failure)
                    Notifier.Message(Loc.T("notify.againFailed"), Copy.Failure(failure));
                else
                    _recentlyReturned.Remove(id);
                break;
            case NotificationAction.OpenSettings:
                OpenSettings();
                break;
        }
    }

    // Tray

    private void OnTrayEvent(TrayEvent e)
    {
        switch (e.Code)
        {
            case Native.NIN_SELECT or Native.NIN_KEYSELECT:
                // The click that lands on the icon first closes an open flyout. Don't reopen it.
                if (_flyout is null && (DateTime.UtcNow - _flyoutClosedAt).TotalMilliseconds > 400)
                    ShowFlyout(e.Anchor);
                else
                    _flyout?.Dismiss();
                break;
            case Native.WM_CONTEXTMENU:
                ShowMenu(e.Anchor);
                break;
        }
    }

    private string? LastWindowTitle()
    {
        var hwnd = _windows.LastForeground;
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd))
            return null;
        var title = Win32Windows.Title(hwnd);
        return title.Length > 0 ? title : null;
    }

    private void ShowFlyout(Native.POINT anchor)
    {
        _panel?.Dismiss();
        var model = new TrayFlyoutModel(Hotkey.ToString()) { SnoozeTarget = LastWindowTitle() };
        model.Update(_snoozer.Snoozes, DateTimeOffset.UtcNow);
        var flyout = new TrayFlyout(model);
        _flyout = flyout;
        flyout.Closed += (_, _) =>
        {
            _flyout = null;
            _flyoutClosedAt = DateTime.UtcNow;
        };
        flyout.BringBackRequested += (_, id) => BringBack(id);
        flyout.ChangeTimeRequested += (_, id) => ChangeTime(id);
        flyout.BringBackAllRequested += (_, _) => _snoozer.BringBackAll();
        flyout.SnoozeRequested += (_, _) => OpenPanel(_windows.LastForeground);
        flyout.SettingsRequested += (_, _) => OpenSettings();
        flyout.Present(_tray?.Bounds(), anchor);
    }

    private void BringBack(Guid id)
    {
        if (_snoozer.BringBack(id) is { } snooze)
            Win32Windows.Activate(Win32Windows.HandleOf(snooze.Window.Ref));
    }

    private void ShowMenu(Native.POINT anchor)
    {
        _flyout?.Dismiss();
        var title = LastWindowTitle();
        var hasSnoozes = _snoozer.Snoozes.Count > 0;
        _tray?.ShowMenu(
        [
            new TrayMenuItem(Copy.SnoozeTarget(title), title is null ? null : () => OpenPanel(_windows.LastForeground), Hotkey.ToString()),
            TrayMenuItem.Separator,
            new TrayMenuItem(Loc.T("tray.showSnoozed"), () => ShowFlyout(anchor), IsDefault: true),
            new TrayMenuItem(Loc.T("tray.bringBackAll"), hasSnoozes ? () => _snoozer.BringBackAll() : null),
            TrayMenuItem.Separator,
            new TrayMenuItem(Loc.T("tray.settings"), OpenSettings),
            TrayMenuItem.Separator,
            new TrayMenuItem(Loc.T("tray.quit"), Quit),
        ], anchor);
    }

    // Windows

    private void ShowWelcome()
    {
        var welcome = new WelcomeWindow(Hotkey.Parts);
        welcome.Closed += (_, _) =>
        {
            StartAtSignIn.Set(welcome.StartAtSignIn);
            _settings = _settings with { WelcomeShown = true };
            _settings.Save();
            if (welcome.OpenSettings)
                OpenSettings();
        };
        welcome.Show();
        welcome.Activate();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var model = new SettingsModel(_settings, StartAtSignIn.IsEnabled)
        {
            UpdateStatus = Loc.T("settings.updatesIdle"),
        };
        model.PropertyChanged += (_, e) => OnSettingChanged(model, e);

        var window = new SettingsWindow(model)
        {
            ApplyShortcut = shortcut => _host.RegisterHotkey(shortcut),
        };
        window.RecordingChanged += (_, recording) =>
        {
            if (recording)
                _host.UnregisterHotkey();
            else
                _host.RegisterHotkey(Hotkey);
        };
        window.UpdateCheckRequested += async (_, _) =>
        {
            model.Checking = true;
            model.UpdateStatus = Loc.T("settings.updatesChecking");
            var result = await UpdateChecker.CheckAsync().ConfigureAwait(true);
            model.Checking = false;
            model.UpdateUrl = result.Newer is null ? null : result.Url;
            model.UpdateStatus = result.Failed ? Loc.T("settings.updatesFailed")
                : result.Newer is { } newer ? Loc.F("settings.updatesAvailable", $"{newer.Major}.{newer.Minor}.{newer.Build}")
                : Loc.T("settings.updatesCurrent");
        };
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        window.Show();
        window.Activate();
    }

    private void OnSettingChanged(SettingsModel model, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsModel.Shortcut) or nameof(SettingsModel.Morning) or nameof(SettingsModel.Evening) or nameof(SettingsModel.BringToFront):
                _settings = model.ApplyTo(_settings);
                _settings.Save();
                break;
            case nameof(SettingsModel.StartAtSignIn):
                StartAtSignIn.Set(model.StartAtSignIn);
                break;
        }
    }

    public void Quit()
    {
        var hidden = _snoozer.Snoozes.Count(s => s.State == SnoozeState.Hidden);
        if (hidden > 0 && new QuitWindow(hidden).ShowDialog() != true)
            return;
        BringEverythingBack("quit");
        _app.Shutdown();
    }

    // Safety

    private void BringEverythingBack(string reason)
    {
        var count = _snoozer.BringBackAll();
        Log.Info($"Brought back {count} windows ({reason})");
    }

    private void InstallCrashHandlers()
    {
        // If we die, Windows restarts us (after a minute of uptime) and the recovery callback shows
        // every hidden window on the way down. Launch-time reconcile then finds them visible and
        // forgets the snoozes. Nothing stays hidden with no way back.
        // Only after a crash or hang: after a reboot or an update, "Start with Windows" decides.
        if (Native.RegisterApplicationRestart("--background", Native.RESTART_NO_REBOOT | Native.RESTART_NO_PATCH) != 0)
            Log.Info("Restart after a crash is not available");
        s_recoveryCallback = _ =>
        {
            ShowAllHidden();
            Native.ApplicationRecoveryFinished(true);
            return 0;
        };
        if (Native.RegisterApplicationRecoveryCallback(s_recoveryCallback, IntPtr.Zero, 5000, 0) != 0)
            Log.Info("Crash recovery callback is not available");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            ShowAllHidden();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
        _app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            try
            {
                BringEverythingBack("error");
            }
            catch (Exception inner)
            {
                Log.Error("Couldn't restore through the snoozer", inner);
                ShowAllHidden();
            }
            Notifier.Message(Loc.T("notify.recoveredTitle"), Loc.T("notify.recoveredBody"));
            e.Handled = true;
        };
    }

    /// <summary>Last resort, safe from any thread: show every window we hid.</summary>
    private static void ShowAllHidden()
    {
        foreach (var (handle, minimized) in s_hidden)
            Native.ShowWindowAsync(handle, minimized ? Native.SW_SHOWNOACTIVATE : Native.SW_SHOWNA);
    }

    private static void WriteTrayIcon()
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            using var resource = typeof(TrayApp).Assembly.GetManifestResourceStream("tray.ico");
            if (resource is null)
                return;
            using var file = File.Create(Paths.TrayIcon);
            resource.CopyTo(file);
        }
        catch (IOException ex)
        {
            // Another instance may be reading it; the copy from last time is identical.
            Log.Error("Couldn't write the tray icon", ex);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _panel?.Dismiss();
        _flyout?.Dismiss();
        _tray?.Dispose();
        _windows.Dispose();
        _host.Dispose();
    }
}
