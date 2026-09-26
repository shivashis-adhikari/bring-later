using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BringLater.Core;
using BringLater.Win32;

namespace BringLater.UI;

/// <summary>App icons for list rows: the window's own icon when it has one, otherwise the exe's.</summary>
internal static class IconCache
{
    private static readonly Dictionary<string, ImageSource?> ByKey = [];

    public static ImageSource? For(SnoozeApp app, string windowRef)
    {
        var key = app.Id.Length > 0 ? app.Id : windowRef;
        if (ByKey.TryGetValue(key, out var cached))
            return cached;
        var icon = FromWindow(Win32Windows.HandleOf(windowRef)) ?? FromFile(app.Id);
        ByKey[key] = icon;
        return icon;
    }

    private static BitmapSource? FromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
            return null;
        // Ask the window, but never wait on an app that isn't responding.
        Native.SendMessageTimeoutW(hwnd, Native.WM_GETICON, Native.ICON_BIG, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out var icon);
        if (icon == IntPtr.Zero)
            icon = Native.GetClassLongPtrW(hwnd, Native.GCLP_HICON);
        return icon == IntPtr.Zero ? null : Convert(Native.CopyIcon(icon));
    }

    private static BitmapSource? FromFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        var info = new Native.SHFILEINFOW();
        Native.SHGetFileInfoW(path, 0, ref info, (uint)Marshal.SizeOf<Native.SHFILEINFOW>(), Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
        return Convert(info.hIcon);
    }

    /// <summary>Copies the icon into a frozen bitmap and destroys the handle.</summary>
    private static BitmapSource? Convert(IntPtr icon)
    {
        if (icon == IntPtr.Zero)
            return null;
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Native.DestroyIcon(icon);
        }
    }
}
