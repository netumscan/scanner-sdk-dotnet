using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

internal static partial class DemoLogRedactor
{
    internal static string Redact(
        DemoLogSource source,
        string message,
        bool includeSensitivePayloads = false)
    {
        if (includeSensitivePayloads)
        {
            return message;
        }

        if (source == DemoLogSource.Scan)
        {
            return "<scan payload redacted>";
        }
        if (source == DemoLogSource.UsbHid)
        {
            return "<USB HID payload redacted>";
        }

        var redacted = SerialAssignmentRegex().Replace(message, match =>
            $"{match.Groups[1].Value}={Mask(match.Groups[2].Value)}");
        redacted = DeviceIdAssignmentRegex().Replace(redacted, match =>
            $"{match.Groups[1].Value}={Mask(match.Groups[2].Value)}");
        redacted = MacAddressRegex().Replace(redacted, match => Mask(match.Value));
        if (source == DemoLogSource.Command)
        {
            redacted = CommandPayloadRegex().Replace(redacted, match =>
                $"{match.Groups[1].Value}=<payload redacted>");
        }
        return redacted;
    }

    internal static string RedactText(string value, bool includeSensitivePayloads = false)
    {
        if (includeSensitivePayloads || string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = SerialAssignmentRegex().Replace(value, match =>
            $"{match.Groups[1].Value}={Mask(match.Groups[2].Value)}");
        redacted = DeviceIdAssignmentRegex().Replace(redacted, match =>
            $"{match.Groups[1].Value}={Mask(match.Groups[2].Value)}");
        return MacAddressRegex().Replace(redacted, match => Mask(match.Value));
    }

    private static string Mask(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "***";
        }
        var suffixLength = Math.Min(4, value.Length);
        return new string('*', Math.Max(4, value.Length - suffixLength)) + value[^suffixLength..];
    }

    [GeneratedRegex("(?i)\\b(serial(?:number)?|sn)\\s*[=:]\\s*([A-Za-z0-9#_.-]{4,})")]
    private static partial Regex SerialAssignmentRegex();

    [GeneratedRegex("(?i)\\b(device(?:id)?)\\s*=\\s*([^\\s,;]+)")]
    private static partial Regex DeviceIdAssignmentRegex();

    [GeneratedRegex("(?i)\\b[0-9A-F]{2}(?::[0-9A-F]{2}){5}\\b")]
    private static partial Regex MacAddressRegex();

    [GeneratedRegex("(?i)\\b(raw|request|response|text|textBytes|rawBytes)=\\S+")]
    private static partial Regex CommandPayloadRegex();
}

internal sealed record DemoCompatibilityRecordContext(
    string SdkVersion,
    string SelectedModel,
    string SelectedTransport,
    string SessionState,
    string SelectedDeviceSummary,
    string ResolvedModelSummary,
    string CapabilitySummary,
    string OperationSupportSummary,
    string DeviceInfo,
    string Battery,
    int ScanCount,
    string RecentFailure,
    IReadOnlyList<ScannerSdkLib.CommandTrace> CommandTraces,
    string ExportedLogs,
    bool IncludeSensitivePayloads,
    bool FakeMode = false,
    string WorkflowState = "",
    string ComTarget = "",
    string NativeDllStatus = "");

internal static class DemoCompatibilityRecordBuilder
{
    internal static string Build(DemoCompatibilityRecordContext context)
    {
        var includeSensitive = context.IncludeSensitivePayloads;
        var evidence = ScannerSdkLib.Internal.BuildEvidence.Capture(context.SdkVersion);
        var builder = new StringBuilder();
        builder.AppendLine("ScannerSDK WPF Demo Compatibility Record");
        builder.AppendLine($"ExportedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine($"SensitivePayloadsIncluded={includeSensitive}");
        builder.AppendLine();
        builder.AppendLine("[Runtime]");
        builder.AppendLine($"SdkVersion={context.SdkVersion}");
        builder.AppendLine($"SdkCommit={evidence.SdkCommit}");
        builder.AppendLine($"BuildEvidence={System.Text.Json.JsonSerializer.Serialize(evidence)}");
        builder.AppendLine($"DemoVersion={typeof(DemoCompatibilityRecordBuilder).Assembly.GetName().Version}");
        builder.AppendLine($"DemoBuild={Environment.GetEnvironmentVariable("NSDK_DEMO_BUILD") ?? string.Empty}");
        builder.AppendLine($"Windows={Environment.OSVersion.VersionString}");
        builder.AppendLine($"Architecture={RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"Framework={RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Process64Bit={Environment.Is64BitProcess}");
        builder.AppendLine($"FakeMode={context.FakeMode}");
        builder.AppendLine($"WorkflowState={context.WorkflowState}");
        builder.AppendLine($"NativeDll={context.NativeDllStatus}");
        builder.AppendLine($"SelectedModel={context.SelectedModel}");
        builder.AppendLine($"SelectedTransport={context.SelectedTransport}");
        builder.AppendLine($"ComTarget={context.ComTarget}");
        builder.AppendLine($"SessionState={context.SessionState}");
        builder.AppendLine();
        builder.AppendLine("[Device]");
        builder.AppendLine(DemoLogRedactor.RedactText(context.SelectedDeviceSummary, includeSensitive));
        builder.AppendLine(context.ResolvedModelSummary);
        builder.AppendLine(context.CapabilitySummary);
        builder.AppendLine(context.OperationSupportSummary);
        builder.AppendLine();
        builder.AppendLine("[Info]");
        builder.AppendLine(DemoLogRedactor.RedactText(context.DeviceInfo, includeSensitive));
        builder.AppendLine();
        builder.AppendLine("[Battery]");
        builder.AppendLine(context.Battery);
        builder.AppendLine();
        builder.AppendLine("[Scan]");
        builder.AppendLine($"ScanCount={context.ScanCount}");
        builder.AppendLine(includeSensitive
            ? "Scan payloads are available in the exported diagnostic logs."
            : "Scan payloads are redacted.");
        builder.AppendLine();
        builder.AppendLine("[RecentFailure]");
        builder.AppendLine(DemoLogRedactor.RedactText(context.RecentFailure, includeSensitive));
        builder.AppendLine();
        builder.AppendLine("[KnownLimitations]");
        builder.AppendLine(Environment.GetEnvironmentVariable("NSDK_KNOWN_LIMITATIONS") ?? "<none recorded>");
        builder.AppendLine();
        builder.AppendLine("[CommandTrace]");
        foreach (var trace in context.CommandTraces.TakeLast(100))
        {
            var formatted = DemoFormatters.FormatTrace(trace);
            builder.AppendLine(includeSensitive
                ? formatted
                : DemoLogRedactor.Redact(DemoLogSource.Command, formatted));
        }
        builder.AppendLine();
        builder.AppendLine("[FilteredLogs]");
        builder.Append(context.ExportedLogs);
        return builder.ToString();
    }

}
