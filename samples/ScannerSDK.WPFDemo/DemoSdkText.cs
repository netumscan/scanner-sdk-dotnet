namespace ScannerSDK.WPFDemo;

public static class DemoSdkText
{
    public static string Resolve(string localizationKey, string fallback)
    {
        return string.IsNullOrWhiteSpace(fallback) ? HumanizedFallback(localizationKey) : fallback;
    }

    private static string HumanizedFallback(string localizationKey)
    {
        HashSet<string> acronyms = new(StringComparer.OrdinalIgnoreCase)
        {
            "ACK", "BLE", "BT", "GATT", "HID", "ID", "RF", "SDK", "SPP", "UI", "USB",
        };
        var normalized = localizationKey.StartsWith("nsdk.", StringComparison.Ordinal)
            ? localizationKey["nsdk.".Length..]
            : localizationKey;
        var words = normalized
            .Replace('.', '_')
            .Replace('-', '_')
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => acronyms.Contains(word)
                ? word.ToUpperInvariant()
                : char.ToUpperInvariant(word[0]) + word[1..]);
        var text = string.Join(" ", words);
        return string.IsNullOrWhiteSpace(text) ? localizationKey : text;
    }
}
