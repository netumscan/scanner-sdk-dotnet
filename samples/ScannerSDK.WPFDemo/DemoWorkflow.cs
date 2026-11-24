using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

public enum DemoWorkflowState
{
    NotInitialized,
    Idle,
    Discovering,
    Connecting,
    Connected,
    Disconnecting,
    ShuttingDown,
}

public sealed record DemoFailure(
    string Operation,
    string Code,
    int? SdkCode,
    int? PlatformCode,
    bool Recoverable,
    string Message,
    string RecoverySuggestion)
{
    public string DisplayText
    {
        get
        {
            var sdk = SdkCode is null ? string.Empty : $", sdk={SdkCode}";
            var platform = PlatformCode is null ? string.Empty : $", platform={PlatformCode}";
            var recovery = string.IsNullOrWhiteSpace(RecoverySuggestion)
                ? string.Empty
                : $"{Environment.NewLine}{RecoverySuggestion}";
            return $"{Operation}: {Message} [{Code}{sdk}{platform}]{recovery}";
        }
    }
}

internal static class DemoErrorFormatter
{
    internal static DemoFailure FromException(string operation, Exception error)
    {
        var actual = error is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? error
            : error;

        if (actual is ScannerSdkLib.ScannerException scanner)
        {
            var recoverable = scanner.Code is 4 or 5 or 6 or 7 or 8 or 9 or 12;
            return new DemoFailure(
                operation,
                scanner.ErrorName,
                scanner.Code,
                null,
                recoverable,
                scanner.Message,
                RecoveryForCode(scanner.Code));
        }

        if (actual is OperationCanceledException)
        {
            return new DemoFailure(
                operation,
                "Canceled",
                null,
                null,
                true,
                "Operation was canceled.",
                "Wait for the current operation to finish, then retry.");
        }

        if (actual is NotSupportedException)
        {
            return new DemoFailure(
                operation,
                "NotSupported",
                3,
                null,
                false,
                actual.Message,
                "Choose a capability supported by the selected model and transport.");
        }

        if (actual is TimeoutException)
        {
            return new DemoFailure(
                operation,
                "Timeout",
                5,
                null,
                true,
                actual.Message,
                "Verify the target is reachable and retry.");
        }

        return new DemoFailure(
            operation,
            actual.GetType().Name,
            null,
            null,
            true,
            actual.Message,
            "Review Diagnostics and logs, then retry the operation.");
    }

    internal static DemoFailure FromDiscoveryFailure(ScannerSdkLib.DiscoveryFailure failure) =>
        new(
            "Discovery",
            failure.Code.ToString(),
            null,
            failure.PlatformErrorCode,
            failure.Recoverable,
            failure.Message,
            failure.Recoverable
                ? "Verify the adapter and Windows permissions, then start discovery again."
                : "Review the selected transport and Windows adapter status.");

    internal static DemoFailure FromSessionFailure(ScannerSdkLib.SessionFailure failure) =>
        new(
            "Session",
            failure.Issue.ToString(),
            null,
            failure.PlatformErrorCode,
            true,
            failure.Message,
            "Reconnect the device. If the failure repeats, export the compatibility record.");

    private static string RecoveryForCode(int code) =>
        code switch
        {
            2 => "Retry SDK initialization.",
            4 => "Wait for the active operation to finish, then retry.",
            5 => "Verify the device is ready and retry.",
            6 or 7 => "Check the cable, COM target, permissions, or Bluetooth adapter, then reconnect.",
            8 => "Stop discovery, verify the adapter, then search again.",
            9 => "Verify the selected device and transport, then reconnect.",
            12 => "Wait for READY or reconnect the device.",
            _ => "Review Diagnostics and logs before retrying.",
        };
}

internal interface ISerialPortCatalog
{
    IReadOnlyList<string> GetPortNames();
}

internal sealed class SystemSerialPortCatalog : ISerialPortCatalog
{
    private static readonly Regex ComPortPattern = new(
        "^COM(?<number>[1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<string> GetPortNames() =>
        SerialPort.GetPortNames()
            .Select(static value => TryNormalize(value, out var normalized) ? normalized : string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => ParsePortNumber(value))
            .ToArray();

    internal static bool TryNormalize(string? value, out string normalized)
    {
        var candidate = value?.Trim() ?? string.Empty;
        var match = ComPortPattern.Match(candidate);
        if (!match.Success)
        {
            normalized = string.Empty;
            return false;
        }

        normalized = $"COM{int.Parse(match.Groups["number"].Value)}";
        return true;
    }

    private static int ParsePortNumber(string value) =>
        int.Parse(ComPortPattern.Match(value).Groups["number"].Value);
}

internal static class DemoNativeDllProbe
{
    internal static string Probe()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ScannerSDK.Native.dll");
        if (!File.Exists(path))
        {
            return "not present";
        }

        try
        {
            if (!NativeLibrary.TryLoad(path, out var handle))
            {
                return $"present but not loadable ({path})";
            }

            NativeLibrary.Free(handle);
            return $"present and loadable ({path})";
        }
        catch (Exception error)
        {
            return $"present but load failed ({error.GetType().Name}: {error.Message})";
        }
    }
}
