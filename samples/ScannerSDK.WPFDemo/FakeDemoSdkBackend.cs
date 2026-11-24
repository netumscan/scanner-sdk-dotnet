using System.Globalization;
using System.Text;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

internal enum FakeDemoScenario
{
    Success,
    Unsupported,
    Timeout,
}

internal sealed class FakeDemoSdkBackend : IDemoSdkBackend
{
    private bool initialized;
    private bool discovering;

    internal FakeDemoSdkBackend(FakeDemoScenario scenario = FakeDemoScenario.Success)
    {
        Scenario = scenario;
    }

    internal FakeDemoScenario Scenario { get; set; }

    internal FakeDemoSession? LastSession { get; private set; }

    internal int DiscoveryStartCount { get; private set; }

    internal ScannerSdkLib.DiscoveredDevice? LastConnectTarget { get; private set; }

    public bool IsFake => true;

    public string Version => "fake-1.0";

    public event Action<ScannerSdkLib.DiscoveredDevice>? DiscoveryReceived;

    public event Action<ScannerSdkLib.DiscoveryFailure>? DiscoveryFailureReceived
    {
        add { }
        remove { }
    }

    public event Action<ScannerSdkLib.SdkLogEvent>? LogReceived;

    public event Action<string>? UsbHidTraceReceived
    {
        add { }
        remove { }
    }

    public event Action<ScannerSdkLib.CommandTrace>? CommandTraceReceived
    {
        add { }
        remove { }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        initialized = true;
        LogReceived?.Invoke(new ScannerSdkLib.SdkLogEvent(ScannerSdkLib.SdkLogLevel.Info, "Fake SDK initialized"));
        return Task.CompletedTask;
    }

    public async Task StartDiscoveryAsync(
        ScannerSdkLib.TransportType transport,
        string selectedModelKey,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        DiscoveryStartCount++;
        discovering = true;
        await Task.Yield();
        if (!discovering)
        {
            return;
        }

        DiscoveryReceived?.Invoke(new ScannerSdkLib.DiscoveredDevice(
            transport == ScannerSdkLib.TransportType.UsbSerial ? "COM99" : $"fake-{transport.ToString().ToLowerInvariant()}-01",
            transport == ScannerSdkLib.TransportType.UsbHid ? "Fake USB Scanner" : "Fake BLE Scanner",
            transport,
            string.IsNullOrWhiteSpace(selectedModelKey) ? "fake.reference" : selectedModelKey,
            "Fake Mode deterministic target",
            transport == ScannerSdkLib.TransportType.BleGatt ? -48 : null));
    }

