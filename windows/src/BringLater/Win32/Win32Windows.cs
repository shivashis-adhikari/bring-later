using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using BringLater.Core;

namespace BringLater.Win32;

internal enum CaptureRefusal
{
    /// <summary>The desktop, taskbar or nothing at all has focus.</summary>
    NoWindow,
    Dialog,
    ToolWindow,
    Elevated,
}

internal sealed record CapturedWindow(IntPtr Handle, WindowTarget Target, Native.RECT Bounds);

internal readonly record struct CaptureResult(CapturedWindow? Window, CaptureRefusal? Refusal);

/// <summary>Finds, hides, restores and watches other apps' top-level windows.</summary>
internal sealed class Win32Windows : IWindowSystem, IDisposable
{
    private static readonly HashSet<string> ShellClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
        "MultitaskingViewFrame", "ForegroundStaging", "Shell_InputSwitchTopLevelWindow",
    ];

    /// <summary>UWP apps live inside this frame. Hiding it only blanks their content, so they are minimized instead.</summary>
    private const string UwpFrameClass = "ApplicationFrameWindow";

    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMilliseconds(750);

    private readonly Native.WinEventProc _callback;
    private readonly IntPtr[] _hooks;
    private readonly bool _weAreElevated = IsElevated((uint)Environment.ProcessId) == true;
    private HashSet<IntPtr> _watched = [];

    public Win32Windows()
    {
        _callback = OnWinEvent;
        const uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        _hooks =
        [
            Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, flags),
            Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZEEND, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _callback, 0, 0, flags),
            Native.SetWinEventHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, flags),
        ];
    }

    /// <summary>The last window of another app that had focus and could be snoozed. Used by the tray menu, where our own click has taken focus.</summary>
    public IntPtr LastForeground { get; private set; }

    public event EventHandler? LastForegroundChanged;

    /// <summary>A snoozed window was shown, restored from minimized, or destroyed by someone other than us.</summary>
    public event EventHandler? WatchedWindowChanged;

    public void Watch(IEnumerable<Snooze> snoozes) =>
        _watched = [.. snoozes.Where(s => s.State == SnoozeState.Hidden).Select(s => HandleOf(s.Window.Ref))];

    public static string RefOf(IntPtr hwnd) => string.Create(CultureInfo.InvariantCulture, $"hwnd:{hwnd.ToInt64():X}");

    public static IntPtr HandleOf(string reference) =>
        reference.StartsWith("hwnd:", StringComparison.Ordinal)
        && long.TryParse(reference.AsSpan(5), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? new IntPtr(value)
            : IntPtr.Zero;

    public CaptureResult Capture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
            return new CaptureResult(null, CaptureRefusal.NoWindow);
        var root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        if (root != IntPtr.Zero)
            hwnd = root;

        _ = Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId || !LooksLikeAppWindow(hwnd))
            return new CaptureResult(null, CaptureRefusal.NoWindow);

        var exStyle = Native.GetWindowLongPtrW(hwnd, Native.GWL_EXSTYLE).ToInt64();
        if ((exStyle & Native.WS_EX_TOOLWINDOW) != 0 && (exStyle & Native.WS_EX_APPWINDOW) == 0)
            return new CaptureResult(null, CaptureRefusal.ToolWindow);
        var owner = Native.GetWindow(hwnd, Native.GW_OWNER);
        if (owner != IntPtr.Zero && Native.IsWindowVisible(owner))
            return new CaptureResult(null, CaptureRefusal.Dialog);

        var process = ProcessInfo(pid);
        if (process.Elevated && !_weAreElevated)
            return new CaptureResult(null, CaptureRefusal.Elevated);

        var title = Title(hwnd);
        var appName = AppName(process.Path, title, ClassName(hwnd));
        var bounds = Bounds(hwnd);
        var target = new WindowTarget(
            RefOf(hwnd),
            title.Length > 0 ? title : appName,
            new SnoozeApp(process.Path ?? "", appName, (int)pid, process.Started),
            new Frame(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        return new CaptureResult(new CapturedWindow(hwnd, target, bounds), null);
    }

    public HideResult Hide(WindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var hwnd = HandleOf(target.Ref);
        if (!Native.IsWindow(hwnd) || !IsSameProcess(hwnd, target.App))
            return HideResult.Failed(HideFailure.Gone);

        var minimize = ClassName(hwnd) == UwpFrameClass;
        if (!Native.ShowWindowAsync(hwnd, minimize ? Native.SW_MINIMIZE : Native.SW_HIDE))
        {
            var error = Marshal.GetLastPInvokeError();
            return HideResult.Failed(error == Native.ERROR_ACCESS_DENIED ? HideFailure.Elevated : HideFailure.Refused);
        }

        if (WaitUntil(() => minimize ? Native.IsIconic(hwnd) : !Native.IsWindowVisible(hwnd)))
            return HideResult.Hidden(minimize ? HideMethod.Minimize : HideMethod.Hide);

        // A busy app may still have our request queued. Queue the opposite right behind it so the
        // window ends up visible whenever the app gets to them.
        Native.ShowWindowAsync(hwnd, minimize ? Native.SW_SHOWNOACTIVATE : Native.SW_SHOWNA);
        return HideResult.Failed(Native.IsWindow(hwnd) ? HideFailure.Refused : HideFailure.Gone);
    }

    public bool Restore(Snooze snooze)
    {
        ArgumentNullException.ThrowIfNull(snooze);
        if (Probe(snooze) == LiveState.Gone)
            return false;
        var hwnd = HandleOf(snooze.Window.Ref);
        var minimized = snooze.Method == HideMethod.Minimize;
        Native.ShowWindowAsync(hwnd, minimized ? Native.SW_SHOWNOACTIVATE : Native.SW_SHOWNA);
        if (WaitUntil(() => minimized ? !Native.IsIconic(hwnd) : Native.IsWindowVisible(hwnd)))
            MoveOnScreenIfLost(hwnd);
        // A hung app shows the window once it responds; the request is already queued.
        return Native.IsWindow(hwnd);
    }

    public LiveState Probe(Snooze snooze)
    {
        ArgumentNullException.ThrowIfNull(snooze);
        var hwnd = HandleOf(snooze.Window.Ref);
        if (!Native.IsWindow(hwnd) || !IsSameProcess(hwnd, snooze.App))
            return LiveState.Gone;
        var visible = snooze.Method == HideMethod.Minimize ? !Native.IsIconic(hwnd) : Native.IsWindowVisible(hwnd);
        return visible ? LiveState.Visible : LiveState.Hidden;
    }

    /// <summary>Brings a window to the front. Windows only allows this from the app the user is interacting with, which is the case after a notification or tray click.</summary>
    public static void Activate(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd))
            return;
        if (Native.IsIconic(hwnd))
            Native.ShowWindowAsync(hwnd, Native.SW_RESTORE);
        if (Native.SetForegroundWindow(hwnd))
            return;

        var foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var ourThread = Native.GetCurrentThreadId();
        if (foregroundThread != ourThread && Native.AttachThreadInput(ourThread, foregroundThread, true))
        {
            Native.SetForegroundWindow(hwnd);
            Native.AttachThreadInput(ourThread, foregroundThread, false);
        }
        if (Native.GetForegroundWindow() != hwnd)
        {
            var flash = new Native.FLASHWINFO
            {
                cbSize = Marshal.SizeOf<Native.FLASHWINFO>(),
                hwnd = hwnd,
                dwFlags = Native.FLASHW_ALL | Native.FLASHW_TIMERNOFG,
            };
            Native.FlashWindowEx(ref flash);
        }
    }

    public static Native.RECT Bounds(IntPtr hwnd)
    {
        // The extended frame excludes the invisible resize borders, so it matches what the user sees.
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT frame, Marshal.SizeOf<Native.RECT>()) == 0)
            return frame;
        Native.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    private static bool LooksLikeAppWindow(IntPtr hwnd)
    {
        if (!Native.IsWindowVisible(hwnd) || ShellClasses.Contains(ClassName(hwnd)))
            return false;
        // Cloaked windows are on another virtual desktop or suspended UWP frames.
        return Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static bool IsSameProcess(IntPtr hwnd, SnoozeApp app)
    {
        _ = Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != app.Pid)
            return false;
        // The pid matches; make sure it's the same process and not a new one that reused the number.
        return app.Started is not { } started || ProcessInfo(pid).Started is not { } now || now == started;
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > SettleTimeout)
                return false;
            Thread.Sleep(15);
        }
        return true;
    }

    private static void MoveOnScreenIfLost(IntPtr hwnd)
    {
        if (Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONULL) != IntPtr.Zero)
            return;
        // Its display was disconnected while it was hidden. Center it on the primary display.
        var monitor = Native.MonitorFromPoint(default, Native.MONITOR_DEFAULTTOPRIMARY);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfoW(monitor, ref info) || !Native.GetWindowRect(hwnd, out var rect))
            return;
        var work = info.rcWork;
        var x = work.Left + Math.Max(0, (work.Width - rect.Width) / 2);
        var y = work.Top + Math.Max(0, (work.Height - rect.Height) / 2);
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != Native.OBJID_WINDOW || idChild != Native.CHILDID_SELF)
            return;

        if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
        {
            if (LooksLikeAppWindow(hwnd) && hwnd != LastForeground)
            {
                LastForeground = hwnd;
                LastForegroundChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (_watched.Contains(hwnd))
        {
            WatchedWindowChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private readonly record struct ProcessDetails(string? Path, DateTimeOffset? Started, bool Elevated);

    private static ProcessDetails ProcessInfo(uint pid)
    {
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
            return new ProcessDetails(null, null, Elevated: true);
        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            var path = Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
            DateTimeOffset? started = Native.GetProcessTimes(handle, out var creation, out _, out _, out _)
                ? DateTimeOffset.FromFileTime(creation).ToUniversalTime()
                : null;
            return new ProcessDetails(path, started, IsElevated(handle) != false);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static bool? IsElevated(uint pid)
    {
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            return IsElevated(handle);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    /// <summary>True or false when the token can be read. Null when it can't, which from a normal process means it belongs to an elevated one.</summary>
    private static bool? IsElevated(IntPtr process)
    {
        if (!Native.OpenProcessToken(process, Native.TOKEN_QUERY, out var token))
            return null;
        try
        {
            return Native.GetTokenInformation(token, Native.TokenElevation, out var elevated, sizeof(int), out _)
                ? elevated != 0
                : null;
        }
        finally
        {
            Native.CloseHandle(token);
        }
    }

    public static string Title(IntPtr hwnd)
    {
        // GetWindowText on another process's window reads the stored caption without sending a
        // message, so a hung app can't block us here.
        var length = Native.GetWindowTextLengthW(hwnd);
        if (length <= 0)
            return "";
        var buffer = new char[length + 1];
        var copied = Native.GetWindowTextW(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, copied)).Trim();
    }

    private static string ClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = Native.GetClassNameW(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    private static string AppName(string? exePath, string title, string className)
    {
        if (className == UwpFrameClass && title.Length > 0)
            return title;
        if (exePath is null)
            return title.Length > 0 ? title : "Unknown app";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall back to the file name below.
        }
        return Path.GetFileNameWithoutExtension(exePath);
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            if (hook != IntPtr.Zero)
                Native.UnhookWinEvent(hook);
        }
    }
}
