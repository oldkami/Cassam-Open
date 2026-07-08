using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Cassam.Ui.Reports;

/// <summary>
/// PDF renderer for the three manager-view reports (T2.11,
/// design §11). Wraps QuestPDF (MIT, commercial-friendly per
/// DD-08) so the platform-neutral pipeline can target a single
/// PDF interface while the head project owns the actual emit.
///
/// <para>
/// Lives in <c>Cassam.Ui/Reports/</c> rather than
/// <c>Cassam.Ui.Hardware.Common</c> because QuestPDF brings
/// SkiaSharp transitive deps that would fatten the
/// platform-neutral test surface. The renderer is a thin
/// shell — the testable math (DTOs, row shapes, enums) all
/// stays in Hardware.Common so the existing test suite
/// exercises the data without dragging QuestPDF in.
/// </para>
///
/// <para>
/// QuestPDF requires <see cref="QuestPDF.Settings.License"/>
/// to be set before any document composition. The renderer
/// sets the <see cref="LicenseType.Community"/> key once
/// (idempotent), matching the DD-08 commercial-use decision.
/// </para>
/// </summary>
public sealed class QuestPdfReportRenderer : IReportExporter, IInventoryExporter, ICashSessionExporter
{
    private static int s_licenseConfigured;

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Pdf;

    /// <inheritdoc />
    public string FileExtension => "pdf";

    public QuestPdfReportRenderer()
    {
        // The Community license is free for revenue under
        // ~$1M USD (per QuestPDF FAQ). Since Cassam SAS is a
        // single-tenant Colombian SMB, the Community key is
        // the right ceiling. If Cassam ever moves above the
        // threshold, the Professional license swap is one
        // line of code (the License property + an env-driven
        // hint at startup).
        if (System.Threading.Interlocked.Exchange(ref s_licenseConfigured, 1) == 0)
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }
    }

    // ---- IReportExporter (DailySales) ----
    Task IReportExporter.ExportAsync(IReadOnlyList<DailySalesRow> rows, Stream output, CancellationToken ct)
        => RenderDailySalesAsync(rows, output, ct);

    // ---- IInventoryExporter ----
    Task IInventoryExporter.ExportAsync(IReadOnlyList<InventoryRow> rows, Stream output, CancellationToken ct)
        => RenderInventoryAsync(rows, output, ct);

    // ---- ICashSessionExporter ----
    Task ICashSessionExporter.ExportAsync(IReadOnlyList<CashSessionRow> rows, Stream output, CancellationToken ct)
        => RenderCashSessionAsync(rows, output, ct);

    /// <summary>
    /// Public renderers matching the per-row-shape static helpers
    /// in the platform-neutral module. The static helpers can be
    /// called directly from integration tests (the per-shape tests
    /// already cover the CSV / Excel / JSON contracts; this method
    /// is exercised by a thin end-to-end test in
    /// <c>QuestPdfReportRendererTests</c> once QuestPDF ships).
    /// </summary>
    public Task RenderDailySalesAsync(
        IReadOnlyList<DailySalesRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(output);
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4.Portrait());
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Ventas del día").FontSize(18).SemiBold();
                    col.Item().Text($"Generado: {DateTimeOffset.UtcNow:dd/MM/yyyy HH:mm} UTC")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(70, Unit.Point);  // Fecha
                        columns.ConstantColumn(80, Unit.Point);  // Factura
                        columns.RelativeColumn();                // Cliente
                        columns.ConstantColumn(70, Unit.Point);  // Subtotal
                        columns.ConstantColumn(60, Unit.Point);  // IVA
                        columns.ConstantColumn(70, Unit.Point);  // Total
                        columns.ConstantColumn(70, Unit.Point);  // Estado
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("Fecha").SemiBold();
                        header.Cell().Text("Factura").SemiBold();
                        header.Cell().Text("Cliente").SemiBold();
                        header.Cell().Text("Subtotal").SemiBold().AlignRight();
                        header.Cell().Text("IVA").SemiBold().AlignRight();
                        header.Cell().Text("Total").SemiBold().AlignRight();
                        header.Cell().Text("Estado").SemiBold();
                    });

                    foreach (var r in rows)
                    {
                        table.Cell().Text(r.Date.ToString("dd/MM/yyyy HH:mm"));
                        table.Cell().Text(r.InvoiceNumber ?? "—");
                        table.Cell().Text(r.CustomerName ?? "Consumidor final");
                        table.Cell().Text(FormatCop(r.Subtotal)).AlignRight();
                        table.Cell().Text(FormatCop(r.TaxTotal)).AlignRight();
                        table.Cell().Text(FormatCop(r.Total)).AlignRight();
                        table.Cell().Text(r.Status);
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        });

        return Task.Run(() => document.GeneratePdf(output), ct);
    }

    public Task RenderInventoryAsync(
        IReadOnlyList<InventoryRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(output);
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4.Portrait());
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Inventario").FontSize(18).SemiBold();
                    col.Item().Text($"Generado: {DateTimeOffset.UtcNow:dd/MM/yyyy HH:mm} UTC")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(70, Unit.Point);  // SKU
                        columns.RelativeColumn();                // Nombre
                        columns.ConstantColumn(50, Unit.Point);  // Cat
                        columns.ConstantColumn(60, Unit.Point);  // Precio
                        columns.ConstantColumn(40, Unit.Point);  // Stock
                        columns.ConstantColumn(60, Unit.Point);  // Reorder
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("SKU").SemiBold();
                        header.Cell().Text("Nombre").SemiBold();
                        header.Cell().Text("Cat").SemiBold();
                        header.Cell().Text("Precio").SemiBold().AlignRight();
                        header.Cell().Text("Stock").SemiBold().AlignRight();
                        header.Cell().Text("Reorder").SemiBold().AlignRight();
                    });

                    foreach (var r in rows)
                    {
                        table.Cell().Text(r.Sku);
                        table.Cell().Text(r.Name);
                        table.Cell().Text(r.TaxCategory);
                        table.Cell().Text(FormatCop(r.UnitPrice)).AlignRight();
                        table.Cell().Text(r.StockQuantity.ToString()).AlignRight();
                        table.Cell().Text(r.ReorderLevel.ToString()).AlignRight();
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        });

        return Task.Run(() => document.GeneratePdf(output), ct);
    }

    public Task RenderCashSessionAsync(
        IReadOnlyList<CashSessionRow> rows,
        Stream output,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(output);
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4.Portrait());
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Cierres de caja").FontSize(18).SemiBold();
                    col.Item().Text($"Generado: {DateTimeOffset.UtcNow:dd/MM/yyyy HH:mm} UTC")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(70, Unit.Point);  // Abierta
                        columns.RelativeColumn();                // Abierta por
                        columns.ConstantColumn(70, Unit.Point);  // Apertura
                        columns.ConstantColumn(70, Unit.Point);  // Cierre
                        columns.ConstantColumn(70, Unit.Point);  // Esperado
                        columns.ConstantColumn(70, Unit.Point);  // Variación
                        columns.ConstantColumn(40, Unit.Point);  // Ventas
                        columns.ConstantColumn(80, Unit.Point);  // IVA19
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("Abierta").SemiBold();
                        header.Cell().Text("Abierta por").SemiBold();
                        header.Cell().Text("Apertura").SemiBold().AlignRight();
                        header.Cell().Text("Cierre").SemiBold().AlignRight();
                        header.Cell().Text("Esperado").SemiBold().AlignRight();
                        header.Cell().Text("Variación").SemiBold().AlignRight();
                        header.Cell().Text("Ventas").SemiBold().AlignRight();
                        header.Cell().Text("IVA 19%").SemiBold().AlignRight();
                    });

                    foreach (var r in rows)
                    {
                        table.Cell().Text(r.OpenedAt.ToString("dd/MM HH:mm"));
                        table.Cell().Text(r.OpenedByUserName);
                        table.Cell().Text(FormatCop(r.OpeningAmount)).AlignRight();
                        table.Cell().Text(FormatCop(r.ClosingAmount)).AlignRight();
                        table.Cell().Text(FormatCop(r.ExpectedAmount)).AlignRight();
                        table.Cell().Text(FormatCop(r.VarianceAmount)).AlignRight();
                        table.Cell().Text(r.SaleCount.ToString()).AlignRight();
                        table.Cell().Text(FormatCop(r.TaxBucket19Iva)).AlignRight();
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        });

        return Task.Run(() => document.GeneratePdf(output), ct);
    }

    private static string FormatCop(decimal amount)
        => "$ " + amount.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Composes <see cref="QuestPdfReportRenderer"/> with the
