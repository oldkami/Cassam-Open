using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cassam.Ui.Hardware.Common.Reports;

/// <summary>
/// Manager-side reports view-model (T2.11). Holds the three
/// report row collections (daily sales, inventory, cash
/// session), drives the export commands, and exposes a
/// status line for the toolbar UI.
///
/// <para>
/// The VM does not know which <c>.xaml</c> page renders it;
/// it only knows about <see cref="IReportsService"/> +
/// <see cref="IReportExportPipeline"/>. DataGrid column
/// definitions live on the View side as <c>DataGridTextColumn</c>
/// bindings (auto columns OFF per design — taxes need
/// formatted colour per design §5.3).
/// </para>
///
/// <para>
/// The VM is implemented in <c>Cassam.Ui.Hardware.Common</c>
/// (platform-neutral, tests live in <c>Cassam.Ui.Tests</c>)
/// and the actual XAML view (<c>ReportsView.xaml</c>) lives in
/// the head project (<c>Cassam.Ui/Reports/</c>). DI wires the
/// VM to the View one-to-one.
/// </para>
/// </summary>
public partial class ReportsViewModel : ObservableObject
{
    private readonly IReportsService _service;
    private readonly IReportExportPipeline _exportPipeline;

    /// <summary>Daily sales rows (refreshed when <see cref="RefreshDailySalesAsync"/> runs).</summary>
    public ObservableCollection<DailySalesRow> DailySalesRows { get; } = new();

    /// <summary>Inventory rows (refreshed when <see cref="RefreshInventoryAsync"/> runs).</summary>
    public ObservableCollection<InventoryRow> InventoryRows { get; } = new();

    /// <summary>Cash-session rows (refreshed when <see cref="RefreshCashSessionAsync"/> runs).</summary>
    public ObservableCollection<CashSessionRow> CashSessionRows { get; } = new();

    /// <summary>One-line status shown above the data grid.</summary>
    [ObservableProperty]
    private string _statusMessage = "Listo.";

    /// <summary>True while any refresh is running — used to disable buttons.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The selected day for the "Ventas del día" tab. UTC.</summary>
    [ObservableProperty]
    private DateTimeOffset _selectedDay = DateTimeOffset.UtcNow;

