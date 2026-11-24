using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.MauiDemo;

internal interface IMauiSession
{
    event Action<ScannerSdkLib.SessionState>? StateChanged;

    event Action<MauiScanEvent>? ScanReceived;

    event Action<string>? FailureReceived;

    MauiOperationSupport GetOperationSupport();

    IReadOnlyList<MauiSettingDefinition> GetSettingDefinitions();

    Task<string> ReadSettingAsync(
        MauiSettingDefinition definition,
        CancellationToken cancellationToken = default);

    Task<string> WriteSettingAsync(
        MauiSettingDefinition definition,
        string value,
        bool persist,
        CancellationToken cancellationToken = default);

    string GetResolvedModelKey();

    Task TriggerScanAsync(CancellationToken cancellationToken = default);

    Task<MauiDeviceInfo> RefreshInfoAsync(CancellationToken cancellationToken = default);

    Task<MauiBattery> ReadBatteryAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

internal interface IMauiDemoBackend : IDisposable
{
    string Version { get; }

    event Action<MauiDevice>? DiscoveryReceived;

    event Action<string>? DiscoveryFailureReceived;

    Task<IReadOnlyList<MauiModel>> InitializeAsync(CancellationToken cancellationToken = default);

    Task StartDiscoveryAsync(string modelKey, CancellationToken cancellationToken = default);

    Task StopDiscoveryAsync(CancellationToken cancellationToken = default);

    Task<IMauiSession> ConnectAsync(MauiDevice device, CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

internal sealed class ScannerMauiDemoBackend : IMauiDemoBackend
{
    private readonly ScannerSdkLib.ScannerSdk sdk = new();
    private readonly Dictionary<string, ScannerSdkLib.DiscoveredDevice> discoveredDevices = new(StringComparer.Ordinal);

    internal ScannerMauiDemoBackend()
    {
        sdk.DiscoveryReceived += HandleDiscovery;
        sdk.DiscoveryFailureReceived += HandleDiscoveryFailure;
    }

    public string Version { get; private set; } = "";

    public event Action<MauiDevice>? DiscoveryReceived;

    public event Action<string>? DiscoveryFailureReceived;

    public async Task<IReadOnlyList<MauiModel>> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await sdk.InitializeAsync(cancellationToken).ConfigureAwait(false);
        Version = sdk.Version;
        return sdk.GetSupportedDeviceModels(ScannerSdkLib.TransportType.UsbHid)
            .Select(static model => new MauiModel(model.ModelKey, model.ModelName))
            .ToArray();
    }

    public Task StartDiscoveryAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        lock (discoveredDevices)
        {
            discoveredDevices.Clear();
        }
        return sdk.StartDiscoveryAsync(
            [ScannerSdkLib.TransportType.UsbHid],
            modelKey,
            cancellationToken);
    }

    public Task StopDiscoveryAsync(CancellationToken cancellationToken = default) =>
        sdk.StopDiscoveryAsync(cancellationToken);

    public async Task<IMauiSession> ConnectAsync(
        MauiDevice device,
        CancellationToken cancellationToken = default)
    {
        ScannerSdkLib.DiscoveredDevice source;
        lock (discoveredDevices)
        {
            source = discoveredDevices.GetValueOrDefault(device.DeviceId)
                ?? throw new InvalidOperationException("The selected scanner is no longer available. Refresh devices and try again.");
        }
        var session = await sdk.ConnectReadyAsync(
            source,
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);
        return new ScannerMauiSession(session);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        sdk.ShutdownAsync(cancellationToken);

    public void Dispose()
    {
        sdk.DiscoveryReceived -= HandleDiscovery;
        sdk.DiscoveryFailureReceived -= HandleDiscoveryFailure;
        sdk.Dispose();
    }

    private void HandleDiscovery(ScannerSdkLib.DiscoveredDevice device)
    {
        if (device.TransportType != ScannerSdkLib.TransportType.UsbHid)
        {
            return;
        }
        lock (discoveredDevices)
        {
            discoveredDevices[device.DeviceId] = device;
        }
        DiscoveryReceived?.Invoke(new MauiDevice(device.DeviceId, device.Name, device.ModelKey));
    }

    private void HandleDiscoveryFailure(ScannerSdkLib.DiscoveryFailure failure) =>
        DiscoveryFailureReceived?.Invoke($"Device discovery failed: {failure.Code}");
}

internal sealed class ScannerMauiSession : IMauiSession
{
    private readonly ScannerSdkLib.ScannerSession session;

    internal ScannerMauiSession(ScannerSdkLib.ScannerSession session)
    {
        this.session = session;
    }

    public event Action<ScannerSdkLib.SessionState>? StateChanged
    {
        add => session.StateChanged += value;
        remove => session.StateChanged -= value;
    }

    public event Action<MauiScanEvent>? ScanReceived
    {
        add
        {
            var attach = ScanForwarded is null;
            ScanForwarded += value;
            if (attach)
            {
                session.ScanReceived += ForwardScan;
            }
        }
        remove
        {
            ScanForwarded -= value;
            if (ScanForwarded is null)
            {
                session.ScanReceived -= ForwardScan;
            }
        }
    }

    public event Action<string>? FailureReceived
    {
        add
        {
            var attach = FailureForwarded is null;
            FailureForwarded += value;
            if (attach)
            {
                session.FailureReceived += ForwardFailure;
            }
        }
        remove
        {
            FailureForwarded -= value;
            if (FailureForwarded is null)
            {
                session.FailureReceived -= ForwardFailure;
            }
        }
    }

