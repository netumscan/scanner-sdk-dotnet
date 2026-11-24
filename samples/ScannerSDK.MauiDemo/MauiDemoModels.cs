using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.MauiDemo;

public sealed record MauiModel(string ModelKey, string ModelName)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ModelName)
        ? ModelKey
        : $"{ModelName} ({ModelKey})";
}

public sealed record MauiDevice
{
    internal MauiDevice(string deviceId, string name, string modelKey)
    {
        DeviceId = deviceId;
        Name = name;
        ModelKey = modelKey;
    }

    internal string DeviceId { get; }

    public string Name { get; }

    public string ModelKey { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed scanner" : Name;

    public string ModelSummary => string.IsNullOrWhiteSpace(ModelKey) ? "Model unresolved" : ModelKey;

    public string AccessibilityDescription => $"{DisplayName}, {ModelSummary}. Connect scanner.";
}

public sealed record MauiScanRow(long Sequence, int BarcodeType, string Text)
{
    public string DisplayText => string.IsNullOrEmpty(Text) ? "(empty scan)" : Text;

    public string Summary => $"Type {BarcodeType} · {Text.Length} characters";

    public string AccessibilityDescription => $"Scan {Sequence}. {DisplayText}. {Summary}.";
}

internal sealed record MauiOperationSupport(
    bool RefreshInfo,
    bool Battery,
    bool TriggerScan)
{
    internal static readonly MauiOperationSupport None = new(false, false, false);
}

internal sealed record MauiDeviceInfo(
    string Name,
    string ResolvedModel,
    string Firmware,
    string Hardware)
{
    internal string DisplaySummary()
    {
        var parts = new[]
        {
            string.IsNullOrWhiteSpace(Name) ? null : Name,
            string.IsNullOrWhiteSpace(ResolvedModel) ? null : ResolvedModel,
            string.IsNullOrWhiteSpace(Firmware) ? null : $"FW {Firmware}",
            string.IsNullOrWhiteSpace(Hardware) ? null : $"HW {Hardware}",
        };
        return string.Join(" · ", parts.Where(static part => part is not null)) is { Length: > 0 } summary
            ? summary
            : "No device information returned";
    }
}

internal sealed record MauiBattery(int Percent, string Voltage)
{
    internal string DisplaySummary() => string.IsNullOrWhiteSpace(Voltage)
        ? $"{Percent}%"
        : $"{Percent}% · {Voltage}";
}

internal sealed record MauiScanEvent(int BarcodeType, string Text);

internal sealed record MauiSettingDefinition(
    string EntryKey,
    string DisplayName,
    string GroupSummary,
    bool SupportsRead,
    bool SupportsWrite,
    ScannerSdkLib.CapabilityValueKind ValueKind,
    ScannerSdkLib.CapabilityRiskLevel RiskLevel,
    string ValueHint,
    string DefaultValue,
    IReadOnlyList<string> Options,
    string DomainKey = "other",
    string DomainDisplayName = "Other settings",
    int DomainSortOrder = int.MaxValue);

public sealed class MauiSettingGroup : ObservableCollection<MauiSettingRow>
{
    private bool isSelected;

    internal MauiSettingGroup(
        string domainKey,
        string displayName,
        int sortOrder,
        IEnumerable<MauiSettingRow> settings)
        : base(settings)
    {
        DomainKey = domainKey;
        DisplayName = displayName;
        SortOrder = sortOrder;
    }

    internal string DomainKey { get; }

    public string DisplayName { get; }

    internal int SortOrder { get; }

