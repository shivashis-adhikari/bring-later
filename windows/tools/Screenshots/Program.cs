using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BringLater;
using BringLater.Core;
using BringLater.UI;
using BringLater.Win32;

namespace Screenshots;

/// <summary>
/// Draws each window of the app, in light and dark, exactly as the app builds it. The only thing
/// faked is the data: a fixed clock and a few sample windows.
/// </summary>
internal static class Program
{
    private const double Scale = 2;

    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "screenshots");
        Directory.CreateDirectory(output);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en-US");

        var app = new App();
        app.InitializeComponent();

        foreach (var theme in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            Theme.Apply(theme);
            var suffix = theme == ThemeKind.Light ? "light" : "dark";
            var background = (Brush)app.FindResource("WindowBackgroundBrush");

            Save(PanelWindow(model => { }), output, $"panel-{suffix}");
            Save(PanelWindow(model => model.Selected = 1), output, $"panel-selected-{suffix}");
            Save(PanelWindow(model => model.Query = "fri 2pm"), output, $"panel-typed-{suffix}");
            Save(PanelWindow(model => model.Query = "yesterday"), output, $"panel-error-{suffix}");
            Save(PanelWindow(model => { model.IsPicking = true; model.Pick(Today.AddDays(10)); }), output, $"panel-picker-{suffix}");
            Save(Content(new SnoozePanel(SnoozePanelModel.Refused(Now, "Can't snooze this window", "This app is running as administrator, so Windows won't let Bring Later hide it."))), output, $"panel-refused-{suffix}");
            Save(Content(new TrayFlyout(FlyoutModel(empty: false))), output, $"flyout-{suffix}");
            Save(Content(new TrayFlyout(FlyoutModel(empty: true))), output, $"flyout-empty-{suffix}");
            Save(Framed(new SettingsWindow(new SettingsModel(new AppSettings(), startAtSignIn: true) { UpdateStatus = "Checks GitHub for a newer release when you ask. Nothing is sent automatically." }), background), output, $"settings-{suffix}");
            Save(Framed(new WelcomeWindow(Shortcut.Default.Parts), background), output, $"welcome-{suffix}");
            Save(Framed(new QuitWindow(3), background), output, $"quit-{suffix}");
        }

        Console.WriteLine($"Wrote screenshots to {output}");
        return 0;
    }

    private static DateTimeOffset Now { get; } = new(2026, 9, 23, 15, 14, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 23, 15, 14, 0)));

    private static CivilDate Today => LocalTime.Parts(Now, TimeZoneInfo.Local).Date;

    private static DrawingImage AppIcon(Color color, string glyph)
    {
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            16, Brushes.White, 1);
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            context.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 24, 24), 6, 6);
            context.DrawText(text, new Point((24 - text.Width) / 2, (24 - text.Height) / 2));
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Border PanelWindow(Action<SnoozePanelModel> configure)
    {
        var model = new SnoozePanelModel(Now, TimePrefs.Standard, "Flights to Kathmandu – Google Flights", "Google Chrome", AppIcon(Color.FromRgb(0x1A, 0x61, 0xF4), ""));
        configure(model);
        return Content(new SnoozePanel(model));
    }

    private static TrayFlyoutModel FlyoutModel(bool empty)
    {
        var model = new TrayFlyoutModel(Shortcut.Default.ToString()) { SnoozeTarget = "Quarterly budget.xlsx – Excel" };
        if (!empty)
        {
            // The same rows Update() builds, with stand-in icons since these apps aren't running here.
            Row("Flights to Kathmandu – Google Flights", "Google Chrome", Now.AddHours(3.77), Color.FromRgb(0x1A, 0x61, 0xF4), "\uE774");
            Row("Re: Visa appointment", "Outlook", Now.AddHours(17.77), Color.FromRgb(0x0F, 0x6C, 0xBD), "\uE715");
            Row("Draft – Launch notes.docx", "Word", Now.AddDays(5).AddHours(-6.23), Color.FromRgb(0x2B, 0x57, 0x9A), "\uE8A5");
        }
        return model;

        void Row(string title, string app, DateTimeOffset due, Color color, string glyph) =>
            model.Rows.Add(new SnoozeRow(Guid.NewGuid(), title, $"{app} · {Format.When(due, Now)}", AppIcon(color, glyph), false));
    }

    /// <summary>A popup window's content, detached so it can be laid out and drawn without showing it.</summary>
    private static Border Content(Window window)
    {
        var content = (FrameworkElement)window.Content;
        var dataContext = window.DataContext;
        var resources = window.Resources;
        window.Content = null;
        var host = new Border { Child = content, DataContext = dataContext, Resources = resources };
        return host;
    }

    /// <summary>A standard window's content on its themed background, with a title bar drawn in the Windows 11 style.</summary>
    private static Border Framed(Window window, Brush background)
    {
        var title = window.Title;
        var width = double.IsNaN(window.Width) ? 520 : window.Width;
        var content = Content(window);
        var titleBar = new DockPanel { Height = 32, Background = background, LastChildFill = false };
        titleBar.Children.Add(new TextBlock
        {
            Text = title,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12,
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
        });
        var caption = new TextBlock
        {
            // These windows can't be resized, so Windows shows only the close button.
            Text = "\uE8BB",
            Margin = new Thickness(0, 0, 18, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
        };
        DockPanel.SetDock(caption, Dock.Right);
        titleBar.Children.Add(caption);

        var stack = new DockPanel { Width = width, Background = background };
        DockPanel.SetDock(titleBar, Dock.Top);
        stack.Children.Add(titleBar);
        stack.Children.Add(content);
        return new Border
        {
            Child = stack,
            BorderBrush = (Brush)Application.Current.FindResource("SurfaceStrokeBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
        };
    }

    private static void Save(FrameworkElement element, string folder, string name)
    {
        TextOptions.SetTextFormattingMode(element, TextFormattingMode.Ideal);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();

        var size = element.DesiredSize;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * Scale), (int)Math.Ceiling(size.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
        Console.WriteLine($"  {name}.png  {size.Width:0}×{size.Height:0}");
    }
}