    public Task StopDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        discovering = false;
        return Task.CompletedTask;
    }

    public async Task<IDemoSessionHandle> ConnectReadyAsync(
        ScannerSdkLib.DiscoveredDevice device,
        string fallbackModelKey,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        discovering = false;
        LastConnectTarget = device;
        if (Scenario == FakeDemoScenario.Timeout)
        {
            await Task.Yield();
            throw new TimeoutException("Fake connection timeout.");
        }

        LastSession = new FakeDemoSession(
            device.DeviceId,
            device.TransportType,
            string.IsNullOrWhiteSpace(device.ModelKey) ? fallbackModelKey : device.ModelKey,
            Scenario);
        LastSession.ConnectReady();
        return LastSession;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        discovering = false;
        initialized = false;
        return Task.CompletedTask;
    }

    public IReadOnlyList<ScannerSdkLib.SupportedDeviceModel> GetSupportedDeviceModels(
        ScannerSdkLib.TransportType transport)
    {
        var capability = new ScannerSdkLib.DeviceCapabilitySummary(
            "fake.reference",
            "Fake Reference Scanner",
            true,
            true,
            true,
            true,
            true,
            true,
            ScannerSdkLib.DeviceSupportStatus.Verified);
        var profile = new ScannerSdkLib.DeviceModelProfile(
            capability,
            transport == ScannerSdkLib.TransportType.BleGatt ? ["0000fff0-0000-1000-8000-00805f9b34fb"] : [],
            ["Fake Scanner"],
            transport == ScannerSdkLib.TransportType.UsbHid ? 0x1234 : null,
            transport == ScannerSdkLib.TransportType.UsbHid ? [0x0001] : [],
            null,
            null);
        return [new ScannerSdkLib.SupportedDeviceModel(
            capability.ModelKey,
            capability.ModelName,
            "fake",
            "Fake Series",
            profile)];
    }

    public ScannerSdkLib.DeviceModelProfile? GetDeviceModelProfile(string modelKey) =>
        GetSupportedDeviceModels(ScannerSdkLib.TransportType.UsbHid)
            .FirstOrDefault(model => model.ModelKey == modelKey)?.Profile;

    public IReadOnlyList<ScannerSdkLib.CapabilityDomain> GetCapabilityDomains(
        string modelKey,
        ScannerSdkLib.TransportType transport) =>
        [new("common", "Common", "fake.domain.common", 10, true)];

    public IReadOnlyList<ScannerSdkLib.CapabilityEntry> GetCapabilityEntries(
        string modelKey,
        ScannerSdkLib.TransportType transport) =>
        [
            new(
                "setting.SetAckBeepEnabled",
                ScannerSdkLib.CapabilityEntryKind.Setting,
                "common",
                "feedback",
                "device",
                "feedback",
                "SetAckBeepEnabled",
                "true",
                "Fake writable Boolean setting.",
                ScannerSdkLib.CapabilityEntrySource.Master,
                uint.MaxValue,
                ScannerSdkLib.CapabilityEntryAvailability.Available,
                1,
                true,
                true,
                true,
                false,
                true,
                ScannerSdkLib.CapabilityValueKind.Boolean,
                ScannerSdkLib.CapabilityRiskLevel.Normal,
                "true or false",
                []),
            new(
                "setting.SetVibrationEnabled",
                ScannerSdkLib.CapabilityEntryKind.Setting,
                "common",
                "feedback",
                "device",
                "feedback",
                "SetVibrationEnabled",
                "false",
                "Fake writable vibration setting.",
                ScannerSdkLib.CapabilityEntrySource.Master,
                uint.MaxValue,
                ScannerSdkLib.CapabilityEntryAvailability.Available,
                1,
                true,
                true,
                true,
                false,
                true,
                ScannerSdkLib.CapabilityValueKind.Boolean,
                ScannerSdkLib.CapabilityRiskLevel.Normal,
                "true or false",
                []),
            new(
                "action.TriggerScan",
                ScannerSdkLib.CapabilityEntryKind.Action,
                "common",
                "scan",
                "device",
                "scan",
                "TriggerScan",
                "",
                "Emits a deterministic fake scan.",
                ScannerSdkLib.CapabilityEntrySource.Master,
                uint.MaxValue,
                ScannerSdkLib.CapabilityEntryAvailability.Available,
                1,
                true,
                false,
                false,
                true,
                false,
                ScannerSdkLib.CapabilityValueKind.Action,
                ScannerSdkLib.CapabilityRiskLevel.Normal,
                "",
                []),
            new(
                "action.SetRtcTimestamp",
                ScannerSdkLib.CapabilityEntryKind.Action,
                "common",
                "system",
                "device",
                "time",
                "SetRtcTimestamp",
                "",
                "Fake typed timestamp action.",
                ScannerSdkLib.CapabilityEntrySource.Master,
                uint.MaxValue,
                ScannerSdkLib.CapabilityEntryAvailability.Available,
                1,
                true,
                false,
                false,
                true,
                true,
                ScannerSdkLib.CapabilityValueKind.BytesAscii,
                ScannerSdkLib.CapabilityRiskLevel.Normal,
                "ASCII Unix timestamp seconds",
                []),
            new(
                "action.SetBluetoothName",
                ScannerSdkLib.CapabilityEntryKind.Action,
                "common",
                "connectivity",
                "device",
                "connectivity",
                "SetBluetoothName",
                "",
                "Fake parameterized connectivity action.",
                ScannerSdkLib.CapabilityEntrySource.Master,
                uint.MaxValue,
                ScannerSdkLib.CapabilityEntryAvailability.Available,
                1,
                true,
                false,
                false,
                true,
                true,
                ScannerSdkLib.CapabilityValueKind.BytesAscii,
                ScannerSdkLib.CapabilityRiskLevel.Connectivity,
                "Printable ASCII Bluetooth name",
                []),
        ];

    public IReadOnlyList<ScannerSdkLib.CapabilityLabel> GetCapabilityLabels(
        ScannerSdkLib.CapabilityLabelKind kind) =>
        kind switch
        {
            ScannerSdkLib.CapabilityLabelKind.Domain =>
                [new(kind, "common", "", "Common", "fake.domain.common", 1)],
            ScannerSdkLib.CapabilityLabelKind.Family =>
                [new(kind, "device", "", "Device", "fake.family.device", 1)],
            ScannerSdkLib.CapabilityLabelKind.Section =>
                [
                    new(kind, "feedback", "", "Feedback", "fake.section.feedback", 1),
                    new(kind, "scan", "", "Scan", "fake.section.scan", 2),
                ],
            ScannerSdkLib.CapabilityLabelKind.Group =>
                [
                    new(kind, "feedback", "", "Feedback", "fake.group.feedback", 1),
                    new(kind, "scan", "", "Scan", "fake.group.scan", 2),
                ],
            ScannerSdkLib.CapabilityLabelKind.Setting =>
                [
                    new(kind, "SetAckBeepEnabled", "", "ACK Beep", "fake.setting.ack_beep", 1),
                    new(kind, "SetVibrationEnabled", "", "Vibration", "fake.setting.vibration", 2),
                ],
            ScannerSdkLib.CapabilityLabelKind.Action =>
                [
                    new(kind, "TriggerScan", "", "Trigger Scan", "fake.action.trigger_scan", 1),
                    new(kind, "SetRtcTimestamp", "", "Set RTC Timestamp", "fake.action.set_timestamp", 2),
                    new(kind, "SetBluetoothName", "", "Set Bluetooth Name", "fake.action.set_bluetooth_name", 3),
                ],
            _ => [],
        };

    public ScannerSdkLib.DataRuleKindLabel GetDataRuleKindLabel(ScannerSdkLib.DataRuleKind kind) =>
        new($"fake.data_rule.{kind}", kind.ToString());

    public string Localize(string localizationKey, string fallbackDisplayName, CultureInfo? culture) =>
        fallbackDisplayName;

    public void Dispose()
    {
    }

    private void EnsureInitialized()
    {
        if (!initialized)
        {
            throw new InvalidOperationException("Fake SDK is not initialized.");
        }
    }
}

