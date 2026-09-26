using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BringLater.Win32;

/// <summary>
/// An invisible top-level window that receives what a tray app needs: the global shortcut, tray
/// icon clicks, and system broadcasts (sleep and wake, clock changes, theme changes, sign-out).
/// Message-only windows don't get broadcasts, so this one is a real, never-shown popup.
/// </summary>
internal sealed class HostWindow : IDisposable
{
    public const int TrayCallbackMessage = Native.WM_APP + 1;
    private const int HotkeyId = 1;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");

    public HostWindow()
    {
        var parameters = new HwndSourceParameters("Bring Later")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = (int)Native.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    public event EventHandler? HotkeyPressed;
    public event EventHandler<TrayEvent>? TrayEvent;
    public event EventHandler? TaskbarCreated;
    public event EventHandler? Resumed;
    public event EventHandler? TimeChanged;
    public event EventHandler? ThemeChanged;
    public event EventHandler? SessionEnding;

    /// <summary>Registers <paramref name="shortcut"/>, replacing any earlier one. False if another app already owns it.</summary>
    public bool RegisterHotkey(Shortcut shortcut)
    {
        Native.UnregisterHotKey(Handle, HotkeyId);
        return Native.RegisterHotKey(Handle, HotkeyId, (uint)shortcut.Modifiers | Native.MOD_NOREPEAT, (uint)shortcut.VirtualKey);
    }

    public void UnregisterHotkey() => Native.UnregisterHotKey(Handle, HotkeyId);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_HOTKEY when wParam == HotkeyId:
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;

            case TrayCallbackMessage:
                // NOTIFYICON_VERSION_4: the event is in the low word of lParam, the anchor point in wParam.
                var point = new Native.POINT { X = (short)(wParam.ToInt64() & 0xFFFF), Y = (short)((wParam.ToInt64() >> 16) & 0xFFFF) };
                TrayEvent?.Invoke(this, new TrayEvent((int)(lParam.ToInt64() & 0xFFFF), point));
                handled = true;
                break;

            case Native.WM_POWERBROADCAST when wParam is Native.PBT_APMRESUMEAUTOMATIC or Native.PBT_APMRESUMESUSPEND:
                Resumed?.Invoke(this, EventArgs.Empty);
                break;

            case Native.WM_TIMECHANGE:
                TimeChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Native.WM_SETTINGCHANGE when lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                ThemeChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Native.WM_QUERYENDSESSION:
                // Bring windows back before apps are asked to close, so any "Save changes?"
                // prompt appears on a visible window.
                SessionEnding?.Invoke(this, EventArgs.Empty);
                handled = true;
                return new IntPtr(1);

            case Native.WM_ENDSESSION when wParam != IntPtr.Zero:
                SessionEnding?.Invoke(this, EventArgs.Empty);
                break;

            default:
                if (msg == _taskbarCreated)
                    TaskbarCreated?.Invoke(this, EventArgs.Empty);
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotkey();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

internal readonly record struct TrayEvent(int Code, Native.POINT Anchor);
