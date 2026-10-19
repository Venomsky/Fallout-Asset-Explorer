using Avalonia.Controls;
using FalloutAssetExplorer.App.ViewModels;

namespace FalloutAssetExplorer.App.Views;

public partial class FormatCoverageDashboardView : UserControl
{
    public FormatCoverageDashboardView()
    {
        InitializeComponent();
        DataContext = new FormatCoverageDashboardViewModel();
    }
}
