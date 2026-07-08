using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Reports;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Reports;

/// <summary>
/// Unit tests for <see cref="CsvReportExporter"/>. PR 10 (T2.11).
/// Validates the header row + RFC 4180 escaping rules across all
/// three row shapes (daily sales, inventory, cash session).
/// </summary>
public class CsvReportExporterTests
{
    [Fact]
    public async Task WriteDailySalesAsync_emits_header_and_one_row_per_input()
    {
        var rows = new[]
        {
            new DailySalesRow(
                Guid.NewGuid(),
                DateTimeOffset.Parse("2026-06-24T09:15:00+00:00"),
                "FE-001-00001",
                "Consumidor final",
                null,
                4521.80m, 859.33m, 5381.13m,
                "Efectivo",
                "Cajero Demo",
                "TRANSMITTED",
                "3 items, 2 distinct SKUs"),
        };
        var csv = await ExportAsync(s => CsvReportExporter.WriteDailySalesAsync(rows, s));

        var lines = SplitLines(csv);
        lines[0].Should().Be("VentaId,Fecha,NumeroFactura,Cliente,NIT,SubtotalCOP,IVACOP,TotalCOP,MetodoPago,Cajero,Estado,Lineas");
        lines[1].Should().Contain("FE-001-00001");
        lines[1].Should().Contain("Efectivo");
        lines[1].Should().Contain("TRANSMITTED");
        lines[1].Should().Contain("4521.80");
        lines[1].Should().Contain("5381.13");
    }

    [Fact]
    public async Task WriteInventoryAsync_emits_known_columns()
    {
        var rows = new[]
        {
            new InventoryRow(
                Guid.NewGuid(),
                "001-7701234",
                "7701234567890",
                "Arroz Diana 1kg",
                "S",
                3500m,
                145,
                30,
                DateTimeOffset.Parse("2026-06-24T06:00:00+00:00")),
        };
        var csv = await ExportAsync(s => CsvReportExporter.WriteInventoryAsync(rows, s));

        var lines = SplitLines(csv);
        lines[0].Should().Be("ProductoId,SKU,CodigoBarras,Nombre,CategoriaImpuesto,PrecioUnitarioCOP,Stock,ReorderLevel,UltimoMovimiento");
        lines[1].Should().Contain("001-7701234");
        lines[1].Should().Contain("Arroz Diana 1kg");
        lines[1].Should().Contain("145");
    }

    [Fact]
    public async Task WriteCashSessionAsync_emits_tax_buckets()
    {
        var rows = new[]
        {
            new CashSessionRow(
                Guid.NewGuid(),
                DateTimeOffset.Parse("2026-06-24T08:00:00+00:00"),
                DateTimeOffset.Parse("2026-06-24T18:00:00+00:00"),
                "Administrador Demo",
                50000m, 173420m, 173420m, 0m, 47,
                TaxBucket5Base: 0m, TaxBucket5Iva: 0m,
                TaxBucket19Base: 110800m, TaxBucket19Iva: 21052m,
                TaxBucketExemptBase: 22600m),
        };
        var csv = await ExportAsync(s => CsvReportExporter.WriteCashSessionAsync(rows, s));

        var lines = SplitLines(csv);
        lines[0].Should().Contain("BaseGravada19COP");
        lines[1].Should().Contain("110800.00");
        lines[1].Should().Contain("21052.00");
        lines[1].Should().Contain("22600.00");
        lines[1].Should().Contain("Administrador Demo");
    }

    [Fact]
    public async Task WriteAsync_escapes_commas_and_quotes_per_rfc4180()
    {
        var rows = new[]
        {
            new DailySalesRow(
                Guid.NewGuid(),
                DateTimeOffset.Parse("2026-06-24T09:15:00+00:00"),
                "FE-001,with,commas",
                "Cliente \"con comillas\"",
                null,
                100m, 19m, 119m,
                "Efectivo",
                "Cajero Demo",
                "TRANSMITTED",
                "line\nwith\nnewlines"),
        };
        var csv = await ExportAsync(s => CsvReportExporter.WriteDailySalesAsync(rows, s));
        csv.Should().Contain("\"FE-001,with,commas\"");
        csv.Should().Contain("\"Cliente \"\"con comillas\"\"\"");
        csv.Should().Contain("\"line\nwith\nnewlines\"");
    }

    [Fact]
    public async Task WriteAsync_includes_utf8_bom_for_excel_compatibility()
    {
        var rows = Array.Empty<DailySalesRow>();
        var bytes = await ExportBytesAsync(s => CsvReportExporter.WriteDailySalesAsync(rows, s));
        // The UTF-8 BOM is 0xEF 0xBB 0xBF.
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);
    }

    [Fact]
    public async Task WriteAsync_uses_CRLF_line_endings()
    {
        var rows = new[]
        {
            new DailySalesRow(Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, 0, 0, 0, "Efectivo", "Cajero Demo", "TRANSMITTED", "1 line"),
        };
        var csv = await ExportAsync(s => CsvReportExporter.WriteDailySalesAsync(rows, s));
        csv.Should().Contain("\r\n", "RFC 4180 mandates CRLF");
    }

    private static async Task<string> ExportAsync(Action<Stream> write)
    {
        using var ms = new MemoryStream();
        await Task.Run(() => write(ms));
        ms.Position = 0;
        using var reader = new StreamReader(ms, new UTF8Encoding(true));
        return await reader.ReadToEndAsync();
    }

    private static async Task<byte[]> ExportBytesAsync(Action<Stream> write)
    {
        using var ms = new MemoryStream();
        await Task.Run(() => write(ms));
        return ms.ToArray();
    }

    private static string[] SplitLines(string csv)
        => csv.Split(new[] { "\r\n" }, StringSplitOptions.None);
}