internal sealed class FakeDemoSession : IDemoSessionHandle
{
    private readonly string modelKey;
    private readonly FakeDemoScenario scenario;
    private ScannerSdkLib.SessionState state = ScannerSdkLib.SessionState.Idle;
    private ScannerSdkLib.ScanTextCharset charset = ScannerSdkLib.ScanTextCharset.Utf8;
    private byte[] terminator = [0x0D];

    internal string? LastCapabilityActionKey { get; private set; }

    internal byte[] LastCapabilityActionValue { get; private set; } = [];
    private bool ackBeepEnabled = true;

    internal FakeDemoSession(
        string deviceId,
        ScannerSdkLib.TransportType transportType,
        string modelKey,
        FakeDemoScenario scenario)
    {
        DeviceId = deviceId;
        TransportType = transportType;
        this.modelKey = string.IsNullOrWhiteSpace(modelKey) ? "fake.reference" : modelKey;
        this.scenario = scenario;
    }

    public string DeviceId { get; }

    public ScannerSdkLib.TransportType TransportType { get; }

    public ScannerSdkLib.SessionState LatestState => state;

    public event Action<ScannerSdkLib.SessionState>? StateChanged;

    public event Action<ScannerSdkLib.ScanEvent>? ScanReceived;

    public event Action<ScannerSdkLib.SessionFailure>? FailureReceived;

    public event Action<ScannerSdkLib.SessionInitializationStageEvent>? InitializationStageReceived;

    public event Action<ScannerSdkLib.CommandTrace>? CommandTraceReceived
    {
        add { }
        remove { }
    }

    internal void ConnectReady()
    {
        Transition(ScannerSdkLib.SessionState.Connected);
        Transition(ScannerSdkLib.SessionState.Ready);
    }

    internal void SimulateRemoteDisconnect()
    {
        FailureReceived?.Invoke(new ScannerSdkLib.SessionFailure(
            null,
            DeviceId,
            TransportType,
            ScannerSdkLib.TransportIssue.PlatformError,
            "Fake remote disconnect.",
            995));
        Transition(ScannerSdkLib.SessionState.Disconnected);
    }

    public void SetScanTextCharset(ScannerSdkLib.ScanTextCharset value) => charset = value;

    public Task SetScanTextTerminatorAsync(byte[] terminatorBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        terminator = [.. terminatorBytes];
        return Task.CompletedTask;
    }

