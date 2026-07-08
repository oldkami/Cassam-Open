using System;
using System.IO;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using Cassam.Ui.Hardware.Common.Reports;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cassam.Ui.Reports;

/// <summary>
/// Code-behind for <see cref="ReportsView"/>. Manages the
/// three sub-tabs (visible / collapsed) + the export toolbar.
///
/// <para>
/// The VM (<see cref="ReportsViewModel"/>) was extended in
/// PR 10 (T2.11) with explicit <c>ExportDailySalesAsync</c> /
/// <c>ExportInventoryAsync</c> / <c>ExportCashSessionAsync</c>
/// methods that accept a <see cref="ExportFormat"/> + a
/// destination stream. The code-behind writes the export to
/// <c>%AppData%/Cassam/Reports/</c> as a platform-neutral
/// sink; the WinUI-specific <c>FileSavePicker</c> integration
/// is a follow-up PR 11 refinement.
/// </para>
/// </summary>
public sealed partial class ReportsView : Page
{
    public ReportsViewModel ViewModel { get; }

    public ReportsView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<ReportsViewModel>();
        this.DataContext = ViewModel;

        // Listen for VM status updates so the footer bar
        // reflects the latest action result.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Load the default tab (Ventas del día).
        _ = RefreshActiveTabAsync();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
        => await RefreshActiveTabAsync();

    private void OnDailySalesClicked(object sender, RoutedEventArgs e)
    {
        ShowOnly(DailySalesContainer);
        ActiveReportTitle.Text = "Ventas del día";
        _ = RefreshActiveTabAsync();
    }

    private void OnInventoryClicked(object sender, RoutedEventArgs e)
    {
        ShowOnly(InventoryContainer);
        ActiveReportTitle.Text = "Inventario";
        _ = RefreshActiveTabAsync();
    }

    private void OnCashCloseClicked(object sender, RoutedEventArgs e)
    {
        ShowOnly(CashSessionContainer);
        ActiveReportTitle.Text = "Cierre de caja";
        _ = RefreshActiveTabAsync();
    }

    private async Task RefreshActiveTabAsync()
    {
        if (DailySalesContainer.Visibility == Visibility.Visible)
        {
            await ViewModel.RefreshDailySalesCommand.ExecuteAsync(null);
        }
        else if (InventoryContainer.Visibility == Visibility.Visible)
        {
            await ViewModel.RefreshInventoryCommand.ExecuteAsync(null);
        }
        else
        {
            await ViewModel.RefreshCashSessionCommand.ExecuteAsync(null);
        }
    }

    private void ShowOnly(FrameworkElement visibleContainer)
    {
        DailySalesContainer.Visibility = (visibleContainer == DailySalesContainer) ? Visibility.Visible : Visibility.Collapsed;
        InventoryContainer.Visibility = (visibleContainer == InventoryContainer) ? Visibility.Visible : Visibility.Collapsed;
        CashSessionContainer.Visibility = (visibleContainer == CashSessionContainer) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportsViewModel.StatusMessage))
        {
            StatusText.Text = ViewModel.StatusMessage ?? "Listo.";
        }
    }

    // ---- Export handlers ----

    private async void OnExportPdfClicked(object sender, RoutedEventArgs e)
        => await ExportActiveAsync(ExportFormat.Pdf, "reporte.pdf");

    private async void OnExportExcelClicked(object sender, RoutedEventArgs e)
        => await ExportActiveAsync(ExportFormat.Excel, "reporte.xlsx");

    private async void OnExportCsvClicked(object sender, RoutedEventArgs e)
        => await ExportActiveAsync(ExportFormat.Csv, "reporte.csv");

    private async Task ExportActiveAsync(ExportFormat format, string defaultFileName)
    {
        using var ms = new MemoryStream();
        if (DailySalesContainer.Visibility == Visibility.Visible)
        {
            await ViewModel.ExportDailySalesAsync(format, ms);
        }
        else if (InventoryContainer.Visibility == Visibility.Visible)
        {
            await ViewModel.ExportInventoryAsync(format, ms);
        }
        else
        {
            await ViewModel.ExportCashSessionAsync(format, ms);
        }

        // Platform-neutral sink: write the bytes to
        // AppData\Roaming\Cassam\Reports\ — the
        // WinUI-specific FileSavePicker hooks in PR 11.
        await SaveToFileAsync(defaultFileName, ms.ToArray());
    }

    private static async Task SaveToFileAsync(string fileName, byte[] bytes)
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var reportsDir = Path.Combine(folder, "Cassam", "Reports");
            Directory.CreateDirectory(reportsDir);
            var path = Path.Combine(reportsDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{fileName}");
            await File.WriteAllBytesAsync(path, bytes);
            System.Diagnostics.Debug.WriteLine($"[Reports] Saved {bytes.Length} bytes to {path}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] Failed to save export: {ex.Message}");
        }
    }
}
