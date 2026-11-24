namespace ScannerSDK.WpfQuickStart;

public sealed record QuickModel(string ModelKey, string ModelName)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ModelName)
        ? ModelKey
        : $"{ModelName} ({ModelKey})";
}

public sealed record QuickDevice
{
    internal QuickDevice(string deviceId, string name, string modelKey)
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
}

public sealed record QuickScanRow(long Sequence, int BarcodeType, string Text)
{
    public string DisplayText => string.IsNullOrEmpty(Text) ? "(empty scan)" : Text;

    public string Summary => $"Type {BarcodeType} · {Text.Length} characters";
}

internal sealed record QuickOperationSupport(
    bool RefreshInfo,
    bool Battery,
    bool TriggerScan)
{
    internal static readonly QuickOperationSupport None = new(false, false, false);
}

internal sealed record QuickDeviceInfo(
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

internal sealed record QuickBattery(int Percent, string Voltage)
{
    internal string DisplaySummary() => string.IsNullOrWhiteSpace(Voltage)
        ? $"{Percent}%"
        : $"{Percent}% · {Voltage}";
}

internal sealed record QuickScanEvent(int BarcodeType, string Text);
