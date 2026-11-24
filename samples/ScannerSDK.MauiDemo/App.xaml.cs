namespace ScannerSDK.MauiDemo;

public partial class App : Application
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly MauiDemoViewModel viewModel;
#if WINDOWS
    private global::Windows.UI.ViewManagement.AccessibilitySettings? accessibilitySettings;
    private global::Windows.UI.ViewManagement.UISettings? uiSettings;
    private Microsoft.UI.Xaml.Window? nativeWindow;
    private bool highContrastSubscribed;
    private int closeRequested;
    private int allowClose;
#endif

    public App(MauiDemoViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        RequestedThemeChanged += HandleRequestedThemeChanged;
#if WINDOWS
        TryCreateAccessibilityServices();
#endif
        ApplyVisualTheme();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage(viewModel))
        {
            Title = "NT-91 Scanner SDK MAUI Demo",
            Width = 1180,
            Height = 780,
            MinimumWidth = 720,
            MinimumHeight = 600,
        };
        window.Created += HandleWindowCreated;
        window.Destroying += HandleWindowDestroying;
        return window;
    }

    private void HandleWindowCreated(object? sender, EventArgs e)
    {
#if WINDOWS
        if (sender is not Window window || window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window platformWindow)
        {
            return;
        }
        nativeWindow = platformWindow;
        platformWindow.AppWindow.Closing += HandleNativeWindowClosing;
        TrySubscribeHighContrastChanges();
        ApplyVisualTheme();
#endif
    }

    private async void HandleWindowDestroying(object? sender, EventArgs e)
    {
        RequestedThemeChanged -= HandleRequestedThemeChanged;
#if WINDOWS
        TryUnsubscribeHighContrastChanges();
#endif
        try
        {
            await viewModel.ShutdownAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Scanner SDK shutdown failed: {error.GetType().Name}");
        }
    }

    private void HandleRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
        Dispatcher.Dispatch(ApplyVisualTheme);

#if WINDOWS
    private void HandleHighContrastChanged(
        global::Windows.UI.ViewManagement.AccessibilitySettings sender,
        object args) => Dispatcher.Dispatch(ApplyVisualTheme);
#endif

    private void ApplyVisualTheme()
    {
#if WINDOWS
        if (TryApplyHighContrastTheme())
        {
            return;
        }
#endif

        var dark = RequestedTheme == AppTheme.Dark;
        SetThemeColor("CurrentCanvasColor", dark ? "CanvasDarkColor" : "CanvasColor");
        SetThemeColor("CurrentSurfaceColor", dark ? "SurfaceDarkColor" : "SurfaceColor");
        SetThemeColor("CurrentInkColor", dark ? "InkDarkColor" : "InkColor");
        SetThemeColor("CurrentMutedColor", dark ? "MutedDarkColor" : "MutedColor");
        SetThemeColor("CurrentLineColor", dark ? "LineDarkColor" : "LineColor");
        SetThemeColor("CurrentAccentColor", dark ? "AccentDarkColor" : "AccentColor");
        SetThemeColor("CurrentAccentSoftColor", dark ? "AccentSoftDarkColor" : "AccentSoftColor");
        SetThemeColor("CurrentAccentTextColor", dark ? "InkColor" : "SurfaceColor");
        SetThemeColor("CurrentStatusSurfaceColor", "InkColor");
        SetThemeColor("CurrentStatusTextColor", "SurfaceColor");
        SetThemeColor("CurrentWarningSurfaceColor", dark ? "WarningSurfaceDarkColor" : "WarningSurfaceColor");
        SetThemeColor("CurrentWarningStrokeColor", dark ? "WarningStrokeDarkColor" : "WarningStrokeColor");
    }

    private void SetThemeColor(string targetKey, string sourceKey) =>
        Resources[targetKey] = Resources[sourceKey];

    private void SetThemeColor(string targetKey, Microsoft.Maui.Graphics.Color color) =>
        Resources[targetKey] = color;

#if WINDOWS
    private void TryCreateAccessibilityServices()
    {
        try
        {
            accessibilitySettings = new global::Windows.UI.ViewManagement.AccessibilitySettings();
            uiSettings = new global::Windows.UI.ViewManagement.UISettings();
        }
        catch (System.Runtime.InteropServices.COMException error)
        {
            accessibilitySettings = null;
            uiSettings = null;
            LogOptionalAccessibilityFailure("create", error);
        }
    }

    private void TrySubscribeHighContrastChanges()
    {
        if (accessibilitySettings is null || highContrastSubscribed)
        {
            return;
        }
        try
        {
            accessibilitySettings.HighContrastChanged += HandleHighContrastChanged;
            highContrastSubscribed = true;
        }
        catch (System.Runtime.InteropServices.COMException error)
        {
            LogOptionalAccessibilityFailure("subscribe", error);
        }
    }

    private void TryUnsubscribeHighContrastChanges()
    {
        if (accessibilitySettings is null || !highContrastSubscribed)
        {
            return;
        }
        try
        {
            accessibilitySettings.HighContrastChanged -= HandleHighContrastChanged;
        }
        catch (System.Runtime.InteropServices.COMException error)
        {
            LogOptionalAccessibilityFailure("unsubscribe", error);
        }
        finally
        {
            highContrastSubscribed = false;
        }
    }

    private bool TryApplyHighContrastTheme()
    {
        if (accessibilitySettings is null || uiSettings is null)
        {
            return false;
        }
        try
        {
            if (!accessibilitySettings.HighContrast)
            {
                return false;
            }
            var background = ToMauiColor(uiSettings.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Background));
            var foreground = ToMauiColor(uiSettings.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Foreground));
            SetThemeColor("CurrentCanvasColor", background);
            SetThemeColor("CurrentSurfaceColor", background);
            SetThemeColor("CurrentInkColor", foreground);
            SetThemeColor("CurrentMutedColor", foreground);
            SetThemeColor("CurrentLineColor", foreground);
            SetThemeColor("CurrentAccentColor", foreground);
            SetThemeColor("CurrentAccentSoftColor", background);
            SetThemeColor("CurrentAccentTextColor", background);
            SetThemeColor("CurrentStatusSurfaceColor", background);
            SetThemeColor("CurrentStatusTextColor", foreground);
            SetThemeColor("CurrentWarningSurfaceColor", background);
            SetThemeColor("CurrentWarningStrokeColor", foreground);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException error)
        {
            LogOptionalAccessibilityFailure("query", error);
            return false;
        }
    }

    private static void LogOptionalAccessibilityFailure(
        string operation,
        System.Runtime.InteropServices.COMException error) =>
        System.Diagnostics.Debug.WriteLine(
            $"Windows accessibility {operation} is unavailable (0x{error.HResult:X8}); using the application theme.");

    private static Microsoft.Maui.Graphics.Color ToMauiColor(global::Windows.UI.Color color) =>
        Microsoft.Maui.Graphics.Color.FromRgba(color.R, color.G, color.B, color.A);
#endif

#if WINDOWS
    private async void HandleNativeWindowClosing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (Volatile.Read(ref allowClose) != 0)
        {
            return;
        }

        args.Cancel = true;
        if (Interlocked.Exchange(ref closeRequested, 1) != 0)
        {
            return;
        }

        try
        {
            await viewModel.ShutdownAsync().WaitAsync(ShutdownTimeout).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            System.Diagnostics.Debug.WriteLine("Scanner SDK shutdown timed out while closing the window.");
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Scanner SDK shutdown failed: {error.GetType().Name}");
        }
        finally
        {
            Interlocked.Exchange(ref allowClose, 1);
            nativeWindow?.Close();
        }
    }
#endif
}
