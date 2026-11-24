using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ScannerSDK.WPFDemo;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private bool shutdownCompleted;
    private bool shutdownStarted;

    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    internal MainWindow(MainWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.ConfirmDanger = ConfirmDanger;
        viewModel.CopyTextRequested += CopyText;
        viewModel.SaveTextRequested += SaveText;
        viewModel.LogStore.Entries.CollectionChanged += LogEntries_CollectionChanged;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await viewModel.InitializeOnLoadedAsync().ConfigureAwait(true);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = SettingsButton.ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = viewModel.LanguageLabelText, IsEnabled = false });
        menu.Items.Add(new Separator());
        foreach (var language in viewModel.LanguageOptions)
        {
            var item = new MenuItem
            {
                Header = language.DisplayName,
                IsCheckable = true,
                IsChecked = language.Tag == viewModel.SelectedLanguageOption.Tag,
            };
            item.Click += (_, _) =>
            {
                menu.IsOpen = false;
                viewModel.SelectedLanguageOption = language;
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = SettingsButton;
        menu.IsOpen = true;
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        if (shutdownStarted)
        {
            return;
        }

        shutdownStarted = true;
        await viewModel.ShutdownAsync().ConfigureAwait(true);
        viewModel.CopyTextRequested -= CopyText;
        viewModel.SaveTextRequested -= SaveText;
        viewModel.LogStore.Entries.CollectionChanged -= LogEntries_CollectionChanged;
        shutdownCompleted = true;
        _ = Dispatcher.BeginInvoke(Close, System.Windows.Threading.DispatcherPriority.Normal);
    }

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (viewModel.LogStore.Entries.Count == 0)
        {
            return;
        }

        var logDataGrid = LogsWorkspace.LogDataGridControl;
        var scrollViewer = FindVisualChild<ScrollViewer>(logDataGrid);
        var isAtBottom = scrollViewer is null ||
            scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset <= 1;
        if (isAtBottom)
        {
            logDataGrid.ScrollIntoView(viewModel.LogStore.Entries[^1]);
        }
    }

    private bool ConfirmDanger(string title, string message)
    {
        return MessageBox.Show(
            this,
            message,
            string.Format(DemoUiText.Get("confirm_title"), title),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning
        ) == MessageBoxResult.OK;
    }

    private void CopyText(string text)
    {
        try
        {
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
            }
        }
        catch (Exception error)
        {
            viewModel.ReportUiFailure("copy to clipboard", error);
        }
    }

    private void SaveText(string suggestedFileName, string text)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedFileName,
            Filter = DemoUiText.Get("save_text_filter"),
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                File.WriteAllText(dialog.FileName, text);
            }
            catch (Exception error)
            {
                viewModel.ReportUiFailure("export file", error);
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
