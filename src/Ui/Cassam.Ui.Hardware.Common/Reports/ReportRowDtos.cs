using System;
using System.Collections.Generic;

namespace Cassam.Ui.Hardware.Common.Reports;

/// <summary>
/// Row shape for the "Ventas del día" report. One row per
/// <c>sale</c> row joined with the <c>sale_line_items</c>
/// total. Replaces the legacy <c>RFacturaCarta.rpt</c> +
/// <c>RFacturaMediaCarta.rpt</c> per design §11.
/// </summary>
/// <param name="SaleId">Internal sale identifier.</param>
/// <param name="Date">Sale timestamp (UTC, formatted as DD/MM/YYYY in the View).</param>
/// <param name="InvoiceNumber">DIAN-issued invoice number (set when sold; null for fully offline + pre-numbering rows).</param>
/// <param name="CustomerName">Counterparty name (null for consumidor final).</param>
/// <param name="CustomerNit">Counterparty NIT/CC (null for consumidor final).</param>
/// <param name="Subtotal">COP subtotal, gravado + exento (excl. IVA).</param>
/// <param name="TaxTotal">Sum of IVA across all line items.</param>
/// <param name="Total">Public total (subtotal + tax).</param>
/// <param name="PaymentMethod">Cash | Card | Transfer | Other.</param>
/// <param name="CashierName">Logged-in cashier display name.</param>
/// <param name="Status">QUEUED | TRANSMITTED | REJECTED | NO_PRINT per pos-fiscal-dian-transmission.</param>
/// <param name="LineItemSummary">Aggregated line items (e.g. "3 items, 2 distinct SKUs").</param>
public sealed record DailySalesRow(
    Guid SaleId,
    DateTimeOffset Date,
    string? InvoiceNumber,
    string? CustomerName,
    string? CustomerNit,
    decimal Subtotal,
    decimal TaxTotal,
    decimal Total,
    string PaymentMethod,
    string CashierName,
    string Status,
    string LineItemSummary);

/// <summary>
/// Row shape for the "Inventario" report. One row per
/// <c>products</c> entry. Replaces the legacy
/// <c>RInventarioCardex.rpt</c>.
/// </summary>
/// <param name="ProductId">Internal product identifier.</param>
/// <param name="Sku">Tenant-scoped SKU string.</param>
/// <param name="Barcode">Barcode (null when no barcode assigned).</param>
/// <param name="Name">Product display name (es-CO).</param>
/// <param name="TaxCategory">S (Standard 19%) | Z (Zero-rated 0%) | E (Exempt) | O (Other impoconsumo).</param>
/// <param name="UnitPrice">Default sale unit price (COP, incl. IVA at default rate).</param>
/// <param name="StockQuantity">On-hand stock after last count.</param>
/// <param name="ReorderLevel">Threshold for "needs reorder" highlight.</param>
/// <param name="LastMovementDate">Last inventory movement timestamp (UTC).</param>
public sealed record InventoryRow(
    Guid ProductId,
    string Sku,
    string? Barcode,
    string Name,
    string TaxCategory,
    decimal UnitPrice,
    decimal StockQuantity,
    decimal ReorderLevel,
    DateTimeOffset? LastMovementDate);

/// <summary>
/// Row shape for the "Cierre de caja" report. One row per
/// closed cash session with tax-bucket breakdown per design
/// §5.3 + FISCAL_AUDIT.md §2 (CierreDeCaja.vb parity).
/// </summary>
/// <param name="SessionId">Cash session identifier.</param>
/// <param name="OpenedAt">Open timestamp (UTC).</param>
/// <param name="ClosedAt">Close timestamp (UTC).</param>
/// <param name="OpenedByUserName">Display name from session-state.</param>
/// <param name="OpeningAmount">COP entered at open.</param>
/// <param name="ClosingAmount">COP counted at close.</param>
/// <param name="ExpectedAmount">Opening + sum of sales during session.</param>
/// <param name="VarianceAmount">Closing - Expected (0 means exact).</param>
/// <param name="SaleCount">Number of sales recorded in the session.</param>
/// <param name="TaxBucket5Base">Base gravada 5% (historical rate).</param>
/// <param name="TaxBucket5Iva">IVA 5% total.</param>
/// <param name="TaxBucket19Base">Base gravada 19% (current rate).</param>
/// <param name="TaxBucket19Iva">IVA 19% total.</param>
/// <param name="TaxBucketExemptBase">Base exenta total.</param>
public sealed record CashSessionRow(
    Guid SessionId,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    string OpenedByUserName,
    decimal OpeningAmount,
    decimal ClosingAmount,
    decimal ExpectedAmount,
    decimal VarianceAmount,
    int SaleCount,
    decimal TaxBucket5Base,
    decimal TaxBucket5Iva,
    decimal TaxBucket19Base,
    decimal TaxBucket19Iva,
    decimal TaxBucketExemptBase);

/// <summary>
/// Three typed collections returned by
/// <see cref="IReportsService"/>. Kept as separate record
/// types so the ViewModel can hold strong-typed rows rather
/// than an untyped <c>List&lt;object&gt;</c>.
/// </summary>
/// <param name="AsOf">Wall-clock timestamp when the snapshot was taken.</param>
/// <param name="Rows">Materialised rows.</param>
public sealed record DailySalesReport(DateTimeOffset AsOf, IReadOnlyList<DailySalesRow> Rows);

/// <param name="AsOf">Wall-clock timestamp when the snapshot was taken.</param>
/// <param name="Rows">Materialised rows.</param>
public sealed record InventoryReport(DateTimeOffset AsOf, IReadOnlyList<InventoryRow> Rows);

/// <param name="AsOf">Wall-clock timestamp when the snapshot was taken.</param>
/// <param name="Rows">Materialised rows.</param>
public sealed record CashSessionReport(DateTimeOffset AsOf, IReadOnlyList<CashSessionRow> Rows);
