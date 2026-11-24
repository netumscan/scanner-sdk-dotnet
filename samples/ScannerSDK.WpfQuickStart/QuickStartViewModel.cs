using System.Collections.ObjectModel;
using System.Windows.Input;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WpfQuickStart;

public sealed class QuickStartViewModel : ObservableObject
{
    private const int MaxScans = 20;

    private readonly IQuickStartBackend backend;
    private readonly SynchronizationContext? uiContext;
    private readonly CancellationTokenSource lifetime = new();
    private readonly AsyncCommand refreshDevicesCommand;
    private readonly AsyncCommand connectCommand;
    private readonly AsyncCommand disconnectCommand;
    private readonly AsyncCommand triggerScanCommand;
    private readonly AsyncCommand refreshInfoCommand;
    private readonly AsyncCommand readBatteryCommand;
    private readonly HashSet<string> sensitiveDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    private IQuickSession? session;
    private QuickModel? selectedModel;
    private string sdkVersion = "—";
    private string status = "Initializing SDK";
    private string connectedDeviceName = "Not connected";
    private string resolvedModel = "—";
    private string deviceInfo = "Not loaded";
    private string battery = "Not loaded";
    private string lastAction = "";
    private bool isBusy;
    private bool isInitialized;
    private bool isReady;
    private bool canRetry;
    private QuickOperationSupport operationSupport = QuickOperationSupport.None;
    private long scanSequence;
    private int operationActive;
    private int shutdownStarted;
    private int intentionalDisconnect;

    internal QuickStartViewModel(IQuickStartBackend backend)
    {
        this.backend = backend;
        uiContext = SynchronizationContext.Current;
        backend.DiscoveryReceived += HandleDiscovery;
        backend.DiscoveryFailureReceived += HandleDiscoveryFailure;

        refreshDevicesCommand = new AsyncCommand(RefreshDevicesAsync, () => CanRefreshDevices);
        connectCommand = new AsyncCommand(
            parameter => ConnectAsync(parameter as QuickDevice),
            parameter => parameter is QuickDevice && CanConnect);
        disconnectCommand = new AsyncCommand(DisconnectAsync, () => CanDisconnect);
        triggerScanCommand = new AsyncCommand(TriggerScanAsync, () => CanTriggerScan);
        refreshInfoCommand = new AsyncCommand(RefreshInfoAsync, () => CanRefreshInfo);
        readBatteryCommand = new AsyncCommand(ReadBatteryAsync, () => CanReadBattery);
    }

    public ObservableCollection<QuickModel> Models { get; } = [];

    public ObservableCollection<QuickDevice> Devices { get; } = [];

    public ObservableCollection<QuickScanRow> Scans { get; } = [];

    public QuickModel? SelectedModel
    {
        get => selectedModel;
        set
        {
            if (SetProperty(ref selectedModel, value))
            {
                RaiseStateChanged();
            }
        }
    }

