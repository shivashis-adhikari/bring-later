using System.Globalization;
using System.Windows.Input;

namespace BringLater.Win32;

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Alt = (int)Native.MOD_ALT,
    Control = (int)Native.MOD_CONTROL,
    Shift = (int)Native.MOD_SHIFT,
    Win = (int)Native.MOD_WIN,
}

/// <summary>A global keyboard shortcut: modifiers plus one virtual key.</summary>
internal readonly record struct Shortcut(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>Win+Shift+Z. Z for snooze, and nothing in Windows or PowerToys claims it.</summary>
    public static Shortcut Default { get; } = new(HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x5A);

    /// <summary>
    /// A shortcut needs Ctrl, Alt or Win. Shift alone would steal typed capitals, and Ctrl+Alt
    /// without anything else is AltGr on many keyboards, so it would steal typed characters too.
    /// </summary>
    public bool IsValid =>
        VirtualKey != 0
        && (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0
        && (Modifiers & ~HotkeyModifiers.Shift) != (HotkeyModifiers.Control | HotkeyModifiers.Alt);

    /// <summary>Key names in the order Windows uses: Win, Ctrl, Alt, Shift, then the key.</summary>
    public IReadOnlyList<string> Parts
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            parts.Add(KeyName(VirtualKey));
            return parts;
        }
    }

    public override string ToString() => string.Join("+", Parts);

    public static Shortcut? FromWpf(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None)
            return null;
        var mods = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        return new Shortcut(mods, KeyInterop.VirtualKeyFromKey(key));
    }

    /// <summary>Stored form, e.g. "Win+Shift+0x5A". Stable across keyboard layouts.</summary>
    public string Serialize() =>
        string.Join("+", Parts.SkipLast(1).Append(string.Create(CultureInfo.InvariantCulture, $"0x{VirtualKey:X2}")));

    public static Shortcut? Deserialize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var mods = HotkeyModifiers.None;
        var key = 0;
        foreach (var part in text.Split('+'))
        {
            switch (part)
            {
                case "Win": mods |= HotkeyModifiers.Win; break;
                case "Ctrl": mods |= HotkeyModifiers.Control; break;
                case "Alt": mods |= HotkeyModifiers.Alt; break;
                case "Shift": mods |= HotkeyModifiers.Shift; break;
                default:
                    if (!part.StartsWith("0x", StringComparison.Ordinal)
                        || !int.TryParse(part.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out key))
                        return null;
                    break;
            }
        }
        var shortcut = new Shortcut(mods, key);
        return shortcut.IsValid ? shortcut : null;
    }

    private static string KeyName(int vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87)
            return string.Create(CultureInfo.InvariantCulture, $"F{vk - 0x6F}");
        return KeyInterop.KeyFromVirtualKey(vk) switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "Page Up",
            Key.PageDown => "Page Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemTilde => "`",
            var other => other.ToString(),
        };
    }
}