    public string CountText => $"{Count} setting{(Count == 1 ? string.Empty : "s")}";

    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (isSelected == value)
            {
                return;
            }
            isSelected = value;
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed class MauiSettingRow : ObservableObject
{
    private string currentValue = "Not read";
    private string writeValue;
    private string status = "Ready";
    private bool persistWrite;
    private bool riskConfirmed;

    internal MauiSettingRow(MauiSettingDefinition definition)
    {
        Definition = definition;
        writeValue = MauiSettingCodec.DefaultWriteValue(definition);
    }

    internal MauiSettingDefinition Definition { get; }

    public string DisplayName => Definition.DisplayName;

    public string GroupSummary => Definition.GroupSummary;

    public string CapabilitySummary =>
        $"{Definition.ValueKind} · {(Definition.SupportsRead ? "read" : "no read")} · {(Definition.SupportsWrite ? "write" : "read only")}";

    public string CurrentValue
    {
        get => currentValue;
        internal set => SetProperty(ref currentValue, value ?? string.Empty);
    }

    public string WriteValue
    {
        get => writeValue;
        set
        {
            var normalized = value ?? string.Empty;
            if (SetProperty(ref writeValue, normalized) && IsBooleanEditor)
            {
                NotifyPropertyChanged(nameof(BooleanWriteValue));
            }
        }
    }

    public bool BooleanWriteValue
    {
        get => MauiSettingCodec.TryParseBoolean(WriteValue, out var value) && value;
        set
        {
            WriteValue = value ? "true" : "false";
            NotifyPropertyChanged();
        }
    }

    public string Status
    {
        get => status;
        internal set => SetProperty(ref status, value);
    }

    public bool PersistWrite
    {
        get => persistWrite;
        set => SetProperty(ref persistWrite, value);
    }

    public bool RiskConfirmed
    {
        get => riskConfirmed;
        set => SetProperty(ref riskConfirmed, value);
    }

    public bool SupportsRead => Definition.SupportsRead;

    public bool SupportsWrite => Definition.SupportsWrite;

    public bool IsBooleanEditor => SupportsWrite && Definition.ValueKind == ScannerSdkLib.CapabilityValueKind.Boolean;

    public bool IsEnumEditor => SupportsWrite && Definition.ValueKind == ScannerSdkLib.CapabilityValueKind.Enum;

    public bool UsesTextEditor => SupportsWrite && !IsBooleanEditor && !IsEnumEditor;

    public bool RequiresRiskConfirmation =>
        SupportsWrite && Definition.RiskLevel != ScannerSdkLib.CapabilityRiskLevel.Normal;

    public string RiskText => $"{Definition.RiskLevel} setting — confirm before writing";

    public IReadOnlyList<string> Options => Definition.Options;

    public string InputHint => string.IsNullOrWhiteSpace(Definition.ValueHint)
        ? Definition.ValueKind switch
        {
            ScannerSdkLib.CapabilityValueKind.UInt8 => "0-255 or 0x00-0xFF",
            ScannerSdkLib.CapabilityValueKind.UInt16 => "0-65535 or 0x0000-0xFFFF",
            ScannerSdkLib.CapabilityValueKind.BytesAscii => "ASCII text",
            _ => "Hex bytes, for example 01 FF",
        }
        : Definition.ValueHint;

    public string AccessibilityDescription =>
        $"{DisplayName}. {CapabilitySummary}. Current value {CurrentValue}. Status {Status}.";
}

internal static class MauiSettingCodec
{
    internal static string DefaultWriteValue(MauiSettingDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.DefaultValue))
        {
            return definition.DefaultValue;
        }
        return definition.ValueKind switch
        {
            ScannerSdkLib.CapabilityValueKind.Boolean => "false",
            ScannerSdkLib.CapabilityValueKind.Enum => definition.Options.FirstOrDefault() ?? string.Empty,
            ScannerSdkLib.CapabilityValueKind.UInt8 or ScannerSdkLib.CapabilityValueKind.UInt16 => "0",
            _ => string.Empty,
        };
    }