    public ReportsViewModel(IReportsService service, IReportExportPipeline exportPipeline)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(exportPipeline);
        _service = service;
        _exportPipeline = exportPipeline;
    }

    /// <summary>Refresh the daily sales rows for <see cref="SelectedDay"/>.</summary>
    [RelayCommand]
    public async Task RefreshDailySalesAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var report = await _service.GetDailySalesAsync(SelectedDay, ct);
            DailySalesRows.Clear();
            foreach (var r in report.Rows) DailySalesRows.Add(r);
            StatusMessage = $"Ventas del día cargadas — {report.Rows.Count} registros.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Refresh the inventory snapshot.</summary>
    [RelayCommand]
    public async Task RefreshInventoryAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var report = await _service.GetInventoryAsync(ct);
            InventoryRows.Clear();
            foreach (var r in report.Rows) InventoryRows.Add(r);
            StatusMessage = $"Inventario cargado — {report.Rows.Count} productos.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Refresh the cash-session history.</summary>
    [RelayCommand]
    public async Task RefreshCashSessionAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var report = await _service.GetCashSessionReportAsync(days: 30, ct);
            CashSessionRows.Clear();
            foreach (var r in report.Rows) CashSessionRows.Add(r);
            StatusMessage = $"Cierres de caja cargados — {report.Rows.Count} sesiones.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Export the currently-loaded rows using <paramref name="format"/>.</summary>
    public async Task ExportDailySalesAsync(ExportFormat format, Stream output, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await _exportPipeline.ExportDailySalesAsync(format, DailySalesRows, output, ct);
            StatusMessage = $"Ventas exportadas a {format} — {DailySalesRows.Count} filas.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportInventoryAsync(ExportFormat format, Stream output, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await _exportPipeline.ExportInventoryAsync(format, InventoryRows, output, ct);
            StatusMessage = $"Inventario exportado a {format} — {InventoryRows.Count} filas.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportCashSessionAsync(ExportFormat format, Stream output, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await _exportPipeline.ExportCashSessionAsync(format, CashSessionRows, output, ct);
            StatusMessage = $"Cierre de caja exportado a {format} — {CashSessionRows.Count} filas.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// Selects the right exporter per <see cref="ExportFormat"/>
/// + row shape. Implemented as an interface so tests can
/// substitute a recording pipeline and so the QuestPDF-backed
/// pipeline in the Uno project can be swapped in without
/// touching the VM.
/// </summary>
public interface IReportExportPipeline
{
    Task ExportDailySalesAsync(ExportFormat format, IReadOnlyList<DailySalesRow> rows, Stream output, CancellationToken ct = default);
    Task ExportInventoryAsync(ExportFormat format, IReadOnlyList<InventoryRow> rows, Stream output, CancellationToken ct = default);
    Task ExportCashSessionAsync(ExportFormat format, IReadOnlyList<CashSessionRow> rows, Stream output, CancellationToken ct = default);
}

/// <summary>
/// Platform-neutral pipeline that serves the CSV / Excel /
/// JSON formats. PDF is wired by
/// <see cref="QuestPdfReportExportPipeline"/> in the Uno
/// project (PR 11+ refinement); for now the platform-neutral
/// pipeline throws <see cref="NotSupportedException"/> on
/// PDF so the manager view still shows the toolbar but the
/// PDF button raises a clear "not yet wired" exception.
/// </summary>
public sealed class PlatformNeutralReportExportPipeline : IReportExportPipeline
{
    /// <inheritdoc />
    public Task ExportDailySalesAsync(ExportFormat format, IReadOnlyList<DailySalesRow> rows, Stream output, CancellationToken ct = default)
        => ExportCoreAsync(format, "PDF", output,
            csv: () => CsvReportExporter.WriteDailySalesAsync(rows, output, ct),
            excel: () => ExcelReportExporter.WriteDailySalesAsync(rows, output, ct),
            json: () => JsonReportExporter.WriteDailySalesAsync(rows, output, ct));

    /// <inheritdoc />
    public Task ExportInventoryAsync(ExportFormat format, IReadOnlyList<InventoryRow> rows, Stream output, CancellationToken ct = default)
        => ExportCoreAsync(format, "PDF", output,
            csv: () => CsvReportExporter.WriteInventoryAsync(rows, output, ct),
            excel: () => ExcelReportExporter.WriteInventoryAsync(rows, output, ct),
            json: () => JsonReportExporter.WriteInventoryAsync(rows, output, ct));

    /// <inheritdoc />
    public Task ExportCashSessionAsync(ExportFormat format, IReadOnlyList<CashSessionRow> rows, Stream output, CancellationToken ct = default)
        => ExportCoreAsync(format, "PDF", output,
            csv: () => CsvReportExporter.WriteCashSessionAsync(rows, output, ct),
            excel: () => ExcelReportExporter.WriteCashSessionAsync(rows, output, ct),
            json: () => JsonReportExporter.WriteCashSessionAsync(rows, output, ct));

    private static async Task ExportCoreAsync(
        ExportFormat format,
        string unsupportedFormatName,
        Stream output,
        Func<Task> csv,
        Func<Task> excel,
        Func<Task> json)
    {
        ArgumentNullException.ThrowIfNull(output);
        switch (format)
        {
            case ExportFormat.Csv:
                await csv();
                break;
            case ExportFormat.Excel:
                await excel();
                break;
            case ExportFormat.Json:
                await json();
                break;
            case ExportFormat.Pdf:
            default:
                throw new NotSupportedException(
                    $"{unsupportedFormatName} export is not wired in the platform-neutral pipeline. " +
                    "Wire QuestPdfReportExportPipeline in Cassam.Ui for PDF export.");
        }
    }
}
