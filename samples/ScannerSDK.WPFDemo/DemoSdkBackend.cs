using System.Globalization;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

internal interface IDemoSdkBackend : IDisposable
{
    bool IsFake { get; }

    string Version { get; }

    event Action<ScannerSdkLib.DiscoveredDevice>? DiscoveryReceived;

    event Action<ScannerSdkLib.DiscoveryFailure>? DiscoveryFailureReceived;

    event Action<ScannerSdkLib.SdkLogEvent>? LogReceived;

    event Action<string>? UsbHidTraceReceived;

    event Action<ScannerSdkLib.CommandTrace>? CommandTraceReceived;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task StartDiscoveryAsync(
        ScannerSdkLib.TransportType transport,
        string selectedModelKey,
        CancellationToken cancellationToken = default);

    Task StopDiscoveryAsync(CancellationToken cancellationToken = default);

    Task<IDemoSessionHandle> ConnectReadyAsync(
        ScannerSdkLib.DiscoveredDevice device,
        string fallbackModelKey,
        CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<ScannerSdkLib.SupportedDeviceModel> GetSupportedDeviceModels(
        ScannerSdkLib.TransportType transport);

    ScannerSdkLib.DeviceModelProfile? GetDeviceModelProfile(string modelKey);

    IReadOnlyList<ScannerSdkLib.CapabilityDomain> GetCapabilityDomains(
        string modelKey,
        ScannerSdkLib.TransportType transport);

    IReadOnlyList<ScannerSdkLib.CapabilityEntry> GetCapabilityEntries(
        string modelKey,
        ScannerSdkLib.TransportType transport);

    IReadOnlyList<ScannerSdkLib.CapabilityLabel> GetCapabilityLabels(
        ScannerSdkLib.CapabilityLabelKind kind);

    ScannerSdkLib.DataRuleKindLabel GetDataRuleKindLabel(ScannerSdkLib.DataRuleKind kind);

    string Localize(string localizationKey, string fallbackDisplayName, CultureInfo? culture);
}

internal interface IDemoSessionHandle
{
    string DeviceId { get; }

    ScannerSdkLib.TransportType TransportType { get; }

    ScannerSdkLib.SessionState LatestState { get; }

    event Action<ScannerSdkLib.SessionState>? StateChanged;

    event Action<ScannerSdkLib.ScanEvent>? ScanReceived;

    event Action<ScannerSdkLib.SessionFailure>? FailureReceived;

    event Action<ScannerSdkLib.SessionInitializationStageEvent>? InitializationStageReceived;

    event Action<ScannerSdkLib.CommandTrace>? CommandTraceReceived;

    void SetScanTextCharset(ScannerSdkLib.ScanTextCharset charset);