    public Task<ScannerSdkLib.ScannerInfo> RefreshInfoAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildInfo());
    }

    public Task<ScannerSdkLib.SessionInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        InitializationStageReceived?.Invoke(new ScannerSdkLib.SessionInitializationStageEvent(
            1, DeviceId, modelKey, ScannerSdkLib.SessionInitializationStage.Completed, 1, 1, true, 0, "Fake READY"));
        return Task.FromResult(new ScannerSdkLib.SessionInitializationResult(
            BuildInfo(),
            new ScannerSdkLib.BatteryInfo("86", "4.02V", 86),
            true));
    }

    public string GetResolvedModelKey() => modelKey;

    public ScannerSdkLib.DeviceCapabilitySummary GetDeviceCapabilitySummary() =>
        new(modelKey, "Fake Reference Scanner", true, true, true, true, true, true, ScannerSdkLib.DeviceSupportStatus.Verified);

    public ScannerSdkLib.SessionOperationSupport GetOperationSupport() =>
        new(true, true, true, true, true, true, true);

    public Task<ScannerSdkLib.CapabilityValue> ReadCapabilityValueAsync(
        string entryKey,
        CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ScannerSdkLib.CapabilityValue.FromBoolean(ackBeepEnabled));
    }

    public Task<ScannerSdkLib.CommandResponse> WriteCapabilityValueAsync(
        string entryKey,
        ScannerSdkLib.CapabilityValue value,
        bool persist = true,
        CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        ackBeepEnabled = value.BooleanValue ?? value.Bytes.FirstOrDefault() != 0;
        return Task.FromResult(Acknowledged());
    }

    public async Task<ScannerSdkLib.CommandResponse> ExecuteCapabilityActionAsync(
        string entryKey,
        byte[]? valueBytes = null,
        CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        LastCapabilityActionKey = entryKey;
        LastCapabilityActionValue = valueBytes?.ToArray() ?? [];
        if (entryKey == "action.TriggerScan")
        {
            await TriggerScanAsync(cancellationToken).ConfigureAwait(false);
        }
        return Acknowledged();
    }

    public Task<ScannerSdkLib.BatteryInfo> GetBatteryInfoAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ScannerSdkLib.BatteryInfo("86", "4.02V", 86));
    }

    public async Task<ScannerSdkLib.BatteryInfo?> GetCachedBatteryInfoAsync(CancellationToken cancellationToken = default) =>
        await GetBatteryInfoAsync(cancellationToken).ConfigureAwait(false);

    public Task<ScannerSdkLib.StorageUsage> GetStorageUsageAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ScannerSdkLib.StorageUsage(12, 128, 1024, 896, "12,128,1024"));
    }

    public Task<ScannerSdkLib.CommandResponse> SetTimestampAsync(
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default) =>
        CompleteCommand(cancellationToken);

    public Task<ScannerSdkLib.CommandResponse> SetAckBeepEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ackBeepEnabled = enabled;
        return CompleteCommand(cancellationToken);
    }

    public Task<ScannerSdkLib.CommandResponse> SetVibrationEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        CompleteCommand(cancellationToken);

    public Task<ScannerSdkLib.CommandResponse> ApplyDataRuleAsync(
        ScannerSdkLib.DataRule rule,
        CancellationToken cancellationToken = default) =>
        CompleteCommand(cancellationToken);

    public Task TriggerScanAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        var text = $"FAKE-{DateTimeOffset.UtcNow:HHmmss}";
        var payload = charset.ToEncoding().GetBytes(text).Concat(terminator).ToArray();
        ScanReceived?.Invoke(new ScannerSdkLib.ScanEvent(
            (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            128,
            text,
            charset.ToEncoding().GetBytes(text),
            payload));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Transition(ScannerSdkLib.SessionState.Disconnected);
        return Task.CompletedTask;
    }

    private Task<ScannerSdkLib.CommandResponse> CompleteCommand(CancellationToken cancellationToken)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Acknowledged());
    }

    private void EnsureSupported()
    {
        if (scenario == FakeDemoScenario.Unsupported)
        {
            throw new NotSupportedException("Fake scenario reports this operation as unsupported.");
        }
        if (state != ScannerSdkLib.SessionState.Ready)
        {
            throw new InvalidOperationException($"Fake session is not ready: {state}");
        }
    }

    private void Transition(ScannerSdkLib.SessionState value)
    {
        state = value;
        StateChanged?.Invoke(value);
    }

    private ScannerSdkLib.ScannerInfo BuildInfo() =>
        new(
            DeviceId,
            "Fake Reference Scanner",
            "FAKE-0001",
            "1.2.3",
            "A1",
            "ScannerSDK",
            "Fake",
            "B",
            "R",
            TransportType.ToString(),
            "",
            "W",
            "BT",
            "C",
            "",
            "01",
            "",
            "Fake Scanner",
            "5.0");

    private static ScannerSdkLib.CommandResponse Acknowledged() =>
        new(Encoding.ASCII.GetBytes("OK"), [0x06], true, 0, [], true);
}
