using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BringLater.Win32;

namespace BringLater.UI;

/// <summary>The list of snoozed windows, opened from the notification area icon.</summary>
internal sealed partial class TrayFlyout : Window
{
    private bool _closing;

    public TrayFlyout(TrayFlyoutModel model)
    {
        DataContext = model;
        InitializeComponent();
        Opacity = 0;
        Deactivated += (_, _) => Dismiss();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Dismiss();
        };
    }

    public event EventHandler<Guid>? BringBackRequested;
    public event EventHandler<Guid>? ChangeTimeRequested;
    public event EventHandler? BringBackAllRequested;
    public event EventHandler? SnoozeRequested;
    public event EventHandler? SettingsRequested;

    public void Present(Native.RECT? icon, Native.POINT cursor)
    {
        Show();
        Placement.NearTray(this, icon, cursor);
        Opacity = 1;
        Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    public void Dismiss()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }

    private static Guid IdOf(object sender) => (Guid)((FrameworkElement)sender).Tag;

    private void OnBringBack(object sender, RoutedEventArgs e) => BringBackRequested?.Invoke(this, IdOf(sender));

    private void OnChangeTime(object sender, RoutedEventArgs e)
    {
        var id = IdOf(sender);
        Dismiss();
        ChangeTimeRequested?.Invoke(this, id);
    }

    private void OnBringBackAll(object sender, RoutedEventArgs e) => BringBackAllRequested?.Invoke(this, EventArgs.Empty);

    private void OnSnooze(object sender, RoutedEventArgs e)
    {
        Dismiss();
        SnoozeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        Dismiss();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }
}