    public string SdkVersion
    {
        get => sdkVersion;
        private set => SetProperty(ref sdkVersion, value);
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string ConnectedDeviceName
    {
        get => connectedDeviceName;
        private set => SetProperty(ref connectedDeviceName, value);
    }

    public string ResolvedModel
    {
        get => resolvedModel;
        private set => SetProperty(ref resolvedModel, value);
    }

    public string DeviceInfo
    {
        get => deviceInfo;
        private set => SetProperty(ref deviceInfo, value);
    }

    public string Battery
    {
        get => battery;
        private set => SetProperty(ref battery, value);
    }

    public string LastAction
    {
        get => lastAction;
        private set
        {
            if (SetProperty(ref lastAction, value))
            {
                NotifyPropertyChanged(nameof(HasLastAction));
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                RaiseStateChanged();
            }
        }
    }

    public bool IsReady
    {
        get => isReady;
        private set
        {
            if (SetProperty(ref isReady, value))
            {
                RaiseStateChanged();
            }
        }
    }

    public bool HasDevices => Devices.Count > 0;

    public bool HasNoDevices => !HasDevices;

    public bool HasScans => Scans.Count > 0;

    public bool HasNoScans => !HasScans;

    public bool HasLastAction => !string.IsNullOrWhiteSpace(LastAction);

    public bool CanRefreshDevices =>
        isInitialized && SelectedModel is not null && session is null && !IsBusy;

    public bool CanConnect => session is null && !IsBusy;

    public bool CanDisconnect => session is not null && !IsBusy;

    public bool CanTriggerScan => IsReady && operationSupport.TriggerScan && !IsBusy;

    public bool CanRefreshInfo => IsReady && operationSupport.RefreshInfo && !IsBusy;

    public bool CanReadBattery => IsReady && operationSupport.Battery && !IsBusy;

    public string RefreshButtonText => canRetry ? "Retry Device Refresh" : "Refresh Devices";

    public ICommand RefreshDevicesCommand => refreshDevicesCommand;

    public ICommand ConnectCommand => connectCommand;

    public ICommand DisconnectCommand => disconnectCommand;

    public ICommand TriggerScanCommand => triggerScanCommand;

    public ICommand RefreshInfoCommand => refreshInfoCommand;

    public ICommand ReadBatteryCommand => readBatteryCommand;

    public async Task InitializeAsync()
    {
        if (isInitialized || Volatile.Read(ref shutdownStarted) != 0)
        {
            return;
        }
        await RunExclusiveAsync(async () =>
        {
            Status = "Initializing SDK";
            var models = await backend.InitializeAsync(lifetime.Token).ConfigureAwait(true);
            Models.Clear();
            foreach (var model in models
                .Where(static model => !string.IsNullOrWhiteSpace(model.ModelKey))
                .DistinctBy(static model => model.ModelKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static model => model.ModelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static model => model.ModelKey, StringComparer.OrdinalIgnoreCase))
            {
                Models.Add(model);
            }
            SelectedModel = Models.FirstOrDefault(static model =>
                    model.ModelKey.Equals("CS7501", StringComparison.OrdinalIgnoreCase))
                ?? Models.FirstOrDefault();
            SdkVersion = string.IsNullOrWhiteSpace(backend.Version) ? "Unknown" : backend.Version;
            isInitialized = true;
            canRetry = Models.Count > 0;
            Status = Models.Count == 0 ? "No USB CDC models are available" : "Ready to refresh devices";
            RaiseStateChanged();
        }, "SDK initialization failed").ConfigureAwait(true);
    }

    internal async Task RefreshDevicesAsync()
    {
        var model = SelectedModel;
        if (model is null || session is not null)
        {
            return;
        }
        await RunExclusiveAsync(async () =>
        {
            Devices.Clear();
            RaiseDeviceCollectionChanged();
            canRetry = false;
            LastAction = "";
            Status = "Refreshing USB CDC devices";
            await backend.StartDiscoveryAsync(model.ModelKey, lifetime.Token).ConfigureAwait(true);
            Status = Devices.Count == 0
                ? "No matching USB CDC scanners found"
                : $"Found {Devices.Count} USB CDC scanner{(Devices.Count == 1 ? "" : "s")}";
            canRetry = true;
            RaiseStateChanged();
        }, "Device refresh failed").ConfigureAwait(true);
    }

    internal async Task ConnectAsync(QuickDevice? device)
    {
        if (device is null || session is not null)
        {
            return;
        }
        await RunExclusiveAsync(async () =>
        {
            Status = $"Connecting to {device.DisplayName}";
            await backend.StopDiscoveryAsync(lifetime.Token).ConfigureAwait(true);
            var connected = await backend.ConnectAsync(device, lifetime.Token).ConfigureAwait(true);
            session = connected;
            AttachSession(connected);
            try
            {
                operationSupport = connected.GetOperationSupport();
                ResolvedModel = connected.GetResolvedModelKey();
                ConnectedDeviceName = device.DisplayName;
                IsReady = true;
                canRetry = false;
                Status = "Connected and ready";
                LastAction = $"Resolved model: {ResolvedModel}";
            }
            catch
            {
                DetachSession(connected);
                session = null;
                await connected.DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
                throw;
            }
        }, "Connection failed").ConfigureAwait(true);
    }

    internal async Task DisconnectAsync()
    {
        var active = session;
        if (active is null)
        {
            return;
        }
        await RunExclusiveAsync(async () =>
        {
            Interlocked.Exchange(ref intentionalDisconnect, 1);
            try
            {
                await active.DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
                if (ReferenceEquals(session, active))
                {
                    ClearSession(active, "Disconnected", retry: true);
                }
            }
            finally
            {
                Interlocked.Exchange(ref intentionalDisconnect, 0);
            }
        }, "Disconnect failed").ConfigureAwait(true);
    }

    internal Task TriggerScanAsync() => RunSessionCommandAsync(
        operationSupport.TriggerScan,
        "Trigger Scan is not supported by this session",
        async active =>
        {
            await active.TriggerScanAsync(lifetime.Token).ConfigureAwait(true);
            LastAction = "Trigger Scan command sent";
        });

    internal Task RefreshInfoAsync() => RunSessionCommandAsync(
        operationSupport.RefreshInfo,
        "Refresh Device Info is not supported by this session",
        async active =>
        {
            var info = await active.RefreshInfoAsync(lifetime.Token).ConfigureAwait(true);
            DeviceInfo = info.DisplaySummary();
            LastAction = "Device information refreshed";
        });

    internal Task ReadBatteryAsync() => RunSessionCommandAsync(
        operationSupport.Battery,
        "Read Battery is not supported by this session",
        async active =>
        {
            var result = await active.ReadBatteryAsync(lifetime.Token).ConfigureAwait(true);
            Battery = result.DisplaySummary();
            LastAction = "Battery information refreshed";
        });

    internal async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
        {
            return;
        }
        lifetime.Cancel();
        backend.DiscoveryReceived -= HandleDiscovery;
        backend.DiscoveryFailureReceived -= HandleDiscoveryFailure;
        var active = session;
        if (active is not null)
        {
            DetachSession(active);
            session = null;
            try
            {
                await active.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Shutdown remains best-effort after the window is destroyed.
            }
        }
        try
        {
            await backend.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            backend.Dispose();
            lifetime.Dispose();
        }
    }

