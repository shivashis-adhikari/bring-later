using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using BringLater.Core;

namespace BringLater.UI;

internal sealed class PresetRow(string label, string time, string shortcut, DateTimeOffset? date) : Observable
{
    private bool _isSelected;

    public string Label { get; } = label;
    public string Time { get; } = time;
    public string Shortcut { get; } = shortcut;
    /// <summary>Null for "Pick a date and time…".</summary>
    public DateTimeOffset? Date { get; } = date;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

internal sealed class DayCell(CivilDate date, bool inMonth, bool isToday, bool enabled) : Observable
{
    private bool _isSelected;

    public CivilDate Date { get; } = date;
    public string Label { get; } = date.Day.ToString(CultureInfo.CurrentCulture);
    public bool InMonth { get; } = inMonth;
    public bool IsToday { get; } = isToday;
    public bool IsEnabled { get; } = enabled;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>State behind the snooze panel: presets, typed input and the date picker.</summary>
internal sealed class SnoozePanelModel : Observable
{
    private static readonly string[] MonthWords = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private readonly TimeZoneInfo _zone = TimeZoneInfo.Local;
    private readonly TimePrefs _prefs;
    private readonly CivilDate _today;
    private string _query = "";
    private string _result = "";
    private bool _resultIsError;
    private DateTimeOffset? _parsed;
    private int _selected = -1;
    private bool _isPicking;
    private string _error = "";
    private (int Year, int Month) _month;
    private CivilDate _pickedDate;
    private string _pickedTime;
    private string _pickResult = "";
    private bool _pickIsError;
    private DateTimeOffset? _picked;

    public SnoozePanelModel(DateTimeOffset now, TimePrefs prefs, string title, string subtitle, ImageSource? icon)
    {
        Now = now;
        _prefs = prefs;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
        _today = LocalTime.Parts(now, _zone).Date;

        var presets = Presets.Compute(now, _zone, prefs);
        var rows = presets.Select((p, i) => new PresetRow(Copy.Preset(p.Kind), Format.Short(p.Date, now), $"Ctrl+{i + 1}", p.Date)).ToList();
        rows.Add(new PresetRow(Copy.PickDateAndTime, "", $"Ctrl+{rows.Count + 1}", null));
        Rows = new ObservableCollection<PresetRow>(rows);

        _pickedDate = _today.AddDays(1);
        _pickedTime = TimeText(prefs.Morning);
        _month = (_pickedDate.Year, _pickedDate.Month);
        WeekdayHeaders = WeekdayOrder().Select(d => CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(d)).ToList();
        BuildMonth();
        ReparsePick();
    }

    /// <summary>A panel that explains why a window can't be snoozed, instead of offering times.</summary>
    public static SnoozePanelModel Refused(DateTimeOffset now, string title, string message) =>
        new(now, TimePrefs.Standard, title, "", null) { Message = message };

