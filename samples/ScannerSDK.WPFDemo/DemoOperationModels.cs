using System.Collections.ObjectModel;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

public enum DemoOperationKind
{
    InitializeSession,
    RefreshInfo,
    ReadBattery,
    ReadStorageUsage,
    TriggerScan,
    SetCurrentTimestamp,
    SetAckBeep,
    SetVibration,
    CapabilityAction,
}

public sealed class DemoOperationItem : ViewModelBase
{
    private bool isEnabled;
    private string disabledReason = string.Empty;
    private string inputValue = string.Empty;
    private string status = DemoUiText.Get("device_action_status_idle");

    public DemoOperationItem(
        string key,
        DemoOperationKind kind,
        string title,
        string description,
        string primaryButtonText,
        int sortOrder,
        ScannerSdkLib.CapabilityEntry? entry = null,
        string secondaryButtonText = "")
    {
        Key = key;
        Kind = kind;
        Title = title;
        Description = description;
        PrimaryButtonText = primaryButtonText;
        SecondaryButtonText = secondaryButtonText;
        SortOrder = sortOrder;
        Entry = entry;
    }

    public string Key { get; }

    public DemoOperationKind Kind { get; }

    public string Title { get; }

    public string Description { get; }

    public string PrimaryButtonText { get; }

    public string SecondaryButtonText { get; }

    public int SortOrder { get; }

    public ScannerSdkLib.CapabilityEntry? Entry { get; }

    public bool RequiresValue => Kind == DemoOperationKind.CapabilityAction && Entry?.RequiresValue == true;

    public bool HasSecondaryAction => !string.IsNullOrWhiteSpace(SecondaryButtonText);

    public bool Dangerous => Entry?.RiskLevel != null && Entry.RiskLevel != ScannerSdkLib.CapabilityRiskLevel.Normal;

    public string RiskBadgeText => Entry is null
        ? DemoUiText.Get("dynamic_operation_semantic_badge")
        : string.Format(
            Dangerous ? DemoUiText.Get("device_action_dangerous_badge") : DemoUiText.Get("device_action_normal_badge"),
            RiskLevelText);

    public string RiskLevelText => Entry?.RiskLevel switch
    {
        ScannerSdkLib.CapabilityRiskLevel.Destructive => DemoUiText.Get("device_action_risk_destructive"),
        ScannerSdkLib.CapabilityRiskLevel.Connectivity => DemoUiText.Get("device_action_risk_connectivity"),
        ScannerSdkLib.CapabilityRiskLevel.DataLoss => DemoUiText.Get("device_action_risk_data_loss"),
        _ => DemoUiText.Get("device_action_risk_normal"),
    };

    public string ValueHint => Entry is null || string.IsNullOrWhiteSpace(Entry.ValueHint)
        ? DemoUiText.Get("dynamic_operation_input_hint")
        : Entry.ValueHint;

    public bool IsEnabled
    {
        get => isEnabled;
        private set => SetProperty(ref isEnabled, value);
    }

    public string DisabledReason
    {
        get => disabledReason;
        private set
        {
            if (SetProperty(ref disabledReason, value))
            {
                OnPropertyChanged(nameof(AvailabilityText));
            }
        }
    }

    public string AvailabilityText => IsEnabled
        ? DemoUiText.Get("dynamic_operation_available")
        : DisabledReason;

    public string InputValue
    {
        get => inputValue;
        set => SetProperty(ref inputValue, value);
    }

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    public void UpdateAvailability(bool hasActiveSession, bool isReady, bool isBusy, string sessionState)
    {
        if (!hasActiveSession)
        {
            SetAvailability(false, DemoUiText.Get("dynamic_operation_disabled_no_session"));
            return;
        }

        if (!isReady)
        {
            SetAvailability(
                false,
                string.Format(DemoUiText.Get("dynamic_operation_disabled_not_ready"), sessionState));
            return;
        }

        if (isBusy)
        {
            SetAvailability(false, DemoUiText.Get("dynamic_operation_disabled_busy"));
            return;
        }

        SetAvailability(true, string.Empty);
    }

    private void SetAvailability(bool enabled, string reason)
    {
        IsEnabled = enabled;
        DisabledReason = reason;
        OnPropertyChanged(nameof(AvailabilityText));
    }
}

