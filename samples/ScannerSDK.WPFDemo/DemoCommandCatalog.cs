using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

public static class DemoCommandCatalog
{
    internal static IReadOnlyList<ModelOption> BuildModelOptions(
        IDemoSdkBackend backend,
        ScannerSdkLib.TransportType transport
    )
    {
        var supported = backend.GetSupportedDeviceModels(transport);
        if (supported.Count > 0)
        {
            return supported
                .Select(static model => new ModelOption(model.ModelKey, model.ModelName, model.SeriesName))
                .ToArray();
        }

        return Array.Empty<ModelOption>();
    }

    internal static IReadOnlyList<TransportOption> BuildTransportOptions(IDemoSdkBackend? backend = null)
    {
        string Label(string key, string fallback) => backend?.Localize(key, fallback, DemoUiText.Culture) ?? fallback;
        return
        [
            new(ScannerSdkLib.TransportType.UsbSerial, Label("nsdk.transport_type.usb_serial", "USB Serial / SPP over COM")),
            new(ScannerSdkLib.TransportType.UsbHid, Label("nsdk.transport_type.usb_hid", "USB HID")),
            new(ScannerSdkLib.TransportType.BleGatt, Label("nsdk.transport_type.ble_gatt", "BLE GATT")),
        ];
    }

    public static IReadOnlyList<CharsetOption> BuildCharsetOptions()
    {
        return
        [
            new(ScannerSdkLib.ScanTextCharset.Utf8, "UTF-8"),
            new(ScannerSdkLib.ScanTextCharset.UsAscii, "US-ASCII"),
            new(ScannerSdkLib.ScanTextCharset.Iso88591, "ISO-8859-1"),
            new(ScannerSdkLib.ScanTextCharset.Gbk, "GBK"),
        ];
    }

    public static IReadOnlyList<TerminatorOption> BuildTerminatorOptions()
    {
        return
        [
            new("CR (0D)", [0x0D]),
            new("LF (0A)", [0x0A]),
            new("CRLF (0D0A)", [0x0D, 0x0A]),
            new("TAB (09)", [0x09]),
        ];
    }

    public static IReadOnlyList<DataRuleModeOption> BuildDataRuleModeOptions()
    {
        return
        [
            new(DataRuleMode.Prefix, "Set Prefix"),
            new(DataRuleMode.Suffix, "Set Suffix"),
            new(DataRuleMode.HideStart, "Hide Start"),
            new(DataRuleMode.HideEnd, "Hide End"),
            new(DataRuleMode.HideMiddle, "Hide Middle"),
            new(DataRuleMode.Replace, "Replace"),
        ];
    }

    public static IReadOnlyList<string> BuildSettingGroups(IEnumerable<SettingItem> settings)
    {
        return new[] { "All" }
            .Concat(settings
                .Select(static item => item.Group)
                .Where(static group => !string.IsNullOrWhiteSpace(group))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static group => group, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

}
