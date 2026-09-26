using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BringLater.Win32;

namespace BringLater.UI;

/// <summary>
/// Positions popup windows in physical pixels. WPF's Left/Top are unreliable across displays with
/// different scaling, so the math happens in device pixels and is redone if the DPI changes.
/// </summary>
internal static class Placement
{
    /// <summary>Centers the window on <paramref name="target"/>, kept inside that display's work area.</summary>
    public static void CenterOn(Window window, Native.RECT target) =>
        Place(window, size =>
        {
            var work = WorkArea(new Native.POINT { X = target.Left + target.Width / 2, Y = target.Top + target.Height / 2 });
            var x = target.Left + (target.Width - size.Width) / 2;
            var y = target.Top + (target.Height - size.Height) / 2;
            return Clamp(x, y, size, work);
        });

    /// <summary>Places the window beside the tray icon, on whichever side of the screen the taskbar sits.</summary>
    public static void NearTray(Window window, Native.RECT? icon, Native.POINT fallback) =>
        Place(window, size =>
        {
            var anchor = icon ?? new Native.RECT { Left = fallback.X, Top = fallback.Y, Right = fallback.X, Bottom = fallback.Y };
            var center = new Native.POINT { X = anchor.Left + anchor.Width / 2, Y = anchor.Top + anchor.Height / 2 };
            var work = WorkArea(center);
            var edge = TaskbarEdge();
            var x = center.X - size.Width / 2;
            var y = center.Y - size.Height / 2;
            switch (edge)
            {
                case 0: x = work.Left; break;                // left
                case 1: y = work.Top; break;                 // top
                case 2: x = work.Right - size.Width; break;  // right
                default: y = work.Bottom - size.Height; break; // bottom
            }
            return Clamp(x, y, size, work);
        });

    private static void Place(Window window, Func<Native.RECT, Native.POINT> position)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        void Move()
        {
            if (!Native.GetWindowRect(hwnd, out var rect))
                return;
            var point = position(rect);
            Native.SetWindowPos(hwnd, IntPtr.Zero, point.X, point.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        Move();
        // Moving onto a display with different scaling resizes the window; center it again once.
        void OnDpiChanged(object? sender, DpiChangedEventArgs e)
        {
            window.DpiChanged -= OnDpiChanged;
            window.Dispatcher.BeginInvoke(Move);
        }
        window.DpiChanged += OnDpiChanged;
    }

    private static Native.POINT Clamp(int x, int y, Native.RECT size, Native.RECT work) => new()
    {
        X = Math.Max(work.Left, Math.Min(x, work.Right - size.Width)),
        Y = Math.Max(work.Top, Math.Min(y, work.Bottom - size.Height)),
    };

    private static Native.RECT WorkArea(Native.POINT point)
    {
        var monitor = Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        return Native.GetMonitorInfoW(monitor, ref info) ? info.rcWork : new Native.RECT { Right = 1280, Bottom = 720 };
    }

    private static uint TaskbarEdge()
    {
        var data = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>() };
        return Native.SHAppBarMessage(Native.ABM_GETTASKBARPOS, ref data) != UIntPtr.Zero ? data.uEdge : 3;
    }
}
