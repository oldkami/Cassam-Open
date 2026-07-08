using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Reports;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Reports;

/// <summary>
/// Unit tests for <see cref="ReportsViewModel"/>. PR 10
/// (T2.11). Verifies that the VM populates its observable
/// collections + status messages + export commands.
/// </summary>
public class ReportsViewModelTests
{
    private static (ReportsViewModel Vm, RecordingExportPipeline Pipeline) BuildVm()
    {
        var pipeline = new RecordingExportPipeline();
        var service = new InMemoryReportsService();
        var vm = new ReportsViewModel(service, pipeline);
        return (vm, pipeline);
    }

    [Fact]
    public async Task RefreshDailySalesAsync_populates_rows_and_status()
    {
        var (vm, _) = BuildVm();

        await vm.RefreshDailySalesCommand.ExecuteAsync(null);

        vm.DailySalesRows.Should().NotBeEmpty();
        vm.StatusMessage.Should().Contain("Ventas del día cargadas");
    }

    [Fact]
    public async Task RefreshInventoryAsync_populates_inventory_rows()
    {
        var (vm, _) = BuildVm();

        await vm.RefreshInventoryCommand.ExecuteAsync(null);

        vm.InventoryRows.Should().NotBeEmpty();
        vm.StatusMessage.Should().Contain("Inventario cargado");
    }

    [Fact]
    public async Task RefreshCashSessionAsync_populates_session_rows()
    {
        var (vm, _) = BuildVm();

        await vm.RefreshCashSessionCommand.ExecuteAsync(null);

        vm.CashSessionRows.Should().NotBeEmpty();
        vm.StatusMessage.Should().Contain("Cierres de caja cargados");
    }

    [Fact]
    public async Task ExportDailySalesAsync_invokes_pipeline_with_csv_format()
    {
        var (vm, pipeline) = BuildVm();
        await vm.RefreshDailySalesCommand.ExecuteAsync(null);

        using var output = new MemoryStream();
        await vm.ExportDailySalesAsync(ExportFormat.Csv, output);

        pipeline.LastFormat.Should().Be(ExportFormat.Csv);
        pipeline.LastDailySalesCount.Should().Be(vm.DailySalesRows.Count);
    }

    [Fact]
    public async Task ExportInventoryAsync_throws_for_unsupported_pdf_in_platform_neutral_pipeline()
    {
        // This test uses the REAL platform-neutral pipeline
        // (not the recording one) because we want to verify
        // the explicit PDF rejection path.
        var pipeline = new PlatformNeutralReportExportPipeline();
        var vm = new ReportsViewModel(new InMemoryReportsService(), pipeline);
        await vm.RefreshInventoryCommand.ExecuteAsync(null);

        using var output = new MemoryStream();
        var act = () => vm.ExportInventoryAsync(ExportFormat.Pdf, output);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ExportCashSessionAsync_routes_through_pipeline()
    {
        var (vm, pipeline) = BuildVm();
        await vm.RefreshCashSessionCommand.ExecuteAsync(null);

        using var output = new MemoryStream();
        await vm.ExportCashSessionAsync(ExportFormat.Json, output);

        pipeline.LastFormat.Should().Be(ExportFormat.Json);
        pipeline.LastCashSessionCount.Should().Be(vm.CashSessionRows.Count);
    }

    [Fact]
    public void Constructor_rejects_null_service()
    {
        var pipeline = new RecordingExportPipeline();
        var act = () => new ReportsViewModel(null!, pipeline);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_pipeline()
    {
        var service = new InMemoryReportsService();
        var act = () => new ReportsViewModel(service, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsBusy_is_false_initially()
    {
        var (vm, _) = BuildVm();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task IsBusy_resets_to_false_after_refresh_completes()
    {
        var (vm, _) = BuildVm();

        await vm.RefreshDailySalesCommand.ExecuteAsync(null);

        vm.IsBusy.Should().BeFalse();
    }

    /// <summary>
    /// Captures the last export call so the test can assert
    /// that the VM routes through the pipeline correctly
    /// without coupling to the underlying CSV/Excel/JSON
    /// emission details (those are exercised in the per-format
    /// tests).
    /// </summary>
    private sealed class RecordingExportPipeline : IReportExportPipeline
    {
        public ExportFormat LastFormat { get; private set; }
        public int LastDailySalesCount { get; private set; }
        public int LastInventoryCount { get; private set; }
        public int LastCashSessionCount { get; private set; }

        public Task ExportDailySalesAsync(ExportFormat format, IReadOnlyList<DailySalesRow> rows, Stream output, System.Threading.CancellationToken ct = default)
        {
            LastFormat = format;
            LastDailySalesCount = rows.Count;
            return Task.CompletedTask;
        }

        public Task ExportInventoryAsync(ExportFormat format, IReadOnlyList<InventoryRow> rows, Stream output, System.Threading.CancellationToken ct = default)
        {
            LastFormat = format;
            LastInventoryCount = rows.Count;
            return Task.CompletedTask;
        }

        public Task ExportCashSessionAsync(ExportFormat format, IReadOnlyList<CashSessionRow> rows, Stream output, System.Threading.CancellationToken ct = default)
        {
            LastFormat = format;
            LastCashSessionCount = rows.Count;
            return Task.CompletedTask;
        }
    }
}
