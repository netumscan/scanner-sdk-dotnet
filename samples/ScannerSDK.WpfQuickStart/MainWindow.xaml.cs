using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ScannerSDK.WpfQuickStart;

public partial class MainWindow : Window
{
    private const double CompactWidth = 900;
    private readonly QuickStartViewModel viewModel;
    private bool initialized;
    private bool compact;
    private bool shutdownStarted;
    private bool shutdownComplete;

    internal MainWindow(QuickStartViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Loaded += HandleLoaded;
        SizeChanged += HandleSizeChanged;
        Closing += HandleClosing;
    }

    private async void HandleLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
        {
            return;
        }
        initialized = true;
        await viewModel.InitializeAsync();
    }

    private void HandleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var shouldCompact = ActualWidth > 0 && ActualWidth < CompactWidth;
        if (shouldCompact == compact)
        {
            return;
        }
        compact = shouldCompact;
        if (compact)
        {
            WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            WorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(0);
            WorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(0);
            Grid.SetColumn(SessionPanel, 0);
            Grid.SetRow(SessionPanel, 1);
            SessionPanel.Margin = new Thickness(0, 24, 0, 0);
        }
        else
        {
            WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(360);
            WorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(24);
            WorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(SessionPanel, 2);
            Grid.SetRow(SessionPanel, 0);
            SessionPanel.Margin = new Thickness(0);
        }
    }

    private async void HandleClosing(object? sender, CancelEventArgs e)
    {
        if (shutdownComplete)
        {
            return;
        }
        e.Cancel = true;
        if (shutdownStarted)
        {
            return;
        }
        shutdownStarted = true;
        try
        {
            await viewModel.ShutdownAsync();
        }
        finally
        {
            shutdownComplete = true;
            Close();
        }
    }
}
