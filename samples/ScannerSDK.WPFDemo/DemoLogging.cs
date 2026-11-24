using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace ScannerSDK.WPFDemo;

public enum DemoLogSource
{
    UI,
    SDK,
    Core,
    Discovery,
    Session,
    Scan,
    Command,
    UsbHid,
    Serial,
}

public enum DemoLogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

public enum DemoLogLevelFilter
{
    All,
    Debug,
    Info,
    Warn,
    Error,
}

public sealed class DemoLogEntry : ViewModelBase
{
    private bool showSensitivePayloads;

    public DemoLogEntry(
        DateTime timestamp,
        DemoLogSource source,
        DemoLogLevel level,
        string message)
    {
        Timestamp = timestamp;
        Source = source;
        Level = level;
        Message = message;
    }

    public DateTime Timestamp { get; }

    public DemoLogSource Source { get; }

    public DemoLogLevel Level { get; }

    public string Message { get; }

    public string TimeText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public string SourceDisplay => DemoUiText.Get($"log_source_{Source.ToString().ToLowerInvariant()}");

    public string LevelDisplay => DemoUiText.Get($"log_level_{Level.ToString().ToLowerInvariant()}");

    public string DisplayText =>
        $"[{TimeText}] [{SourceDisplay}] [{LevelDisplay}] " +
        DemoLogRedactor.Redact(Source, Message, showSensitivePayloads);

    internal void SetSensitivePayloadVisibility(bool value)
    {
        if (showSensitivePayloads == value)
        {
            return;
        }

        showSensitivePayloads = value;
        OnPropertyChanged(nameof(DisplayText));
    }

    internal void RefreshLocalizedLabels()
    {
        OnPropertyChanged(nameof(SourceDisplay));
        OnPropertyChanged(nameof(LevelDisplay));
        OnPropertyChanged(nameof(DisplayText));
    }
}

public sealed class DemoLogSourceFilterItem : ViewModelBase
{
    private bool isSelected;

    public DemoLogSourceFilterItem(DemoLogSource source)
    {
        Source = source;
    }

    public DemoLogSource Source { get; }

    public string DisplayName => DemoUiText.Get($"log_source_{Source.ToString().ToLowerInvariant()}");

    public void RefreshLocalizedLabel() => OnPropertyChanged(nameof(DisplayName));

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}

public sealed class DemoLogStore : ViewModelBase
{
    private const int MaxEvents = 500;
    private string query = string.Empty;
    private DemoLogLevelFilter levelFilter = DemoLogLevelFilter.All;
    private bool showSensitivePayloads;

    public DemoLogStore()
    {
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;
        SourceFilterItems = new ObservableCollection<DemoLogSourceFilterItem>(
            Enum.GetValues<DemoLogSource>().Select(static source => new DemoLogSourceFilterItem(source))
        );
        foreach (var item in SourceFilterItems)
        {
            item.PropertyChanged += (_, _) =>
            {
                EntriesView.Refresh();
                OnPropertyChanged(nameof(FilterSummary));
            };
        }
    }

    public ObservableCollection<DemoLogEntry> Entries { get; } = [];

    public ICollectionView EntriesView { get; }

    public IReadOnlyList<DemoLogLevelFilter> LevelFilters { get; } = Enum.GetValues<DemoLogLevelFilter>();

    public ObservableCollection<DemoLogSourceFilterItem> SourceFilterItems { get; }

    public bool ShowSensitivePayloads
    {
        get => showSensitivePayloads;
        set
        {
            if (!SetProperty(ref showSensitivePayloads, value))
            {
                return;
            }

            foreach (var entry in Entries)
            {
                entry.SetSensitivePayloadVisibility(value);
            }
        }
    }

    public string Query
    {
        get => query;
        set
        {
            if (SetProperty(ref query, value))
            {
                EntriesView.Refresh();
                OnPropertyChanged(nameof(FilterSummary));
            }
        }
    }

    public DemoLogLevelFilter LevelFilter
    {
        get => levelFilter;
        set
        {
            if (SetProperty(ref levelFilter, value))
            {
                EntriesView.Refresh();
                OnPropertyChanged(nameof(FilterSummary));
            }
        }
    }

    public string FilterSummary =>
        $"filtered={EntriesView.Cast<DemoLogEntry>().Count()} total={Entries.Count} source={SourceFilterSummary()} level={LevelFilter}";

    public void Append(DemoLogSource source, DemoLogLevel level, string message)
    {
        var entry = new DemoLogEntry(DateTime.Now, source, level, message);
        entry.SetSensitivePayloadVisibility(ShowSensitivePayloads);
        Entries.Add(entry);
        while (Entries.Count > MaxEvents)
        {
            Entries.RemoveAt(0);
        }

        OnPropertyChanged(nameof(FilterSummary));
    }

    public void AppendCommandResult(string title, global::ScannerSDK.CommandResponse response)
    {
        Append(DemoLogSource.Command, DemoLogLevel.Info, DemoFormatters.FormatResponse(title, response));
    }

    public void Clear()
    {
        Entries.Clear();
        OnPropertyChanged(nameof(FilterSummary));
    }

    public string ExportAll(bool includeSensitivePayloads = false) =>
        FormatEntries(Entries, includeSensitivePayloads);

    public string ExportFiltered(bool includeSensitivePayloads = false) =>
        FormatEntries(EntriesView.Cast<DemoLogEntry>(), includeSensitivePayloads);

    public void RefreshLocalizedLabels()
    {
        foreach (var item in SourceFilterItems)
        {
            item.RefreshLocalizedLabel();
        }
        foreach (var entry in Entries)
        {
            entry.RefreshLocalizedLabels();
        }
        EntriesView.Refresh();
        OnPropertyChanged(nameof(FilterSummary));
    }

    private bool FilterEntry(object item)
    {
        if (item is not DemoLogEntry entry)
        {
            return false;
        }

        if (LevelFilter != DemoLogLevelFilter.All && entry.Level.ToString() != LevelFilter.ToString())
        {
            return false;
        }

        var selectedSources = SourceFilterItems
            .Where(static item => item.IsSelected)
            .Select(static item => item.Source)
            .ToHashSet();
        if (selectedSources.Count > 0 && !selectedSources.Contains(entry.Source))
        {
            return false;
        }

        var trimmedQuery = Query.Trim();
        return trimmedQuery.Length == 0 ||
            entry.Message.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
            entry.Source.ToString().Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
            entry.Level.ToString().Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatEntries(
        IEnumerable<DemoLogEntry> entries,
        bool includeSensitivePayloads)
    {
        var builder = new StringBuilder();
        builder.AppendLine(DemoUiText.Get("logs_export_title"));
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            DemoUiText.Get("logs_exported_at"),
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)));
        builder.AppendLine();
        foreach (var entry in entries)
        {
            builder.AppendLine(
                $"[{entry.TimeText}] [{entry.SourceDisplay}] [{entry.LevelDisplay}] " +
                DemoLogRedactor.Redact(entry.Source, entry.Message, includeSensitivePayloads));
        }

        return builder.ToString();
    }

    private string SourceFilterSummary()
    {
        var selected = SourceFilterItems
            .Where(static item => item.IsSelected)
            .Select(static item => item.Source.ToString())
            .ToArray();
        return selected.Length == 0 ? "All" : string.Join("/", selected);
    }
}
