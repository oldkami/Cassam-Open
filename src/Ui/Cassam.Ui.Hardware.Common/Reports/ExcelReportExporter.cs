using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Reports;

/// <summary>
/// Minimal XLSX exporter that emits a valid OOXML SpreadsheetML
/// (<c>.xlsx</c>) using only the platform-neutral BCL primitives.
/// Produces a single-sheet workbook with a header row + one row
/// per input, no styling, no formulas, no shared strings
/// optimisation. Trade-off: the XLSX is bigger than a minimal
/// hand-tuned emit, but the file opens cleanly in Excel +
/// LibreOffice Calc + Google Sheets.
///
/// <para>
/// Why a hand-rolled emitter and not a NuGet (e.g.
/// ClosedXML, MiniExcel)? Two reasons:
/// </para>
/// <list type="bullet">
///   <item>The report is read-only: one flat table, one sheet.</item>
///   <item>QuestPDF is the only Phase 2 PDF dependency (design §11).
/// Adding ClosedXML (LGPL) for one emission would balloon the
/// test surface for marginal value.</item>
/// </list>
///
/// <para>
/// The output is a ZIP container with four well-known parts:
/// <c>[Content_Types].xml</c>, <c>_rels/.rels</c>,
/// <c>xl/workbook.xml</c>, and <c>xl/worksheets/sheet1.xml</c>.
/// Each XML is hand-serialised — the strings are escaped for
/// ampersand, less-than, greater-than, and double-quote per
/// the XML 1.0 specification.
/// </para>
/// </summary>
public static class ExcelReportExporter
{
    public static Task WriteDailySalesAsync(
        IReadOnlyList<DailySalesRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        var header = new[]
        {
            "VentaId", "Fecha", "NumeroFactura", "Cliente", "NIT",
            "SubtotalCOP", "IVACOP", "TotalCOP", "MetodoPago", "Cajero", "Estado", "Lineas"
        };
        var cellLists = new List<string[]>(rows.Count);
        foreach (var r in rows)
        {
            cellLists.Add(new[]
            {
                r.SaleId.ToString(),
                r.Date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                r.InvoiceNumber ?? string.Empty,
                r.CustomerName ?? string.Empty,
                r.CustomerNit ?? string.Empty,
                FormatNumber(r.Subtotal),
                FormatNumber(r.TaxTotal),
                FormatNumber(r.Total),
                r.PaymentMethod,
                r.CashierName,
                r.Status,
                r.LineItemSummary,
            });
        }
        return XlsxWriter.WriteAsync(output, ct, header, cellLists);
    }

    public static Task WriteInventoryAsync(
        IReadOnlyList<InventoryRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        var header = new[]
        {
            "ProductoId", "SKU", "CodigoBarras", "Nombre", "CategoriaImpuesto",
            "PrecioUnitarioCOP", "Stock", "ReorderLevel", "UltimoMovimiento"
        };
        var cellLists = new List<string[]>(rows.Count);
        foreach (var r in rows)
        {
            cellLists.Add(new[]
            {
                r.ProductId.ToString(),
                r.Sku,
                r.Barcode ?? string.Empty,
                r.Name,
                r.TaxCategory,
                FormatNumber(r.UnitPrice),
                r.StockQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                r.ReorderLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                r.LastMovementDate?.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            });
        }
        return XlsxWriter.WriteAsync(output, ct, header, cellLists);
    }

    public static Task WriteCashSessionAsync(
        IReadOnlyList<CashSessionRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        var header = new[]
        {
            "SesionId", "Abierta", "Cerrada", "AbiertaPor",
            "AperturaCOP", "CierreCOP", "EsperadoCOP", "VariacionCOP", "Ventas",
            "BaseGravada5COP", "IVA5COP", "BaseGravada19COP", "IVA19COP", "BaseExentaCOP"
        };
        var cellLists = new List<string[]>(rows.Count);
        foreach (var r in rows)
        {
            cellLists.Add(new[]
            {
                r.SessionId.ToString(),
                r.OpenedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                r.ClosedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                r.OpenedByUserName,
                FormatNumber(r.OpeningAmount),
                FormatNumber(r.ClosingAmount),
                FormatNumber(r.ExpectedAmount),
                FormatNumber(r.VarianceAmount),
                r.SaleCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FormatNumber(r.TaxBucket5Base),
                FormatNumber(r.TaxBucket5Iva),
                FormatNumber(r.TaxBucket19Base),
                FormatNumber(r.TaxBucket19Iva),
                FormatNumber(r.TaxBucketExemptBase),
            });
        }
        return XlsxWriter.WriteAsync(output, ct, header, cellLists);
    }

    private static string FormatNumber(decimal value)
        => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Internal helper that emits the OOXML container. Lives in
    /// this file so the public surface is "one method per row
    /// shape" — the OOXML details stay encapsulated.
    /// </summary>
    private static class XlsxWriter
    {
        public static async Task WriteAsync(
            Stream output,
            CancellationToken ct,
            string[] header,
            IReadOnlyList<string[]> rowCells)
        {
            var ms = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(zip, "[Content_Types].xml", ContentTypes());
                WriteEntry(zip, "_rels/.rels", PackageRels());
                WriteEntry(zip, "xl/workbook.xml", Workbook());
                WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
                WriteEntry(zip, "xl/worksheets/sheet1.xml", Worksheet(header, rowCells));
            }
            ms.Position = 0;
            await ms.CopyToAsync(output, ct);
        }

        private static void WriteEntry(System.IO.Compression.ZipArchive zip, string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            writer.Write(content);
        }

        private static string ContentTypes()
            => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
               + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
               + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
               + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
               + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
               + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
               + "</Types>";

        private static string PackageRels()
            => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
               + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
               + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
               + "</Relationships>";

        private static string Workbook()
            => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
               + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\""
               + " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
               + "<sheets><sheet name=\"Reporte\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
               + "</workbook>";

        private static string WorkbookRels()
            => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
               + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
               + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
               + "</Relationships>";

        private static string Worksheet(
            string[] header,
            IReadOnlyList<string[]> rowCells)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetData>");
            sb.Append("<row r=\"1\">");
            for (int i = 0; i < header.Length; i++)
            {
                sb.Append("<c r=\"").Append(ColumnLetter(i + 1)).Append("1\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                  .Append(Escape(header[i]))
                  .Append("</t></is></c>");
            }
            sb.Append("</row>");
            for (int rowIdx = 0; rowIdx < rowCells.Count; rowIdx++)
            {
                var cells = rowCells[rowIdx];
                sb.Append("<row r=\"").Append(rowIdx + 2).Append("\">");
                for (int colIdx = 0; colIdx < cells.Length; colIdx++)
                {
                    sb.Append("<c r=\"").Append(ColumnLetter(colIdx + 1)).Append(rowIdx + 2).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                      .Append(Escape(cells[colIdx]))
                      .Append("</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string ColumnLetter(int oneBasedColumn)
        {
            // Excel column encoding: A=1..Z=26, AA=27.. etc.
            var sb = new System.Text.StringBuilder();
            while (oneBasedColumn > 0)
            {
                int rem = (oneBasedColumn - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                oneBasedColumn = (oneBasedColumn - 1) / 26;
            }
            return sb.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            // Limit per Excel cell to 32767 chars; truncate generously.
            if (value.Length > 32767) value = value.Substring(0, 32767);
            // Ampersand first so the other replacements don't double-escape.
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
