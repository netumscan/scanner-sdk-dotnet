using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const int DiscoveryWorkspaceIndex = 0;
    private const int ConsoleWorkspaceIndex = 1;
    private const int LogsWorkspaceIndex = 2;
    private const int ScanConsoleTabIndex = 0;
    private const int ActionsConsoleTabIndex = 0;
    private const int CapabilitiesConsoleTabIndex = 1;
    private const int DataRulesConsoleTabIndex = 2;
    private const int DiagnosticsConsoleTabIndex = 3;

    private readonly IDemoSdkBackend backend;
    private readonly ISerialPortCatalog serialPortCatalog;
    private readonly DemoSessionCoordinator sessionCoordinator;
    private readonly ObservableCollection<SettingItem> settings = [];
    private readonly ICollectionView settingsView;
    private readonly Dispatcher uiDispatcher;
    private readonly List<ScannerSdkLib.CommandTrace> commandTraceHistory = [];
    private readonly Dictionary<string, CapabilityCatalogSnapshot> capabilityCatalogCache = [];
    private readonly DemoCommandRunner sessionCommandRunner = new();
    private readonly string nativeDllStatus;

    private ScannerSdkLib.DeviceCapabilitySummary? publicCapabilitySummary;
    private ScannerSdkLib.DeviceCapabilitySummary? selectedModelCapabilitySummary;
    private ScannerSdkLib.SessionOperationSupport? operationSupport;
    private DemoDeviceItem? selectedDiscoveryDevice;
    private SettingItem? selectedSettingItem;
    private ModelOption selectedModelOption;
    private TransportOption selectedTransportOption;
    private CharsetOption selectedCharsetOption;
    private TerminatorOption selectedTerminatorOption;
    private DataRuleModeOption selectedDataRuleModeOption;
    private SettingsSourceFilter selectedSettingsSourceFilter = SettingsSourceFilter.All;
    private string selectedSettingGroup = "All";
    private string settingFilterText = string.Empty;
    private string sdkVersion = "<not initialized>";
    private string sessionStateText = "Not connected";
    private string selectedDeviceSummary = "No device selected";
    private string selectedModelSummary = "Waiting for SDK model catalog";
    private string resolvedModelSummary = "Not connected";
    private string capabilitySummaryText = "Not connected";
    private string operationSupportSummary = "Not connected";
    private string diagnosticsSummary = "SDK not initialized";
    private string recentFailureText = "<none>";
    private string discoveryHintText = "Select a model and transport, then search devices.";
    private string lastResultText = string.Empty;
    private string lastScanText = "No scan received yet";
    private string lastScanRawHex = string.Empty;
    private string lastScanMetadata = "Waiting for scan data";
    private string dataRuleValueA = string.Empty;
    private string dataRuleValueB = string.Empty;
    private string deviceInfoText = "<not loaded>";
    private string batteryText = "<not loaded>";
    private string commandTraceText = string.Empty;
    private int scanCount;
    private int visibleSettingsCount;
    private int selectedWorkspaceIndex;
    private int selectedConsoleTabIndex;
    private bool isConnected;
    private bool isReady;
    private bool enableUsbHidTrace = true;
    private bool isLoadingSupportedModels;
    private string supportedModelsError = string.Empty;
    private bool isLoadingCapabilityCatalog;
    private string capabilityCatalogError = string.Empty;
    private CapabilityCatalogDomainItem? selectedCapabilityDomain;
    private DemoLanguageOption selectedLanguageOption;
    private int capabilityCatalogGeneration;
    private bool includeSensitivePayloadsInExport;
    private DemoWorkflowState workflowState = DemoWorkflowState.NotInitialized;
    private DemoFailure? recentFailure;
    private string manualSerialPort = string.Empty;

    public MainWindowViewModel()
        : this(CreateDefaultBackend(), new SystemSerialPortCatalog())
    {
    }

    internal MainWindowViewModel(IDemoSdkBackend backend, ISerialPortCatalog serialPortCatalog)
    {
        this.backend = backend;
        this.serialPortCatalog = serialPortCatalog;
        nativeDllStatus = backend.IsFake ? "not required (Fake Mode)" : DemoNativeDllProbe.Probe();
        sessionCoordinator = new DemoSessionCoordinator(backend);
        uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        ModelOptions = new ObservableCollection<ModelOption>(BuildInitialModelOptions());
        TransportOptions = new ObservableCollection<TransportOption>(DemoCommandCatalog.BuildTransportOptions(backend));
        CharsetOptions = new ObservableCollection<CharsetOption>(DemoCommandCatalog.BuildCharsetOptions());
        TerminatorOptions = new ObservableCollection<TerminatorOption>(DemoCommandCatalog.BuildTerminatorOptions());
        DataRuleModeOptions = new ObservableCollection<DataRuleModeOption>(DemoCommandCatalog.BuildDataRuleModeOptions());
        LanguageOptions = new ObservableCollection<DemoLanguageOption>(BuildLanguageOptions());
        CapabilityDomains = [];
        OperationGroups = [];
        SettingsSourceFilters = Enum.GetValues<SettingsSourceFilter>();
        DiscoveryDevices = [];
        SerialPorts = [];
        SettingGroups = ["All"];
        selectedModelOption = ModelOptions.First();
        selectedTransportOption = TransportOptions.First();
        selectedCharsetOption = CharsetOptions.First(static option => option.Charset == ScannerSdkLib.ScanTextCharset.Utf8);
        selectedTerminatorOption = TerminatorOptions.First(static option => option.Hex == "0D");
        selectedDataRuleModeOption = DataRuleModeOptions.First(static option => option.Mode == DataRuleMode.Prefix);
        selectedLanguageOption = LanguageOptions.First(static option => option.Tag == "system");
        selectedModelSummary = $"Customer selected: {selectedModelOption.DisplayName}";
        discoveryHintText = FormatDiscoveryHint();

        settingsView = CollectionViewSource.GetDefaultView(settings);
        settingsView.Filter = FilterSetting;

        InitializeSdkCommand = new AsyncRelayCommand(InitializeSdkAsync);
        SearchDevicesCommand = new AsyncRelayCommand(SearchDevicesAsync);
        StopDiscoveryCommand = new AsyncRelayCommand(StopDiscoveryAsync);
        ConnectSelectedDeviceCommand = new AsyncRelayCommand(ConnectSelectedDeviceAsync);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        BackToDiscoveryCommand = new RelayCommand(() => SelectedWorkspaceIndex = DiscoveryWorkspaceIndex);
        OpenDiscoveryCommand = new RelayCommand(() => SelectedWorkspaceIndex = DiscoveryWorkspaceIndex);
        OpenConsoleCommand = new RelayCommand(() => SelectedWorkspaceIndex = ConsoleWorkspaceIndex);
        OpenLogsCommand = new RelayCommand(() => SelectedWorkspaceIndex = LogsWorkspaceIndex);
        OpenScanWorkspaceCommand = new RelayCommand(() => SelectConsoleTab(ScanConsoleTabIndex));
        OpenActionsCommand = new RelayCommand(() => SelectConsoleTab(ActionsConsoleTabIndex));
        OpenMasterSettingsCommand = new RelayCommand(() => SelectConsoleTab(CapabilitiesConsoleTabIndex));
        OpenModuleSettingsCommand = new RelayCommand(() => SelectConsoleTab(CapabilitiesConsoleTabIndex));
        OpenDataRulesCommand = new RelayCommand(() => SelectConsoleTab(DataRulesConsoleTabIndex));
        OpenDiagnosticsCommand = new RelayCommand(() => SelectConsoleTab(DiagnosticsConsoleTabIndex));
        PresetQuickScanCommand = new AsyncRelayCommand(PresetQuickScanAsync);
        PresetInfoReadCommand = new AsyncRelayCommand(PresetInfoReadAsync);
        PresetBatteryReadCommand = new AsyncRelayCommand(PresetBatteryReadAsync);
        PresetMasterCommandsCommand = new RelayCommand(() => SelectConsoleTab(ActionsConsoleTabIndex));
        PresetProductSettingsCommand = new RelayCommand(() => SelectConsoleTab(CapabilitiesConsoleTabIndex));
        PresetDataRulesCommand = new RelayCommand(() => SelectConsoleTab(DataRulesConsoleTabIndex));
        RefreshInfoCommand = new AsyncRelayCommand(RefreshInfoAsync);
        GetBatteryCommand = new AsyncRelayCommand(GetBatteryAsync);
        GetStorageUsageCommand = new AsyncRelayCommand(GetStorageUsageAsync);
        InitializeSessionCommand = new AsyncRelayCommand(InitializeSessionAsync);
        TriggerScanCommand = new AsyncRelayCommand(() => RunSessionActionAsync("TriggerScan", active => active.TriggerScanAsync()));
        EnableAckBeepCommand = new AsyncRelayCommand(() => SetAckBeepEnabledAsync(true));
        DisableAckBeepCommand = new AsyncRelayCommand(() => SetAckBeepEnabledAsync(false));
        EnableVibrationCommand = new AsyncRelayCommand(() => SetVibrationEnabledAsync(true));
        DisableVibrationCommand = new AsyncRelayCommand(() => SetVibrationEnabledAsync(false));
        SetTimestampCommand = new AsyncRelayCommand(SetTimestampAsync);
        ExecuteDemoOperationCommand = new AsyncParameterRelayCommand(
            parameter => ExecuteDemoOperationAsync(parameter, secondaryAction: false),
            parameter => parameter is DemoOperationItem operation && operation.IsEnabled);
        ExecuteDemoOperationSecondaryCommand = new AsyncParameterRelayCommand(
            parameter => ExecuteDemoOperationAsync(parameter, secondaryAction: true),
            parameter => parameter is DemoOperationItem operation && operation.IsEnabled && operation.HasSecondaryAction);
        ExecuteCapabilityActionCommand = new AsyncParameterRelayCommand(
            ExecuteCapabilityActionEntryAsync,
            parameter => IsReady && !IsSessionCommandBusy && parameter is DemoDeviceActionItem
        );
        ApplyCharsetCommand = new AsyncRelayCommand(ApplyCharsetAsync);
        ApplyTerminatorCommand = new AsyncRelayCommand(ApplyTerminatorAsync);
        ApplyDataRuleCommand = new AsyncRelayCommand(ApplyDataRuleAsync);
        ReadCapabilityValueCommand = new AsyncParameterRelayCommand(ReadCapabilityValueEntryAsync);
        WriteCapabilityValueCommand = new AsyncParameterRelayCommand(WriteCapabilityValueEntryAsync);
        ReadVisibleSettingsCommand = new AsyncRelayCommand(ReadVisibleSettingsAsync);
        OpenCapabilityDomainCommand = new ParameterRelayCommand(OpenCapabilityDomain);
        BackToCapabilityDomainsCommand = new RelayCommand(() => SelectedCapabilityDomain = null);
        RetryCapabilityCatalogCommand = new AsyncRelayCommand(() => ReloadCapabilityCatalogAsync(force: true));
        RetrySupportedModelsCommand = new AsyncRelayCommand(ReloadSupportedModelsAsync);
        RefreshSerialPortsCommand = new RelayCommand(RefreshSerialPortsWithFeedback);
        ReadCapabilityDomainCommand = new AsyncParameterRelayCommand(ReadCapabilityDomainAsync);
        ClearLogsCommand = new RelayCommand(LogStore.Clear);
        CopyAllLogsCommand = new RelayCommand(() => CopyTextRequested?.Invoke(LogStore.ExportAll(IncludeSensitivePayloadsInExport)));
        CopyFilteredLogsCommand = new RelayCommand(() => CopyTextRequested?.Invoke(LogStore.ExportFiltered(IncludeSensitivePayloadsInExport)));
        ExportAllLogsCommand = new RelayCommand(() => SaveTextRequested?.Invoke(
            "scanner-sdk-wpf-all.log",
            LogStore.ExportAll(IncludeSensitivePayloadsInExport)));
        ExportFilteredLogsCommand = new RelayCommand(() => SaveTextRequested?.Invoke(
            "scanner-sdk-wpf-filtered.log",
            LogStore.ExportFiltered(IncludeSensitivePayloadsInExport)));
        ExportCompatibilityRecordCommand = new RelayCommand(() =>
            SaveTextRequested?.Invoke("scanner-sdk-wpf-compatibility.txt", BuildCompatibilityRecord()));

        backend.LogReceived += Sdk_LogReceived;
        backend.UsbHidTraceReceived += Sdk_UsbHidTraceReceived;
        backend.CommandTraceReceived += Sdk_CommandTraceReceived;
        sessionCommandRunner.BusyChanged += SessionCommandRunner_BusyChanged;
        RefreshDiagnostics();
        RefreshInteractionSurface();
    }

    private static IDemoSdkBackend CreateDefaultBackend()
    {
#if DEBUG
        if (string.Equals(
                Environment.GetEnvironmentVariable("SCANNER_SDK_WPF_FAKE"),
                "1",
                StringComparison.Ordinal))
        {
            return new FakeDemoSdkBackend();
        }
#endif
        return new ScannerSdkDemoBackend();
    }

    public event Action<string>? CopyTextRequested;

    public event Action<string, string>? SaveTextRequested;

    public Func<string, string, bool>? ConfirmDanger { get; set; }

    public DemoLogStore LogStore { get; } = new();

    public bool IsSessionCommandBusy => sessionCommandRunner.IsBusy;

    public bool IncludeSensitivePayloadsInExport
    {
        get => includeSensitivePayloadsInExport;
        set
        {
            if (SetProperty(ref includeSensitivePayloadsInExport, value))
            {
                LogStore.ShowSensitivePayloads = value;
            }
        }
    }

    public ObservableCollection<ModelOption> ModelOptions { get; }

    public ObservableCollection<TransportOption> TransportOptions { get; }

    public ObservableCollection<CharsetOption> CharsetOptions { get; }

    public ObservableCollection<TerminatorOption> TerminatorOptions { get; }

    public ObservableCollection<DataRuleModeOption> DataRuleModeOptions { get; }

    public ObservableCollection<DemoLanguageOption> LanguageOptions { get; }

    public ObservableCollection<CapabilityCatalogDomainItem> CapabilityDomains { get; }

    public IReadOnlyList<SettingsSourceFilter> SettingsSourceFilters { get; }

    public ObservableCollection<DemoDeviceItem> DiscoveryDevices { get; }

    public ObservableCollection<string> SerialPorts { get; }

    public ObservableCollection<DemoOperationGroup> OperationGroups { get; }

    public ObservableCollection<string> SettingGroups { get; }

    public ICollectionView SettingsView => settingsView;

    public int TotalSettingsCount => settings.Count;

    public AsyncRelayCommand InitializeSdkCommand { get; }

    public AsyncRelayCommand SearchDevicesCommand { get; }

    public AsyncRelayCommand StopDiscoveryCommand { get; }

    public AsyncRelayCommand ConnectSelectedDeviceCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public RelayCommand BackToDiscoveryCommand { get; }

    public RelayCommand OpenDiscoveryCommand { get; }

    public RelayCommand OpenConsoleCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public RelayCommand OpenScanWorkspaceCommand { get; }

    public RelayCommand OpenActionsCommand { get; }

    public RelayCommand OpenMasterSettingsCommand { get; }

    public RelayCommand OpenModuleSettingsCommand { get; }

    public RelayCommand OpenDataRulesCommand { get; }

    public RelayCommand OpenDiagnosticsCommand { get; }

    public AsyncRelayCommand PresetQuickScanCommand { get; }

    public AsyncRelayCommand PresetInfoReadCommand { get; }

    public AsyncRelayCommand PresetBatteryReadCommand { get; }

    public RelayCommand PresetMasterCommandsCommand { get; }

    public RelayCommand PresetProductSettingsCommand { get; }

    public RelayCommand PresetDataRulesCommand { get; }

    public AsyncRelayCommand RefreshInfoCommand { get; }

    public AsyncRelayCommand GetBatteryCommand { get; }

    public AsyncRelayCommand GetStorageUsageCommand { get; }

    public AsyncRelayCommand InitializeSessionCommand { get; }

    public AsyncRelayCommand TriggerScanCommand { get; }

    public AsyncRelayCommand EnableAckBeepCommand { get; }

    public AsyncRelayCommand DisableAckBeepCommand { get; }

    public AsyncRelayCommand EnableVibrationCommand { get; }

    public AsyncRelayCommand DisableVibrationCommand { get; }

    public AsyncRelayCommand SetTimestampCommand { get; }

    public AsyncParameterRelayCommand ExecuteDemoOperationCommand { get; }

    public AsyncParameterRelayCommand ExecuteDemoOperationSecondaryCommand { get; }

    public AsyncParameterRelayCommand ExecuteCapabilityActionCommand { get; }

    public AsyncRelayCommand ApplyCharsetCommand { get; }

    public AsyncRelayCommand ApplyTerminatorCommand { get; }

    public AsyncRelayCommand ApplyDataRuleCommand { get; }

    public AsyncParameterRelayCommand ReadCapabilityValueCommand { get; }

    public AsyncParameterRelayCommand WriteCapabilityValueCommand { get; }

    public AsyncRelayCommand ReadVisibleSettingsCommand { get; }

    public ParameterRelayCommand OpenCapabilityDomainCommand { get; }

    public RelayCommand BackToCapabilityDomainsCommand { get; }

    public AsyncRelayCommand RetryCapabilityCatalogCommand { get; }

    public AsyncRelayCommand RetrySupportedModelsCommand { get; }

    public RelayCommand RefreshSerialPortsCommand { get; }

    public AsyncParameterRelayCommand ReadCapabilityDomainCommand { get; }

    public RelayCommand ClearLogsCommand { get; }

    public RelayCommand CopyAllLogsCommand { get; }

    public RelayCommand CopyFilteredLogsCommand { get; }

    public RelayCommand ExportAllLogsCommand { get; }

    public RelayCommand ExportFilteredLogsCommand { get; }

    public RelayCommand ExportCompatibilityRecordCommand { get; }

    public ModelOption SelectedModelOption
    {
        get => selectedModelOption;
        set
        {
            if (value is null)
            {
                return;
            }
            if (IsTargetLocked)
            {
                return;
            }

            if (SetProperty(ref selectedModelOption, value))
            {
                SelectedModelSummary = string.IsNullOrWhiteSpace(value.ModelKey)
                    ? value.DisplayName
                    : $"Customer selected: {value.DisplayName}";
                DiscoveryHintText = FormatDiscoveryHint();
                if (sessionCoordinator.IsInitialized && !string.IsNullOrWhiteSpace(value.ModelKey))
                {
                    RefreshProductSurfaceForSelectedModel();
                    _ = ReloadCapabilityCatalogAsync();
                }
            }
        }
    }

    public TransportOption SelectedTransportOption
    {
        get => selectedTransportOption;
        set
        {
            if (value is null)
            {
                return;
            }
            if (IsTargetLocked)
            {
                return;
            }

            if (SetProperty(ref selectedTransportOption, value))
            {
                if (sessionCoordinator.IsInitialized)
                {
                    RefreshModelOptionsFromSdk();
                }
                if (value.TransportType == ScannerSdkLib.TransportType.UsbSerial)
                {
                    RefreshSerialPortsWithFeedback();
                }
                OnPropertyChanged(nameof(IsSerialTransport));
                DiscoveryHintText = FormatDiscoveryHint();
                RefreshDiagnostics();
                _ = ReloadCapabilityCatalogAsync();
            }
        }
    }

    public DemoLanguageOption SelectedLanguageOption
    {
        get => selectedLanguageOption;
        set
        {
            if (value is null)
            {
                return;
            }

            if (!SetProperty(ref selectedLanguageOption, value))
            {
                return;
            }

            DemoUiText.SetCulture(value.Tag);
            RefreshLanguageOptions();
            RefreshDataRuleOptionsFromSdk();
            capabilityCatalogCache.Clear();
            OnPropertyChanged(string.Empty);
            LogStore.RefreshLocalizedLabels();
            _ = ReloadCapabilityCatalogAsync(force: true);
        }
    }

    public CharsetOption SelectedCharsetOption
    {
        get => selectedCharsetOption;
        set
        {
            if (value is not null)
            {
                SetProperty(ref selectedCharsetOption, value);
            }
        }
    }

    public TerminatorOption SelectedTerminatorOption
    {
        get => selectedTerminatorOption;
        set
        {
            if (value is not null)
            {
                SetProperty(ref selectedTerminatorOption, value);
            }
        }
    }

    public DataRuleModeOption SelectedDataRuleModeOption
    {
        get => selectedDataRuleModeOption;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedDataRuleModeOption, value))
            {
                OnPropertyChanged(nameof(DataRuleValueBVisibility));
            }
        }
    }

    public SettingsSourceFilter SelectedSettingsSourceFilter
    {
        get => selectedSettingsSourceFilter;
        set
        {
            if (SetProperty(ref selectedSettingsSourceFilter, value))
            {
                RefreshSettingsView();
            }
        }
    }

    public string SelectedSettingGroup
    {
        get => selectedSettingGroup;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedSettingGroup, value))
            {
                RefreshSettingsView();
            }
        }
    }

    public string SettingFilterText
    {
        get => settingFilterText;
        set
        {
            if (SetProperty(ref settingFilterText, value))
            {
                RefreshSettingsView();
            }
        }
    }

    public DemoDeviceItem? SelectedDiscoveryDevice
    {
        get => selectedDiscoveryDevice;
        set
        {
            if (SetProperty(ref selectedDiscoveryDevice, value))
            {
                SelectedDeviceSummary = value is null
                    ? "No device selected"
                    : $"{value.DisplayName} / id={value.DeviceId}";
                RefreshInteractionSurface();
            }
        }
    }

    public string SdkVersion
    {
        get => sdkVersion;
        set => SetProperty(ref sdkVersion, value);
    }

    public string SessionStateText
    {
        get => sessionStateText;
        set => SetProperty(ref sessionStateText, value);
    }

    public string SelectedDeviceSummary
    {
        get => selectedDeviceSummary;
        set => SetProperty(ref selectedDeviceSummary, value);
    }

    public string SelectedModelSummary
    {
        get => selectedModelSummary;
        set => SetProperty(ref selectedModelSummary, value);
    }

    public string ResolvedModelSummary
    {
        get => resolvedModelSummary;
        set => SetProperty(ref resolvedModelSummary, value);
    }

    public string CapabilitySummaryText
    {
        get => capabilitySummaryText;
        set => SetProperty(ref capabilitySummaryText, value);
    }

    public string OperationSupportSummary
    {
        get => operationSupportSummary;
        set => SetProperty(ref operationSupportSummary, value);
    }

    public string DiagnosticsSummary
    {
        get => diagnosticsSummary;
        set => SetProperty(ref diagnosticsSummary, value);
    }

    public string RecentFailureText
    {
        get => recentFailureText;
        set
        {
            if (SetProperty(ref recentFailureText, value))
            {
                OnPropertyChanged(nameof(HasRecentFailure));
            }
        }
    }

    public string DiscoveryHintText
    {
        get => discoveryHintText;
        set => SetProperty(ref discoveryHintText, value);
    }

    public string LastResultText
    {
        get => lastResultText;
        set
        {
            if (SetProperty(ref lastResultText, value))
            {
                OnPropertyChanged(nameof(HasLastResult));
            }
        }
    }

    public string LastScanText
    {
        get => lastScanText;
        set => SetProperty(ref lastScanText, value);
    }

    public string LastScanRawHex
    {
        get => lastScanRawHex;
        set => SetProperty(ref lastScanRawHex, value);
    }

    public string LastScanMetadata
    {
        get => lastScanMetadata;
        set => SetProperty(ref lastScanMetadata, value);
    }

    public string DataRuleValueA
    {
        get => dataRuleValueA;
        set => SetProperty(ref dataRuleValueA, value);
    }

    public string DataRuleValueB
    {
        get => dataRuleValueB;
        set => SetProperty(ref dataRuleValueB, value);
    }

    public string DeviceInfoText
    {
        get => deviceInfoText;
        set => SetProperty(ref deviceInfoText, value);
    }

    public string BatteryText
    {
        get => batteryText;
        set => SetProperty(ref batteryText, value);
    }

    public string CommandTraceText
    {
        get => commandTraceText;
        set => SetProperty(ref commandTraceText, value);
    }

    public int ScanCount
    {
        get => scanCount;
        set
        {
            if (SetProperty(ref scanCount, value))
            {
                OnPropertyChanged(nameof(ScanWorkspaceScanCountText));
            }
        }
    }

    public int VisibleSettingsCount
    {
        get => visibleSettingsCount;
        private set => SetProperty(ref visibleSettingsCount, value);
    }

    public SettingItem? SelectedSettingItem
    {
        get => selectedSettingItem;
        set
        {
            if (SetProperty(ref selectedSettingItem, value))
            {
                RefreshSelectedSettingSummary();
            }
        }
    }

    public int SelectedWorkspaceIndex
    {
        get => selectedWorkspaceIndex;
        set => SetProperty(ref selectedWorkspaceIndex, value);
    }

    public int SelectedConsoleTabIndex
    {
        get => selectedConsoleTabIndex;
        set
        {
            if (SetProperty(ref selectedConsoleTabIndex, value))
            {
                OnPropertyChanged(nameof(IsCommonConsoleTab));
            }
        }
    }

    public bool IsCommonConsoleTab => SelectedConsoleTabIndex == ActionsConsoleTabIndex;

    public CapabilityCatalogDomainItem? SelectedCapabilityDomain
    {
        get => selectedCapabilityDomain;
        set
        {
            if (SetProperty(ref selectedCapabilityDomain, value))
            {
                OnPropertyChanged(nameof(IsCapabilityDomainListVisible));
                OnPropertyChanged(nameof(IsCapabilityDomainDetailVisible));
                OnPropertyChanged(nameof(CapabilityDomainDetailSummary));
            }
        }
    }

    public bool IsCapabilityDomainListVisible => SelectedCapabilityDomain is null;

    public bool IsCapabilityDomainDetailVisible => SelectedCapabilityDomain is not null;

    public bool IsLoadingCapabilityCatalog
    {
        get => isLoadingCapabilityCatalog;
        private set
        {
            if (SetProperty(ref isLoadingCapabilityCatalog, value))
            {
                OnPropertyChanged(nameof(IsCapabilityCatalogEmpty));
            }
        }
    }

    public string CapabilityCatalogError
    {
        get => capabilityCatalogError;
        private set
        {
            if (SetProperty(ref capabilityCatalogError, value))
            {
                OnPropertyChanged(nameof(HasCapabilityCatalogError));
                OnPropertyChanged(nameof(IsCapabilityCatalogEmpty));
            }
        }
    }

    public bool HasCapabilityCatalogError => !string.IsNullOrWhiteSpace(CapabilityCatalogError);

    public bool IsCapabilityCatalogEmpty => !IsLoadingCapabilityCatalog && !HasCapabilityCatalogError && CapabilityDomains.Count == 0;

    public bool IsLoadingSupportedModels
    {
        get => isLoadingSupportedModels;
        private set
        {
            if (SetProperty(ref isLoadingSupportedModels, value))
            {
                OnPropertyChanged(nameof(SupportedModelsStatusText));
                OnPropertyChanged(nameof(CanStartDiscoverySurface));
            }
        }
    }

    public string SupportedModelsError
    {
        get => supportedModelsError;
        private set
        {
            if (SetProperty(ref supportedModelsError, value))
            {
                OnPropertyChanged(nameof(HasSupportedModelsError));
                OnPropertyChanged(nameof(SupportedModelsStatusText));
            }
        }
    }

    public bool HasSupportedModelsError => !string.IsNullOrWhiteSpace(SupportedModelsError);

    public string SupportedModelsStatusText => IsLoadingSupportedModels
        ? DemoUiText.Get("models_loading")
        : SupportedModelsError;

    public string CapabilityCatalogSummary => string.Format(
        DemoUiText.Get("capability_catalog_summary"),
        CapabilityDomains.Count,
        CapabilityDomains.Sum(static domain => domain.Settings.Count),
        CapabilityDomains.Sum(static domain => domain.Actions.Count));

    public string CapabilityDomainDetailSummary => SelectedCapabilityDomain is null
        ? string.Empty
        : string.Format(
            DemoUiText.Get("capability_domain_detail_summary"),
            SelectedCapabilityDomain.Title,
            SelectedCapabilityDomain.Sections.Count,
            SelectedCapabilityDomain.Settings.Count,
            SelectedCapabilityDomain.ActionCount);

    public bool IsConnected
    {
        get => isConnected;
        set
        {
            if (SetProperty(ref isConnected, value))
            {
                OnPropertyChanged(nameof(IsDisconnected));
            }
        }
    }

    public bool IsDisconnected => !IsConnected;

    public DemoWorkflowState WorkflowState
    {
        get => workflowState;
        private set
        {
            if (SetProperty(ref workflowState, value))
            {
                OnPropertyChanged(nameof(IsTargetLocked));
                OnPropertyChanged(nameof(CanStartDiscoverySurface));
                OnPropertyChanged(nameof(CanStopDiscoverySurface));
                OnPropertyChanged(nameof(CanConnectSelectedDeviceSurface));
                RefreshDiagnostics();
            }
        }
    }

    public bool IsTargetLocked =>
        WorkflowState is DemoWorkflowState.Discovering
            or DemoWorkflowState.Connecting
            or DemoWorkflowState.Connected
            or DemoWorkflowState.Disconnecting
            or DemoWorkflowState.ShuttingDown;

    public bool IsFakeMode => backend.IsFake;

    public bool IsSerialTransport =>
        SelectedTransportOption.TransportType == ScannerSdkLib.TransportType.UsbSerial;

    public string ManualSerialPort
    {
        get => manualSerialPort;
        set
        {
            if (SetProperty(ref manualSerialPort, value) &&
                IsSerialTransport &&
                SystemSerialPortCatalog.TryNormalize(value, out var normalized))
            {
                SelectSerialPort(normalized, isEnumerated: SerialPorts.Contains(normalized));
            }
        }
    }

    public DemoFailure? RecentFailure => recentFailure;

    public bool IsReady
    {
        get => isReady;
        set => SetProperty(ref isReady, value);
    }

    public bool EnableUsbHidTrace
    {
        get => enableUsbHidTrace;
        set
        {
            if (SetProperty(ref enableUsbHidTrace, value))
            {
                Environment.SetEnvironmentVariable("NSDK_USB_HID_TRACE", value ? "1" : null);
                LogStore.Append(DemoLogSource.UI, DemoLogLevel.Info, $"USB HID trace {(value ? "enabled" : "disabled")}");
                RefreshDiagnostics();
            }
        }
    }

    public Visibility DataRuleValueBVisibility =>
        SelectedDataRuleModeOption.Mode is DataRuleMode.HideMiddle or DataRuleMode.Replace
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string SettingsWorkspaceSummary =>
        $"source={SelectedSettingsSourceFilter} group={SelectedSettingGroup} query={FormatFilterQuery()} visible={VisibleSettingsCount}/{TotalSettingsCount}";

    public bool HasActiveSession => sessionCoordinator.ActiveSession is not null && IsConnected;

    public bool CanInitializeSdkSurface =>
        !sessionCoordinator.IsInitialized || !string.IsNullOrWhiteSpace(SdkVersion);

    public bool CanStartDiscoverySurface =>
        sessionCoordinator.IsInitialized &&
        WorkflowState == DemoWorkflowState.Idle &&
        !IsLoadingSupportedModels &&
        !string.IsNullOrWhiteSpace(SelectedModelOption.ModelKey) &&
        !HasActiveSession;

    public bool CanStopDiscoverySurface =>
        sessionCoordinator.IsInitialized &&
        WorkflowState == DemoWorkflowState.Discovering &&
        !HasActiveSession;

    public bool CanConnectSelectedDeviceSurface =>
        sessionCoordinator.IsInitialized &&
        WorkflowState == DemoWorkflowState.Idle &&
        SelectedDiscoveryDevice is not null &&
        !HasActiveSession;

    public bool CanOpenConsoleSurface => HasActiveSession;

    public bool CanDisconnectSurface => HasActiveSession;

    public bool CanInitializeSessionSurface => CanExecuteOperation(DemoOperationKind.InitializeSession);

    public bool CanRefreshInfoSurface => CanExecuteOperation(DemoOperationKind.RefreshInfo);

    public bool CanGetBatterySurface => CanExecuteOperation(DemoOperationKind.ReadBattery);

    public bool CanGetMemorySurface => CanExecuteOperation(DemoOperationKind.ReadStorageUsage);

    public bool CanTriggerScanSurface => CanExecuteOperation(DemoOperationKind.TriggerScan);

    public bool CanSetTimestampSurface => CanExecuteOperation(DemoOperationKind.SetCurrentTimestamp);

    public bool CanAdjustFeedbackSurface => CanSetAckBeepSurface || CanSetVibrationSurface;

    public bool CanSetAckBeepSurface => CanExecuteOperation(DemoOperationKind.SetAckBeep);

    public bool CanSetVibrationSurface => CanExecuteOperation(DemoOperationKind.SetVibration);

    public bool CanOpenSettingsSurface =>
        IsReady && !IsSessionCommandBusy &&
        ((publicCapabilitySummary?.SupportsSettingsRead ?? false) || (publicCapabilitySummary?.SupportsSettingsWrite ?? false));

    public bool CanReadSettingsSurface =>
        IsReady && !IsSessionCommandBusy &&
        (publicCapabilitySummary?.SupportsSettingsRead ?? false);

    public bool CanWriteSettingsSurface =>
        IsReady && !IsSessionCommandBusy &&
        (publicCapabilitySummary?.SupportsSettingsWrite ?? false);

    public bool CanReadVisibleSettingsSurface => CanReadSettingsSurface && VisibleSettingsCount > 0;

    public bool CanExecuteDataRuleSurface =>
        IsReady && !IsSessionCommandBusy &&
        (operationSupport?.SupportsApplyDataRule ?? false) &&
        (publicCapabilitySummary?.SupportsDataRules ?? false);

    public string InteractionHintText => BuildInteractionHint();

    public string SettingsCapabilityHintText => BuildSettingsCapabilityHint();

    public string QuickScenarioHintText => BuildQuickScenarioHint();

    public bool HasOperationGroups => OperationGroups.Count > 0;

    public bool IsOperationCatalogEmpty => !HasOperationGroups;

    public string DynamicOperationTitle => DemoUiText.Get("dynamic_operation_title");

    public string DynamicOperationCaption => DemoUiText.Get("dynamic_operation_caption");

    public string DynamicOperationEmptyText => DemoUiText.Get("dynamic_operation_empty");

    public string ActionsQuickTitle => DemoUiText.Get("actions_quick_title");

    public string ActionsQuickCaption => DemoUiText.Get("actions_quick_caption");

    public string ActionsQuickScanTitle => DemoUiText.Get("actions_quick_scan_title");

    public string ActionsQuickScanCaption => DemoUiText.Get("actions_quick_scan_caption");

    public string ActionsInfoTitle => DemoUiText.Get("actions_info_title");

    public string ActionsInfoCaption => DemoUiText.Get("actions_info_caption");

    public string ActionsBatteryTitle => DemoUiText.Get("actions_battery_title");

    public string ActionsBatteryCaption => DemoUiText.Get("actions_battery_caption");

    public string ActionsAdvancedTitle => DemoUiText.Get("actions_advanced_title");

    public string ActionsAdvancedCaption => DemoUiText.Get("actions_advanced_caption");

    public string ActionsScanOperationsTitle => DemoUiText.Get("actions_scan_ops_title");

    public string ActionsScanOperationsCaption => DemoUiText.Get("actions_scan_ops_caption");

    public string ActionsFeedbackTitle => DemoUiText.Get("actions_feedback_title");

    public string ActionsFeedbackCaption => DemoUiText.Get("actions_feedback_caption");

    public string ActionsNotesTitle => DemoUiText.Get("actions_notes_title");

    public string ActionsNotesBody => DemoUiText.Get("actions_notes_body");

    public string ActionsNotesWarning => DemoUiText.Get("actions_notes_warning");

    public string ActionsQuickScanButtonText => DemoUiText.Get("actions_button_quick_scan");

    public string ActionsInfoButtonText => DemoUiText.Get("actions_button_info");

    public string ActionsBatteryButtonText => DemoUiText.Get("actions_button_battery");

    public string ActionsStorageTitle => DemoUiText.Get("actions_storage_title");

    public string ActionsStorageCaption => DemoUiText.Get("actions_storage_caption");

    public string ActionsStorageUsageButtonText => DemoUiText.Get("actions_button_memory_usage");

    public string ActionsProductSettingsButtonText => DemoUiText.Get("actions_button_product_settings");

    public string ActionsDataRuleButtonText => DemoUiText.Get("actions_button_data_rule_builder");

    public string ActionsTriggerScanButtonText => DemoUiText.Get("actions_button_trigger_scan");

    public string ActionsTimestampButtonText => DemoUiText.Get("actions_button_timestamp");

    public string ActionsAckOnButtonText => DemoUiText.Get("actions_button_ack_on");

    public string ActionsAckOffButtonText => DemoUiText.Get("actions_button_ack_off");

    public string ActionsVibrationOnButtonText => DemoUiText.Get("actions_button_vibration_on");

    public string ActionsVibrationOffButtonText => DemoUiText.Get("actions_button_vibration_off");

    public string ScanWorkspaceTitle => DemoUiText.Get("scan_workspace_title");

    public string ScanWorkspaceCaption => DemoUiText.Get("scan_workspace_caption");

    public string ScanWorkspaceInitializeButtonText => DemoUiText.Get("scan_workspace_button_initialize");

    public string ScanWorkspaceRefreshInfoButtonText => DemoUiText.Get("scan_workspace_button_refresh_info");

    public string ScanWorkspaceBatteryButtonText => DemoUiText.Get("scan_workspace_button_battery");

    public string ScanWorkspaceTriggerScanButtonText => DemoUiText.Get("scan_workspace_button_trigger_scan");

    public string ScanWorkspaceTimestampButtonText => DemoUiText.Get("scan_workspace_button_timestamp");

    public string ScanWorkspaceAckOnButtonText => DemoUiText.Get("scan_workspace_button_ack_on");

    public string ScanWorkspaceAckOffButtonText => DemoUiText.Get("scan_workspace_button_ack_off");

    public string ScanWorkspaceVibrationOnButtonText => DemoUiText.Get("scan_workspace_button_vibration_on");

    public string ScanWorkspaceVibrationOffButtonText => DemoUiText.Get("scan_workspace_button_vibration_off");

    public string ScanWorkspaceLastScanTitle => DemoUiText.Get("scan_workspace_last_scan_title");

    public string ScanWorkspaceScanCountText => string.Format(DemoUiText.Get("scan_workspace_scan_count"), ScanCount);

    public string ScanWorkspaceParseTitle => DemoUiText.Get("scan_workspace_parse_title");

    public string ScanWorkspaceCharsetLabel => DemoUiText.Get("scan_workspace_charset_label");

    public string ScanWorkspaceApplyCharsetButtonText => DemoUiText.Get("scan_workspace_button_apply_charset");

    public string ScanWorkspaceTerminatorLabel => DemoUiText.Get("scan_workspace_terminator_label");

    public string ScanWorkspaceApplyTerminatorButtonText => DemoUiText.Get("scan_workspace_button_apply_terminator");

    public string AppTitle => DemoUiText.Get("app_title");

    public string AppSubtitle => DemoUiText.Get("app_subtitle");

    public string FakeModeBannerText => DemoUiText.Get("fake_mode_banner");

    public string NavDiscoveryText => DemoUiText.Get("nav_discovery");

    public string NavConsoleText => DemoUiText.Get("nav_console");

    public string NavLogsText => DemoUiText.Get("nav_logs");

    public string TabDiscoveryText => DemoUiText.Get("tab_discovery");

    public string DiscoveryWorkspaceTitle => DemoUiText.Get("discovery_workspace_title");

    public string DiscoveryWorkspaceCaption => DemoUiText.Get("discovery_workspace_caption");

    public string DiscoveryMetricStatusTitle => DemoUiText.Get("discovery_metric_status");

    public string DiscoveryMetricTransportTitle => DemoUiText.Get("discovery_metric_transport");

    public string DiscoveryMetricSelectedModelTitle => DemoUiText.Get("discovery_metric_selected_model");

    public string DiscoveryMetricDiscoveredDevicesTitle => DemoUiText.Get("discovery_metric_discovered_devices");

    public string DiscoveryDiagnosticsTitle => DemoUiText.Get("discovery_diagnostics_title");

    public string DiscoveryModelLabel => DemoUiText.Get("discovery_model_label");

    public string DiscoveryTransportLabel => DemoUiText.Get("discovery_transport_label");

    public string SerialTargetLabelText => DemoUiText.Get("serial_target_label");

    public string RefreshSerialPortsText => DemoUiText.Get("serial_refresh_ports");

    public string DiscoveryUsbTraceText => DemoUiText.Get("discovery_usb_trace");

    public string DiscoverySelectedModelSummaryTitle => DemoUiText.Get("discovery_selected_model_summary_title");

    public string DiscoveryInitializeSdkButtonText => DemoUiText.Get("discovery_button_initialize_sdk");

    public string DiscoveryStartSearchButtonText => DemoUiText.Get("discovery_button_start_search");

    public string DiscoveryStopSearchButtonText => DemoUiText.Get("discovery_button_stop_search");

    public string DiscoveryConnectDeviceButtonText => DemoUiText.Get("discovery_button_connect_device");

    public string DiscoveryOpenConsoleButtonText => DemoUiText.Get("discovery_button_open_console");

    public string DiscoveryDisconnectButtonText => DemoUiText.Get("discovery_button_disconnect");

    public string DiscoveryDevicesTitle => DemoUiText.Get("discovery_devices_title");

    public string DiscoveryDevicesCaption => DemoUiText.Get("discovery_devices_caption");

    public string DiscoveryCurrentSelectionTitle => DemoUiText.Get("discovery_current_selection_title");

    public string DiscoveryScenarioPresetsTitle => DemoUiText.Get("discovery_scenario_presets_title");

    public string DiscoveryScenarioPresetsCaption => DemoUiText.Get("discovery_scenario_presets_caption");

    public string DiscoveryQuickScanButtonText => DemoUiText.Get("discovery_button_quick_scan");

    public string DiscoveryInfoButtonText => DemoUiText.Get("discovery_button_info");

    public string DiscoveryBatteryButtonText => DemoUiText.Get("discovery_button_battery");

    public string DiscoveryMasterCommandsButtonText => DemoUiText.Get("discovery_button_master_commands");

    public string DiscoveryProductSettingsButtonText => DemoUiText.Get("discovery_button_product_settings");

    public string DiscoveryDataRuleButtonText => DemoUiText.Get("discovery_button_data_rule");

    public string DiscoveryDiagnosticsSnapshotTitle => DemoUiText.Get("discovery_diagnostics_snapshot_title");

    public string DiscoveryDiagnosticsSnapshotCaption => DemoUiText.Get("discovery_diagnostics_snapshot_caption");

    public string TabLogsText => DemoUiText.Get("tab_logs");

    public string LogsFiltersTitle => DemoUiText.Get("logs_filters_title");

    public string LogsFiltersCaption => DemoUiText.Get("logs_filters_caption");

    public string LogsCopyAllButtonText => DemoUiText.Get("logs_button_copy_all");

    public string LogsCopyFilteredButtonText => DemoUiText.Get("logs_button_copy_filtered");

    public string LogsExportAllButtonText => DemoUiText.Get("logs_button_export_all");

    public string LogsExportFilteredButtonText => DemoUiText.Get("logs_button_export_filtered");

    public string LogsCompatibilityButtonText => DemoUiText.Get("logs_button_compatibility");

    public string LogsIncludeSensitivePayloadsText => DemoUiText.Get("logs_include_sensitive_payloads");

    public string LogsClearButtonText => DemoUiText.Get("logs_button_clear");

    public string LogsColumnTimeTitle => DemoUiText.Get("logs_column_time");

    public string LogsColumnSourceTitle => DemoUiText.Get("logs_column_source");

    public string LogsColumnLevelTitle => DemoUiText.Get("logs_column_level");

    public string LogsColumnMessageTitle => DemoUiText.Get("logs_column_message");

    public string TabConsoleText => DemoUiText.Get("tab_console");

    public string ConsoleStatusTitle => DemoUiText.Get("console_status_title");

    public string ConsoleStatusCaption => DemoUiText.Get("console_status_caption");

    public string ConsoleBackToDiscoveryButtonText => DemoUiText.Get("console_button_back_to_discovery");

    public string ConsoleDisconnectButtonText => DemoUiText.Get("console_button_disconnect");

    public string ConsoleMetricSessionTitle => DemoUiText.Get("console_metric_session");

    public string ConsoleMetricDeviceTitle => DemoUiText.Get("console_metric_device");

    public string ConsoleMetricCustomerModelTitle => DemoUiText.Get("console_metric_customer_model");

    public string ConsoleMetricSdkModelTitle => DemoUiText.Get("console_metric_sdk_model");

    public string ConsoleMetricCapabilityTitle => DemoUiText.Get("console_metric_capability");

    public string ConsoleMetricOperationSupportTitle => DemoUiText.Get("console_metric_operation_support");

    public string ConsoleMetricRecentFailureTitle => DemoUiText.Get("console_metric_recent_failure");

    public string ConsoleMetricFailureFeedbackTitle => DemoUiText.Get("console_metric_failure_feedback");

    public string ConsoleMetricLastActionResultTitle => DemoUiText.Get("console_metric_last_action_result");

    public string ConsoleToolsTitle => DemoUiText.Get("console_tools_title");

    public string ConsoleToolsCaption => DemoUiText.Get("console_tools_caption");

    public string ConsoleTabActionsText => DemoUiText.Get("console_tab_actions");

    public string ConsoleTabCapabilitiesText => DemoUiText.Get("console_tab_capabilities");

    public string ConsoleTabMasterSettingsText => DemoUiText.Get("console_tab_master_settings");

    public string ConsoleTabModuleSettingsText => DemoUiText.Get("console_tab_module_settings");

    public string ConsoleTabDataRulesText => DemoUiText.Get("console_tab_data_rules");

    public string ConsoleTabDiagnosticsText => DemoUiText.Get("console_tab_diagnostics");

    public string CapabilityCatalogTitle => DemoUiText.Get("capability_catalog_title");

    public string CapabilityCatalogDescription => DemoUiText.Get("capability_catalog_description");

    public string CapabilityCatalogLoadingText => DemoUiText.Get("capability_catalog_loading");

    public string CapabilityCatalogEmptyText => DemoUiText.Get("capability_catalog_empty");

    public string CapabilityCatalogRetryText => DemoUiText.Get("retry");

    public string CapabilityBackText => DemoUiText.Get("back");

    public string CapabilityReadDomainText => DemoUiText.Get("capability_read_domain");

    public string CapabilityCurrentText => DemoUiText.Get("capability_current");

    public string CapabilityPersistText => DemoUiText.Get("capability_persist");

    public string CapabilityReadText => DemoUiText.Get("capability_read");

    public string CapabilityWriteText => DemoUiText.Get("capability_write");

    public string CapabilityExecuteText => DemoUiText.Get("capability_execute");

    public string LanguageLabelText => DemoUiText.Get("language_label");

    public string SettingsLabelText => DemoUiText.Get("settings_label");

    public string DataRuleBuilderModeText => DemoUiText.Get("data_rule_builder_mode");

    public string DataRuleValueAText => DemoUiText.Get("data_rule_value_a");

    public string DataRuleValueBText => DemoUiText.Get("data_rule_value_b");

    public string DataRuleBuilderTitleText => DemoUiText.Get("data_rule_builder_title");

    public string DataRuleBuilderCaptionText => DemoUiText.Get("data_rule_builder_caption");

    public string DataRuleModeText => DemoUiText.Get("data_rule_mode");

    public string DataRulePrimaryValueText => DemoUiText.Get("data_rule_primary_value");

    public string DataRuleSecondaryValueText => DemoUiText.Get("data_rule_secondary_value");

    public string DataRuleNotesTitleText => DemoUiText.Get("data_rule_notes_title");

    public string DataRuleNotesBodyText => DemoUiText.Get("data_rule_notes_body");

    public string DataRuleTraceCaptionText => DemoUiText.Get("data_rule_trace_caption");

    public string DiagnosticsMetricReadyStateTitle => DemoUiText.Get("diagnostics_metric_ready_state");

    public string DiagnosticsMetricUsbHidTraceTitle => DemoUiText.Get("diagnostics_metric_usb_hid_trace");

    public string DiagnosticsStatusSnapshotTitle => DemoUiText.Get("diagnostics_status_snapshot_title");

    public string DiagnosticsStatusSnapshotCaption => DemoUiText.Get("diagnostics_status_snapshot_caption");

    public string DiagnosticsResultFailureTitle => DemoUiText.Get("diagnostics_result_failure_title");

    public string DiagnosticsResultFailureCaption => DemoUiText.Get("diagnostics_result_failure_caption");

    public string DiagnosticsCommandTraceTitle => DemoUiText.Get("diagnostics_command_trace_title");

    public string DiagnosticsCommandTraceCaption => DemoUiText.Get("diagnostics_command_trace_caption");

    public string BottomDeviceInfoTitle => DemoUiText.Get("bottom_device_info_title");

    public string BottomBatteryResultTitle => DemoUiText.Get("bottom_battery_result_title");

    public string DataRuleCapabilityHintText => BuildDataRuleCapabilityHint();

    public bool HasLastResult => !string.IsNullOrWhiteSpace(LastResultText);

    public bool HasRecentFailure =>
        !string.IsNullOrWhiteSpace(RecentFailureText) &&
        !string.Equals(RecentFailureText, "<none>", StringComparison.OrdinalIgnoreCase);

    public string SelectedSettingTitle =>
        SelectedSettingItem is null ? "No setting selected" : SelectedSettingItem.DisplayName;

    public string SelectedSettingDetailSummary =>
        SelectedSettingItem is null
            ? "Select a row to inspect key, type, source, current value, and write support."
            : $"key={SelectedSettingItem.KeyText}{Environment.NewLine}" +
              $"source={SelectedSettingItem.Source}{Environment.NewLine}" +
              $"group={SelectedSettingItem.Group}{Environment.NewLine}" +
              $"kind={SelectedSettingItem.Kind}{Environment.NewLine}" +
              $"capabilities={SelectedSettingItem.Capabilities}{Environment.NewLine}" +
              $"current={SelectedSettingItem.CurrentValue}{Environment.NewLine}" +
              $"default={SelectedSettingItem.DefaultValue}{Environment.NewLine}" +
              $"status={SelectedSettingItem.Status}";

    public string SelectedSettingNoteSummary =>
        SelectedSettingItem is null
            ? "Notes will appear here."
            : string.IsNullOrWhiteSpace(SelectedSettingItem.Notes)
                ? "No additional notes for the selected setting."
                : SelectedSettingItem.Notes;

    public string SelectedSettingOptionsSummary =>
        SelectedSettingItem is null
            ? "Structured options will appear here."
            : string.IsNullOrWhiteSpace(SelectedSettingItem.OptionsSummary)
                ? "No predefined options for the selected setting."
                : SelectedSettingItem.OptionsSummary;

    public async Task InitializeOnLoadedAsync()
    {
        await InitializeSdkAsync().ConfigureAwait(true);
    }

    internal Task StartDiscoveryForTestAsync() => SearchDevicesAsync();

    internal Task StopDiscoveryForTestAsync() => StopDiscoveryAsync();

    internal Task ConnectForTestAsync() => ConnectSelectedDeviceAsync();

    internal Task DisconnectForTestAsync() => DisconnectAsync();

    internal Task TriggerScanForTestAsync() => TriggerScanCommandForTestAsync();

    internal Task ExecuteOperationForTestAsync(string key, bool secondaryAction = false)
    {
        var operation = OperationGroups
            .SelectMany(static group => group.Operations)
            .Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        return ExecuteDemoOperationAsync(operation, secondaryAction);
    }

    private Task TriggerScanCommandForTestAsync() =>
        RunSessionActionAsync("TriggerScan", active => active.TriggerScanAsync());

    public void ReportUiFailure(string operation, Exception error)
    {
        recentFailure = DemoErrorFormatter.FromException(operation, error);
        OnPropertyChanged(nameof(RecentFailure));
        RecentFailureText = recentFailure.DisplayText;
        LastResultText = recentFailure.DisplayText;
        LogStore.Append(DemoLogSource.UI, DemoLogLevel.Error, recentFailure.DisplayText);
    }

    public async Task ShutdownAsync()
    {
        if (WorkflowState == DemoWorkflowState.ShuttingDown)
        {
            return;
        }

        WorkflowState = DemoWorkflowState.ShuttingDown;
        sessionCommandRunner.CancelPending();
        try
        {
            if (sessionCoordinator.ActiveSession is { } active)
            {
                DetachSession(active);
            }

            if (sessionCoordinator.IsInitialized)
            {
                backend.DiscoveryReceived -= Sdk_DiscoveryReceived;
                backend.DiscoveryFailureReceived -= Sdk_DiscoveryFailureReceived;
            }
            await sessionCoordinator.ShutdownAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LogStore.Append(DemoLogSource.UI, DemoLogLevel.Warn, $"shutdown failed: {ex.Message}");
        }
        finally
        {
            sessionCommandRunner.BusyChanged -= SessionCommandRunner_BusyChanged;
            sessionCommandRunner.Dispose();
            backend.LogReceived -= Sdk_LogReceived;
            backend.UsbHidTraceReceived -= Sdk_UsbHidTraceReceived;
            backend.CommandTraceReceived -= Sdk_CommandTraceReceived;
            backend.Dispose();
        }
    }

    private async Task InitializeSdkAsync()
    {
        await RunUiActionAsync("initialize SDK", async () =>
        {
            Environment.SetEnvironmentVariable("NSDK_USB_HID_TRACE", EnableUsbHidTrace ? "1" : null);
            if (!sessionCoordinator.IsInitialized)
            {
                backend.DiscoveryReceived += Sdk_DiscoveryReceived;
                backend.DiscoveryFailureReceived += Sdk_DiscoveryFailureReceived;
                try
                {
                    await sessionCoordinator.InitializeAsync().ConfigureAwait(true);
                }
                catch
                {
                    backend.DiscoveryReceived -= Sdk_DiscoveryReceived;
                    backend.DiscoveryFailureReceived -= Sdk_DiscoveryFailureReceived;
                    throw;
                }
                LogStore.Append(DemoLogSource.SDK, DemoLogLevel.Info, "SDK initialized");
            }

            SdkVersion = backend.Version;
            WorkflowState = DemoWorkflowState.Idle;
            RefreshModelOptionsFromSdk();
            RefreshDataRuleOptionsFromSdk();
            await ReloadCapabilityCatalogAsync(force: true).ConfigureAwait(true);
            RefreshProductSurfaceForSelectedModel();
            RefreshDiagnostics();
            RefreshInteractionSurface();
        }).ConfigureAwait(true);
    }

    private async Task SearchDevicesAsync()
    {
        await RunUiActionAsync("search devices", async () =>
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            DiscoveryDevices.Clear();
            SelectedDiscoveryDevice = null;
            WorkflowState = DemoWorkflowState.Discovering;
            await sessionCoordinator.StartDiscoveryAsync(
                SelectedTransportOption.TransportType,
                SelectedModelOption.ModelKey).ConfigureAwait(true);
            if (IsSerialTransport && SystemSerialPortCatalog.TryNormalize(ManualSerialPort, out var manualPort))
            {
                SelectSerialPort(manualPort, isEnumerated: SerialPorts.Contains(manualPort));
            }
            SessionStateText = "Discovering";
            RefreshInteractionSurface();
            LogStore.Append(
                DemoLogSource.Discovery,
                DemoLogLevel.Info,
                $"search started transport={SelectedTransportOption.TransportType} model={SelectedModelOption.ModelKey}"
            );
        }).ConfigureAwait(true);
    }

    private void RefreshSerialPorts()
    {
        SerialPorts.Clear();
        foreach (var port in serialPortCatalog.GetPortNames())
        {
            SerialPorts.Add(port);
        }

        DiscoveryDevices.Clear();
        foreach (var port in SerialPorts)
        {
            AddSerialPortTarget(port, isEnumerated: true);
        }

        if (SystemSerialPortCatalog.TryNormalize(ManualSerialPort, out var manual))
        {
            SelectSerialPort(manual, isEnumerated: SerialPorts.Contains(manual));
        }
        else
        {
            SelectedDiscoveryDevice = DiscoveryDevices.FirstOrDefault();
        }

        DiscoveryHintText = SerialPorts.Count == 0
            ? "No COM ports were enumerated. Enter a valid COMx target manually."
            : $"Found {SerialPorts.Count} COM port(s). You can also enter a virtual COMx target.";
    }

    private void RefreshSerialPortsWithFeedback()
    {
        try
        {
            RefreshSerialPorts();
        }
        catch (Exception error)
        {
            ReportUiFailure("refresh COM ports", error);
        }
    }

    private void SelectSerialPort(string port, bool isEnumerated)
    {
        var existing = DiscoveryDevices.FirstOrDefault(
            item => string.Equals(item.DeviceId, port, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = AddSerialPortTarget(port, isEnumerated);
        }

        SelectedDiscoveryDevice = existing;
        if (!string.Equals(ManualSerialPort, port, StringComparison.Ordinal))
        {
            manualSerialPort = port;
            OnPropertyChanged(nameof(ManualSerialPort));
        }
    }

    private DemoDeviceItem AddSerialPortTarget(string port, bool isEnumerated)
    {
        var item = new DemoDeviceItem(
            port,
            port,
            ScannerSdkLib.TransportType.UsbSerial,
            SelectedModelOption.ModelKey,
            null,
            isEnumerated ? "Windows COM port" : "Manual virtual COM target");
        DiscoveryDevices.Add(item);
        return item;
    }

    private async Task StopDiscoveryAsync()
    {
        await RunUiActionAsync("stop discovery", async () =>
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            await sessionCoordinator.StopDiscoveryAsync().ConfigureAwait(true);
            WorkflowState = DemoWorkflowState.Idle;
            if (!IsConnected)
            {
                SessionStateText = "Discovery stopped";
            }

            RefreshInteractionSurface();
        }).ConfigureAwait(true);
    }

    private async Task ConnectSelectedDeviceAsync()
    {
        await RunUiActionAsync("connect selected device", async () =>
        {
            await EnsureInitializedAsync().ConfigureAwait(true);
            var selected = SelectedDiscoveryDevice ?? throw new InvalidOperationException("No discovered device selected.");
            WorkflowState = DemoWorkflowState.Connecting;
            SessionStateText = "Connecting";
            var connected = await sessionCoordinator.ConnectReadyAsync(
                selected,
                SelectedModelOption.ModelKey).ConfigureAwait(true);

            AttachSession(connected);
            IsConnected = true;
            IsReady = connected.LatestState == ScannerSdkLib.SessionState.Ready;
            WorkflowState = DemoWorkflowState.Connected;
            SessionStateText = connected.LatestState.ToString();
            SelectedWorkspaceIndex = ConsoleWorkspaceIndex;
            await LoadSessionSurfaceAsync(connected).ConfigureAwait(true);
            RefreshInteractionSurface();
            LogStore.Append(DemoLogSource.Session, DemoLogLevel.Info, $"connected id={connected.DeviceId} transport={connected.TransportType}");
        }).ConfigureAwait(true);
    }

    private async Task DisconnectAsync()
    {
        sessionCommandRunner.CancelPending();
        await RunUiActionAsync("disconnect", async () =>
        {
            if (sessionCoordinator.ActiveSession is not { } active)
            {
                return;
            }

            WorkflowState = DemoWorkflowState.Disconnecting;
            DetachSession(active);
            await sessionCoordinator.DisconnectAsync().ConfigureAwait(true);
            IsConnected = false;
            IsReady = false;
            publicCapabilitySummary = null;
            operationSupport = null;
            SessionStateText = "Disconnected";
            ResolvedModelSummary = "Not connected";
            CapabilitySummaryText = "Not connected";
            OperationSupportSummary = "Not connected";
            WorkflowState = DemoWorkflowState.Idle;
            RebuildOperationCatalog();
            RefreshInteractionSurface();
            LogStore.Append(DemoLogSource.Session, DemoLogLevel.Info, "disconnected");
        }).ConfigureAwait(true);
    }

    private async Task RefreshInfoAsync()
    {
        await RunSessionActionAsync("refresh info", async active =>
        {
            var info = await active.RefreshInfoAsync().ConfigureAwait(true);
            DeviceInfoText = DemoFormatters.FormatInfo(info);
            LastResultText = DeviceInfoText;
        }).ConfigureAwait(true);
    }

    private async Task GetBatteryAsync()
    {
        await RunSessionActionAsync("get battery", async active =>
        {
            var battery = await active.GetBatteryInfoAsync().ConfigureAwait(true);
            BatteryText = DemoFormatters.FormatBattery(battery);
            LastResultText = BatteryText;
        }).ConfigureAwait(true);
    }

    private async Task GetStorageUsageAsync()
    {
        await RunSessionActionAsync("get memory usage", async active =>
        {
            var usage = await active.GetStorageUsageAsync().ConfigureAwait(true);
            LastResultText = DemoFormatters.FormatStorageUsage(usage);
            LogStore.Append(DemoLogSource.Command, DemoLogLevel.Info, $"StorageUsage: {LastResultText}");
        }).ConfigureAwait(true);
    }

    private async Task InitializeSessionAsync()
    {
        await RunSessionActionAsync("initialize session", async active =>
        {
            var result = await active.InitializeAsync().ConfigureAwait(true);
            DeviceInfoText = DemoFormatters.FormatInfo(result.Info);
            BatteryText = DemoFormatters.FormatBattery(result.BatteryInfo);
            LastResultText = $"modelConfigApplied={result.ModelConfigApplied}{Environment.NewLine}{DeviceInfoText}{Environment.NewLine}{BatteryText}";
            await LoadSessionSurfaceAsync(active).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task SetTimestampAsync()
    {
        await RunSessionActionAsync("set timestamp", async active =>
        {
            var response = await active.SetTimestampAsync().ConfigureAwait(true);
            LastResultText = DemoFormatters.FormatResponse("SetTimestamp", response);
            LogStore.AppendCommandResult("SetTimestamp", response);
        }).ConfigureAwait(true);
    }

    private async Task ExecuteDemoOperationAsync(object? parameter, bool secondaryAction)
    {
        if (parameter is not DemoOperationItem operation || !operation.IsEnabled)
        {
            return;
        }

        var input = operation.InputValue.Trim();
        if (operation.RequiresValue && input.Length == 0)
        {
            await RunDemoOperationActionAsync(
                operation,
                "execute device action",
                _ => throw new InvalidOperationException(operation.ValueHint)).ConfigureAwait(true);
            return;
        }

        if (operation.Dangerous)
        {
            var confirmed = ConfirmDanger?.Invoke(
                operation.Title,
                BuildDeviceActionConfirmationMessage(
                    operation.Title,
                    operation.Entry?.GroupKey ?? string.Empty,
                    operation.RiskBadgeText,
                    operation.RequiresValue,
                    input)) ?? true;
            if (!confirmed)
            {
                operation.Status = DemoUiText.Get("device_action_status_cancelled");
                LastResultText = string.Format(DemoUiText.Get("device_action_cancelled_message"), operation.Title);
                LogStore.Append(DemoLogSource.UI, DemoLogLevel.Info, LastResultText);
                return;
            }
        }

        await RunDemoOperationActionAsync(
            operation,
            operation.Key,
            async active =>
            {
                switch (operation.Kind)
                {
                    case DemoOperationKind.InitializeSession:
                    {
                        var result = await active.InitializeAsync().ConfigureAwait(true);
                        DeviceInfoText = DemoFormatters.FormatInfo(result.Info);
                        BatteryText = DemoFormatters.FormatBattery(result.BatteryInfo);
                        LastResultText = $"modelConfigApplied={result.ModelConfigApplied}{Environment.NewLine}{DeviceInfoText}{Environment.NewLine}{BatteryText}";
                        await LoadSessionSurfaceAsync(active).ConfigureAwait(true);
                        break;
                    }
                    case DemoOperationKind.RefreshInfo:
                    {
                        var info = await active.RefreshInfoAsync().ConfigureAwait(true);
                        DeviceInfoText = DemoFormatters.FormatInfo(info);
                        LastResultText = DeviceInfoText;
                        break;
                    }
                    case DemoOperationKind.ReadBattery:
                    {
                        var battery = await active.GetBatteryInfoAsync().ConfigureAwait(true);
                        BatteryText = DemoFormatters.FormatBattery(battery);
                        LastResultText = BatteryText;
                        break;
                    }
                    case DemoOperationKind.ReadStorageUsage:
                    {
                        var usage = await active.GetStorageUsageAsync().ConfigureAwait(true);
                        LastResultText = DemoFormatters.FormatStorageUsage(usage);
                        LogStore.Append(DemoLogSource.Command, DemoLogLevel.Info, $"StorageUsage: {LastResultText}");
                        break;
                    }
                    case DemoOperationKind.TriggerScan:
                        await active.TriggerScanAsync().ConfigureAwait(true);
                        LastResultText = DemoUiText.Get("dynamic_operation_trigger_sent");
                        break;
                    case DemoOperationKind.SetCurrentTimestamp:
                    {
                        var response = await active.SetTimestampAsync().ConfigureAwait(true);
                        LastResultText = DemoFormatters.FormatResponse("SetTimestamp", response);
                        LogStore.AppendCommandResult("SetTimestamp", response);
                        break;
                    }
                    case DemoOperationKind.SetAckBeep:
                    {
                        var enabled = !secondaryAction;
                        var response = await active.SetAckBeepEnabledAsync(enabled).ConfigureAwait(true);
                        LastResultText = DemoFormatters.FormatResponse($"SetAckBeepEnabled({enabled})", response);
                        LogStore.AppendCommandResult($"SetAckBeepEnabled({enabled})", response);
                        break;
                    }
                    case DemoOperationKind.SetVibration:
                    {
                        var enabled = !secondaryAction;
                        var response = await active.SetVibrationEnabledAsync(enabled).ConfigureAwait(true);
                        LastResultText = DemoFormatters.FormatResponse($"SetVibrationEnabled({enabled})", response);
                        LogStore.AppendCommandResult($"SetVibrationEnabled({enabled})", response);
                        break;
                    }
                    case DemoOperationKind.CapabilityAction:
                    {
                        var entry = operation.Entry ?? throw new InvalidOperationException("Capability action entry is missing.");
                        var response = operation.RequiresValue
                            ? await active.ExecuteCapabilityActionAsync(entry.EntryKey, Encoding.ASCII.GetBytes(input)).ConfigureAwait(true)
                            : await active.ExecuteCapabilityActionAsync(entry.EntryKey).ConfigureAwait(true);
                        LastResultText = DemoFormatters.FormatResponse($"CapabilityAction({entry.EntryKey})", response);
                        LogStore.AppendCommandResult($"CapabilityAction({entry.EntryKey})", response);
                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unsupported demo operation: {operation.Kind}");
                }
            }).ConfigureAwait(true);
    }

    private async Task RunDemoOperationActionAsync(
        DemoOperationItem operation,
        string actionName,
        Func<IDemoSessionHandle, Task> action)
    {
        await RunSessionActionAsync(actionName, async active =>
        {
            operation.Status = DemoUiText.Get("dynamic_operation_status_running");
            try
            {
                await action(active).ConfigureAwait(true);
                operation.Status = DemoUiText.Get("dynamic_operation_status_completed");
            }
            catch (Exception ex)
            {
                operation.Status = string.Format(DemoUiText.Get("capability_status_failed"), ex.Message);
                throw;
            }
        }).ConfigureAwait(true);
    }

    private async Task ExecuteCapabilityActionEntryAsync(object? parameter)
    {
        await RunSessionActionAsync("execute device action", async active =>
        {
            var item = parameter as DemoDeviceActionItem ?? throw new InvalidOperationException("No device action selected.");
            var input = item.InputValue.Trim();
            if (item.RequiresValue && input.Length == 0)
            {
                throw new InvalidOperationException(item.ValueHintDisplay);
            }

            if (item.Dangerous)
            {
                var confirmed = ConfirmDanger?.Invoke(item.DisplayName, BuildDeviceActionConfirmationMessage(item, input)) ?? true;
                if (!confirmed)
                {
                    item.Status = DemoUiText.Get("device_action_status_cancelled");
                    LastResultText = string.Format(DemoUiText.Get("device_action_cancelled_message"), item.DisplayName);
                    LogStore.Append(DemoLogSource.UI, DemoLogLevel.Info, LastResultText);
                    return;
                }
            }

            var response = item.RequiresValue
                ? await active.ExecuteCapabilityActionAsync(item.KeyText, Encoding.ASCII.GetBytes(input)).ConfigureAwait(true)
                : await active.ExecuteCapabilityActionAsync(item.KeyText).ConfigureAwait(true);
            item.Status = response.Acknowledged
                ? DemoUiText.Get("device_action_status_acknowledged")
                : DemoUiText.Get("device_action_status_sent");
            LastResultText = DemoFormatters.FormatResponse($"CapabilityAction({item.KeyText})", response);
            LogStore.AppendCommandResult($"CapabilityAction({item.KeyText})", response);
        }).ConfigureAwait(true);
    }

    private async Task ApplyCharsetAsync()
    {
        await RunUiActionAsync("apply charset", () =>
        {
            var active = RequireSession();
            active.SetScanTextCharset(SelectedCharsetOption.Charset);
            LastResultText = $"Local scan charset set to {SelectedCharsetOption.DisplayName}";
            LogStore.Append(DemoLogSource.UI, DemoLogLevel.Info, LastResultText);
            return Task.CompletedTask;
        }).ConfigureAwait(true);
    }

    private async Task ApplyTerminatorAsync()
    {
        await RunSessionActionAsync("apply terminator", async active =>
        {
            await active.SetScanTextTerminatorAsync(SelectedTerminatorOption.Bytes).ConfigureAwait(true);
            LastResultText = $"Scan terminator set to {SelectedTerminatorOption.DisplayName}";
        }).ConfigureAwait(true);
    }

    private async Task ApplyDataRuleAsync()
    {
        await RunSessionActionAsync("apply data rule", async active =>
        {
            var rule = BuildDataRule();
            var response = await active.ApplyDataRuleAsync(rule).ConfigureAwait(true);
            LastResultText = DemoFormatters.FormatResponse($"DataRule({rule.Kind})", response);
            LogStore.AppendCommandResult($"DataRule({rule.Kind})", response);
        }).ConfigureAwait(true);
    }

    private async Task PresetQuickScanAsync()
    {
        SelectConsoleTab(ScanConsoleTabIndex);
        await RunSessionActionAsync("preset quick scan", active => active.TriggerScanAsync()).ConfigureAwait(true);
    }

    private async Task PresetInfoReadAsync()
    {
        SelectConsoleTab(DiagnosticsConsoleTabIndex);
        await RefreshInfoAsync().ConfigureAwait(true);
    }

    private async Task PresetBatteryReadAsync()
    {
        SelectConsoleTab(DiagnosticsConsoleTabIndex);
        await GetBatteryAsync().ConfigureAwait(true);
    }

    private void SelectConsoleTab(int tabIndex, SettingsSourceFilter? sourceFilter = null)
    {
        SelectedWorkspaceIndex = ConsoleWorkspaceIndex;
        if (sourceFilter is SettingsSourceFilter filter)
        {
            SelectedSettingsSourceFilter = filter;
        }

        SelectedConsoleTabIndex = tabIndex;
    }

    private async Task ReadCapabilityValueEntryAsync(object? parameter)
    {
        await RunSerializedSessionActionAsync("read setting", async (active, cancellationToken) =>
        {
            var item = parameter as SettingItem ?? throw new InvalidOperationException("No setting selected.");
            if (!item.SupportsRead)
            {
                throw new NotSupportedException($"Setting is read-only unavailable: {item.KeyText}");
            }

            var value = await active.ReadCapabilityValueAsync(item.EntryKey, cancellationToken).ConfigureAwait(true);
            item.CurrentValue = DemoFormatters.FormatCapabilityValue(value);
            item.Status = "read";
            if (ReferenceEquals(SelectedSettingItem, item))
            {
                RefreshSelectedSettingSummary();
            }
            LastResultText = $"{item.KeyText} => {item.CurrentValue}";
            LogStore.Append(DemoLogSource.Command, DemoLogLevel.Info, $"setting read {LastResultText}");
        }).ConfigureAwait(true);
    }

    private async Task WriteCapabilityValueEntryAsync(object? parameter)
    {
        await RunSerializedSessionActionAsync("write setting", async (active, cancellationToken) =>
        {
            var item = parameter as SettingItem ?? throw new InvalidOperationException("No setting selected.");
            if (!item.SupportsWrite)
            {
                throw new NotSupportedException($"Setting does not support write: {item.KeyText}");
            }

            var encoded = DemoParsing.EncodeCapabilityValue(item.Entry, item.Options, item.WriteValue);
            if (item.Dangerous)
            {
                var confirmed = ConfirmDanger?.Invoke(
                    item.DisplayName,
                    string.Format(DemoUiText.Get("capability_write_risk_confirm"), item.DisplayName, item.WriteValue)) ?? true;
                if (!confirmed)
                {
                    item.Status = DemoUiText.Get("capability_operation_cancelled");
                    LogStore.Append(DemoLogSource.UI, DemoLogLevel.Info, $"risk setting write cancelled key={item.EntryKey}");
                    return;
                }

                LogStore.Append(DemoLogSource.UI, DemoLogLevel.Info, $"risk setting write confirmed key={item.EntryKey}");
            }

            var response = await active.WriteCapabilityValueAsync(
                item.EntryKey,
                encoded,
                item.PersistWrite,
                cancellationToken).ConfigureAwait(true);
            item.Status = response.Acknowledged ? "write acknowledged" : "write sent";
            item.CurrentValue = DemoFormatters.FormatCapabilityValue(encoded);
            if (ReferenceEquals(SelectedSettingItem, item))
            {
                RefreshSelectedSettingSummary();
            }
            LastResultText = DemoFormatters.FormatResponse($"WriteCapabilityValue({item.KeyText})", response);
            LogStore.AppendCommandResult($"WriteCapabilityValue({item.KeyText}, persist={item.PersistWrite})", response);
        }).ConfigureAwait(true);
    }

    private async Task ReadVisibleSettingsAsync()
    {
        await RunSerializedSessionActionAsync("read visible settings", async (active, cancellationToken) =>
        {
            foreach (var item in settingsView.Cast<SettingItem>().Where(static item => item.SupportsRead).ToArray())
            {
                try
                {
                    var value = await active.ReadCapabilityValueAsync(item.EntryKey, cancellationToken).ConfigureAwait(true);
                    item.CurrentValue = DemoFormatters.FormatCapabilityValue(value);
                    item.Status = "read";
                    if (ReferenceEquals(SelectedSettingItem, item))
                    {
                        RefreshSelectedSettingSummary();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = $"failed: {ex.Message}";
                    if (ReferenceEquals(SelectedSettingItem, item))
                    {
                        RefreshSelectedSettingSummary();
                    }
                    LogStore.Append(DemoLogSource.Command, DemoLogLevel.Warn, $"setting read failed key={item.KeyText}: {ex.Message}");
                }
            }

            LastResultText = "Visible readable settings refreshed.";
        }).ConfigureAwait(true);
    }

    private async Task SetAckBeepEnabledAsync(bool enabled)
    {
        await RunSessionActionAsync($"set ack beep {enabled}", async active =>
        {
            var response = await active.SetAckBeepEnabledAsync(enabled).ConfigureAwait(true);
            LastResultText = DemoFormatters.FormatResponse($"SetAckBeepEnabled({enabled})", response);
            LogStore.AppendCommandResult($"SetAckBeepEnabled({enabled})", response);
        }).ConfigureAwait(true);
    }

    private async Task SetVibrationEnabledAsync(bool enabled)
    {
        await RunSessionActionAsync($"set vibration {enabled}", async active =>
        {
            var response = await active.SetVibrationEnabledAsync(enabled).ConfigureAwait(true);
            LastResultText = DemoFormatters.FormatResponse($"SetVibrationEnabled({enabled})", response);
            LogStore.AppendCommandResult($"SetVibrationEnabled({enabled})", response);
        }).ConfigureAwait(true);
    }

    private async Task RunSessionActionAsync(string actionName, Func<IDemoSessionHandle, Task> action)
    {
        await RunSerializedSessionActionAsync(
            actionName,
            (active, _) => action(active)).ConfigureAwait(true);
    }

    private async Task RunSerializedSessionActionAsync(
        string actionName,
        Func<IDemoSessionHandle, CancellationToken, Task> action)
    {
        await RunUiActionAsync(actionName, async () =>
        {
            await sessionCommandRunner.RunAsync(async cancellationToken =>
            {
                var active = RequireReadySession();
                await action(active, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task RunUiActionAsync(string actionName, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(actionName))
            {
                LogStore.Append(DemoLogSource.UI, DemoLogLevel.Info, $"{actionName} completed");
            }
        }
        catch (OperationCanceledException)
        {
            var message = $"{actionName} cancelled";
            LastResultText = message;
            LogStore.Append(DemoLogSource.UI, DemoLogLevel.Warn, message);
        }
        catch (Exception ex)
        {
            recentFailure = DemoErrorFormatter.FromException(actionName, ex);
            OnPropertyChanged(nameof(RecentFailure));
            LastResultText = recentFailure.DisplayText;
            RecentFailureText = recentFailure.DisplayText;
            LogStore.Append(DemoLogSource.UI, DemoLogLevel.Error, recentFailure.DisplayText);
            if (WorkflowState is DemoWorkflowState.Discovering
                or DemoWorkflowState.Connecting
                or DemoWorkflowState.Disconnecting)
            {
                WorkflowState = sessionCoordinator.ActiveSession is null
                    ? DemoWorkflowState.Idle
                    : DemoWorkflowState.Connected;
            }
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (!sessionCoordinator.IsInitialized)
        {
            await InitializeSdkAsync().ConfigureAwait(true);
        }
    }

    private IDemoSessionHandle RequireSession()
    {
        return sessionCoordinator.ActiveSession ?? throw new InvalidOperationException("No active scanner session.");
    }

    private IDemoSessionHandle RequireReadySession()
    {
        var active = RequireSession();
        if (active.LatestState != ScannerSdkLib.SessionState.Ready)
        {
            throw new InvalidOperationException($"Session is not ready: {active.LatestState}");
        }

        return active;
    }

    private async Task LoadSessionSurfaceAsync(IDemoSessionHandle active)
    {
        var modelKey = active.GetResolvedModelKey();
        ResolvedModelSummary = $"SDK resolved: {modelKey}";
        publicCapabilitySummary = active.GetDeviceCapabilitySummary();
        operationSupport = active.GetOperationSupport();
        CapabilitySummaryText = publicCapabilitySummary.DisplaySummary;
        OperationSupportSummary = operationSupport.DisplaySummary;
        await ReloadCapabilityCatalogAsync(modelKey, active.TransportType, force: true).ConfigureAwait(true);

        try
        {
            var cachedBattery = await active.GetCachedBatteryInfoAsync().ConfigureAwait(true);
            if (cachedBattery is not null)
            {
                BatteryText = DemoFormatters.FormatBattery(cachedBattery);
            }
        }
        catch
        {
            // Cached battery is optional; live reads are available from the console.
        }

        RefreshDiagnostics();
        RefreshInteractionSurface();
    }

    private void LoadSettings(IReadOnlyList<ScannerSdkLib.CapabilityEntry> entries)
    {
        settings.Clear();
        foreach (var entry in entries
                     .Where(static entry => entry.Kind == ScannerSdkLib.CapabilityEntryKind.Setting)
                     .OrderBy(static entry => entry.GroupKey)
                     .ThenBy(static entry => entry.EntryKey))
        {
            settings.Add(new SettingItem(entry, entry.Options));
        }

        SettingGroups.Clear();
        foreach (var group in DemoCommandCatalog.BuildSettingGroups(settings))
        {
            SettingGroups.Add(group);
        }

        SelectedSettingGroup = "All";
        SelectedSettingItem = null;
        OnPropertyChanged(nameof(TotalSettingsCount));
        RefreshSettingsView();
        LogStore.Append(DemoLogSource.SDK, DemoLogLevel.Info, $"loaded {settings.Count} setting definition(s)");
    }

    private async Task ReloadCapabilityCatalogAsync(
        string? requestedModelKey = null,
        ScannerSdkLib.TransportType? requestedTransport = null,
        bool force = false)
    {
        if (!sessionCoordinator.IsInitialized)
        {
            return;
        }

        var modelKey = requestedModelKey ?? SelectedModelOption.ModelKey;
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            ApplyCapabilityCatalog([]);
            CapabilityCatalogError = DemoUiText.Get("models_empty");
            return;
        }

        var transport = requestedTransport ?? SelectedTransportOption.TransportType;
        var culture = DemoUiText.Culture;
        var cacheKey = $"{modelKey}:{transport}:{culture.Name}";
        var generation = ++capabilityCatalogGeneration;
        IsLoadingCapabilityCatalog = true;
        CapabilityCatalogError = string.Empty;
        LogStore.Append(DemoLogSource.SDK, DemoLogLevel.Info, $"capability catalog loading model={modelKey} transport={transport} culture={culture.Name}");

        try
        {
            if (force)
            {
                capabilityCatalogCache.Remove(cacheKey);
            }

            if (!capabilityCatalogCache.TryGetValue(cacheKey, out var snapshot))
            {
                snapshot = await Task.Run(() => new CapabilityCatalogSnapshot(
                    backend.GetCapabilityDomains(modelKey, transport),
                    backend.GetCapabilityEntries(modelKey, transport),
                    Enum.GetValues<ScannerSdkLib.CapabilityLabelKind>()
                        .SelectMany(backend.GetCapabilityLabels)
                        .ToArray())).ConfigureAwait(true);
                capabilityCatalogCache[cacheKey] = snapshot;
            }

            if (generation != capabilityCatalogGeneration)
            {
                return;
            }

            var catalog = CapabilityCatalogBuilder.Build(
                snapshot.Domains,
                snapshot.Entries,
                snapshot.Labels,
                culture,
                (domain, selectedCulture) => backend.Localize(
                    domain.LocalizationKey,
                    domain.DisplayName,
                    selectedCulture),
                (label, selectedCulture) => backend.Localize(
                    label.LocalizationKey,
                    label.DisplayName,
                    selectedCulture));
            ApplyCapabilityCatalog(catalog);
            LogStore.Append(
                DemoLogSource.SDK,
                DemoLogLevel.Info,
                $"capability catalog loaded model={modelKey} transport={transport} domains={catalog.Count} entries={catalog.Sum(static domain => domain.ItemCount)}");
        }
        catch (Exception ex)
        {
            if (generation != capabilityCatalogGeneration)
            {
                return;
            }

            CapabilityCatalogError = string.Format(DemoUiText.Get("capability_catalog_load_failed_detail"), ex.Message);
            LogStore.Append(DemoLogSource.SDK, DemoLogLevel.Error, $"capability catalog failed model={modelKey} transport={transport}: {ex.Message}");
        }
        finally
        {
            if (generation == capabilityCatalogGeneration)
            {
                IsLoadingCapabilityCatalog = false;
                OnPropertyChanged(nameof(IsCapabilityCatalogEmpty));
            }
        }
    }

    private void ApplyCapabilityCatalog(IReadOnlyList<CapabilityCatalogDomainItem> catalog)
    {
        var selectedDomainKey = SelectedCapabilityDomain?.Key;
        CapabilityDomains.Clear();
        foreach (var domain in catalog)
        {
            CapabilityDomains.Add(domain);
        }

        SelectedCapabilityDomain = selectedDomainKey is null
            ? null
            : CapabilityDomains.FirstOrDefault(domain => domain.Key == selectedDomainKey);

        settings.Clear();
        foreach (var item in catalog.SelectMany(static domain => domain.Settings))
        {
            settings.Add(item);
        }

        SettingGroups.Clear();
        foreach (var group in DemoCommandCatalog.BuildSettingGroups(settings))
        {
            SettingGroups.Add(group);
        }
        SelectedSettingGroup = "All";
        SelectedSettingItem = null;
        OnPropertyChanged(nameof(TotalSettingsCount));
        OnPropertyChanged(nameof(CapabilityCatalogSummary));
        OnPropertyChanged(nameof(IsCapabilityCatalogEmpty));
        RefreshSettingsView();
        RebuildOperationCatalog();
        RefreshInteractionSurface();
    }

    private void OpenCapabilityDomain(object? parameter)
    {
        if (parameter is not CapabilityCatalogDomainItem domain)
        {
            return;
        }

        SelectedCapabilityDomain = domain;
        LogStore.Append(DemoLogSource.UI, DemoLogLevel.Info, $"capability domain opened key={domain.Key} items={domain.ItemCount}");
    }

    private async Task ReadCapabilityDomainAsync(object? parameter)
    {
        var domain = parameter as CapabilityCatalogDomainItem ?? SelectedCapabilityDomain;
        if (domain is null)
        {
            return;
        }

        await RunSerializedSessionActionAsync("read capability domain", async (active, cancellationToken) =>
        {
            foreach (var item in domain.Settings.Where(static item => item.SupportsRead))
            {
                try
                {
                    var value = await active.ReadCapabilityValueAsync(item.EntryKey, cancellationToken).ConfigureAwait(true);
                    item.CurrentValue = DemoFormatters.FormatCapabilityValue(value);
                    item.Status = DemoUiText.Get("capability_status_read");
                    LogStore.Append(DemoLogSource.Command, DemoLogLevel.Info, $"setting read key={item.EntryKey} value={item.CurrentValue}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = string.Format(DemoUiText.Get("capability_status_failed"), ex.Message);
                    LogStore.Append(DemoLogSource.Command, DemoLogLevel.Warn, $"setting read failed key={item.EntryKey}: {ex.Message}");
                }
            }
        }).ConfigureAwait(true);
    }

    private void RefreshSettingsView()
    {
        settingsView.Refresh();
        VisibleSettingsCount = settingsView.Cast<object>().Count();
        OnPropertyChanged(nameof(SettingsWorkspaceSummary));
        if (SelectedSettingItem is not null && !settingsView.Cast<object>().Contains(SelectedSettingItem))
        {
            SelectedSettingItem = null;
        }
    }

    private void RefreshSelectedSettingSummary()
    {
        OnPropertyChanged(nameof(SelectedSettingTitle));
        OnPropertyChanged(nameof(SelectedSettingDetailSummary));
        OnPropertyChanged(nameof(SelectedSettingNoteSummary));
        OnPropertyChanged(nameof(SelectedSettingOptionsSummary));
    }

    private void RefreshSettingActionAvailability()
    {
        foreach (var item in settings)
        {
            item.CanReadAction = item.SupportsRead && CanReadSettingsSurface;
            item.CanWriteAction = item.SupportsWrite && CanWriteSettingsSurface;
        }
    }

    private void RefreshInteractionSurface()
    {
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(CanInitializeSdkSurface));
        OnPropertyChanged(nameof(CanStartDiscoverySurface));
        OnPropertyChanged(nameof(CanStopDiscoverySurface));
        OnPropertyChanged(nameof(CanConnectSelectedDeviceSurface));
        OnPropertyChanged(nameof(CanOpenConsoleSurface));
        OnPropertyChanged(nameof(CanDisconnectSurface));
        OnPropertyChanged(nameof(CanInitializeSessionSurface));
        OnPropertyChanged(nameof(CanRefreshInfoSurface));
        OnPropertyChanged(nameof(CanGetBatterySurface));
        OnPropertyChanged(nameof(CanGetMemorySurface));
        OnPropertyChanged(nameof(CanTriggerScanSurface));
        OnPropertyChanged(nameof(CanSetTimestampSurface));
        OnPropertyChanged(nameof(CanAdjustFeedbackSurface));
        OnPropertyChanged(nameof(CanSetAckBeepSurface));
        OnPropertyChanged(nameof(CanSetVibrationSurface));
        OnPropertyChanged(nameof(CanOpenSettingsSurface));
        OnPropertyChanged(nameof(CanReadSettingsSurface));
        OnPropertyChanged(nameof(CanWriteSettingsSurface));
        OnPropertyChanged(nameof(CanReadVisibleSettingsSurface));
        OnPropertyChanged(nameof(CanExecuteDataRuleSurface));
        OnPropertyChanged(nameof(InteractionHintText));
        OnPropertyChanged(nameof(SettingsCapabilityHintText));
        OnPropertyChanged(nameof(QuickScenarioHintText));
        OnPropertyChanged(nameof(DataRuleCapabilityHintText));
        RefreshOperationAvailability();
        ExecuteDemoOperationCommand.RaiseCanExecuteChanged();
        ExecuteDemoOperationSecondaryCommand.RaiseCanExecuteChanged();
        ExecuteCapabilityActionCommand.RaiseCanExecuteChanged();
        RefreshSettingActionAvailability();
    }

    private void RebuildOperationCatalog()
    {
        var previousState = OperationGroups
            .SelectMany(static group => group.Operations)
            .ToDictionary(
                static operation => operation.Key,
                static operation => (operation.InputValue, operation.Status),
                StringComparer.Ordinal);
        var groups = DemoOperationCatalogBuilder.Build(
            CapabilityDomains,
            publicCapabilitySummary ?? selectedModelCapabilitySummary,
            operationSupport);

        OperationGroups.Clear();
        foreach (var group in groups)
        {
            foreach (var operation in group.Operations)
            {
                if (previousState.TryGetValue(operation.Key, out var state))
                {
                    operation.InputValue = state.InputValue;
                    operation.Status = state.Status;
                }
            }
            OperationGroups.Add(group);
        }

        RefreshOperationAvailability();
        OnPropertyChanged(nameof(HasOperationGroups));
        OnPropertyChanged(nameof(IsOperationCatalogEmpty));
        OnPropertyChanged(nameof(CanAdjustFeedbackSurface));
        LogStore.Append(
            DemoLogSource.SDK,
            DemoLogLevel.Info,
            $"loaded {OperationGroups.Sum(static group => group.Operations.Count)} dynamic operation(s)");
    }

    private void RefreshOperationAvailability()
    {
        foreach (var operation in OperationGroups.SelectMany(static group => group.Operations))
        {
            operation.UpdateAvailability(HasActiveSession, IsReady, IsSessionCommandBusy, SessionStateText);
        }
    }

    private bool CanExecuteOperation(DemoOperationKind kind) =>
        OperationGroups.SelectMany(static group => group.Operations)
            .Any(operation => operation.Kind == kind && operation.IsEnabled);

    private bool FilterSetting(object item)
    {
        if (item is not SettingItem setting)
        {
            return false;
        }

        if (SelectedSettingsSourceFilter == SettingsSourceFilter.Master &&
            setting.Entry.Source != ScannerSdkLib.CapabilityEntrySource.Master)
        {
            return false;
        }

        if (SelectedSettingsSourceFilter == SettingsSourceFilter.Module &&
            setting.Entry.Source != ScannerSdkLib.CapabilityEntrySource.Module)
        {
            return false;
        }

        if (!string.Equals(SelectedSettingGroup, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(setting.Group, SelectedSettingGroup, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = SettingFilterText.Trim();
        return query.Length == 0 ||
            setting.KeyText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            setting.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            setting.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            setting.Kind.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private ScannerSdkLib.DataRule BuildDataRule()
    {
        var encoding = SelectedCharsetOption.Charset.ToEncoding();
        return SelectedDataRuleModeOption.Mode switch
        {
            DataRuleMode.Prefix => ScannerSdkLib.DataRule.Prefix(encoding.GetBytes(DataRuleValueA)),
            DataRuleMode.Suffix => ScannerSdkLib.DataRule.Suffix(encoding.GetBytes(DataRuleValueA)),
            DataRuleMode.HideStart => ScannerSdkLib.DataRule.HideStart(DemoParsing.ParseByteText(DataRuleValueA, "HideStart length")),
            DataRuleMode.HideEnd => ScannerSdkLib.DataRule.HideEnd(DemoParsing.ParseByteText(DataRuleValueA, "HideEnd length")),
            DataRuleMode.HideMiddle => ScannerSdkLib.DataRule.HideMiddle(
                DemoParsing.ParseByteText(DataRuleValueA, "HideMiddle start"),
                DemoParsing.ParseByteText(DataRuleValueB, "HideMiddle length")
            ),
            DataRuleMode.Replace => ScannerSdkLib.DataRule.Replace(
                encoding.GetBytes(DataRuleValueA),
                encoding.GetBytes(DataRuleValueB)
            ),
            _ => throw new InvalidOperationException("Unsupported data rule mode."),
        };
    }

    private void RefreshProductSurfaceForSelectedModel()
    {
        if (string.IsNullOrWhiteSpace(SelectedModelOption.ModelKey))
        {
            selectedModelCapabilitySummary = null;
            CapabilitySummaryText = DemoUiText.Get("models_empty");
            RebuildOperationCatalog();
            return;
        }

        try
        {
            var profile = backend.GetDeviceModelProfile(SelectedModelOption.ModelKey);
            selectedModelCapabilitySummary = profile?.Capability;
            CapabilitySummaryText = profile?.Capability.DisplaySummary ?? "No public model profile available.";
            RebuildOperationCatalog();
            RefreshDiagnostics();
        }
        catch (Exception ex)
        {
            selectedModelCapabilitySummary = null;
            CapabilitySummaryText = $"Unable to load profile: {ex.Message}";
            RebuildOperationCatalog();
        }
    }

    private void RefreshModelOptionsFromSdk()
    {
        IsLoadingSupportedModels = true;
        SupportedModelsError = string.Empty;
        LogStore.Append(
            DemoLogSource.SDK,
            DemoLogLevel.Info,
            $"supported model loading started transport={SelectedTransportOption.TransportType}");
        try
        {
            var current = SelectedModelOption.ModelKey;
            var models = DemoCommandCatalog.BuildModelOptions(backend, SelectedTransportOption.TransportType);
            if (models.Count == 0)
            {
                ModelOptions.Clear();
                ModelOptions.Add(new ModelOption(string.Empty, DemoUiText.Get("models_empty")));
                SelectedModelOption = ModelOptions[0];
                SupportedModelsError = DemoUiText.Get("models_empty");
                LogStore.Append(
                    DemoLogSource.SDK,
                    DemoLogLevel.Warn,
                    $"supported model loading returned empty transport={SelectedTransportOption.TransportType}");
                return;
            }

            ModelOptions.Clear();
            foreach (var model in models)
            {
                ModelOptions.Add(model);
            }

            SelectedModelOption = ModelOptions.FirstOrDefault(option => option.ModelKey == current)
                ?? ModelOptions.First();
            LogStore.Append(
                DemoLogSource.SDK,
                DemoLogLevel.Info,
                $"supported model loading succeeded transport={SelectedTransportOption.TransportType} count={models.Count}");
        }
        catch (Exception ex)
        {
            ModelOptions.Clear();
            ModelOptions.Add(new ModelOption(string.Empty, DemoUiText.Get("models_unavailable")));
            SelectedModelOption = ModelOptions[0];
            SupportedModelsError = string.Format(DemoUiText.Get("models_load_failed_detail"), ex.Message);
            LogStore.Append(DemoLogSource.SDK, DemoLogLevel.Warn, $"supported model list unavailable: {ex.Message}");
        }
        finally
        {
            IsLoadingSupportedModels = false;
            RefreshInteractionSurface();
        }
    }

    private Task ReloadSupportedModelsAsync()
    {
        RefreshModelOptionsFromSdk();
        return Task.CompletedTask;
    }

    private static IReadOnlyList<DemoLanguageOption> BuildLanguageOptions() =>
    [
        new("system", DemoUiText.Get("language_system")),
        new("zh-CN", "简体中文"),
        new("en", "English"),
    ];

    private void RefreshLanguageOptions()
    {
        var selectedTag = SelectedLanguageOption.Tag;
        LanguageOptions.Clear();
        foreach (var option in BuildLanguageOptions())
        {
            LanguageOptions.Add(option);
        }
        selectedLanguageOption = LanguageOptions.First(option => option.Tag == selectedTag);
        OnPropertyChanged(nameof(SelectedLanguageOption));

        var selectedTransport = SelectedTransportOption.TransportType;
        TransportOptions.Clear();
        foreach (var option in DemoCommandCatalog.BuildTransportOptions(backend))
        {
            TransportOptions.Add(option);
        }
        selectedTransportOption = TransportOptions.First(option => option.TransportType == selectedTransport);
        OnPropertyChanged(nameof(SelectedTransportOption));
    }

    private void RefreshDataRuleOptionsFromSdk()
    {
        if (!sessionCoordinator.IsInitialized)
        {
            return;
        }

        var selectedMode = SelectedDataRuleModeOption.Mode;
        var mappings = new (DataRuleMode Mode, ScannerSdkLib.DataRuleKind Kind)[]
        {
            (DataRuleMode.Prefix, ScannerSdkLib.DataRuleKind.Prefix),
            (DataRuleMode.Suffix, ScannerSdkLib.DataRuleKind.Suffix),
            (DataRuleMode.HideStart, ScannerSdkLib.DataRuleKind.HideStart),
            (DataRuleMode.HideEnd, ScannerSdkLib.DataRuleKind.HideEnd),
            (DataRuleMode.HideMiddle, ScannerSdkLib.DataRuleKind.HideMiddle),
            (DataRuleMode.Replace, ScannerSdkLib.DataRuleKind.Replace),
        };
        DataRuleModeOptions.Clear();
        foreach (var (mode, kind) in mappings)
        {
            var label = backend.GetDataRuleKindLabel(kind);
            DataRuleModeOptions.Add(new DataRuleModeOption(
                mode,
                backend.Localize(label.LocalizationKey, label.FallbackDisplayName, DemoUiText.Culture)));
        }
        selectedDataRuleModeOption = DataRuleModeOptions.First(option => option.Mode == selectedMode);
        OnPropertyChanged(nameof(SelectedDataRuleModeOption));
        OnPropertyChanged(nameof(DataRuleValueBVisibility));
    }

    private static IReadOnlyList<ModelOption> BuildInitialModelOptions()
    {
        return [new ModelOption(string.Empty, DemoUiText.Get("models_loading"))];
    }

    private string FormatDiscoveryHint()
    {
        if (string.IsNullOrWhiteSpace(SelectedModelOption.ModelKey))
        {
            return DemoUiText.Get("models_select_hint");
        }

        return SelectedTransportOption.TransportType switch
        {
            ScannerSdkLib.TransportType.UsbHid => $"USB HID discovery uses SDK profile filtering for {SelectedModelOption.ModelKey}.",
            ScannerSdkLib.TransportType.UsbSerial => "USB CDC discovery matches the scanner's USB VID/PID. You can also enter a COM port manually.",
            ScannerSdkLib.TransportType.BleGatt => $"BLE scan filters by public profile for {SelectedModelOption.ModelKey}.",
            _ => "Selected transport is not a primary desktop demo path.",
        };
    }

    private string FormatFilterQuery()
    {
        var query = SettingFilterText.Trim();
        return query.Length == 0 ? "<none>" : query;
    }

    private string BuildInteractionHint()
    {
        if (!sessionCoordinator.IsInitialized)
        {
            return "Initialize SDK first. Before that, discovery and session actions stay locked.";
        }

        if (!HasActiveSession)
        {
            return SelectedDiscoveryDevice is null
                ? "Select a discovered device before connecting. Discovery actions are available."
                : "Device selected. You can connect now, but console actions remain locked until the session is ready.";
        }

        if (!IsReady)
        {
            return $"Session active but not ready yet: {SessionStateText}. Wait for ready state before running scan, feedback, settings, or data rule commands.";
        }

        var blockedActions = new List<string>();
        if (!CanTriggerScanSurface)
        {
            blockedActions.Add("trigger scan");
        }

        if (!CanGetBatterySurface)
        {
            blockedActions.Add("battery");
        }

        if (!CanSetAckBeepSurface)
        {
            blockedActions.Add("ack beep");
        }

        if (!CanSetVibrationSurface)
        {
            blockedActions.Add("vibration");
        }

        if (!CanOpenSettingsSurface)
        {
            blockedActions.Add("settings");
        }

        if (!CanExecuteDataRuleSurface)
        {
            blockedActions.Add("data rules");
        }

        if (blockedActions.Count > 0)
        {
            return $"Session ready, but this device does not support: {string.Join(", ", blockedActions)}.";
        }

        return "Session ready. Scan, feedback control, settings read/write, and data rule execution are available.";
    }

    private string BuildSettingsCapabilityHint()
    {
        if (!HasActiveSession)
        {
            return "No active session. Settings actions stay disabled until a device is connected.";
        }

        if (!IsReady)
        {
            return $"Session not ready: {SessionStateText}. Wait before reading or writing settings.";
        }

        if (CanReadSettingsSurface && CanWriteSettingsSurface)
        {
            return "This device supports both settings read and settings write.";
        }

        if (CanReadSettingsSurface)
        {
            return "This device supports settings read only. Write actions stay disabled.";
        }

        if (CanWriteSettingsSurface)
        {
            return "This device supports settings write only. Read actions stay disabled.";
        }

        return "This device does not expose public settings read/write support in the current session.";
    }

    private string BuildQuickScenarioHint()
    {
        if (!HasActiveSession)
        {
            return "Quick scenarios stay disabled until a device is connected.";
        }

        if (!IsReady)
        {
            return $"Quick scenarios are waiting for ready state. Current session state: {SessionStateText}.";
        }

        var blocked = new List<string>();
        if (!CanTriggerScanSurface)
        {
            blocked.Add("quick scan");
        }

        if (!CanRefreshInfoSurface)
        {
            blocked.Add("info snapshot");
        }

        if (!CanGetBatterySurface)
        {
            blocked.Add("battery snapshot");
        }

        if (!CanOpenSettingsSurface)
        {
            blocked.Add("advanced settings");
        }

        if (!CanExecuteDataRuleSurface)
        {
            blocked.Add("data rule builder");
        }

        return blocked.Count == 0
            ? "All quick scenarios are available."
            : $"Unavailable in quick scenarios: {string.Join(", ", blocked)}.";
    }

    private string BuildDataRuleCapabilityHint()
    {
        if (!HasActiveSession)
        {
            return "Data Rule builder requires an active device session.";
        }

        if (!IsReady)
        {
            return $"Data Rule builder is locked until ready. Current state: {SessionStateText}.";
        }

        return CanExecuteDataRuleSurface
            ? "Data Rule execution is available for this device."
            : "This device/session does not support public Data Rule execution.";
    }

    private void RefreshDiagnostics()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SDK Version: {SdkVersion}");
        builder.AppendLine($"Demo Version: {typeof(MainWindowViewModel).Assembly.GetName().Version}");
        builder.AppendLine($"Fake Mode: {IsFakeMode}");
        builder.AppendLine($"Initialized: {sessionCoordinator.IsInitialized}");
        builder.AppendLine($"Workflow: {WorkflowState}");
        builder.AppendLine($"Selected Model: {SelectedModelOption.ModelKey}");
        builder.AppendLine($"Transport: {SelectedTransportOption.TransportType}");
        builder.AppendLine($"COM Target: {(IsSerialTransport ? ManualSerialPort : "<not applicable>")}");
        builder.AppendLine($"Windows: {Environment.OSVersion}");
        builder.AppendLine($"Framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Process Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Native DLL: {nativeDllStatus}");
        builder.AppendLine($"USB HID Trace: {EnableUsbHidTrace}");
        builder.AppendLine($"Connected: {IsConnected}");
        builder.AppendLine($"Ready: {IsReady}");
        builder.AppendLine($"Settings: {settings.Count}");
        builder.AppendLine($"Recent Failure: {recentFailure?.DisplayText ?? "<none>"}");
        DiagnosticsSummary = builder.ToString().TrimEnd();
    }

    private static string BuildDeviceActionConfirmationMessage(DemoDeviceActionItem item, string input)
    {
        return BuildDeviceActionConfirmationMessage(
            item.DisplayName,
            item.GroupName,
            item.RiskBadgeText,
            item.RequiresValue,
            input);
    }

    private static string BuildDeviceActionConfirmationMessage(
        string displayName,
        string groupName,
        string riskBadgeText,
        bool requiresValue,
        string input)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Format(DemoUiText.Get("device_action_confirm_action"), displayName));
        builder.AppendLine(string.Format(DemoUiText.Get("device_action_confirm_group"), groupName));
        builder.AppendLine(string.Format(DemoUiText.Get("device_action_confirm_risk"), riskBadgeText));
        if (requiresValue)
        {
            builder.AppendLine(string.Format(DemoUiText.Get("device_action_confirm_input"), input));
        }

        builder.Append(DemoUiText.Get("device_action_confirm_tail"));
        return builder.ToString();
    }

    private string BuildCompatibilityRecord()
    {
        return DemoCompatibilityRecordBuilder.Build(new DemoCompatibilityRecordContext(
            SdkVersion,
            SelectedModelOption.ModelKey,
            SelectedTransportOption.TransportType.ToString(),
            SessionStateText,
            SelectedDeviceSummary,
            ResolvedModelSummary,
            CapabilitySummaryText,
            OperationSupportSummary,
            DeviceInfoText,
            BatteryText,
            ScanCount,
            RecentFailureText,
            commandTraceHistory,
            LogStore.ExportFiltered(IncludeSensitivePayloadsInExport),
            IncludeSensitivePayloadsInExport,
            IsFakeMode,
            WorkflowState.ToString(),
            IsSerialTransport ? ManualSerialPort : "<not applicable>",
            nativeDllStatus));
    }

    private void SessionCommandRunner_BusyChanged(bool busy)
    {
        InvokeOnUi(() =>
        {
            OnPropertyChanged(nameof(IsSessionCommandBusy));
            RefreshInteractionSurface();
        });
    }

    private void AttachSession(IDemoSessionHandle active)
    {
        active.StateChanged += Session_StateChanged;
        active.ScanReceived += Session_ScanReceived;
        active.FailureReceived += Session_FailureReceived;
        active.InitializationStageReceived += Session_InitializationStageReceived;
        active.CommandTraceReceived += Session_CommandTraceReceived;
    }

    private void DetachSession(IDemoSessionHandle active)
    {
        active.StateChanged -= Session_StateChanged;
        active.ScanReceived -= Session_ScanReceived;
        active.FailureReceived -= Session_FailureReceived;
        active.InitializationStageReceived -= Session_InitializationStageReceived;
        active.CommandTraceReceived -= Session_CommandTraceReceived;
    }

    private void Session_StateChanged(ScannerSdkLib.SessionState state)
    {
        InvokeOnUi(() =>
        {
            SessionStateText = state.ToString();
            IsReady = state == ScannerSdkLib.SessionState.Ready;
            IsConnected = state is ScannerSdkLib.SessionState.Connected or ScannerSdkLib.SessionState.Ready or ScannerSdkLib.SessionState.Busy;
            if (IsConnected)
            {
                WorkflowState = DemoWorkflowState.Connected;
            }
            else if (state is ScannerSdkLib.SessionState.Disconnected or ScannerSdkLib.SessionState.Error)
            {
                sessionCommandRunner.CancelPending();
                if (sessionCoordinator.ActiveSession is { } active)
                {
                    DetachSession(active);
                    sessionCoordinator.ReleaseActiveSession(active);
                }
                WorkflowState = DemoWorkflowState.Idle;
                SelectedWorkspaceIndex = DiscoveryWorkspaceIndex;
                publicCapabilitySummary = null;
                operationSupport = null;
                RebuildOperationCatalog();
            }
            LogStore.Append(DemoLogSource.Session, DemoLogLevel.Info, $"state={state}");
            RefreshDiagnostics();
            RefreshInteractionSurface();
        });
    }

    private void Session_ScanReceived(ScannerSdkLib.ScanEvent scan)
    {
        InvokeOnUi(() =>
        {
            ScanCount++;
            LastScanText = SelectedCharsetOption.Charset.ToEncoding().GetString(scan.TextBytes);
            LastScanRawHex = Convert.ToHexString(scan.RawBytes);
            LastScanMetadata = $"timestamp={scan.TimestampMs} barcodeType={scan.BarcodeType} textBytes={scan.TextBytes.Length} rawBytes={scan.RawBytes.Length}";
            LogStore.Append(DemoLogSource.Scan, DemoLogLevel.Info, DemoFormatters.FormatScan(scan, SelectedCharsetOption.Charset));
        });
    }

    private void Session_FailureReceived(ScannerSdkLib.SessionFailure failure)
    {
        InvokeOnUi(() =>
        {
            recentFailure = DemoErrorFormatter.FromSessionFailure(failure);
            OnPropertyChanged(nameof(RecentFailure));
            RecentFailureText = recentFailure.DisplayText;
            LogStore.Append(DemoLogSource.Session, DemoLogLevel.Error, RecentFailureText);
        });
    }

    private void Session_InitializationStageReceived(ScannerSdkLib.SessionInitializationStageEvent stage)
    {
        InvokeOnUi(() => LogStore.Append(
            DemoLogSource.Session,
            stage.Success ? DemoLogLevel.Info : DemoLogLevel.Warn,
            $"init stage={stage.Stage} trace={stage.TraceId} success={stage.Success} error={stage.ErrorCode} message={stage.Message ?? string.Empty}"
        ));
    }

    private void Session_CommandTraceReceived(ScannerSdkLib.CommandTrace trace)
    {
        // The SDK-level event is also subscribed; keep this hook for future session-local UI without double logging.
        _ = trace;
    }

    private void Sdk_DiscoveryReceived(ScannerSdkLib.DiscoveredDevice device)
    {
        InvokeOnUi(() =>
        {
            var item = new DemoDeviceItem(
                device.Name,
                device.DeviceId,
                device.TransportType,
                device.ModelKey,
                device.Rssi,
                device.MatchReason ?? string.Empty
            );
            var existing = DiscoveryDevices.FirstOrDefault(existing => existing.DeviceId == item.DeviceId);
            if (existing is not null)
            {
                DiscoveryDevices.Remove(existing);
            }

            DiscoveryDevices.Add(item);
            SelectedDiscoveryDevice ??= item;
            LogStore.Append(DemoLogSource.Discovery, DemoLogLevel.Info, $"device id={device.DeviceId} name={device.Name} transport={device.TransportType} model={device.ModelKey}");
        });
    }

    private void Sdk_DiscoveryFailureReceived(ScannerSdkLib.DiscoveryFailure failure)
    {
        InvokeOnUi(() =>
        {
            recentFailure = DemoErrorFormatter.FromDiscoveryFailure(failure);
            OnPropertyChanged(nameof(RecentFailure));
            RecentFailureText = recentFailure.DisplayText;
            WorkflowState = DemoWorkflowState.Idle;
            LogStore.Append(DemoLogSource.Discovery, failure.Recoverable ? DemoLogLevel.Warn : DemoLogLevel.Error, RecentFailureText);
        });
    }

    private void Sdk_LogReceived(ScannerSdkLib.SdkLogEvent logEvent)
    {
        InvokeOnUi(() => LogStore.Append(DemoLogSource.SDK, MapLevel(logEvent.Level), logEvent.Message));
    }

    private void Sdk_UsbHidTraceReceived(string message)
    {
        InvokeOnUi(() => LogStore.Append(DemoLogSource.UsbHid, DemoLogLevel.Debug, message));
    }

    private void Sdk_CommandTraceReceived(ScannerSdkLib.CommandTrace trace)
    {
        InvokeOnUi(() =>
        {
            commandTraceHistory.Add(trace);
            if (commandTraceHistory.Count > 200)
            {
                commandTraceHistory.RemoveAt(0);
            }

            CommandTraceText = string.Join(Environment.NewLine, commandTraceHistory.TakeLast(20).Select(DemoFormatters.FormatTrace));
            LogStore.Append(DemoLogSource.Command, trace.ErrorCode == 0 ? DemoLogLevel.Info : DemoLogLevel.Warn, DemoFormatters.FormatTrace(trace));
        });
    }

    private void InvokeOnUi(Action action)
    {
        if (uiDispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = uiDispatcher.BeginInvoke(action);
    }

    private static DemoLogLevel MapLevel(ScannerSdkLib.SdkLogLevel level)
    {
        return level switch
        {
            ScannerSdkLib.SdkLogLevel.Debug => DemoLogLevel.Debug,
            ScannerSdkLib.SdkLogLevel.Info => DemoLogLevel.Info,
            ScannerSdkLib.SdkLogLevel.Warn => DemoLogLevel.Warn,
            ScannerSdkLib.SdkLogLevel.Error => DemoLogLevel.Error,
            _ => DemoLogLevel.Info,
        };
    }

}
