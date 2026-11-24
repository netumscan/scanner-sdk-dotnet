using System.Windows;

namespace ScannerSDK.WpfQuickStart;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var viewModel = new QuickStartViewModel(new ScannerQuickStartBackend());
        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }
}
