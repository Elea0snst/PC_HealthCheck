using Avalonia.Controls;
using PC_HealthCheck.ViewModels;

namespace PC_HealthCheck.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += (_, _) => BindChartFromViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel oldVm)
            oldVm.ChartRefreshRequested -= OnChartRefreshRequested;
        BindChartFromViewModel();
        if (DataContext is MainWindowViewModel newVm)
            newVm.ChartRefreshRequested += OnChartRefreshRequested;
    }

    private void BindChartFromViewModel()
    {
        if (DataContext is MainWindowViewModel vm)
            MonitoringChart.Render(vm.BuildChartSnapshot());
    }

    private void OnChartRefreshRequested(object? sender, EventArgs e) => BindChartFromViewModel();

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ChartRefreshRequested -= OnChartRefreshRequested;
            vm.Dispose();
        }
        base.OnClosed(e);
    }
}
