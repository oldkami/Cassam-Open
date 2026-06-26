using Cassam.Ui.Hardware.Common.Manager;

namespace Cassam.Ui.Manager;

/// <summary>
/// Code-behind for <see cref="ReportsNavigationView"/>. Drives
/// the three sub-tab buttons + swaps the placeholder title.
/// </summary>
public sealed partial class ReportsNavigationView : Page
{
    /// <summary>The reports-navigation view-model (singleton, DI-resolved).</summary>
    public ReportsNavigationViewModel ViewModel { get; }

    public ReportsNavigationView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<ReportsNavigationViewModel>();
        this.DataContext = ViewModel;
    }

    private void OnDailySalesClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Select(ReportSubTab.DailySales);
        ActiveReportTitle.Text = "Ventas del día";
    }

    private void OnInventoryClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Select(ReportSubTab.Inventory);
        ActiveReportTitle.Text = "Inventario";
    }

    private void OnCashCloseClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Select(ReportSubTab.CashClose);
        ActiveReportTitle.Text = "Cierre de caja";
    }
}