/// platform-neutral pipeline so the manager view can route
/// <see cref="ExportFormat.Pdf"/> through QuestPDF instead of
/// hitting the <c>NotSupportedException</c> from the
/// platform-neutral pipeline. Registered in DI as
/// <see cref="IReportExportPipeline"/>.
/// </summary>
public sealed class QuestPdfReportExportPipeline : IReportExportPipeline
{
    private readonly QuestPdfReportRenderer _renderer = new();

    public Task ExportDailySalesAsync(ExportFormat format, IReadOnlyList<DailySalesRow> rows, Stream output, CancellationToken ct = default)
    {
        if (format == ExportFormat.Pdf)
        {
            return _renderer.RenderDailySalesAsync(rows, output, ct);
        }
        return new PlatformNeutralReportExportPipeline().ExportDailySalesAsync(format, rows, output, ct);
    }

    public Task ExportInventoryAsync(ExportFormat format, IReadOnlyList<InventoryRow> rows, Stream output, CancellationToken ct = default)
    {
        if (format == ExportFormat.Pdf)
        {
            return _renderer.RenderInventoryAsync(rows, output, ct);
        }
        return new PlatformNeutralReportExportPipeline().ExportInventoryAsync(format, rows, output, ct);
    }

    public Task ExportCashSessionAsync(ExportFormat format, IReadOnlyList<CashSessionRow> rows, Stream output, CancellationToken ct = default)
    {
        if (format == ExportFormat.Pdf)
        {
            return _renderer.RenderCashSessionAsync(rows, output, ct);
        }
        return new PlatformNeutralReportExportPipeline().ExportCashSessionAsync(format, rows, output, ct);
    }
}
