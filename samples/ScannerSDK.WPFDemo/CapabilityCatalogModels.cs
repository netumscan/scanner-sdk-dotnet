using System.Collections.ObjectModel;
using System.Globalization;
using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

public sealed class CapabilityCatalogDomainItem
{
    public CapabilityCatalogDomainItem(
        ScannerSdkLib.CapabilityDomain definition,
        string title,
        IEnumerable<CapabilityCatalogSectionItem> sections)
    {
        Definition = definition;
        Title = title;
        Sections = new ObservableCollection<CapabilityCatalogSectionItem>(sections);
    }

    public ScannerSdkLib.CapabilityDomain Definition { get; }

    public string Key => Definition.Key;

    public string Title { get; }

    public ObservableCollection<CapabilityCatalogSectionItem> Sections { get; }

    public IReadOnlyList<SettingItem> Settings => Sections.SelectMany(static section => section.Settings).ToArray();

    public IReadOnlyList<DemoDeviceActionItem> Actions => Sections.SelectMany(static section => section.Actions).ToArray();

    public int ItemCount => Settings.Count + Actions.Count;

    public int ReadableCount => Settings.Count(static item => item.SupportsRead);

    public int WritableCount => Settings.Count(static item => item.SupportsWrite);

    public int ActionCount => Actions.Count;

    public string Summary => string.Format(
        DemoUiText.Get("capability_domain_summary"),
        ReadableCount,
        WritableCount,
        ActionCount);

    public string ItemCountText => string.Format(DemoUiText.Get("capability_item_count"), ItemCount);
}

public sealed class CapabilityCatalogSectionItem
{
    public CapabilityCatalogSectionItem(
        string familyKey,
        string familyTitle,
        string sectionKey,
        string sectionTitle,
        IEnumerable<SettingItem> settings,
        IEnumerable<DemoDeviceActionItem> actions)
    {
        FamilyKey = familyKey;
        FamilyTitle = familyTitle;
        SectionKey = sectionKey;
        SectionTitle = sectionTitle;
        Settings = new ObservableCollection<SettingItem>(settings);
        Actions = new ObservableCollection<DemoDeviceActionItem>(actions);
    }

    public string FamilyKey { get; }

    public string FamilyTitle { get; }

    public string SectionKey { get; }

    public string SectionTitle { get; }

    public string Title => new[] { FamilyTitle, SectionTitle }
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .DefaultIfEmpty(DemoUiText.Get("capability_section_other"))
        .Aggregate((left, right) => $"{left} / {right}");

    public ObservableCollection<SettingItem> Settings { get; }

    public ObservableCollection<DemoDeviceActionItem> Actions { get; }
}

public sealed record CapabilityOptionItem(string Value, string DisplayName);

public sealed record CapabilityCatalogSnapshot(
    IReadOnlyList<ScannerSdkLib.CapabilityDomain> Domains,
    IReadOnlyList<ScannerSdkLib.CapabilityEntry> Entries,
    IReadOnlyList<ScannerSdkLib.CapabilityLabel> Labels);

public static class CapabilityCatalogBuilder
{
    public static IReadOnlyList<CapabilityCatalogDomainItem> Build(
        ScannerSdkLib.ScannerSdk sdk,
        IReadOnlyList<ScannerSdkLib.CapabilityDomain> domains,
        IReadOnlyList<ScannerSdkLib.CapabilityEntry> entries,
        IReadOnlyList<ScannerSdkLib.CapabilityLabel> labels,
        CultureInfo culture) => Build(
            domains,
            entries,
            labels,
            culture,
            sdk.Localize,
            sdk.Localize);

