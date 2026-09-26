using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BringLater.Win32;
using Microsoft.Win32;

namespace BringLater.UI;

internal enum ThemeKind
{
    Light,
    Dark,
    HighContrast,
}

/// <summary>Follows the Windows app theme (light, dark or a high-contrast theme) and applies it live.</summary>
internal static class Theme
{
    private const int PaletteIndex = 0;

    public static ThemeKind Current { get; private set; } = ThemeKind.Light;

    public static ThemeKind Detect()
    {
        if (SystemParameters.HighContrast)
            return ThemeKind.HighContrast;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0 ? ThemeKind.Dark : ThemeKind.Light;
    }

    /// <summary>Loads the palette for <paramref name="kind"/> into the app's resources and restyles open windows.</summary>
    public static void Apply(ThemeKind kind)
    {
        Current = kind;
        var palette = kind switch
        {
            ThemeKind.HighContrast => HighContrastPalette(),
            ThemeKind.Dark => new ResourceDictionary { Source = new Uri("pack://application:,,,/BringLater;component/UI/Colors.Dark.xaml") },
            _ => new ResourceDictionary { Source = new Uri("pack://application:,,,/BringLater;component/UI/Colors.Light.xaml") },
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Count > PaletteIndex)
            dictionaries[PaletteIndex] = palette;
        else
            dictionaries.Insert(PaletteIndex, palette);

        foreach (Window window in Application.Current.Windows)
            StyleFrame(window);
    }

    /// <summary>
    /// Matches the title bar of a standard window to the theme: dark title bar in dark mode, and on
    /// Windows 11 a caption colored like the window so the frame and content read as one surface.
    /// </summary>
    public static void StyleFrame(Window window)
    {
        if (window.WindowStyle == WindowStyle.None)
            return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var dark = Current == ThemeKind.Dark ? 1 : 0;
        _ = Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        var corners = Native.DWMWCP_ROUND;
        _ = Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));

        if (Current != ThemeKind.HighContrast && Application.Current.TryFindResource("CaptionColor") is Color caption)
        {
            var colorRef = caption.R | caption.G << 8 | caption.B << 16;
            _ = Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
        }
    }

    /// <summary>High-contrast themes define their own colors. Every brush maps to a system color so the user's theme wins.</summary>
    private static ResourceDictionary HighContrastPalette()
    {
        var window = SystemColors.WindowBrush;
        var text = SystemColors.WindowTextBrush;
        var gray = SystemColors.GrayTextBrush;
        var highlight = SystemColors.HighlightBrush;
        var highlightText = SystemColors.HighlightTextBrush;
        var hot = SystemColors.HotTrackBrush;

        var palette = new ResourceDictionary
        {
            ["ShadowColor"] = Colors.Transparent,
            ["ShadowOpacity"] = 0.0,
            ["CaptionColor"] = SystemColors.WindowColor,
        };
        foreach (var key in new[] { "WindowBackgroundBrush", "SurfaceBrush", "CardBrush", "ControlBrush", "ControlHoverBrush", "ControlPressedBrush", "InputBrush", "InputFocusedBrush", "KeycapBrush" })
            palette[key] = window;
        foreach (var key in new[] { "SurfaceStrokeBrush", "CardStrokeBrush", "DividerBrush", "ControlStrokeBrush", "ControlStrokeBottomBrush", "InputStrokeBrush", "InputBottomBrush", "KeycapStrokeBrush", "KeycapShadowBrush", "ToggleOffStrokeBrush", "ToggleOffKnobBrush", "ScrollThumbBrush", "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "FocusOuterBrush" })
            palette[key] = text;
        foreach (var key in new[] { "AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "SelectedBrush", "AccentTintBrush", "SubtleHoverBrush", "SubtlePressedBrush" })
            palette[key] = highlight;
        palette["AccentTextBrush"] = highlightText;
        palette["ToggleOnKnobBrush"] = highlightText;
        palette["TextDisabledBrush"] = gray;
        palette["AccentDisabledBrush"] = gray;
        palette["CriticalBrush"] = hot;
        palette["FocusInnerBrush"] = window;
        return palette;
    }
}