    internal static ScannerSdkLib.CapabilityValue Encode(MauiSettingDefinition definition, string? value)
    {
        var normalized = value ?? string.Empty;
        return definition.ValueKind switch
        {
            ScannerSdkLib.CapabilityValueKind.Boolean =>
                ScannerSdkLib.CapabilityValue.FromBoolean(ParseBoolean(normalized)),
            ScannerSdkLib.CapabilityValueKind.Enum =>
                ScannerSdkLib.CapabilityValue.FromBytes(definition.ValueKind, Encoding.ASCII.GetBytes(ParseEnum(definition, normalized))),
            ScannerSdkLib.CapabilityValueKind.UInt8 =>
                ScannerSdkLib.CapabilityValue.FromBytes(definition.ValueKind, [ParseByte(normalized, definition.DisplayName)]),
            ScannerSdkLib.CapabilityValueKind.UInt16 =>
                ScannerSdkLib.CapabilityValue.FromBytes(definition.ValueKind, EncodeUInt16(ParseUInt16(normalized, definition.DisplayName))),
            ScannerSdkLib.CapabilityValueKind.BytesAscii => ScannerSdkLib.CapabilityValue.FromAsciiText(normalized),
            ScannerSdkLib.CapabilityValueKind.Action =>
                throw new NotSupportedException("Action entries cannot be edited as settings."),
            _ => ScannerSdkLib.CapabilityValue.FromBytes(definition.ValueKind, ParseHex(normalized)),
        };
    }

    internal static string Format(MauiSettingDefinition definition, ScannerSdkLib.CapabilityValue value)
    {
        if (definition.ValueKind == ScannerSdkLib.CapabilityValueKind.Boolean && value.BooleanValue is bool boolean)
        {
            return boolean ? "true" : "false";
        }
        if (definition.ValueKind == ScannerSdkLib.CapabilityValueKind.BytesAscii && value.TextValue is not null)
        {
            return value.TextValue;
        }
        if (definition.ValueKind == ScannerSdkLib.CapabilityValueKind.Enum)
        {
            return Encoding.ASCII.GetString(value.Bytes);
        }
        if (definition.ValueKind == ScannerSdkLib.CapabilityValueKind.UInt8 && value.Bytes.Length == 1)
        {
            return value.Bytes[0].ToString(CultureInfo.InvariantCulture);
        }
        if (definition.ValueKind == ScannerSdkLib.CapabilityValueKind.UInt16 && value.Bytes.Length == 2)
        {
            return ((value.Bytes[0] << 8) | value.Bytes[1]).ToString(CultureInfo.InvariantCulture);
        }
        return value.Bytes.Length == 0 ? "(empty)" : Convert.ToHexString(value.Bytes);
    }

    internal static bool TryParseBoolean(string? value, out bool parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = false;
            return false;
        }
        switch (value.Trim().ToLowerInvariant())
        {
            case "1" or "true" or "yes" or "on":
                parsed = true;
                return true;
            case "0" or "false" or "no" or "off":
                parsed = false;
                return true;
            default:
                parsed = false;
                return false;
        }
    }

    private static bool ParseBoolean(string value) => TryParseBoolean(value, out var parsed)
        ? parsed
        : throw new ArgumentException("Boolean value must be true/false or 1/0.");

    private static string ParseEnum(MauiSettingDefinition definition, string value)
    {
        var option = definition.Options.FirstOrDefault(candidate =>
            string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return option ?? throw new ArgumentException("Select one of the available values.");
    }

    private static byte ParseByte(string value, string name) => byte.TryParse(value, out var result)
        ? result
        : byte.TryParse(TrimHexPrefix(value), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result)
            ? result
            : throw new ArgumentException($"{name} must be a value from 0 to 255.");

    private static ushort ParseUInt16(string value, string name) => ushort.TryParse(value, out var result)
        ? result
        : ushort.TryParse(TrimHexPrefix(value), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result)
            ? result
            : throw new ArgumentException($"{name} must be a value from 0 to 65535.");

    private static byte[] ParseHex(string value)
    {
        var compact = new string(value.Where(static character =>
            !char.IsWhiteSpace(character) && character is not '-' and not ':').ToArray());
        if (compact.Length == 0)
        {
            return [];
        }
        if (compact.Length % 2 != 0)
        {
            throw new ArgumentException("Hex input must contain an even number of digits.");
        }
        try
        {
            return Convert.FromHexString(compact);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Hex input contains an invalid character.");
        }
    }

    private static string TrimHexPrefix(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed[2..] : trimmed;
    }

    private static byte[] EncodeUInt16(ushort value) => [(byte)(value >> 8), (byte)value];
}