    public void BeginShutdown() => _ = ShutdownAsync();

    private async Task RunSessionCommandAsync(
        bool supported,
        string unsupportedMessage,
        Func<IQuickSession, Task> operation)
    {
        var active = session;
        if (active is null || !IsReady)
        {
            LastAction = "Connect a ready scanner first";
            return;
        }
        if (!supported)
        {
            LastAction = unsupportedMessage;
            return;
        }
        await RunExclusiveAsync(() => operation(active), "Command failed").ConfigureAwait(true);
    }

    private async Task RunExclusiveAsync(Func<Task> operation, string failurePrefix)
    {
        if (Interlocked.Exchange(ref operationActive, 1) != 0)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Window shutdown owns cancellation and does not need a UI error.
        }
        catch (Exception error)
        {
            var message = $"{failurePrefix}: {Sanitize(error.Message)}";
            Status = message;
            LastAction = message;
            canRetry = session is null && isInitialized;
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref operationActive, 0);
            RaiseStateChanged();
        }
    }

    private void AttachSession(IQuickSession active)
    {
        active.StateChanged += HandleSessionState;
        active.ScanReceived += HandleScan;
        active.FailureReceived += HandleSessionFailure;
    }

    private void DetachSession(IQuickSession active)
    {
        active.StateChanged -= HandleSessionState;
        active.ScanReceived -= HandleScan;
        active.FailureReceived -= HandleSessionFailure;
    }

    private void ClearSession(IQuickSession active, string nextStatus, bool retry)
    {
        if (!ReferenceEquals(session, active))
        {
            return;
        }
        DetachSession(active);
        session = null;
        operationSupport = QuickOperationSupport.None;
        IsReady = false;
        ConnectedDeviceName = "Not connected";
        ResolvedModel = "—";
        canRetry = retry;
        Status = nextStatus;
        RaiseStateChanged();
    }

    private void HandleDiscovery(QuickDevice device) => PostToUi(() =>
    {
        sensitiveDeviceIds.Add(device.DeviceId);
        var existing = Devices.FirstOrDefault(candidate => candidate.DeviceId == device.DeviceId);
        if (existing is not null)
        {
            var index = Devices.IndexOf(existing);
            Devices[index] = device;
        }
        else
        {
            var insertAt = 0;
            while (insertAt < Devices.Count &&
                StringComparer.OrdinalIgnoreCase.Compare(Devices[insertAt].DisplayName, device.DisplayName) <= 0)
            {
                insertAt++;
            }
            Devices.Insert(insertAt, device);
        }
        RaiseDeviceCollectionChanged();
    });

    private void HandleDiscoveryFailure(string message) => PostToUi(() =>
    {
        Status = Sanitize(message);
        LastAction = Status;
        canRetry = true;
        RaiseStateChanged();
    });

    private void HandleSessionState(ScannerSdkLib.SessionState state) => PostToUi(() =>
    {
        var active = session;
        if (active is null)
        {
            return;
        }
        switch (state)
        {
            case ScannerSdkLib.SessionState.Ready:
                IsReady = true;
                Status = "Connected and ready";
                break;
            case ScannerSdkLib.SessionState.Disconnected:
                ClearSession(
                    active,
                    Volatile.Read(ref intentionalDisconnect) != 0 ? "Disconnected" : "Connection lost",
                    retry: true);
                break;
            case ScannerSdkLib.SessionState.Error:
                ClearSession(active, "Connection error", retry: true);
                break;
            default:
                Status = state.ToString();
                break;
        }
    });

    private void HandleScan(QuickScanEvent scan) => PostToUi(() =>
    {
        Scans.Insert(0, new QuickScanRow(
            Interlocked.Increment(ref scanSequence),
            scan.BarcodeType,
            scan.Text));
        while (Scans.Count > MaxScans)
        {
            Scans.RemoveAt(Scans.Count - 1);
        }
        NotifyPropertyChanged(nameof(HasScans));
        NotifyPropertyChanged(nameof(HasNoScans));
    });

    private void HandleSessionFailure(string message) => PostToUi(() =>
    {
        Status = Sanitize(message);
        LastAction = Status;
    });

    private void PostToUi(Action action)
    {
        if (uiContext is null || ReferenceEquals(SynchronizationContext.Current, uiContext))
        {
            action();
            return;
        }
        uiContext.Post(static state => ((Action)state!).Invoke(), action);
    }

    private string Sanitize(string? message)
    {
        var safe = string.IsNullOrWhiteSpace(message) ? "Unknown error" : message;
        foreach (var deviceId in sensitiveDeviceIds)
        {
            safe = safe.Replace(deviceId, "[device]", StringComparison.OrdinalIgnoreCase);
        }
        return safe;
    }

    private void RaiseDeviceCollectionChanged()
    {
        NotifyPropertyChanged(nameof(HasDevices));
        NotifyPropertyChanged(nameof(HasNoDevices));
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        NotifyPropertyChanged(nameof(CanRefreshDevices));
        NotifyPropertyChanged(nameof(CanConnect));
        NotifyPropertyChanged(nameof(CanDisconnect));
        NotifyPropertyChanged(nameof(CanTriggerScan));
        NotifyPropertyChanged(nameof(CanRefreshInfo));
        NotifyPropertyChanged(nameof(CanReadBattery));
        NotifyPropertyChanged(nameof(RefreshButtonText));
        NotifyPropertyChanged(nameof(HasLastAction));
        refreshDevicesCommand.RaiseCanExecuteChanged();
        connectCommand.RaiseCanExecuteChanged();
        disconnectCommand.RaiseCanExecuteChanged();
        triggerScanCommand.RaiseCanExecuteChanged();
        refreshInfoCommand.RaiseCanExecuteChanged();
        readBatteryCommand.RaiseCanExecuteChanged();
    }
}