    Task SetScanTextTerminatorAsync(byte[] terminatorBytes, CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.ScannerInfo> RefreshInfoAsync(CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.SessionInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);

    string GetResolvedModelKey();

    ScannerSdkLib.DeviceCapabilitySummary GetDeviceCapabilitySummary();

    ScannerSdkLib.SessionOperationSupport GetOperationSupport();

    Task<ScannerSdkLib.CapabilityValue> ReadCapabilityValueAsync(
        string entryKey,
        CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.CommandResponse> WriteCapabilityValueAsync(
        string entryKey,
        ScannerSdkLib.CapabilityValue value,
        bool persist = true,
        CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.CommandResponse> ExecuteCapabilityActionAsync(
        string entryKey,
        byte[]? valueBytes = null,
        CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.BatteryInfo> GetBatteryInfoAsync(CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.BatteryInfo?> GetCachedBatteryInfoAsync(CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.StorageUsage> GetStorageUsageAsync(CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.CommandResponse> SetTimestampAsync(
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.CommandResponse> SetAckBeepEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.CommandResponse> SetVibrationEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<ScannerSdkLib.CommandResponse> ApplyDataRuleAsync(
        ScannerSdkLib.DataRule rule,
        CancellationToken cancellationToken = default);

    Task TriggerScanAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

internal sealed class ScannerSdkDemoBackend : IDemoSdkBackend
{
    private readonly ScannerSdkLib.ScannerSdk sdk = new();

    public bool IsFake => false;

    public string Version => sdk.Version;

    public event Action<ScannerSdkLib.DiscoveredDevice>? DiscoveryReceived
    {
        add => sdk.DiscoveryReceived += value;
        remove => sdk.DiscoveryReceived -= value;
    }

    public event Action<ScannerSdkLib.DiscoveryFailure>? DiscoveryFailureReceived
    {
        add => sdk.DiscoveryFailureReceived += value;
        remove => sdk.DiscoveryFailureReceived -= value;
    }

    public event Action<ScannerSdkLib.SdkLogEvent>? LogReceived
    {
        add => sdk.LogReceived += value;
        remove => sdk.LogReceived -= value;
    }

    public event Action<string>? UsbHidTraceReceived
    {
        add => sdk.UsbHidTraceReceived += value;
        remove => sdk.UsbHidTraceReceived -= value;
    }

    public event Action<ScannerSdkLib.CommandTrace>? CommandTraceReceived
    {
        add => sdk.CommandTraceReceived += value;
        remove => sdk.CommandTraceReceived -= value;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        sdk.InitializeAsync(cancellationToken);

    public Task StartDiscoveryAsync(
        ScannerSdkLib.TransportType transport,
        string selectedModelKey,
        CancellationToken cancellationToken = default) =>
        sdk.StartDiscoveryAsync([transport], selectedModelKey, cancellationToken);

    public Task StopDiscoveryAsync(CancellationToken cancellationToken = default) =>
        sdk.StopDiscoveryAsync(cancellationToken);

    public async Task<IDemoSessionHandle> ConnectReadyAsync(
        ScannerSdkLib.DiscoveredDevice device,
        string fallbackModelKey,
        CancellationToken cancellationToken = default)
    {
        var session = await sdk.ConnectReadyAsync(
            device.DeviceId,
            device.TransportType,
            string.IsNullOrWhiteSpace(device.ModelKey) ? fallbackModelKey : device.ModelKey,
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(true);
        return new ScannerSdkDemoSession(session);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        sdk.ShutdownAsync(cancellationToken);

    public IReadOnlyList<ScannerSdkLib.SupportedDeviceModel> GetSupportedDeviceModels(
        ScannerSdkLib.TransportType transport) =>
        sdk.GetSupportedDeviceModels(transport);

    public ScannerSdkLib.DeviceModelProfile? GetDeviceModelProfile(string modelKey) =>
        sdk.GetDeviceModelProfile(modelKey);

    public IReadOnlyList<ScannerSdkLib.CapabilityDomain> GetCapabilityDomains(
        string modelKey,
        ScannerSdkLib.TransportType transport) =>
        sdk.GetCapabilityDomains(modelKey, transport);

    public IReadOnlyList<ScannerSdkLib.CapabilityEntry> GetCapabilityEntries(
        string modelKey,
        ScannerSdkLib.TransportType transport) =>
        sdk.GetCapabilityEntries(modelKey, transport);

    public IReadOnlyList<ScannerSdkLib.CapabilityLabel> GetCapabilityLabels(
        ScannerSdkLib.CapabilityLabelKind kind) =>
        sdk.GetCapabilityLabels(kind);

    public ScannerSdkLib.DataRuleKindLabel GetDataRuleKindLabel(ScannerSdkLib.DataRuleKind kind) =>
        sdk.GetDataRuleKindLabel(kind);

    public string Localize(string localizationKey, string fallbackDisplayName, CultureInfo? culture) =>
        sdk.Localize(localizationKey, fallbackDisplayName, culture);

    public void Dispose() => sdk.Dispose();
}

internal sealed class ScannerSdkDemoSession : IDemoSessionHandle
{
    private readonly ScannerSdkLib.ScannerSession session;

    internal ScannerSdkDemoSession(ScannerSdkLib.ScannerSession session)
    {
        this.session = session;
    }

    public string DeviceId => session.DeviceId;

    public ScannerSdkLib.TransportType TransportType => session.TransportType;

    public ScannerSdkLib.SessionState LatestState => session.LatestState;

    public event Action<ScannerSdkLib.SessionState>? StateChanged
    {
        add => session.StateChanged += value;
        remove => session.StateChanged -= value;
    }

    public event Action<ScannerSdkLib.ScanEvent>? ScanReceived
    {
        add => session.ScanReceived += value;
        remove => session.ScanReceived -= value;
    }

    public event Action<ScannerSdkLib.SessionFailure>? FailureReceived
    {
        add => session.FailureReceived += value;
        remove => session.FailureReceived -= value;
    }

    public event Action<ScannerSdkLib.SessionInitializationStageEvent>? InitializationStageReceived
    {
        add => session.InitializationStageReceived += value;
        remove => session.InitializationStageReceived -= value;
    }

    public event Action<ScannerSdkLib.CommandTrace>? CommandTraceReceived
    {
        add => session.CommandTraceReceived += value;
        remove => session.CommandTraceReceived -= value;
    }

    public void SetScanTextCharset(ScannerSdkLib.ScanTextCharset charset) =>
        session.SetScanTextCharset(charset);

    public Task SetScanTextTerminatorAsync(byte[] terminatorBytes, CancellationToken cancellationToken = default) =>
        session.SetScanTextTerminatorAsync(terminatorBytes, cancellationToken);

    public Task<ScannerSdkLib.ScannerInfo> RefreshInfoAsync(CancellationToken cancellationToken = default) =>
        session.RefreshInfoAsync(cancellationToken);

    public Task<ScannerSdkLib.SessionInitializationResult> InitializeAsync(CancellationToken cancellationToken = default) =>
        session.InitializeAsync(cancellationToken);

    public string GetResolvedModelKey() => session.GetResolvedModelKey();

    public ScannerSdkLib.DeviceCapabilitySummary GetDeviceCapabilitySummary() =>
        session.GetDeviceCapabilitySummary();

    public ScannerSdkLib.SessionOperationSupport GetOperationSupport() =>
        session.GetOperationSupport();

    public Task<ScannerSdkLib.CapabilityValue> ReadCapabilityValueAsync(
        string entryKey,
        CancellationToken cancellationToken = default) =>
        session.ReadCapabilityValueAsync(entryKey, cancellationToken);

    public Task<ScannerSdkLib.CommandResponse> WriteCapabilityValueAsync(
        string entryKey,
        ScannerSdkLib.CapabilityValue value,
        bool persist = true,
        CancellationToken cancellationToken = default) =>
        session.WriteCapabilityValueAsync(entryKey, value, persist, cancellationToken);

    public Task<ScannerSdkLib.CommandResponse> ExecuteCapabilityActionAsync(
        string entryKey,
        byte[]? valueBytes = null,
        CancellationToken cancellationToken = default) =>
        session.ExecuteCapabilityActionAsync(entryKey, valueBytes, cancellationToken);

    public Task<ScannerSdkLib.BatteryInfo> GetBatteryInfoAsync(CancellationToken cancellationToken = default) =>
        session.GetBatteryInfoAsync(cancellationToken);

    public Task<ScannerSdkLib.BatteryInfo?> GetCachedBatteryInfoAsync(CancellationToken cancellationToken = default) =>
        session.GetCachedBatteryInfoAsync(cancellationToken);

    public Task<ScannerSdkLib.StorageUsage> GetStorageUsageAsync(CancellationToken cancellationToken = default) =>
        session.GetStorageUsageAsync(cancellationToken);

    public Task<ScannerSdkLib.CommandResponse> SetTimestampAsync(
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default) =>
        session.SetTimestampAsync(timestamp, cancellationToken);

    public Task<ScannerSdkLib.CommandResponse> SetAckBeepEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        session.SetAckBeepEnabledAsync(enabled, cancellationToken);

    public Task<ScannerSdkLib.CommandResponse> SetVibrationEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        session.SetVibrationEnabledAsync(enabled, cancellationToken);

    public Task<ScannerSdkLib.CommandResponse> ApplyDataRuleAsync(
        ScannerSdkLib.DataRule rule,
        CancellationToken cancellationToken = default) =>
        session.ApplyDataRuleAsync(rule, cancellationToken);

    public Task TriggerScanAsync(CancellationToken cancellationToken = default) =>
        session.TriggerScanAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        session.DisconnectAsync(cancellationToken);
}
