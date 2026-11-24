using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

public sealed record DemoLanguageOption(string Tag, string DisplayName);

public sealed record ModelOption(string ModelKey, string DisplayName, string SeriesName = "")
{
    public string Detail => string.IsNullOrWhiteSpace(SeriesName)
        ? DisplayName
        : $"{DisplayName} · {SeriesName}";
}

public sealed record TransportOption(ScannerSdkLib.TransportType TransportType, string DisplayName);

public sealed record CharsetOption(ScannerSdkLib.ScanTextCharset Charset, string DisplayName);

public sealed record TerminatorOption(string DisplayName, byte[] Bytes)
{
    public string Hex => Convert.ToHexString(Bytes);
}

public sealed record DataRuleModeOption(DataRuleMode Mode, string DisplayName);

public enum DataRuleMode
{
    Prefix,
    Suffix,
    HideStart,
    HideEnd,
    HideMiddle,
    Replace,
}

public enum SettingsSourceFilter
{
    All,
    Master,
    Module,
}

public sealed record DemoDeviceItem(
    string Name,
    string DeviceId,
    ScannerSdkLib.TransportType TransportType,
    string ModelKey,
    int? Rssi,
    string Detail
)
{
    public string RssiDisplay => Rssi?.ToString() ?? string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Detail)
        ? $"{Name} [{TransportType}]"
        : $"{Name} [{TransportType}] {Detail}";
}

public sealed class SettingItem : ViewModelBase
{
    private string currentValue = "<unread>";
    private string writeValue = string.Empty;
    private string status = "idle";
    private bool canReadAction;
    private bool canWriteAction;
    private bool persistWrite = true;

    public SettingItem(
        ScannerSdkLib.CapabilityEntry entry,
        IReadOnlyList<ScannerSdkLib.CapabilityOption>? options = null,
        string? displayName = null,
        IReadOnlyList<CapabilityOptionItem>? optionItems = null)
    {
        Entry = entry;
        Options = options ?? [];
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? (string.IsNullOrWhiteSpace(entry.SemanticKey) ? entry.EntryKey : entry.SemanticKey)
            : displayName;
        OptionItems = optionItems ?? Options.Select(static option => new CapabilityOptionItem(option.Value, option.Value)).ToArray();
        WriteValue = DemoParsing.DefaultWriteValue(entry, Options);
    }

    public ScannerSdkLib.CapabilityEntry Entry { get; }

    public IReadOnlyList<ScannerSdkLib.CapabilityOption> Options { get; }

    public string EntryKey => Entry.EntryKey;

    public string KeyText => Entry.EntryKey;

    public string DisplayName { get; }

    public IReadOnlyList<CapabilityOptionItem> OptionItems { get; }

    public string Group => string.IsNullOrWhiteSpace(Entry.GroupKey) ? "<none>" : Entry.GroupKey;

    public string Source => Entry.Source.ToString();

    public string Kind => Entry.ValueKind.ToString();

    public bool SupportsRead => Entry.SupportsRead;

    public bool SupportsWrite => Entry.SupportsWrite;

    public bool IsBoolean => SupportsWrite && Entry.ValueKind == ScannerSdkLib.CapabilityValueKind.Boolean;

    public bool IsEnum => SupportsWrite && Entry.ValueKind == ScannerSdkLib.CapabilityValueKind.Enum;

    public bool UsesTextEditor => SupportsWrite && !IsBoolean && !IsEnum;

    public bool Dangerous => Entry.RiskLevel != ScannerSdkLib.CapabilityRiskLevel.Normal;

    public string Capabilities => $"read={SupportsRead} write={SupportsWrite}";

    public string DefaultValue => string.IsNullOrWhiteSpace(Entry.DefaultValue)
        ? "<none>"
        : Entry.DefaultValue;

    public string Notes => string.IsNullOrWhiteSpace(Entry.Notes) ? string.Empty : Entry.Notes;

    public string OptionsSummary => Options.Count == 0
        ? string.Empty
        : string.Join(
            Environment.NewLine,
            Options.Select(static option => option.Value));

