using System.IO;
using System.Runtime.InteropServices;

namespace BringLater.Win32;

internal sealed record TrayMenuItem(string Text, Action? Invoke, string? Shortcut = null, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new("", null);
    public bool IsSeparator => Text.Length == 0;
}

/// <summary>The notification area icon and its native context menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private readonly HostWindow _host;
    private readonly string _iconPath;
    private IntPtr _icon;
    private string _tooltip = "Bring Later";
    private bool _added;

    public TrayIcon(HostWindow host, string iconPath)
    {
        _host = host;
        _iconPath = iconPath;
        _host.TaskbarCreated += (_, _) => Add();
        Add();
    }

    public void SetTooltip(string text)
    {
        _tooltip = text;
        if (_added)
        {
            var data = Data(Native.NIF_TIP | Native.NIF_SHOWTIP);
            Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data);
        }
    }

    /// <summary>The icon's rectangle on screen, or null when Windows won't say (for example, when it's in the overflow area).</summary>
    public Native.RECT? Bounds()
    {
        var id = new Native.NOTIFYICONIDENTIFIER
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONIDENTIFIER>(),
            hWnd = _host.Handle,
            uID = IconId,
        };
        return Native.Shell_NotifyIconGetRect(ref id, out var rect) == 0 && rect.Width > 0 ? rect : null;
    }

    public void ShowMenu(IReadOnlyList<TrayMenuItem> items, Native.POINT at)
    {
        var menu = Native.CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsSeparator)
                {
                    Native.AppendMenuW(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                var text = item.Shortcut is null ? item.Text : $"{item.Text}\t{item.Shortcut}";
                var flags = Native.MF_STRING | (item.Invoke is null ? Native.MF_GRAYED : 0);
                Native.AppendMenuW(menu, flags, (UIntPtr)(uint)(i + 1), text);
                if (item.IsDefault)
                    Native.SetMenuDefaultItem(menu, (uint)(i + 1), 0);
            }

            // Without taking the foreground first, the menu wouldn't close when the user clicks
            // elsewhere. The WM_NULL afterwards is the documented fix for the menu closing early.
            Native.SetForegroundWindow(_host.Handle);
            var chosen = Native.TrackPopupMenuEx(menu, Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD | Native.TPM_NONOTIFY, at.X, at.Y, _host.Handle, IntPtr.Zero);
            Native.PostMessageW(_host.Handle, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            // Run the command after the menu's modal loop has fully unwound, not from inside it.
            if (chosen > 0 && chosen <= items.Count && items[chosen - 1].Invoke is { } invoke)
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(invoke);
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    private void Add()
    {
        if (_icon == IntPtr.Zero)
        {
            var size = Native.GetSystemMetrics(Native.SM_CXSMICON);
            _icon = File.Exists(_iconPath) ? Native.LoadImageW(IntPtr.Zero, _iconPath, Native.IMAGE_ICON, size, size, Native.LR_LOADFROMFILE) : IntPtr.Zero;
        }
        var data = Data(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP);
        _added = Native.Shell_NotifyIconW(Native.NIM_ADD, ref data);
        if (!_added)
        {
            // Left over from a previous run that didn't exit cleanly: replace it.
            Native.Shell_NotifyIconW(Native.NIM_DELETE, ref data);
            _added = Native.Shell_NotifyIconW(Native.NIM_ADD, ref data);
        }
        data.uVersion = Native.NOTIFYICON_VERSION_4;
        Native.Shell_NotifyIconW(Native.NIM_SETVERSION, ref data);
    }

    private Native.NOTIFYICONDATAW Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<Native.NOTIFYICONDATAW>(),
        hWnd = _host.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = HostWindow.TrayCallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    public void Dispose()
    {
        var data = Data(0);
        Native.Shell_NotifyIconW(Native.NIM_DELETE, ref data);
        if (_icon != IntPtr.Zero)
            Native.DestroyIcon(_icon);
    }
}