public sealed class DemoOperationGroup
{
    public DemoOperationGroup(string key, string title, int sortOrder, IEnumerable<DemoOperationItem> operations)
    {
        Key = key;
        Title = title;
        SortOrder = sortOrder;
        Operations = new ObservableCollection<DemoOperationItem>(
            operations.OrderBy(static operation => operation.SortOrder)
                .ThenBy(static operation => operation.Title, StringComparer.CurrentCultureIgnoreCase));
    }

    public string Key { get; }

    public string Title { get; }

    public int SortOrder { get; }

    public ObservableCollection<DemoOperationItem> Operations { get; }

    public string Summary => string.Format(DemoUiText.Get("dynamic_operation_group_summary"), Operations.Count);
}

internal static class DemoOperationCatalogBuilder
{
    public static IReadOnlyList<DemoOperationGroup> Build(
        IReadOnlyList<CapabilityCatalogDomainItem> domains,
        ScannerSdkLib.DeviceCapabilitySummary? summary,
        ScannerSdkLib.SessionOperationSupport? operationSupport)
    {
        if (summary is null)
        {
            return [];
        }

        var groups = new Dictionary<string, OperationGroupBuilder>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        AddSemanticOperations(groups, keys, summary, operationSupport);

        var actionOrder = 0;
        foreach (var domain in domains.OrderBy(static domain => domain.Definition.SortOrder))
        {
            var group = GetGroup(
                groups,
                $"capability:{domain.Key}",
                domain.Title,
                1000 + (int)Math.Min(domain.Definition.SortOrder, (uint)(int.MaxValue - 1000)));

            foreach (var section in domain.Sections)
            {
                foreach (var setting in section.Settings)
                {
                    AddPromotedSetting(group, keys, setting, operationSupport, ref actionOrder);
                }

                foreach (var action in section.Actions)
                {
                    AddCapabilityAction(group, keys, action, summary, operationSupport, ref actionOrder);
                }
            }
        }

        return groups.Values
            .Where(static group => group.Operations.Count > 0)
            .OrderBy(static group => group.SortOrder)
            .ThenBy(static group => group.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(static group => new DemoOperationGroup(group.Key, group.Title, group.SortOrder, group.Operations))
            .ToArray();
    }

    private static void AddSemanticOperations(
        IDictionary<string, OperationGroupBuilder> groups,
        ISet<string> keys,
        ScannerSdkLib.DeviceCapabilitySummary summary,
        ScannerSdkLib.SessionOperationSupport? support)
    {
        var session = GetGroup(
            groups,
            "session",
            DemoUiText.Get("dynamic_operation_group_session"),
            0);
        if (summary.SupportsDeviceCommands && (support?.SupportsInitializeSession ?? true))
        {
            Add(session, keys, new DemoOperationItem(
                "session.initialize",
                DemoOperationKind.InitializeSession,
                DemoUiText.Get("scan_workspace_button_initialize"),
                DemoUiText.Get("dynamic_operation_initialize_description"),
                DemoUiText.Get("scan_workspace_button_initialize"),
                10));
        }
        if (summary.SupportsDeviceCommands && (support?.SupportsRefreshInfo ?? true))
        {
            Add(session, keys, new DemoOperationItem(
                "session.refresh_info",
                DemoOperationKind.RefreshInfo,
                DemoUiText.Get("scan_workspace_button_refresh_info"),
                DemoUiText.Get("dynamic_operation_refresh_description"),
                DemoUiText.Get("scan_workspace_button_refresh_info"),
                20));
        }

        var queries = GetGroup(
            groups,
            "queries",
            DemoUiText.Get("dynamic_operation_group_queries"),
            100);
        if (summary.SupportsBattery && (support?.SupportsGetBatteryInfo ?? true))
        {
            Add(queries, keys, new DemoOperationItem(
                "query.battery",
                DemoOperationKind.ReadBattery,
                DemoUiText.Get("scan_workspace_button_battery"),
                DemoUiText.Get("dynamic_operation_battery_description"),
                DemoUiText.Get("actions_button_battery"),
                10));
        }
        if (summary.SupportsDeviceCommands)
        {
            Add(queries, keys, new DemoOperationItem(
                "query.storage_usage",
                DemoOperationKind.ReadStorageUsage,
                DemoUiText.Get("actions_button_memory_usage"),
                DemoUiText.Get("dynamic_operation_storage_description"),
                DemoUiText.Get("actions_button_memory_usage"),
                20));
        }
    }

    private static void AddPromotedSetting(
        OperationGroupBuilder group,
        ISet<string> keys,
        SettingItem setting,
        ScannerSdkLib.SessionOperationSupport? support,
        ref int actionOrder)
    {
        if (!setting.SupportsWrite || !setting.IsBoolean)
        {
            return;
        }

        DemoOperationKind kind;
        string primaryText;
        string secondaryText;
        if (string.Equals(setting.Entry.SemanticKey, "SetAckBeepEnabled", StringComparison.Ordinal))
        {
            if (support?.SupportsSetAckBeepEnabled == false)
            {
                return;
            }
            kind = DemoOperationKind.SetAckBeep;
            primaryText = DemoUiText.Get("scan_workspace_button_ack_on");
            secondaryText = DemoUiText.Get("scan_workspace_button_ack_off");
        }
        else if (string.Equals(setting.Entry.SemanticKey, "SetVibrationEnabled", StringComparison.Ordinal))
        {
            if (support?.SupportsSetVibrationEnabled == false)
            {
                return;
            }
            kind = DemoOperationKind.SetVibration;
            primaryText = DemoUiText.Get("scan_workspace_button_vibration_on");
            secondaryText = DemoUiText.Get("scan_workspace_button_vibration_off");
        }
        else
        {
            return;
        }

        Add(group, keys, new DemoOperationItem(
            setting.EntryKey,
            kind,
            setting.DisplayName,
            string.IsNullOrWhiteSpace(setting.Notes)
                ? DemoUiText.Get("dynamic_operation_boolean_description")
                : setting.Notes,
            primaryText,
            100 + actionOrder++,
            setting.Entry,
            secondaryText));
    }

    private static void AddCapabilityAction(
        OperationGroupBuilder group,
        ISet<string> keys,
        DemoDeviceActionItem action,
        ScannerSdkLib.DeviceCapabilitySummary summary,
        ScannerSdkLib.SessionOperationSupport? support,
        ref int actionOrder)
    {
        if (!action.Entry.SupportsExecute)
        {
            return;
        }

        var kind = DemoOperationKind.CapabilityAction;
        var primaryText = DemoUiText.Get("device_action_execute_button");
        var requiresValue = action.Entry.RequiresValue;

        if (string.Equals(action.Entry.SemanticKey, "TriggerScan", StringComparison.Ordinal))
        {
            if (!summary.SupportsScanControl || support?.SupportsTriggerScan == false)
            {
                return;
            }
            kind = DemoOperationKind.TriggerScan;
            primaryText = DemoUiText.Get("scan_workspace_button_trigger_scan");
            requiresValue = false;
        }
        else if (string.Equals(action.Entry.SemanticKey, "SetRtcTimestamp", StringComparison.Ordinal))
        {
            kind = DemoOperationKind.SetCurrentTimestamp;
            primaryText = DemoUiText.Get("scan_workspace_button_timestamp");
            requiresValue = false;
        }

        var entry = requiresValue == action.Entry.RequiresValue
            ? action.Entry
            : action.Entry with { RequiresValue = requiresValue };
        Add(group, keys, new DemoOperationItem(
            action.KeyText,
            kind,
            action.DisplayName,
            BuildActionDescription(action, kind),
            primaryText,
            200 + actionOrder++,
            entry));
    }

    private static string BuildActionDescription(DemoDeviceActionItem action, DemoOperationKind kind)
    {
        if (kind == DemoOperationKind.SetCurrentTimestamp)
        {
            return DemoUiText.Get("dynamic_operation_timestamp_description");
        }
        if (kind == DemoOperationKind.TriggerScan)
        {
            return DemoUiText.Get("dynamic_operation_trigger_description");
        }
        if (!string.IsNullOrWhiteSpace(action.Entry.Notes))
        {
            return action.Entry.Notes;
        }
        if (!string.IsNullOrWhiteSpace(action.ValueHintDisplay))
        {
            return action.ValueHintDisplay;
        }
        return DemoUiText.Get("dynamic_operation_capability_description");
    }

    private static OperationGroupBuilder GetGroup(
        IDictionary<string, OperationGroupBuilder> groups,
        string key,
        string title,
        int sortOrder)
    {
        if (!groups.TryGetValue(key, out var group))
        {
            group = new OperationGroupBuilder(key, title, sortOrder);
            groups.Add(key, group);
        }
        return group;
    }

    private static void Add(OperationGroupBuilder group, ISet<string> keys, DemoOperationItem operation)
    {
        if (keys.Add(operation.Key))
        {
            group.Operations.Add(operation);
        }
    }

    private sealed record OperationGroupBuilder(
        string Key,
        string Title,
        int SortOrder)
    {
        public List<DemoOperationItem> Operations { get; } = [];
    }
}