    public string CurrentValue
    {
        get => currentValue;
        set => SetProperty(ref currentValue, value);
    }

    public string WriteValue
    {
        get => writeValue;
        set => SetProperty(ref writeValue, value);
    }

    public bool BooleanWriteValue
    {
        get => DemoParsing.TryParseBoolText(WriteValue, out var value) && value;
        set
        {
            WriteValue = value ? "true" : "false";
            OnPropertyChanged();
        }
    }

    public string BooleanTrueText => bool.TrueString;

    public string BooleanFalseText => bool.FalseString;

    public bool PersistWrite
    {
        get => persistWrite;
        set => SetProperty(ref persistWrite, value);
    }

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    public bool CanReadAction
    {
        get => canReadAction;
        set => SetProperty(ref canReadAction, value);
    }

    public bool CanWriteAction
    {
        get => canWriteAction;
        set => SetProperty(ref canWriteAction, value);
    }
}

public sealed class ConsoleCommandGroup
{
    public ConsoleCommandGroup(string title, IEnumerable<ConsoleCommandItem> commands)
    {
        Title = title;
        Commands = new ObservableCollection<ConsoleCommandItem>(commands);
    }

    public string Title { get; }

    public ObservableCollection<ConsoleCommandItem> Commands { get; }

    public string Summary => $"{Commands.Count} command(s)";
}

public sealed class ConsoleCommandItem
{
    public ConsoleCommandItem(
        string title,
        string detail,
        bool isDangerous,
        AsyncRelayCommand command
    )
    {
        Title = title;
        Detail = detail;
        IsDangerous = isDangerous;
        Command = command;
    }

    public string Title { get; }

    public string Detail { get; }

    public bool IsDangerous { get; }

    public AsyncRelayCommand Command { get; }

    public string Tone => IsDangerous ? "Danger" : "Normal";
}

public sealed class DemoDeviceActionItem : ViewModelBase
{
    private string inputValue = string.Empty;
    private string status = DemoUiText.Get("device_action_status_idle");

    public DemoDeviceActionItem(ScannerSdkLib.CapabilityEntry entry, string? displayName = null)
    {
        Entry = entry;
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? (string.IsNullOrWhiteSpace(entry.SemanticKey) ? entry.EntryKey : entry.SemanticKey)
            : displayName;
        GroupName = string.IsNullOrWhiteSpace(entry.GroupKey) ? "<none>" : entry.GroupKey;
        ValueHintDisplay = string.IsNullOrWhiteSpace(entry.ValueHint) ? string.Empty : entry.ValueHint;
    }

    public ScannerSdkLib.CapabilityEntry Entry { get; }

    public string DisplayName { get; }

    public string GroupName { get; }

    public string ValueHintDisplay { get; }

    public bool Dangerous => Entry.RiskLevel != ScannerSdkLib.CapabilityRiskLevel.Normal;

    public bool RequiresValue => Entry.RequiresValue;

    public string KeyText => Entry.EntryKey;

    public string RiskLevelText => Entry.RiskLevel switch
    {
        ScannerSdkLib.CapabilityRiskLevel.Normal => DemoUiText.Get("device_action_risk_normal"),
        ScannerSdkLib.CapabilityRiskLevel.Destructive => DemoUiText.Get("device_action_risk_destructive"),
        ScannerSdkLib.CapabilityRiskLevel.Connectivity => DemoUiText.Get("device_action_risk_connectivity"),
        ScannerSdkLib.CapabilityRiskLevel.DataLoss => DemoUiText.Get("device_action_risk_data_loss"),
        _ => Entry.RiskLevel.ToString(),
    };

    public string RiskBadgeText => string.Format(
        Dangerous
            ? DemoUiText.Get("device_action_dangerous_badge")
            : DemoUiText.Get("device_action_normal_badge"),
        RiskLevelText
    );

    public string SourceAndRiskText => $"{Entry.Source} · {RiskBadgeText}";

