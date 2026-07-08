using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Xml;
using Cassam.Ui.Hardware.Common.Reports;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Reports;

/// <summary>
/// Unit tests for <see cref="ExcelReportExporter"/>. PR 10 (T2.11).
/// Verifies the OOXML container is valid (well-formed ZIP,
/// well-formed XML parts, expected row counts + values).
/// </summary>
public class ExcelReportExporterTests
{
    [Fact]
    public async Task WriteDailySalesAsync_produces_valid_ooxml_zip()
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
                "Efectivo", "Cajero Demo", "TRANSMITTED", "3 items"),
        };
        using var ms = new MemoryStream();
        await ExcelReportExporter.WriteDailySalesAsync(rows, ms);

        var (sheet, header, dataRows, _) = ExtractSheet(ms, expectedRows: 1);

        header.Should().Contain("VentaId");
        header.Should().Contain("TotalCOP");
        dataRows[0].Should().Contain("FE-001-00001");
        dataRows[0].Should().Contain("4521.80");
        var ns = new System.Xml.XmlNamespaceManager(sheet.NameTable);
        ns.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        sheet.SelectNodes("/x:worksheet/x:sheetData/x:row", ns)!.Count.Should().Be(2,
            "header row + one data row");
    }

    [Fact]
    public async Task WriteInventoryAsync_emits_inventory_columns()
    {
        var rows = new[]
        {
            new InventoryRow(
                Guid.NewGuid(),
                "001-7701234",
                "7701234567890",
                "Arroz Diana 1kg",
                "S",
                3500m, 145, 30,
                DateTimeOffset.Parse("2026-06-24T06:00:00+00:00")),
        };
        using var ms = new MemoryStream();
        await ExcelReportExporter.WriteInventoryAsync(rows, ms);

        var (_, header, dataRows, _) = ExtractSheet(ms, expectedRows: 1);
        header.Should().Contain("CategoriaImpuesto");
        dataRows[0].Should().Contain("Arroz Diana 1kg");
        dataRows[0].Should().Contain("145");
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
        await ExcelReportExporter.WriteCashSessionAsync(rows, ms);

        var (_, header, dataRows, _) = ExtractSheet(ms, expectedRows: 1);
        header.Should().Contain("BaseGravada19COP");
        dataRows[0].Should().Contain("110800.00");
    }

    [Fact]
    public async Task WriteAsync_handles_empty_rowset()
    {
        var rows = Array.Empty<DailySalesRow>();
        using var ms = new MemoryStream();
        await ExcelReportExporter.WriteDailySalesAsync(rows, ms);

        var (sheet, _, _, _) = ExtractSheet(ms, expectedRows: 0);
        var ns = new System.Xml.XmlNamespaceManager(sheet.NameTable);
        ns.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        sheet.SelectNodes("/x:worksheet/x:sheetData/x:row", ns)!.Count.Should().Be(1,
            "header row only");
    }

    [Fact]
    public async Task WriteAsync_escapes_special_xml_characters()
    {
        var rows = new[]
        {
            new DailySalesRow(
                Guid.NewGuid(),
                DateTimeOffset.Parse("2026-06-24T09:15:00+00:00"),
                "<FE&001>",
                "Cliente <bold>\"x\"</bold>",
                null,
                100m, 19m, 119m,
                "Efectivo", "Cajero Demo", "TRANSMITTED", "1"),
        };
        using var ms = new MemoryStream();
        await ExcelReportExporter.WriteDailySalesAsync(rows, ms);

        var (_, _, _, rawXml) = ExtractSheet(ms, expectedRows: 1);
        // The raw OOXML must contain the XML entities (not the
        // literal characters) so Excel opens without choking on
        // invalid markup.
        rawXml.Should().Contain("&lt;FE&amp;001&gt;");
        rawXml.Should().Contain("&lt;bold&gt;");
        rawXml.Should().Contain("&quot;");
    }

    /// <summary>
    /// Pull the <c>xl/worksheets/sheet1.xml</c> entry out of the
    /// zip + parse it as XML. Returns the parsed document, the
    /// header text, the data-row texts (unescaped via InnerText),
    /// and the raw XML (for assertions on escaping).
    /// </summary>
    private static (XmlDocument Sheet, string Header, string[] DataRows, string RawXml) ExtractSheet(
        MemoryStream ms, int expectedRows)
    {
        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml");
        entry.Should().NotBeNull("xlsx exports must include xl/worksheets/sheet1.xml");

        string rawXml;
        using (var stream = entry!.Open())
        using (var reader = new StreamReader(stream))
        {
            rawXml = reader.ReadToEnd();
        }
        var doc = new XmlDocument();
        using (var stream = entry.Open())
        {
            doc.Load(stream);
        }
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var rows = doc.SelectNodes("/x:worksheet/x:sheetData/x:row", ns)!;
        rows.Count.Should().Be(expectedRows + 1, "header + N data rows");

        var headerRow = rows[0]!;
        var dataRowNodes = new System.Collections.Generic.List<XmlNode>();
        for (int i = 1; i < rows.Count; i++) dataRowNodes.Add(rows[i]!);

        var header = RowToText(headerRow, ns);
        var data = dataRowNodes.Select(n => RowToText(n, ns)).ToArray();
        return (doc, header, data, rawXml);
    }

    private static string RowToText(XmlNode row, XmlNamespaceManager ns)
    {
        // Concatenate every cell's text with a tab separator so
        // assertions can match header columns + escaped values
        // in a single string. Note: InnerText un-escapes XML
        // entities — for asserting that escaping happened, use
        // the rawXml returned alongside.
        var cells = row.SelectNodes("x:c/x:is/x:t", ns);
        if (cells is null || cells.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append('\t');
            sb.Append(cells[i]!.InnerText);
        }
        return sb.ToString();
    }
}
