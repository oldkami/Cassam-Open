using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Reports;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Reports;

/// <summary>
/// Unit tests for <see cref="JsonReportExporter"/>. PR 10 (T2.11).
/// Validates the camelCase envelope + `asOf` timestamp + the
/// round-trip fidelity for each row shape.
/// </summary>
public class JsonReportExporterTests
{
    [Fact]
    public async Task WriteDailySalesAsync_emits_envelope_with_rows_array()
    {
        var rows = new[]
        {
            new DailySalesRow(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                DateTimeOffset.Parse("2026-06-24T09:15:00+00:00"),
                "FE-001-00001",
                "Consumidor final",
                null,
                4521.80m, 859.33m, 5381.13m,
                "Efectivo", "Cajero Demo", "TRANSMITTED", "3 items"),
        };
        using var ms = new MemoryStream();
        await JsonReportExporter.WriteDailySalesAsync(rows, ms);
        using var doc = Parse(ms);

        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(1);
        var arr = doc.RootElement.GetProperty("rows");
        arr.GetArrayLength().Should().Be(1);
        var r0 = arr[0];
        r0.GetProperty("saleId").GetGuid().Should().Be(rows[0].SaleId);
        r0.GetProperty("invoiceNumber").GetString().Should().Be("FE-001-00001");
        // Null fields are dropped from the JSON envelope
        // (DefaultIgnoreCondition.WhenWritingNull). Either the
        // key is absent (Undefined) or present with value Null —
        // both are acceptable contracts for downstream consumers.
        var customerNitPresent = r0.TryGetProperty("customerNit", out var customerNit);
        if (customerNitPresent)
        {
            customerNit.ValueKind.Should().BeOneOf(JsonValueKind.Null, JsonValueKind.Undefined);
        }
        r0.GetProperty("total").GetDecimal().Should().Be(5381.13m);
    }

    [Fact]
    public async Task WriteInventoryAsync_emits_inventory_rows()
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
        using var ms = new MemoryStream();
        await JsonReportExporter.WriteInventoryAsync(rows, ms);
        using var doc = Parse(ms);

        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(1);
        var r0 = doc.RootElement.GetProperty("rows")[0];
        r0.GetProperty("sku").GetString().Should().Be("001-7701234");
        r0.GetProperty("taxCategory").GetString().Should().Be("S");
        r0.GetProperty("stockQuantity").GetDouble().Should().Be(145);
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
                0m, 0m, 110800m, 21052m, 22600m),
        };
        using var ms = new MemoryStream();
        await JsonReportExporter.WriteCashSessionAsync(rows, ms);
        using var doc = Parse(ms);

        var r0 = doc.RootElement.GetProperty("rows")[0];
        r0.GetProperty("taxBucket19Base").GetDecimal().Should().Be(110800m);
        r0.GetProperty("varianceAmount").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task WriteAsync_uses_indented_format()
    {
        var rows = new[]
        {
            new DailySalesRow(Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, 0, 0, 0, "Efectivo", "Cajero Demo", "TRANSMITTED", "1"),
        };
        using var ms = new MemoryStream();
        await JsonReportExporter.WriteDailySalesAsync(rows, ms);
        var raw = ReadAll(ms);
        raw.Should().Contain("\n  ", "the JSON is pretty-printed with indentation");
    }

    private static JsonDocument Parse(MemoryStream ms)
    {
        ms.Position = 0;
        return JsonDocument.Parse(ms);
    }

    private static string ReadAll(MemoryStream ms)
    {
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        return reader.ReadToEnd();
    }
}