    public string RequirementText => RequiresValue
        ? DemoUiText.Get("device_action_requirement_required")
        : DemoUiText.Get("device_action_requirement_none");

    public string InputValue
    {
        get => inputValue;
        set => SetProperty(ref inputValue, value);
    }

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }
}

public static class DemoFormatters
{
    public static string FormatScan(ScannerSdkLib.ScanEvent scan, ScannerSdkLib.ScanTextCharset charset)
    {
        var text = charset.ToEncoding().GetString(scan.TextBytes);
        return $"ts={scan.TimestampMs} type={scan.BarcodeType} charset={charset} text={text} " +
            $"textBytes={Convert.ToHexString(scan.TextBytes)} rawBytes={Convert.ToHexString(scan.RawBytes)}";
    }

    public static string FormatResponse(string title, ScannerSdkLib.CommandResponse response)
    {
        var text = response.Text.Replace("\r", "\\r").Replace("\n", "\\n");
        return $"{title}: ack={response.Acknowledged} records={response.RecordCount} text={text} raw={response.RawHex}";
    }

    public static string FormatCapabilityValue(ScannerSdkLib.CapabilityValue value)
    {
        if (value.BooleanValue is bool booleanValue)
        {
            return booleanValue.ToString();
        }

        if (!string.IsNullOrEmpty(value.TextValue))
        {
            return value.TextValue;
        }

        if (value.Bytes.Length > 0 && IsPrintableAscii(value.Bytes))
        {
            return Encoding.ASCII.GetString(value.Bytes);
        }

        return Convert.ToHexString(value.Bytes);
    }

    public static string FormatInfo(ScannerSdkLib.ScannerInfo info)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"DeviceId={info.DeviceId}");
        builder.AppendLine($"Name={info.Name}");
        builder.AppendLine($"Serial={info.SerialNumber}");
        builder.AppendLine($"Firmware={info.FirmwareVersion}");
        builder.AppendLine($"Hardware={info.HardwareVersion}");
        builder.AppendLine($"Manufacturer={info.Manufacturer}");
        builder.AppendLine($"Version={info.VersionFormatFamily}/{info.VersionReleaseCode}/{info.VersionExtensionCode}");
        return builder.ToString().TrimEnd();
    }

    public static string FormatTrace(ScannerSdkLib.CommandTrace trace)
    {
        var request = FirstNonEmpty(trace.RequestText, trace.RequestHex);
        var response = FirstNonEmpty(trace.ResponseText, trace.ResponseHex);
        return $"#{trace.TraceId} kind={trace.Kind} op={trace.Operation} entry={EmptyAsDash(trace.EntryKey)} semantic={EmptyAsDash(trace.SemanticKey)} err={trace.ErrorCode} ack={trace.Acknowledged} request={request} response={response}";
    }

    public static string FormatBattery(ScannerSdkLib.BatteryInfo battery)
    {
        return $"raw={battery.RawText} voltage={battery.VoltageText} percent={battery.Percent}";
    }

    public static string FormatStorageUsage(ScannerSdkLib.StorageUsage usage)
    {
        return $"count={usage.BarcodeCount} used={usage.Used}/{usage.Capacity} remaining={usage.Remaining} raw={usage.RawText}";
    }

    public static string EmptyAsDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "-";
    }

    private static bool IsPrintableAscii(byte[] value)
    {
        return value.All(static b => b >= 0x20 && b <= 0x7E);
    }
}

public static class DemoParsing
{
    public static string DefaultWriteValue(
        ScannerSdkLib.CapabilityEntry entry,
        IReadOnlyList<ScannerSdkLib.CapabilityOption>? options = null)
    {
        if (!string.IsNullOrWhiteSpace(entry.DefaultValue))
        {
            return entry.DefaultValue;
        }

        return entry.ValueKind switch
        {
            ScannerSdkLib.CapabilityValueKind.Boolean => "true",
            ScannerSdkLib.CapabilityValueKind.UInt8 => entry.SemanticKey switch
            {
                "ImmediateScanDuration" => "1",
                _ => "0",
            },
            ScannerSdkLib.CapabilityValueKind.UInt16 => "0",
            ScannerSdkLib.CapabilityValueKind.Enum => options?.FirstOrDefault()?.Value ?? string.Empty,
            _ => string.Empty,
        };
    }

