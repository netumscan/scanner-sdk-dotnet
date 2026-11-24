using System.Windows.Controls;

namespace ScannerSDK.WPFDemo;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
    }

    internal DataGrid LogDataGridControl => LogDataGrid;
}