    public static IReadOnlyList<CapabilityCatalogDomainItem> Build(
        IReadOnlyList<ScannerSdkLib.CapabilityDomain> domains,
        IReadOnlyList<ScannerSdkLib.CapabilityEntry> entries,
        IReadOnlyList<ScannerSdkLib.CapabilityLabel> labels,
        CultureInfo culture,
        Func<ScannerSdkLib.CapabilityDomain, CultureInfo, string> localizeDomain,
        Func<ScannerSdkLib.CapabilityLabel, CultureInfo, string> localizeLabel)
    {
        var resolver = new CatalogLabelResolver(labels, culture, localizeDomain, localizeLabel);
        var visibleEntries = entries
            .Where(static entry => entry.VisibleByDefault &&
                entry.Availability == ScannerSdkLib.CapabilityEntryAvailability.Available)
            .ToArray();

        return domains
            .Where(static domain => domain.VisibleByDefault)
            .Select(domain => BuildDomain(domain, visibleEntries, resolver))
            .Where(static domain => domain is not null)
            .Cast<CapabilityCatalogDomainItem>()
            .OrderBy(static domain => domain.Definition.SortOrder)
            .ThenBy(static domain => domain.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static CapabilityCatalogDomainItem? BuildDomain(
        ScannerSdkLib.CapabilityDomain domain,
        IReadOnlyList<ScannerSdkLib.CapabilityEntry> entries,
        CatalogLabelResolver resolver)
    {
        var domainEntries = entries.Where(entry => entry.DomainKey == domain.Key).ToArray();
        if (domainEntries.Length == 0)
        {
            return null;
        }

        var sections = domainEntries
            .GroupBy(static entry => (entry.FamilyKey, entry.SectionKey))
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(resolver.EntryRank)
                    .ThenBy(resolver.EntryTitle, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(static entry => entry.EntryKey, StringComparer.Ordinal)
                    .ToArray();
                return new CapabilityCatalogSectionItem(
                    group.Key.FamilyKey,
                    resolver.TaxonomyTitle(ScannerSdkLib.CapabilityLabelKind.Family, group.Key.FamilyKey),
                    group.Key.SectionKey,
                    resolver.TaxonomyTitle(ScannerSdkLib.CapabilityLabelKind.Section, group.Key.SectionKey),
                    ordered
                        .Where(static entry => entry.Kind == ScannerSdkLib.CapabilityEntryKind.Setting)
                        .Select(entry => new SettingItem(
                            entry,
                            entry.Options,
                            resolver.EntryTitle(entry),
                            resolver.EnumOptions(entry))),
                    ordered
                        .Where(static entry => entry.Kind == ScannerSdkLib.CapabilityEntryKind.Action)
                        .Select(entry => new DemoDeviceActionItem(entry, resolver.EntryTitle(entry))));
            })
            .OrderBy(section => resolver.Rank(ScannerSdkLib.CapabilityLabelKind.Family, section.FamilyKey))
            .ThenBy(section => resolver.Rank(ScannerSdkLib.CapabilityLabelKind.Section, section.SectionKey))
            .ThenBy(static section => section.FamilyTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static section => section.SectionTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var title = resolver.DomainTitle(domain);
        return new CapabilityCatalogDomainItem(
            domain,
            string.IsNullOrWhiteSpace(title) ? Humanize(domain.Key) : title,
            sections);
    }

    private static string Humanize(string key) => string.Join(
        " ",
        key.Replace('-', '_')
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => char.ToUpperInvariant(value[0]) + value[1..]));

    private sealed class CatalogLabelResolver
    {
        private readonly IReadOnlyDictionary<(ScannerSdkLib.CapabilityLabelKind Kind, string Key), ScannerSdkLib.CapabilityLabel> byKindAndKey;
        private readonly IReadOnlyDictionary<(ScannerSdkLib.CapabilityLabelKind Kind, string Owner, string Key), ScannerSdkLib.CapabilityLabel> byOwner;
        private readonly Func<ScannerSdkLib.CapabilityDomain, CultureInfo, string> localizeDomain;
        private readonly Func<ScannerSdkLib.CapabilityLabel, CultureInfo, string> localizeLabel;

        public CatalogLabelResolver(
            IReadOnlyList<ScannerSdkLib.CapabilityLabel> labels,
            CultureInfo culture,
            Func<ScannerSdkLib.CapabilityDomain, CultureInfo, string> localizeDomain,
            Func<ScannerSdkLib.CapabilityLabel, CultureInfo, string> localizeLabel)
        {
            Culture = culture;
            this.localizeDomain = localizeDomain;
            this.localizeLabel = localizeLabel;
            byKindAndKey = labels
                .GroupBy(static label => (label.Kind, label.Key))
                .ToDictionary(static group => group.Key, static group => group.First());
            byOwner = labels
                .GroupBy(static label => (label.Kind, label.OwnerKey, label.Key))
                .ToDictionary(static group => group.Key, static group => group.First());
        }

        public CultureInfo Culture { get; }

        public string DomainTitle(ScannerSdkLib.CapabilityDomain domain) =>
            localizeDomain(domain, Culture);

        public string TaxonomyTitle(ScannerSdkLib.CapabilityLabelKind kind, string key)
        {
            return byKindAndKey.TryGetValue((kind, key), out var label)
                ? Resolve(label)
                : Humanize(key);
        }

        public int Rank(ScannerSdkLib.CapabilityLabelKind kind, string key) =>
            byKindAndKey.TryGetValue((kind, key), out var label) ? label.Rank : int.MaxValue;

        public string EntryTitle(ScannerSdkLib.CapabilityEntry entry)
        {
            var kind = entry.Kind == ScannerSdkLib.CapabilityEntryKind.Action
                ? ScannerSdkLib.CapabilityLabelKind.Action
                : ScannerSdkLib.CapabilityLabelKind.Setting;
            return Find(kind, string.Empty, entry.SemanticKey) is { } label
                ? Resolve(label)
                : Humanize(string.IsNullOrWhiteSpace(entry.SemanticKey) ? entry.EntryKey : entry.SemanticKey);
        }

        public int EntryRank(ScannerSdkLib.CapabilityEntry entry)
        {
            var kind = entry.Kind == ScannerSdkLib.CapabilityEntryKind.Action
                ? ScannerSdkLib.CapabilityLabelKind.Action
                : ScannerSdkLib.CapabilityLabelKind.Setting;
            return Find(kind, string.Empty, entry.SemanticKey)?.Rank ?? int.MaxValue;
        }

        public IReadOnlyList<CapabilityOptionItem> EnumOptions(ScannerSdkLib.CapabilityEntry entry) =>
            entry.Options.Select(option => new CapabilityOptionItem(
                option.Value,
                Find(ScannerSdkLib.CapabilityLabelKind.EnumValue, entry.SemanticKey, option.Value) is { } label
                    ? Resolve(label)
                    : option.Value)).ToArray();

        private ScannerSdkLib.CapabilityLabel? Find(
            ScannerSdkLib.CapabilityLabelKind kind,
            string owner,
            string key)
        {
            return byOwner.TryGetValue((kind, owner, key), out var owned)
                ? owned
                : byKindAndKey.TryGetValue((kind, key), out var label) ? label : null;
        }

        private string Resolve(ScannerSdkLib.CapabilityLabel label) =>
            localizeLabel(label, Culture);
    }
}
