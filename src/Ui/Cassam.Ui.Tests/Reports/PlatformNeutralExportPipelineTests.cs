using System;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Reports;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Reports;

/// <summary>
/// Unit tests for <see cref="PlatformNeutralReportExportPipeline"/>.
/// PR 10 (T2.11). Verifies that the platform-neutral
/// implementation rejects PDF (PR 11+ wires QuestPDF in the
/// head project) and routes the other three formats correctly.
/// </summary>
public class PlatformNeutralExportPipelineTests
{
    [Fact]
    public async Task ExportDailySalesAsync_throws_for_pdf()
    {
        var pipeline = new PlatformNeutralReportExportPipeline();
        var rows = new[] {
            new DailySalesRow(Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, 0, 0, 0, "Efectivo", "x", "TRANSMITTED", "0")
        };
        using var output = new MemoryStream();
        var act = () => pipeline.ExportDailySalesAsync(ExportFormat.Pdf, rows, output);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ExportInventoryAsync_throws_for_pdf()
    {
        var pipeline = new PlatformNeutralReportExportPipeline();
        var rows = new[] {
            new InventoryRow(Guid.NewGuid(), "SKU-1", null, "Test", "S", 100m, 10, 5, DateTimeOffset.UtcNow)
        };
        using var output = new MemoryStream();
        var act = () => pipeline.ExportInventoryAsync(ExportFormat.Pdf, rows, output);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ExportCashSessionAsync_throws_for_pdf()
    {
        var pipeline = new PlatformNeutralReportExportPipeline();
        var rows = new[] {
            new CashSessionRow(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "x", 0m, 0m, 0m, 0m, 0, 0m, 0m, 0m, 0m, 0m)
        };
        using var output = new MemoryStream();
        var act = () => pipeline.ExportCashSessionAsync(ExportFormat.Pdf, rows, output);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ExportDailySalesAsync_routes_csv_through_csv_exporter()
    {
        var pipeline = new PlatformNeutralReportExportPipeline();
        var rows = new[] {
            new DailySalesRow(Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.UtcNow, "FE-001-00001", "Cliente", null, 100m, 19m, 119m, "Efectivo", "Cajero", "TRANSMITTED", "1"),
        };
        using var output = new MemoryStream();
        await pipeline.ExportDailySalesAsync(ExportFormat.Csv, rows, output);

        output.Length.Should().BeGreaterThan(0);
        var text = System.Text.Encoding.UTF8.GetString(output.ToArray());
        text.Should().Contain("VentaId");
        text.Should().Contain("FE-001-00001");
    }

    [Fact]
    public async Task ExportDailySalesAsync_routes_json_through_json_exporter()
    {
        var pipeline = new PlatformNeutralReportExportPipeline();
        var rows = new[] {
            new DailySalesRow(Guid.NewGuid(), DateTimeOffset.UtcNow, "FE-001-00001", null, null, 0, 0, 0, "Efectivo", "Cajero", "TRANSMITTED", "1"),
        };
        using var output = new MemoryStream();
        await pipeline.ExportDailySalesAsync(ExportFormat.Json, rows, output);

        var text = System.Text.Encoding.UTF8.GetString(output.ToArray());
        text.Should().Contain("\"asOf\"");
        text.Should().Contain("\"rows\"");
    }

    [Fact]
    public async Task ExportDailySalesAsync_routes_excel_through_excel_exporter()
    {
        var pipeline = new PlatformNeutralReportExportPipeline();
        var rows = new[] {
            new DailySalesRow(Guid.NewGuid(), DateTimeOffset.UtcNow, "FE-001-00001", null, null, 0, 0, 0, "Efectivo", "Cajero", "TRANSMITTED", "1"),
        };
        using var output = new MemoryStream();
        await pipeline.ExportDailySalesAsync(ExportFormat.Excel, rows, output);

        output.Length.Should().BeGreaterThan(0);
        // .xlsx = zip starts with PK (0x50 0x4B)
        output.ToArray()[0].Should().Be(0x50);
        output.ToArray()[1].Should().Be(0x4B);
    }
}
