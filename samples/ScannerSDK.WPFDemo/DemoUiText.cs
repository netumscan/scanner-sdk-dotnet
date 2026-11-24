using System.Globalization;
using System.Resources;

namespace ScannerSDK.WPFDemo;

public static class DemoUiText
{
    private static readonly ResourceManager ResourceManager = new(
        "ScannerSDK.WPFDemo.DemoUiStrings",
        typeof(DemoUiText).Assembly
    );

    private static CultureInfo culture = ResolveSystemCulture();

    public static CultureInfo Culture => culture;

    public static string Get(string key)
    {
        return ResourceManager.GetString(key, culture) ?? ResourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;
    }

    public static void SetCulture(string languageTag)
    {
        culture = languageTag switch
        {
            "zh-CN" => CultureInfo.GetCultureInfo("zh-CN"),
            "en" => CultureInfo.GetCultureInfo("en"),
            _ => ResolveSystemCulture(),
        };
        CultureInfo.CurrentUICulture = culture;
    }

    private static CultureInfo ResolveSystemCulture() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("zh-CN")
            : CultureInfo.GetCultureInfo("en");
}