    public static byte[] ParseHexBytes(string value)
    {
        var compact = new string(value.Where(static ch => !char.IsWhiteSpace(ch) && ch != '-' && ch != ':').ToArray());
        if (compact.Length == 0)
        {
            return [];
        }

        if (compact.Length % 2 != 0)
        {
            throw new ArgumentException("Hex text must contain an even number of hex digits.");
        }

        return Convert.FromHexString(compact);
    }

    public static byte ParseByteText(string value, string name)
    {
        if (byte.TryParse(value, out var decimalValue))
        {
            return decimalValue;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        if (byte.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var hexValue))
        {
            return hexValue;
        }

        throw new ArgumentException($"{name} must be a byte value.");
    }

    public static ushort ParseUInt16Text(string value, string name)
    {
        if (ushort.TryParse(value, out var decimalValue))
        {
            return decimalValue;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        if (ushort.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var hexValue))
        {
            return hexValue;
        }

        throw new ArgumentException($"{name} must be an unsigned 16-bit value.");
    }

    public static ScannerSdkLib.CapabilityValue EncodeCapabilityValue(
        ScannerSdkLib.CapabilityEntry entry,
        IReadOnlyList<ScannerSdkLib.CapabilityOption> options,
        string value)
    {
        return entry.ValueKind switch
        {
            ScannerSdkLib.CapabilityValueKind.Boolean => ScannerSdkLib.CapabilityValue.FromBoolean(ParseBoolText(value)),
            ScannerSdkLib.CapabilityValueKind.Enum => ScannerSdkLib.CapabilityValue.FromBytes(
                entry.ValueKind,
                ParseEnumBytes(options, value)
            ),
            ScannerSdkLib.CapabilityValueKind.UInt8 => ScannerSdkLib.CapabilityValue.FromBytes(
                entry.ValueKind,
                [ParseByteText(value, entry.EntryKey)]
            ),
            ScannerSdkLib.CapabilityValueKind.UInt16 => ScannerSdkLib.CapabilityValue.FromBytes(
                entry.ValueKind,
                EncodeUInt16(ParseUInt16Text(value, entry.EntryKey))
            ),
            ScannerSdkLib.CapabilityValueKind.BytesAscii => ScannerSdkLib.CapabilityValue.FromAsciiText(value),
            ScannerSdkLib.CapabilityValueKind.Action => ScannerSdkLib.CapabilityValue.FromBytes(entry.ValueKind, []),
            _ => ScannerSdkLib.CapabilityValue.FromBytes(entry.ValueKind, ParseHexBytes(value)),
        };
    }

    public static bool ParseBoolText(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new ArgumentException("Boolean value must be true/false or 1/0."),
        };
    }

    public static bool TryParseBoolText(string value, out bool parsed)
    {
        try
        {
            parsed = ParseBoolText(value);
            return true;
        }
        catch (ArgumentException)
        {
            parsed = false;
            return false;
        }
    }

    private static byte[] ParseEnumBytes(IReadOnlyList<ScannerSdkLib.CapabilityOption> options, string value)
    {
        var trimmed = value.Trim();
        var option = options
            .FirstOrDefault(option =>
                string.Equals(option.Value, trimmed, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            return Encoding.ASCII.GetBytes(option.Value);
        }

        return Encoding.ASCII.GetBytes(trimmed);
    }

    private static byte[] EncodeUInt16(ushort value)
    {
        return [(byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)];
    }

}

public sealed class BooleanToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public sealed class InverseBooleanConverter : System.Windows.Data.IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        System.Globalization.CultureInfo culture) =>
        value is not true;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        System.Globalization.CultureInfo culture) =>
        value is not true;
}
