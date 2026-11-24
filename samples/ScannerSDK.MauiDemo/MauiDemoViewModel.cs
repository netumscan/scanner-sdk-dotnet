using System.Collections.ObjectModel;
using System.Windows.Input;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.MauiDemo;

public sealed class MauiDemoViewModel : ObservableObject
{
    internal const string TargetModelKey = "NT-91";
    private const int MaxScans = 20;

    private readonly IMauiDemoBackend backend;
    private readonly IMauiDemoDispatcher dispatcher;
    private readonly CancellationTokenSource lifetime = new();
    private readonly AsyncCommand initializeCommand;
    private readonly AsyncCommand refreshDevicesCommand;
    private readonly AsyncCommand connectCommand;
    private readonly AsyncCommand disconnectCommand;
    private readonly AsyncCommand triggerScanCommand;
    private readonly AsyncCommand refreshInfoCommand;
    private readonly AsyncCommand readBatteryCommand;
    private readonly AsyncCommand clearScansCommand;
    private readonly AsyncCommand reloadSettingsCommand;
    private readonly AsyncCommand readAllSettingsCommand;
    private readonly AsyncCommand readSettingCommand;
    private readonly AsyncCommand writeSettingCommand;
    private readonly AsyncCommand clearSettingSearchCommand;
    private readonly HashSet<string> sensitiveDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object shutdownGate = new();

    private IMauiSession? session;
    private MauiModel? selectedModel;
    private string sdkVersion = "—";
    private string status = "Initializing SDK";
    private string connectedDeviceName = "Not connected";
    private string resolvedModel = "—";
    private string deviceInfo = "Not loaded";
    private string battery = "Not loaded";
    private string lastAction = "";
    private string settingsStatus = "Connect a ready scanner to load its supported settings";
    private string settingSearchText = "";
    private MauiSettingGroup? selectedSettingGroup;
    private bool isBusy;
    private bool isInitialized;
    private bool initializationFailed;
    private bool isReady;
    private bool canRetry;
    private bool discoveryAttempted;
    private MauiOperationSupport operationSupport = MauiOperationSupport.None;
    private long scanSequence;
    private int operationActive;
    private int shutdownStarted;
    private int intentionalDisconnect;
    private Task? shutdownTask;

    internal MauiDemoViewModel(IMauiDemoBackend backend, IMauiDemoDispatcher dispatcher)
    {
        this.backend = backend;
        this.dispatcher = dispatcher;
        backend.DiscoveryReceived += HandleDiscovery;
        backend.DiscoveryFailureReceived += HandleDiscoveryFailure;

        initializeCommand = new AsyncCommand(InitializeAsync, () => CanInitialize, HandleCommandError);
        refreshDevicesCommand = new AsyncCommand(RefreshDevicesAsync, () => CanRefreshDevices, HandleCommandError);
        connectCommand = new AsyncCommand(
            parameter => ConnectAsync(parameter as MauiDevice),
            parameter => parameter is MauiDevice && CanConnect,
            HandleCommandError);
        disconnectCommand = new AsyncCommand(DisconnectAsync, () => CanDisconnect, HandleCommandError);
        triggerScanCommand = new AsyncCommand(TriggerScanAsync, () => CanTriggerScan, HandleCommandError);
        refreshInfoCommand = new AsyncCommand(RefreshInfoAsync, () => CanRefreshInfo, HandleCommandError);
        readBatteryCommand = new AsyncCommand(ReadBatteryAsync, () => CanReadBattery, HandleCommandError);
        clearScansCommand = new AsyncCommand(ClearScansAsync, () => CanClearScans, HandleCommandError);
        reloadSettingsCommand = new AsyncCommand(ReloadSettingsAsync, () => CanReloadSettings, HandleCommandError);
        readAllSettingsCommand = new AsyncCommand(ReadAllSettingsAsync, () => CanReadAllSettings, HandleCommandError);
        readSettingCommand = new AsyncCommand(
            parameter => ReadSettingAsync(parameter as MauiSettingRow),
            parameter => parameter is MauiSettingRow row && CanReadSetting(row),
            HandleCommandError);
        writeSettingCommand = new AsyncCommand(
            parameter => WriteSettingAsync(parameter as MauiSettingRow),
            parameter => parameter is MauiSettingRow row && CanWriteSetting(row),
            HandleCommandError);
        clearSettingSearchCommand = new AsyncCommand(ClearSettingSearchAsync, handleError: HandleCommandError);
    }

