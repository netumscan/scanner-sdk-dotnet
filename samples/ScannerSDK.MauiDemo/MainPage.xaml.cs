using System.ComponentModel;
using Microsoft.Maui.Accessibility;

namespace ScannerSDK.MauiDemo;

public partial class MainPage : ContentPage
{
    private const double CompactWidth = 900;
    private readonly MauiDemoViewModel viewModel;
    private bool? compact;
    private bool synchronizingDomainSelection;
#if WINDOWS
    private Microsoft.UI.Xaml.UIElement? acceleratorOwner;
    private Microsoft.UI.Xaml.Input.KeyboardAccelerator? searchAccelerator;
#endif

    public MainPage(MauiDemoViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        Loaded += HandleLoaded;
        SizeChanged += HandleSizeChanged;
    }

    private async void HandleLoaded(object? sender, EventArgs e)
    {
        Loaded -= HandleLoaded;
        ApplyResponsiveLayout();
        await viewModel.InitializeAsync().ConfigureAwait(true);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.PropertyChanged += HandleViewModelPropertyChanged;
    }

    protected override void OnDisappearing()
    {
        viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        base.OnDisappearing();
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MauiDemoViewModel.Status) && !string.IsNullOrWhiteSpace(viewModel.Status))
        {
            Dispatcher.Dispatch(() => SemanticScreenReader.Default.Announce(viewModel.Status));
        }
        if (e.PropertyName == nameof(MauiDemoViewModel.HasVisibleSettings))
        {
            Dispatcher.Dispatch(ApplySettingsNavigationVisibility);
        }
    }

    private void HandleSizeChanged(object? sender, EventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (Width <= 0)
        {
            return;
        }
        var shouldCompact = Width < CompactWidth;
        if (compact == shouldCompact)
        {
            return;
        }
        compact = shouldCompact;

        DiscoveryColumn.Width = shouldCompact ? GridLength.Star : new GridLength(360);
        SessionColumn.Width = shouldCompact ? new GridLength(0) : GridLength.Star;
        Grid.SetColumn(SessionPanel, shouldCompact ? 0 : 1);
        Grid.SetRow(SessionPanel, shouldCompact ? 1 : 0);

        SessionIdentityColumnTwo.Width = shouldCompact ? new GridLength(0) : GridLength.Star;
        SessionIdentityRowTwo.Height = shouldCompact ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(ResolvedModelPanel, shouldCompact ? 0 : 1);
        Grid.SetRow(ResolvedModelPanel, shouldCompact ? 1 : 0);
        ApplySettingsNavigationVisibility();
    }

    private void ApplySettingsNavigationVisibility()
    {
        var useCompactNavigation = compact == true;
        DomainNavigatorColumn.Width = useCompactNavigation ? new GridLength(0) : new GridLength(190);
        DomainNavigatorPanel.IsVisible = viewModel.HasVisibleSettings && !useCompactNavigation;
        CompactDomainPicker.IsVisible = viewModel.HasVisibleSettings && useCompactNavigation;
    }

    private void HandleDomainSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!synchronizingDomainSelection)
        {
            ScrollToDomain(e.CurrentSelection.FirstOrDefault() as MauiSettingGroup);
        }
    }

    private void HandleCompactDomainChanged(object? sender, EventArgs e)
    {
        if (!synchronizingDomainSelection)
        {
            ScrollToDomain(CompactDomainPicker.SelectedItem as MauiSettingGroup);
        }
    }

    private void ScrollToDomain(MauiSettingGroup? group)
    {
        if (group is null || group.Count == 0)
        {
            return;
        }
        viewModel.SelectedSettingGroup = group;
        DynamicSettingsList.ScrollTo(group[0], group, ScrollToPosition.Start, animate: true);
    }

    private void HandleSettingsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        var group = viewModel.GetSettingGroupForVisibleItemIndex(e.CenterItemIndex);
        if (group is null || ReferenceEquals(group, viewModel.SelectedSettingGroup))
        {
            return;
        }
        synchronizingDomainSelection = true;
        try
        {
            viewModel.SelectedSettingGroup = group;
            DomainNavigator.SelectedItem = group;
            CompactDomainPicker.SelectedItem = group;
        }
        finally
        {
            synchronizingDomainSelection = false;
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if WINDOWS
        RegisterSearchAccelerator();
#endif
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
#if WINDOWS
        UnregisterSearchAccelerator();
#endif
        base.OnHandlerChanging(args);
    }

#if WINDOWS
    private void RegisterSearchAccelerator()
    {
        UnregisterSearchAccelerator();
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement owner)
        {
            return;
        }
        var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = global::Windows.System.VirtualKey.F,
            Modifiers = global::Windows.System.VirtualKeyModifiers.Control,
        };
        accelerator.Invoked += HandleSearchAcceleratorInvoked;
        owner.KeyboardAccelerators.Add(accelerator);
        acceleratorOwner = owner;
        searchAccelerator = accelerator;
    }

    private void UnregisterSearchAccelerator()
    {
        if (acceleratorOwner is not null && searchAccelerator is not null)
        {
            searchAccelerator.Invoked -= HandleSearchAcceleratorInvoked;
            acceleratorOwner.KeyboardAccelerators.Remove(searchAccelerator);
        }
        acceleratorOwner = null;
        searchAccelerator = null;
    }

    private void HandleSearchAcceleratorInvoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Dispatcher.Dispatch(() => SettingSearchEntry.Focus());
    }
#endif
}
