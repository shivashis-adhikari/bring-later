using System.Windows;

namespace BringLater.UI;

/// <summary>Asks before quitting while windows are snoozed, because quitting brings them all back.</summary>
internal sealed partial class QuitWindow : Window
{
    public QuitWindow(int snoozed)
    {
        InitializeComponent();
        Body.Text = snoozed == 1 ? Loc.T("quit.one") : Loc.F("quit.many", snoozed);
        SourceInitialized += (_, _) => Theme.StyleFrame(this);
    }

    private void OnQuit(object sender, RoutedEventArgs e) => DialogResult = true;
}
