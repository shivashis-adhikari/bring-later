using BringLater.Core;
using BringLater.Win32;

namespace BringLater.UI;

internal sealed record TimeOption(ClockTime Time, string Label)
{
    /// <summary>The closed ComboBox shows the item itself, so it has to read as the label.</summary>
    public override string ToString() => Label;
}

internal sealed class SettingsModel : Observable
{
    private Shortcut _shortcut;
    private bool _recording;
    private string _shortcutMessage = "";
    private TimeOption _morning;
    private TimeOption _evening;
    private bool _bringToFront;
    private bool _startAtSignIn;
    private string _updateStatus = "";
    private bool _checking;
    private string? _updateUrl;

    public SettingsModel(AppSettings settings, bool startAtSignIn)
    {
        _shortcut = settings.HotkeyOrDefault;
        var prefs = settings.TimePrefs;
        Times = Enumerable.Range(0, 48).Select(i => new ClockTime(i / 2, i % 2 * 30))
            .Append(prefs.Morning).Append(prefs.Evening).Distinct().Order()
            .Select(t => new TimeOption(t, Format.Clock(t))).ToList();
        _morning = Times.First(t => t.Time == prefs.Morning);
        _evening = Times.First(t => t.Time == prefs.Evening);
        _bringToFront = settings.BringToFront;
        _startAtSignIn = startAtSignIn;
    }

    public string Version => $"Version {UpdateChecker.CurrentText}";
    public IReadOnlyList<TimeOption> Times { get; }

    public Shortcut Shortcut
    {
        get => _shortcut;
        set
        {
            Set(ref _shortcut, value);
            Raise(nameof(ShortcutParts));
        }
    }

    public IReadOnlyList<string> ShortcutParts => _recording ? ["Press a shortcut"] : _shortcut.Parts;

    public bool Recording
    {
        get => _recording;
        set
        {
            Set(ref _recording, value);
            Raise(nameof(ShortcutParts));
        }
    }

    public string ShortcutMessage
    {
        get => _shortcutMessage;
        set
        {
            Set(ref _shortcutMessage, value);
            Raise(nameof(HasShortcutMessage));
        }
    }

    public bool HasShortcutMessage => _shortcutMessage.Length > 0;

    public TimeOption Morning
    {
        get => _morning;
        set => Set(ref _morning, value);
    }

    public TimeOption Evening
    {
        get => _evening;
        set => Set(ref _evening, value);
    }

    public bool BringToFront
    {
        get => _bringToFront;
        set
        {
            Set(ref _bringToFront, value);
            Raise(nameof(ShowQuietly));
        }
    }

    public bool ShowQuietly
    {
        get => !_bringToFront;
        set => BringToFront = !value;
    }

    public bool StartAtSignIn
    {
        get => _startAtSignIn;
        set => Set(ref _startAtSignIn, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        set => Set(ref _updateStatus, value);
    }

    public bool Checking
    {
        get => _checking;
        set
        {
            Set(ref _checking, value);
            Raise(nameof(CanCheck));
        }
    }

    public bool CanCheck => !_checking;

    public string? UpdateUrl
    {
        get => _updateUrl;
        set
        {
            Set(ref _updateUrl, value);
            Raise(nameof(HasUpdate));
        }
    }

    public bool HasUpdate => _updateUrl is not null;

    public AppSettings ApplyTo(AppSettings settings) => settings with
    {
        Shortcut = _shortcut.Serialize(),
        Morning = _morning.Time.ToString(),
        Evening = _evening.Time.ToString(),
        BringToFront = _bringToFront,
    };
}
