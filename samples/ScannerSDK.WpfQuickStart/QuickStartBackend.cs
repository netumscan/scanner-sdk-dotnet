using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WpfQuickStart;

internal interface IQuickSession
{
    event Action<ScannerSdkLib.SessionState>? StateChanged;

    event Action<QuickScanEvent>? ScanReceived;

    event Action<string>? FailureReceived;

    QuickOperationSupport GetOperationSupport();

    string GetResolvedModelKey();

    Task TriggerScanAsync(CancellationToken cancellationToken = default);

    Task<QuickDeviceInfo> RefreshInfoAsync(CancellationToken cancellationToken = default);

    Task<QuickBattery> ReadBatteryAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

internal interface IQuickStartBackend : IDisposable
{
    string Version { get; }

    event Action<QuickDevice>? DiscoveryReceived;

    event Action<string>? DiscoveryFailureReceived;

    Task<IReadOnlyList<QuickModel>> InitializeAsync(CancellationToken cancellationToken = default);

    Task StartDiscoveryAsync(string modelKey, CancellationToken cancellationToken = default);

    Task StopDiscoveryAsync(CancellationToken cancellationToken = default);

    Task<IQuickSession> ConnectAsync(QuickDevice device, CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

internal sealed class ScannerQuickStartBackend : IQuickStartBackend
{
    private const ScannerSdkLib.TransportType Transport = ScannerSdkLib.TransportType.UsbSerial;
    private readonly ScannerSdkLib.ScannerSdk sdk = new();
    private readonly Dictionary<string, ScannerSdkLib.DiscoveredDevice> discoveredDevices = new(StringComparer.Ordinal);

    internal ScannerQuickStartBackend()
    {
        sdk.DiscoveryReceived += HandleDiscovery;
        sdk.DiscoveryFailureReceived += HandleDiscoveryFailure;
    }

    public string Version { get; private set; } = "";

    public event Action<QuickDevice>? DiscoveryReceived;

    public event Action<string>? DiscoveryFailureReceived;

    public async Task<IReadOnlyList<QuickModel>> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await sdk.InitializeAsync(cancellationToken).ConfigureAwait(false);
        Version = sdk.Version;
        return sdk.GetSupportedDeviceModels(Transport)
            .Select(static model => new QuickModel(model.ModelKey, model.ModelName))
            .ToArray();
    }

    public Task StartDiscoveryAsync(string modelKey, CancellationToken cancellationToken = default)
    {
        lock (discoveredDevices)
        {
            discoveredDevices.Clear();
        }
        return sdk.StartDiscoveryAsync(
            [Transport],
            modelKey,
            cancellationToken);
    }

    public Task StopDiscoveryAsync(CancellationToken cancellationToken = default) =>
        sdk.StopDiscoveryAsync(cancellationToken);

    public async Task<IQuickSession> ConnectAsync(
        QuickDevice device,
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
        return new ScannerQuickSession(session);
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
        if (device.TransportType != Transport)
        {
            return;
        }
        lock (discoveredDevices)
        {
            discoveredDevices[device.DeviceId] = device;
        }
        DiscoveryReceived?.Invoke(new QuickDevice(device.DeviceId, device.Name, device.ModelKey));
    }

    private void HandleDiscoveryFailure(ScannerSdkLib.DiscoveryFailure failure) =>
        DiscoveryFailureReceived?.Invoke($"Device discovery failed: {failure.Code}");
}

internal sealed class ScannerQuickSession : IQuickSession
{
    private readonly ScannerSdkLib.ScannerSession session;

    internal ScannerQuickSession(ScannerSdkLib.ScannerSession session)
    {
        this.session = session;
    }

    public event Action<ScannerSdkLib.SessionState>? StateChanged
    {
        add => session.StateChanged += value;
        remove => session.StateChanged -= value;
    }

    public event Action<QuickScanEvent>? ScanReceived
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

    private event Action<QuickScanEvent>? ScanForwarded;
    private event Action<string>? FailureForwarded;

    public QuickOperationSupport GetOperationSupport()
    {
        var support = session.GetOperationSupport();
        return new QuickOperationSupport(
            support.SupportsRefreshInfo,
            support.SupportsGetBatteryInfo,
            support.SupportsTriggerScan);
    }

    public string GetResolvedModelKey() => session.GetResolvedModelKey();

    public Task TriggerScanAsync(CancellationToken cancellationToken = default) =>
        session.TriggerScanAsync(cancellationToken);

    public async Task<QuickDeviceInfo> RefreshInfoAsync(CancellationToken cancellationToken = default)
    {
        var info = await session.RefreshInfoAsync(cancellationToken).ConfigureAwait(false);
        return new QuickDeviceInfo(
            info.Name,
            session.GetResolvedModelKey(),
            info.FirmwareVersion,
            info.HardwareVersion);
    }

    public async Task<QuickBattery> ReadBatteryAsync(CancellationToken cancellationToken = default)
    {
        var battery = await session.GetBatteryInfoAsync(cancellationToken).ConfigureAwait(false);
        return new QuickBattery(battery.Percent, battery.VoltageText);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        session.DisconnectAsync(cancellationToken);

    private void ForwardScan(ScannerSdkLib.ScanEvent scan) =>
        ScanForwarded?.Invoke(new QuickScanEvent(scan.BarcodeType, scan.Text));

    private void ForwardFailure(ScannerSdkLib.SessionFailure failure) =>
        FailureForwarded?.Invoke($"Session error: {failure.Issue}");
}
