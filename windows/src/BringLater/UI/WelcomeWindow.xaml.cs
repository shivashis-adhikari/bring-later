using System.Windows;

namespace BringLater.UI;

/// <summary>Shown once, on first launch, because a tray app is otherwise invisible.</summary>
internal sealed partial class WelcomeWindow : Window
{
    public WelcomeWindow(IReadOnlyList<string> shortcutParts)
    {
        InitializeComponent();
        Keys.ItemsSource = shortcutParts;
        SourceInitialized += (_, _) => Theme.StyleFrame(this);
    }

    public bool StartAtSignIn => StartToggle.IsChecked == true;

    public bool OpenSettings { get; private set; }

    private void OnDone(object sender, RoutedEventArgs e) => Close();

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        OpenSettings = true;
        Close();
    }
}