    public ObservableCollection<MauiModel> Models { get; } = [];

    public ObservableCollection<MauiDevice> Devices { get; } = [];

    public ObservableCollection<MauiScanRow> Scans { get; } = [];

    public ObservableCollection<MauiSettingRow> Settings { get; } = [];

    public ObservableCollection<MauiSettingGroup> SettingGroups { get; } = [];

    public MauiModel? SelectedModel
    {
        get => selectedModel;
        private set
        {
            if (SetProperty(ref selectedModel, value))
            {
                if (session is null)
                {
                    Devices.Clear();
                    canRetry = false;
                    discoveryAttempted = false;
                    RaiseDeviceCollectionChanged();
                }
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

    public bool HasInitializationError => initializationFailed;

    public string SettingsStatus
    {
        get => settingsStatus;
        private set => SetProperty(ref settingsStatus, value);
    }

    public bool HasSettings => Settings.Count > 0;

    public bool HasNoSettings => !HasSettings;

    public bool HasVisibleSettings => SettingGroups.Count > 0;

    public bool HasNoVisibleSettings => !HasVisibleSettings;

    public string SettingSearchText
    {
        get => settingSearchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (SetProperty(ref settingSearchText, normalized))
            {
                NotifyPropertyChanged(nameof(HasSettingSearch));
                RebuildSettingGroups();
            }
        }
    }

    public bool HasSettingSearch => !string.IsNullOrWhiteSpace(SettingSearchText);

    public MauiSettingGroup? SelectedSettingGroup
    {
        get => selectedSettingGroup;
        set
        {
            if (ReferenceEquals(selectedSettingGroup, value))
            {
                return;
            }
            if (selectedSettingGroup is not null)
            {
                selectedSettingGroup.IsSelected = false;
            }
            if (SetProperty(ref selectedSettingGroup, value) && value is not null)
            {
                value.IsSelected = true;
            }
        }
    }

    public string SettingsEmptyMessage => HasSettings && HasSettingSearch
        ? "No settings match the current search."
        : "No dynamic settings are currently available.";

    public bool CanInitialize =>
        !isInitialized && !IsBusy && Volatile.Read(ref shutdownStarted) == 0;

    public bool CanRefreshDevices =>
        isInitialized && SelectedModel is not null && session is null && !IsBusy;

    public bool CanConnect => isInitialized && session is null && !IsBusy;

    public bool CanDisconnect => session is not null && !IsBusy;

    public bool CanTriggerScan => IsReady && operationSupport.TriggerScan && !IsBusy;

    public bool CanRefreshInfo => IsReady && operationSupport.RefreshInfo && !IsBusy;

    public bool CanReadBattery => IsReady && operationSupport.Battery && !IsBusy;

    public bool CanClearScans => Scans.Count > 0 && !IsBusy && Volatile.Read(ref shutdownStarted) == 0;

    public bool CanReloadSettings => IsReady && session is not null && !IsBusy;

    public bool CanReadAllSettings =>
        IsReady && !IsBusy && Settings.Any(static setting => setting.SupportsRead);

    public string RefreshButtonText => canRetry ? "Retry Device Refresh" : "Refresh Devices";

    public string DeviceEmptyMessage => !isInitialized
        ? "Initialize the SDK before refreshing NT-91 devices."
        : Models.Count == 0
            ? "NT-91 USB HID is not available in this SDK build."
            : discoveryAttempted
                ? "No NT-91 scanner was found. Check the USB connection and whether another app is using the HID device."
                : "Connect an NT-91 scanner over USB HID, then refresh devices.";

    public string ScanCountText => $"{Scans.Count}/{MaxScans} · memory only";

    public string SettingsSummary =>
        HasSettingSearch
            ? $"{VisibleSettingCount} of {Settings.Count} settings · {DomainCountText}"
            : $"{DomainCountText} · {Settings.Count} settings · {Settings.Count(static setting => setting.SupportsRead)} readable · {Settings.Count(static setting => setting.SupportsWrite)} writable";

    private int VisibleSettingCount => SettingGroups.Sum(static group => group.Count);

    private string DomainCountText => $"{SettingGroups.Count} domain{(SettingGroups.Count == 1 ? string.Empty : "s")}";

    public ICommand InitializeCommand => initializeCommand;

    public ICommand RefreshDevicesCommand => refreshDevicesCommand;

    public ICommand ConnectCommand => connectCommand;

    public ICommand DisconnectCommand => disconnectCommand;

    public ICommand TriggerScanCommand => triggerScanCommand;

    public ICommand RefreshInfoCommand => refreshInfoCommand;

    public ICommand ReadBatteryCommand => readBatteryCommand;

    public ICommand ClearScansCommand => clearScansCommand;

    public ICommand ReloadSettingsCommand => reloadSettingsCommand;

    public ICommand ReadAllSettingsCommand => readAllSettingsCommand;

    public ICommand ReadSettingCommand => readSettingCommand;

    public ICommand WriteSettingCommand => writeSettingCommand;

    public ICommand ClearSettingSearchCommand => clearSettingSearchCommand;

    public async Task InitializeAsync()
    {
        if (isInitialized || Volatile.Read(ref shutdownStarted) != 0)
        {
            return;
        }
        await RunExclusiveAsync(async () =>
        {
            initializationFailed = false;
            Status = "Initializing SDK";
            var models = await backend.InitializeAsync(lifetime.Token).ConfigureAwait(true);
            Models.Clear();
            var nt91 = models.FirstOrDefault(static model => IsNt91Model(model.ModelKey));
            if (nt91 is not null)
            {
                Models.Add(new MauiModel(
                    TargetModelKey,
                    string.IsNullOrWhiteSpace(nt91.ModelName) ? TargetModelKey : nt91.ModelName));
            }
            SelectedModel = Models.FirstOrDefault();
            SdkVersion = string.IsNullOrWhiteSpace(backend.Version) ? "Unknown" : backend.Version;
            isInitialized = true;
            canRetry = false;
            discoveryAttempted = false;
            Status = Models.Count == 0
                ? "NT-91 USB HID is not available in this SDK build"
                : "Ready to refresh NT-91 devices";
            RaiseStateChanged();
        }, "SDK initialization failed", () => initializationFailed = true).ConfigureAwait(true);
    }

    internal async Task RefreshDevicesAsync()
    {
        if (SelectedModel is null || session is not null)
        {
            return;
        }
        await RunExclusiveAsync(async () =>
        {
            Devices.Clear();
            discoveryAttempted = true;
            RaiseDeviceCollectionChanged();
            canRetry = false;
            LastAction = "";
            Status = "Refreshing NT-91 USB HID devices";
            await backend.StartDiscoveryAsync(TargetModelKey, lifetime.Token).ConfigureAwait(true);
            Status = Devices.Count == 0
                ? "No NT-91 USB HID scanners found"
                : $"Found {Devices.Count} NT-91 scanner{(Devices.Count == 1 ? "" : "s")}";
            canRetry = Devices.Count == 0;
            RaiseStateChanged();
        }, "Device refresh failed").ConfigureAwait(true);
    }

    internal async Task ConnectAsync(MauiDevice? device)
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
                TryLoadSettings(connected);
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

    internal Task ClearScansAsync()
    {
        Scans.Clear();
        Interlocked.Exchange(ref scanSequence, 0);
        LastAction = "In-memory scan history cleared";
        RaiseScanCollectionChanged();
        return Task.CompletedTask;
    }

    internal Task ReloadSettingsAsync()
    {
        var active = session;
        if (active is null || !IsReady)
        {
            SettingsStatus = "Connect a ready scanner to load its supported settings";
            return Task.CompletedTask;
        }
        return RunExclusiveAsync(() =>
        {
            LoadSettings(active.GetSettingDefinitions());
            LastAction = "Supported settings reloaded";
            return Task.CompletedTask;
        }, "Settings reload failed");
    }

    internal Task ReadSettingAsync(MauiSettingRow? row)
    {
        var active = session;
        if (row is null || active is null || !IsReady || !row.SupportsRead)
        {
            return Task.CompletedTask;
        }
        return RunExclusiveAsync(async () =>
        {
            row.Status = "Reading";
            try
            {
                var value = await active.ReadSettingAsync(row.Definition, lifetime.Token).ConfigureAwait(true);
                row.CurrentValue = value;
                row.WriteValue = value;
                row.Status = "Read completed";
                LastAction = $"Read {row.DisplayName}";
            }
            catch
            {
                row.Status = "Read failed";
                throw;
            }
        }, "Setting read failed");
    }

    internal Task WriteSettingAsync(MauiSettingRow? row)
    {
        var active = session;
        if (row is null || active is null || !IsReady || !row.SupportsWrite)
        {
            return Task.CompletedTask;
        }
        if (row.RequiresRiskConfirmation && !row.RiskConfirmed)
        {
            row.Status = "Confirm the setting risk before writing";
            LastAction = row.Status;
            return Task.CompletedTask;
        }
        return RunExclusiveAsync(async () =>
        {
            row.Status = "Writing";
            try
            {
                var result = await active.WriteSettingAsync(
                    row.Definition,
                    row.WriteValue,
                    row.PersistWrite,
                    lifetime.Token).ConfigureAwait(true);
                row.Status = result;
                if (row.SupportsRead)
                {
                    row.CurrentValue = await active.ReadSettingAsync(row.Definition, lifetime.Token).ConfigureAwait(true);
                    row.WriteValue = row.CurrentValue;
                }
                LastAction = $"Wrote {row.DisplayName}{(row.PersistWrite ? " and requested persistence" : " for this session")}";
            }
            catch
            {
                row.Status = "Write failed";
                throw;
            }
        }, "Setting write failed");
    }

    internal Task ReadAllSettingsAsync()
    {
        var active = session;
        if (active is null || !IsReady)
        {
            return Task.CompletedTask;
        }
        return RunExclusiveAsync(async () =>
        {
            var succeeded = 0;
            var failed = 0;
            foreach (var row in Settings.Where(static setting => setting.SupportsRead).ToArray())
            {
                try
                {
                    row.Status = "Reading";
                    row.CurrentValue = await active.ReadSettingAsync(row.Definition, lifetime.Token).ConfigureAwait(true);
                    row.WriteValue = row.CurrentValue;
                    row.Status = "Read completed";
                    succeeded++;
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    row.Status = "Read failed";
                    failed++;
                }
            }
            SettingsStatus = failed == 0
                ? $"Read {succeeded} supported settings"
                : $"Read {succeeded} settings; {failed} failed";
            LastAction = SettingsStatus;
        }, "Read all settings failed");
    }

    internal Task ClearSettingSearchAsync()
    {
        SettingSearchText = string.Empty;
        return Task.CompletedTask;
    }

    internal MauiSettingGroup? GetSettingGroupForVisibleItemIndex(int index)
    {
        if (index < 0)
        {
            return null;
        }
        var offset = 0;
        foreach (var group in SettingGroups)
        {
            if (index < offset + group.Count)
            {
                return group;
            }
            offset += group.Count;
        }
        return null;
    }

    internal Task ShutdownAsync()
    {
        lock (shutdownGate)
        {
            return shutdownTask ??= ShutdownCoreAsync();
        }
    }

    private async Task ShutdownCoreAsync()
    {
        Interlocked.Exchange(ref shutdownStarted, 1);
        RaiseStateChanged();
        ClearSettings("Session closed; setting values cleared from memory");
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

    private async Task RunSessionCommandAsync(
        bool supported,
        string unsupportedMessage,
        Func<IMauiSession, Task> operation)
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

    private async Task RunExclusiveAsync(
        Func<Task> operation,
        string failurePrefix,
        Action? handleFailure = null)
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
            handleFailure?.Invoke();
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

    private void HandleCommandError(Exception error) => PostToUi(() =>
    {
        var message = $"Command failed: {Sanitize(error.Message)}";
        Status = message;
        LastAction = message;
        RaiseStateChanged();
    });

    private void AttachSession(IMauiSession active)
    {
        active.StateChanged += HandleSessionState;
        active.ScanReceived += HandleScan;
        active.FailureReceived += HandleSessionFailure;
    }

    private void DetachSession(IMauiSession active)
    {
        active.StateChanged -= HandleSessionState;
        active.ScanReceived -= HandleScan;
        active.FailureReceived -= HandleSessionFailure;
    }

    private void ClearSession(IMauiSession active, string nextStatus, bool retry)
    {
        if (!ReferenceEquals(session, active))
        {
            return;
        }
        DetachSession(active);
        session = null;
        operationSupport = MauiOperationSupport.None;
        IsReady = false;
        ConnectedDeviceName = "Not connected";
        ResolvedModel = "—";
        ClearSettings("Connect a ready scanner to load its supported settings");
        canRetry = retry;
        Status = nextStatus;
        RaiseStateChanged();
    }

    private bool CanReadSetting(MauiSettingRow row) =>
        IsReady && !IsBusy && row.SupportsRead;

    private bool CanWriteSetting(MauiSettingRow row) =>
        IsReady && !IsBusy && row.SupportsWrite;

    private void TryLoadSettings(IMauiSession active)
    {
        try
        {
            LoadSettings(active.GetSettingDefinitions());
        }
        catch (Exception error)
        {
            ClearSettings($"Unable to load supported settings: {Sanitize(error.Message)}");
        }
    }

    private void LoadSettings(IReadOnlyList<MauiSettingDefinition> definitions)
    {
        Settings.Clear();
        var rows = definitions
            .DistinctBy(static definition => definition.EntryKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static definition => definition.DomainSortOrder)
            .ThenBy(static definition => definition.DomainDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static definition => definition.GroupSummary, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(static definition => new MauiSettingRow(definition))
            .ToArray();
        foreach (var row in rows)
        {
            Settings.Add(row);
        }
        RebuildSettingGroups();
        SettingsStatus = Settings.Count == 0
            ? "This ready session reports no readable or writable settings"
            : "Supported settings grouped by first-level capability domain";
        RaiseSettingsCollectionChanged();
    }

    private void ClearSettings(string nextStatus)
    {
        Settings.Clear();
        SettingGroups.Clear();
        SelectedSettingGroup = null;
        SettingsStatus = nextStatus;
        RaiseSettingsCollectionChanged();
    }

    private void RebuildSettingGroups()
    {
        var selectedDomainKey = SelectedSettingGroup?.DomainKey;
        var query = SettingSearchText.Trim();
        var visibleRows = string.IsNullOrEmpty(query)
            ? Settings.AsEnumerable()
            : Settings.Where(row => MatchesSettingSearch(row, query));
        var groupedRows = visibleRows
            .GroupBy(static row => row.Definition.DomainKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var first = group.First();
                return new MauiSettingGroup(
                    first.Definition.DomainKey,
                    first.Definition.DomainDisplayName,
                    first.Definition.DomainSortOrder,
                    group);
            })
            .OrderBy(static group => group.SortOrder)
            .ThenBy(static group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SettingGroups.Clear();
        foreach (var group in groupedRows)
        {
            SettingGroups.Add(group);
        }
        SelectedSettingGroup = SettingGroups.FirstOrDefault(group =>
                string.Equals(group.DomainKey, selectedDomainKey, StringComparison.OrdinalIgnoreCase))
            ?? SettingGroups.FirstOrDefault();
        RaiseSettingsCollectionChanged();
    }

    private static bool MatchesSettingSearch(MauiSettingRow row, string query) =>
        row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        row.Definition.DomainDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        row.GroupSummary.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void HandleDiscovery(MauiDevice device) => PostToUi(() =>
    {
        if (!IsNt91Model(device.ModelKey))
        {
            return;
        }
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

    private static bool IsNt91Model(string? modelKey) =>
        string.Equals(NormalizeModelKey(modelKey), "NT91", StringComparison.Ordinal);

    private static string NormalizeModelKey(string? modelKey) => string.IsNullOrWhiteSpace(modelKey)
        ? string.Empty
        : new string(modelKey.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

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

    private void HandleScan(MauiScanEvent scan) => PostToUi(() =>
    {
        Scans.Insert(0, new MauiScanRow(
            Interlocked.Increment(ref scanSequence),
            scan.BarcodeType,
            scan.Text));
        while (Scans.Count > MaxScans)
        {
            Scans.RemoveAt(Scans.Count - 1);
        }
        RaiseScanCollectionChanged();
    });

    private void HandleSessionFailure(string message) => PostToUi(() =>
    {
        Status = Sanitize(message);
        LastAction = Status;
    });

    private void PostToUi(Action action)
    {
        if (Volatile.Read(ref shutdownStarted) != 0)
        {
            return;
        }
        dispatcher.Dispatch(() =>
        {
            if (Volatile.Read(ref shutdownStarted) == 0)
            {
                action();
            }
        });
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
        NotifyPropertyChanged(nameof(DeviceEmptyMessage));
        RaiseStateChanged();
    }

    private void RaiseScanCollectionChanged()
    {
        NotifyPropertyChanged(nameof(HasScans));
        NotifyPropertyChanged(nameof(HasNoScans));
        NotifyPropertyChanged(nameof(ScanCountText));
        RaiseStateChanged();
    }

    private void RaiseSettingsCollectionChanged()
    {
        NotifyPropertyChanged(nameof(HasSettings));
        NotifyPropertyChanged(nameof(HasNoSettings));
        NotifyPropertyChanged(nameof(HasVisibleSettings));
        NotifyPropertyChanged(nameof(HasNoVisibleSettings));
        NotifyPropertyChanged(nameof(SettingsEmptyMessage));
        NotifyPropertyChanged(nameof(SettingsSummary));
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        NotifyPropertyChanged(nameof(CanRefreshDevices));
        NotifyPropertyChanged(nameof(CanInitialize));
        NotifyPropertyChanged(nameof(CanConnect));
        NotifyPropertyChanged(nameof(CanDisconnect));
        NotifyPropertyChanged(nameof(CanTriggerScan));
        NotifyPropertyChanged(nameof(CanRefreshInfo));
        NotifyPropertyChanged(nameof(CanReadBattery));
        NotifyPropertyChanged(nameof(CanClearScans));
        NotifyPropertyChanged(nameof(CanReloadSettings));
        NotifyPropertyChanged(nameof(CanReadAllSettings));
        NotifyPropertyChanged(nameof(RefreshButtonText));
        NotifyPropertyChanged(nameof(HasInitializationError));
        NotifyPropertyChanged(nameof(DeviceEmptyMessage));
        NotifyPropertyChanged(nameof(HasLastAction));
        initializeCommand.RaiseCanExecuteChanged();
        refreshDevicesCommand.RaiseCanExecuteChanged();
        connectCommand.RaiseCanExecuteChanged();
        disconnectCommand.RaiseCanExecuteChanged();
        triggerScanCommand.RaiseCanExecuteChanged();
        refreshInfoCommand.RaiseCanExecuteChanged();
        readBatteryCommand.RaiseCanExecuteChanged();
        clearScansCommand.RaiseCanExecuteChanged();
        reloadSettingsCommand.RaiseCanExecuteChanged();
        readAllSettingsCommand.RaiseCanExecuteChanged();
        readSettingCommand.RaiseCanExecuteChanged();
        writeSettingCommand.RaiseCanExecuteChanged();
    }
}
