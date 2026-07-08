using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Reports;

/// <summary>
/// RFC 4180 CSV exporter. Quote-escapes commas, quotes, and
/// newlines; uses CRLF line terminators per the spec; UTF-8
/// BOM so Excel opens the file with the right encoding.
/// </summary>
/// <remarks>
/// <para>
/// One CSV writer per row shape because the column headers
/// differ. We do not use reflection-based column discovery so
/// a column rename breaks the build (visible at compile
/// time) rather than silently mismatching the rendered
/// header in the report.
/// </para>
/// <para>
/// Tested in <c>CsvReportExporterTests</c> against the
/// in-memory fixture rows from PR 10.
/// </para>
/// </remarks>
public static class CsvReportExporter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    // ---- DailySales ----
    private const string DailySalesHeader =
        "VentaId,Fecha,NumeroFactura,Cliente,NIT,SubtotalCOP,IVACOP,TotalCOP,MetodoPago,Cajero,Estado,Lineas";

    public static Task WriteDailySalesAsync(
        System.Collections.Generic.IReadOnlyList<DailySalesRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        return WriteAsync(output, ct, writer =>
        {
            writer.WriteLine(DailySalesHeader);
            foreach (var r in rows)
            {
                writer.Write(r.SaleId.ToString()); writer.Write(',');
                writer.Write(r.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(Escape(r.InvoiceNumber)); writer.Write(',');
                writer.Write(Escape(r.CustomerName)); writer.Write(',');
                writer.Write(Escape(r.CustomerNit)); writer.Write(',');
                writer.Write(r.Subtotal.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.TaxTotal.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.Total.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(Escape(r.PaymentMethod)); writer.Write(',');
                writer.Write(Escape(r.CashierName)); writer.Write(',');
                writer.Write(Escape(r.Status)); writer.Write(',');
                writer.Write(Escape(r.LineItemSummary));
                writer.WriteLine();
            }
        });
    }

    // ---- Inventory ----
    private const string InventoryHeader =
        "ProductoId,SKU,CodigoBarras,Nombre,CategoriaImpuesto,PrecioUnitarioCOP,Stock,ReorderLevel,UltimoMovimiento";

    public static Task WriteInventoryAsync(
        System.Collections.Generic.IReadOnlyList<InventoryRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        return WriteAsync(output, ct, writer =>
        {
            writer.WriteLine(InventoryHeader);
            foreach (var r in rows)
            {
                writer.Write(r.ProductId.ToString()); writer.Write(',');
                writer.Write(Escape(r.Sku)); writer.Write(',');
                writer.Write(Escape(r.Barcode)); writer.Write(',');
                writer.Write(Escape(r.Name)); writer.Write(',');
                writer.Write(Escape(r.TaxCategory)); writer.Write(',');
                writer.Write(r.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.StockQuantity.ToString(CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.ReorderLevel.ToString(CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.LastMovementDate?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty);
                writer.WriteLine();
            }
        });
    }

    // ---- CashSession ----
    private const string CashSessionHeader =
        "SesionId,Abierta,Cerrada,AbiertaPor,AperturaCOP,CierreCOP,EsperadoCOP,VariacionCOP,Ventas,BaseGravada5COP,IVA5COP,BaseGravada19COP,IVA19COP,BaseExentaCOP";

    public static Task WriteCashSessionAsync(
        System.Collections.Generic.IReadOnlyList<CashSessionRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        return WriteAsync(output, ct, writer =>
        {
            writer.WriteLine(CashSessionHeader);
            foreach (var r in rows)
            {
                writer.Write(r.SessionId.ToString()); writer.Write(',');
                writer.Write(r.OpenedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.ClosedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(Escape(r.OpenedByUserName)); writer.Write(',');
                writer.Write(r.OpeningAmount.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.ClosingAmount.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.ExpectedAmount.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.VarianceAmount.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.SaleCount.ToString(CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.TaxBucket5Base.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.TaxBucket5Iva.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.TaxBucket19Base.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.TaxBucket19Iva.ToString("F2", CultureInfo.InvariantCulture)); writer.Write(',');
                writer.Write(r.TaxBucketExemptBase.ToString("F2", CultureInfo.InvariantCulture));
                writer.WriteLine();
            }
        });
    }

    // ---- Helpers ----
    private static async Task WriteAsync(Stream output, CancellationToken ct, Action<StreamWriter> body)
    {
        ArgumentNullException.ThrowIfNull(output);
        var encoding = Utf8WithBom;
        await using var writer = new StreamWriter(output, encoding, leaveOpen: true);
        // Force CRLF — StreamWriter defaults to Environment.NewLine
        // which is "\n" on Linux/macOS but RFC 4180 mandates
        // CRLF. Pin NewLine to "\r\n" so every platform emits
        // the same byte sequence.
        writer.NewLine = "\r\n";
        await writer.FlushAsync(ct); // ensures BOM
        body(writer);
        await writer.FlushAsync(ct);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        var escaped = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }
}