    private event Action<MauiScanEvent>? ScanForwarded;
    private event Action<string>? FailureForwarded;

    public MauiOperationSupport GetOperationSupport()
    {
        var support = session.GetOperationSupport();
        return new MauiOperationSupport(
            support.SupportsRefreshInfo,
            support.SupportsGetBatteryInfo,
            support.SupportsTriggerScan);
    }

    public IReadOnlyList<MauiSettingDefinition> GetSettingDefinitions()
    {
        var domains = session.GetCapabilityDomains()
            .Where(static domain => !string.IsNullOrWhiteSpace(domain.Key))
            .DistinctBy(static domain => domain.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static domain => domain.Key, StringComparer.OrdinalIgnoreCase);

        return session.GetCapabilityEntries()
            .Where(static entry =>
                entry.Kind == ScannerSdkLib.CapabilityEntryKind.Setting &&
                entry.Availability == ScannerSdkLib.CapabilityEntryAvailability.Available &&
                (entry.SupportsRead || entry.SupportsWrite))
            .Select(entry => MapSettingDefinition(entry, domains))
            .OrderBy(static definition => definition.DomainSortOrder)
            .ThenBy(static definition => definition.DomainDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static definition => definition.GroupSummary, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static definition => definition.EntryKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> ReadSettingAsync(
        MauiSettingDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var value = await session.ReadCapabilityValueAsync(definition.EntryKey, cancellationToken).ConfigureAwait(false);
        return MauiSettingCodec.Format(definition, value);
    }

    public async Task<string> WriteSettingAsync(
        MauiSettingDefinition definition,
        string value,
        bool persist,
        CancellationToken cancellationToken = default)
    {
        var encoded = MauiSettingCodec.Encode(definition, value);
        var response = await session.WriteCapabilityValueAsync(
            definition.EntryKey,
            encoded,
            persist,
            cancellationToken).ConfigureAwait(false);
        return response.Acknowledged ? "Write acknowledged" : "Write sent";
    }

    public string GetResolvedModelKey() => session.GetResolvedModelKey();

    public Task TriggerScanAsync(CancellationToken cancellationToken = default) =>
        session.TriggerScanAsync(cancellationToken);

    public async Task<MauiDeviceInfo> RefreshInfoAsync(CancellationToken cancellationToken = default)
    {
        var info = await session.RefreshInfoAsync(cancellationToken).ConfigureAwait(false);
        return new MauiDeviceInfo(
            info.Name,
            session.GetResolvedModelKey(),
            info.FirmwareVersion,
            info.HardwareVersion);
    }

    public async Task<MauiBattery> ReadBatteryAsync(CancellationToken cancellationToken = default)
    {
        var battery = await session.GetBatteryInfoAsync(cancellationToken).ConfigureAwait(false);
        return new MauiBattery(battery.Percent, battery.VoltageText);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        session.DisconnectAsync(cancellationToken);

    private string ResolveSettingName(ScannerSdkLib.CapabilityEntry entry)
    {
        var label = string.IsNullOrWhiteSpace(entry.SemanticKey)
            ? null
            : session.FindCapabilityLabel(
                ScannerSdkLib.CapabilityLabelKind.Setting,
                entry.SemanticKey);
        var candidate = label?.DisplayName;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }
        candidate = string.IsNullOrWhiteSpace(entry.SemanticKey) ? entry.EntryKey : entry.SemanticKey;
        return SplitIdentifier(candidate[(candidate.LastIndexOf('.') + 1)..]);
    }

    private MauiSettingDefinition MapSettingDefinition(
        ScannerSdkLib.CapabilityEntry entry,
        IReadOnlyDictionary<string, ScannerSdkLib.CapabilityDomain> domains)
    {
        var domainKey = string.IsNullOrWhiteSpace(entry.DomainKey) ? "other" : entry.DomainKey;
        domains.TryGetValue(domainKey, out var domain);
        var domainDisplayName = !string.IsNullOrWhiteSpace(domain?.DisplayName)
            ? domain.DisplayName
            : domainKey.Equals("other", StringComparison.OrdinalIgnoreCase)
                ? "Other settings"
                : SplitIdentifier(domainKey);
        return new MauiSettingDefinition(
            entry.EntryKey,
            ResolveSettingName(entry),
            BuildGroupSummary(entry),
            entry.SupportsRead,
            entry.SupportsWrite,
            entry.ValueKind,
            entry.RiskLevel,
            entry.ValueHint,
            entry.DefaultValue,
            entry.Options.Select(static option => option.Value).ToArray(),
            domainKey,
            domainDisplayName,
            domain?.SortOrder ?? int.MaxValue);
    }

    private static string BuildGroupSummary(ScannerSdkLib.CapabilityEntry entry) => string.Join(
        " · ",
        new[] { entry.Source.ToString(), entry.GroupKey, entry.FamilyKey, entry.SectionKey }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));

    private static string SplitIdentifier(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1]))
            {
                builder.Append(' ');
            }
            builder.Append(character is '_' or '-' ? ' ' : character);
        }
        return builder.ToString().Trim();
    }

    private void ForwardScan(ScannerSdkLib.ScanEvent scan) =>
        ScanForwarded?.Invoke(new MauiScanEvent(scan.BarcodeType, scan.Text));

    private void ForwardFailure(ScannerSdkLib.SessionFailure failure) =>
        FailureForwarded?.Invoke($"Session error: {failure.Issue}");
}
