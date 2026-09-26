using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using BringLater.Win32;

namespace BringLater.UI;

internal sealed partial class SettingsWindow : Window
{
    private readonly SettingsModel _model;

    public SettingsWindow(SettingsModel model)
    {
        _model = model;
        DataContext = model;
        InitializeComponent();
        // Taller than a small laptop screen at 125% scaling; scroll instead of running off the edge.
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        SourceInitialized += (_, _) => Theme.StyleFrame(this);
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => StopRecording();
        Closed += (_, _) => StopRecording();
    }

    /// <summary>Tries to register a new shortcut. Returns false if another app already has it.</summary>
    public Func<Shortcut, bool>? ApplyShortcut { get; set; }

    /// <summary>True while the shortcut box is listening, so the global shortcut can be paused.</summary>
    public event EventHandler<bool>? RecordingChanged;

    public event EventHandler? UpdateCheckRequested;

    private void OnShortcutClick(object sender, RoutedEventArgs e)
    {
        if (_model.Recording)
        {
            StopRecording();
            return;
        }
        _model.ShortcutMessage = "";
        _model.Recording = true;
        RecordingChanged?.Invoke(this, true);
    }

    private void StopRecording()
    {
        if (!_model.Recording)
            return;
        _model.Recording = false;
        RecordingChanged?.Invoke(this, false);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_model.Recording)
            return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var shortcut = Shortcut.FromWpf(Keyboard.Modifiers, key);
        if (shortcut is not { IsValid: true } valid)
        {
            _model.ShortcutMessage = "Use Ctrl, Alt or Win together with another key.";
            return;
        }
        if (ApplyShortcut?.Invoke(valid) != true)
        {
            _model.ShortcutMessage = $"Another app is already using {valid}. Try a different one.";
            return;
        }
        _model.ShortcutMessage = "";
        _model.Shortcut = valid;
        StopRecording();
    }

    private void OnCheckUpdates(object sender, RoutedEventArgs e) => UpdateCheckRequested?.Invoke(this, EventArgs.Empty);

    private void OnDownload(object sender, RoutedEventArgs e) => OpenUrl(_model.UpdateUrl ?? UpdateChecker.ReleasesPage);

    private void OnOpenLink(object sender, RoutedEventArgs e) => OpenUrl((string)((FrameworkElement)sender).Tag);

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(Paths.Logs);
        OpenUrl(Paths.Logs);
    }

    private static void OpenUrl(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Couldn't open a link", ex);
        }
    }
}