    public DateTimeOffset Now { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public bool HasSubtitle => Subtitle.Length > 0;
    public ImageSource? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string Placeholder => Copy.InputPlaceholder;
    public ObservableCollection<PresetRow> Rows { get; }

    public string Message { get; private init; } = "";
    public bool IsRefused => Message.Length > 0;
    public bool ShowInput => !IsRefused && !IsPicking;
    public bool ShowPresets => ShowInput && _query.Trim().Length == 0;
    public bool ShowResult => ShowInput && _query.Trim().Length > 0;

    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value))
                return;
            var parsed = TimeGrammar.Parse(value, Now, _zone, _prefs);
            _parsed = parsed.Date;
            Result = parsed.Date is { } date ? Format.When(date, Now) : parsed.Error is TimeParseError.Empty ? "" : Copy.ParseError(parsed.Error!.Value);
            ResultIsError = parsed.Date is null;
            Raise(nameof(ShowPresets));
            Raise(nameof(ShowResult));
        }
    }

    public string Result
    {
        get => _result;
        private set => Set(ref _result, value);
    }

    public bool ResultIsError
    {
        get => _resultIsError;
        private set => Set(ref _resultIsError, value);
    }

    public int Selected
    {
        get => _selected;
        set
        {
            var clamped = Math.Clamp(value, -1, Rows.Count - 1);
            if (!Set(ref _selected, clamped))
                return;
            for (var i = 0; i < Rows.Count; i++)
                Rows[i].IsSelected = i == clamped;
        }
    }

    public string Error
    {
        get => _error;
        set
        {
            Set(ref _error, value);
            Raise(nameof(HasError));
        }
    }

    public bool HasError => _error.Length > 0;

    public bool IsPicking
    {
        get => _isPicking;
        set
        {
            if (!Set(ref _isPicking, value))
                return;
            Raise(nameof(ShowInput));
            Raise(nameof(ShowPresets));
            Raise(nameof(ShowResult));
        }
    }

    /// <summary>What Enter would choose right now, or null if nothing is chosen yet.</summary>
    public DateTimeOffset? Committable =>
        IsPicking ? _picked
        : ShowResult ? _parsed
        : Selected >= 0 ? Rows[Selected].Date
        : null;

    // Date picker

    public IReadOnlyList<string> WeekdayHeaders { get; }
    public ObservableCollection<DayCell> Days { get; } = [];

    public string MonthTitle => new DateTime(_month.Year, _month.Month, 1).ToString("Y", CultureInfo.CurrentCulture);
    public bool CanGoBack => (_month.Year, _month.Month).CompareTo((_today.Year, _today.Month)) > 0;
    public bool CanGoForward => (_month.Year * 12 + _month.Month) < (_today.Year * 12 + _today.Month + 12);

    public string PickedTime
    {
        get => _pickedTime;
        set
        {
            if (Set(ref _pickedTime, value))
                ReparsePick();
        }
    }

    public string PickResult
    {
        get => _pickResult;
        private set => Set(ref _pickResult, value);
    }

    public bool PickIsError
    {
        get => _pickIsError;
        private set => Set(ref _pickIsError, value);
    }

    public bool CanPick => _picked is not null;

    public void ShiftMonth(int months)
    {
        var index = _month.Year * 12 + _month.Month - 1 + months;
        var candidate = (index / 12, index % 12 + 1);
        if (candidate.CompareTo((_today.Year, _today.Month)) < 0 || (candidate.Item1 * 12 + candidate.Item2) > (_today.Year * 12 + _today.Month + 12))
            return;
        _month = candidate;
        BuildMonth();
    }

    public void Pick(CivilDate date)
    {
        if (date < _today || date > _today.AddDays(366))
            return;
        _pickedDate = date;
        if ((date.Year, date.Month) != _month)
        {
            _month = (date.Year, date.Month);
            BuildMonth();
        }
        foreach (var day in Days)
            day.IsSelected = day.Date == date;
        ReparsePick();
    }

    public void MovePick(int days) => Pick(_pickedDate.AddDays(days));

    private void BuildMonth()
    {
        Days.Clear();
        var first = new CivilDate(_month.Year, _month.Month, 1);
        var order = WeekdayOrder();
        var lead = order.IndexOf(ToDayOfWeek(first.Weekday));
        var start = first.AddDays(-lead);
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var enabled = date >= _today && date <= _today.AddDays(366);
            Days.Add(new DayCell(date, date.Month == _month.Month, date == _today, enabled) { IsSelected = date == _pickedDate });
        }
        Raise(nameof(MonthTitle));
        Raise(nameof(CanGoBack));
        Raise(nameof(CanGoForward));
    }

    private void ReparsePick()
    {
        var phrase = $"{MonthWords[_pickedDate.Month - 1]} {_pickedDate.Day} {_pickedTime}";
        var parsed = _pickedTime.Trim().Length == 0 ? new TimeParseResult(null, TimeParseError.Unrecognized) : TimeGrammar.Parse(phrase, Now, _zone, _prefs);
        _picked = parsed.Date;
        PickResult = parsed.Date is { } date ? Format.When(date, Now) : parsed.Error == TimeParseError.Past ? Copy.ParseError(TimeParseError.Past) : "Type a time, like 9am or 14:30";
        PickIsError = parsed.Date is null;
        Raise(nameof(CanPick));
    }

    private static List<DayOfWeek> WeekdayOrder()
    {
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        return Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)first + i) % 7)).ToList();
    }

    private static DayOfWeek ToDayOfWeek(int isoWeekday) => (DayOfWeek)(isoWeekday % 7);

    /// <summary>A time the grammar reads back, in the user's 12- or 24-hour habit.</summary>
    private static string TimeText(ClockTime time)
    {
        var uses24 = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H', StringComparison.Ordinal);
        var value = new DateTime(2000, 1, 1, time.Hour, time.Minute, 0);
        return uses24 ? value.ToString("HH:mm", CultureInfo.InvariantCulture) : value.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }
}
