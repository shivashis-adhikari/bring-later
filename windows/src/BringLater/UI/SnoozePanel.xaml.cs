using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using BringLater.Win32;

namespace BringLater.UI;

/// <summary>
/// The panel that appears over a window when you press the shortcut. Type a time, use the arrow
/// keys, press Ctrl+1 to Ctrl+5, or click. Esc or clicking elsewhere cancels.
/// </summary>
internal sealed partial class SnoozePanel : Window
{
    private readonly SnoozePanelModel _model;
    private bool _closing;

    public SnoozePanel(SnoozePanelModel model)
    {
        _model = model;
        DataContext = model;
        InitializeComponent();
        Opacity = 0;
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => Dismiss();
    }

    /// <summary>The user picked a time. The panel stays open until the owner calls <see cref="Dismiss"/> or <see cref="ShowError"/>.</summary>
    public event EventHandler<DateTimeOffset>? Chosen;

    /// <summary>Shows the panel centered on <paramref name="target"/> and gives it the keyboard.</summary>
    public void Present(Native.RECT target)
    {
        Show();
        Placement.CenterOn(this, target);
        Opacity = 1;
        Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        if (_model.ShowInput)
            Input.Focus();
    }

    public void ShowError(string message) => _model.Error = message;

    public void Dismiss()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }

    private void Commit()
    {
        if (_model.Committable is not { } date)
            return;
        _model.Error = "";
        Chosen?.Invoke(this, date);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Escape:
                if (_model.IsPicking)
                    LeavePicker();
                else
                    Dismiss();
                e.Handled = true;
                break;

            case Key.Enter:
                Commit();
                e.Handled = true;
                break;

            case Key.Down or Key.Up when _model.ShowPresets:
                var step = e.Key == Key.Down ? 1 : -1;
                _model.Selected = _model.Selected < 0 && step < 0 ? _model.Rows.Count - 1 : Wrap(_model.Selected + step, _model.Rows.Count);
                e.Handled = true;
                break;

            case >= Key.D1 and <= Key.D9 when ctrl && !_model.IsPicking && !_model.IsRefused:
                var index = e.Key - Key.D1;
                if (index < _model.Rows.Count)
                    Choose(_model.Rows[index]);
                e.Handled = true;
                break;

            case Key.Left or Key.Right or Key.Up or Key.Down when _model.IsPicking && !TimeInput.IsKeyboardFocused:
                _model.MovePick(e.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -7, _ => 7 });
                e.Handled = true;
                break;

            case Key.PageUp or Key.PageDown when _model.IsPicking:
                _model.ShiftMonth(e.Key == Key.PageUp ? -1 : 1);
                e.Handled = true;
                break;
        }
    }

    private static int Wrap(int value, int count) => ((value % count) + count) % count;

    private void Choose(PresetRow row)
    {
        _model.Selected = _model.Rows.IndexOf(row);
        if (row.Date is null)
        {
            _model.IsPicking = true;
            // Focus the panel, not the time box, so the arrow keys move through days first.
            Keyboard.Focus(this);
            return;
        }
        Commit();
    }

    private void LeavePicker()
    {
        _model.IsPicking = false;
        Input.Focus();
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PresetRow row)
            Choose(row);
    }

    private void OnDayClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DayCell day)
            _model.Pick(day.Date);
    }

    private void OnBack(object sender, RoutedEventArgs e) => LeavePicker();

    private void OnPreviousMonth(object sender, RoutedEventArgs e) => _model.ShiftMonth(-1);

    private void OnNextMonth(object sender, RoutedEventArgs e) => _model.ShiftMonth(1);

    private void OnPickConfirm(object sender, RoutedEventArgs e) => Commit();

    private void OnClose(object sender, RoutedEventArgs e) => Dismiss();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }
}
