using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Reports;

/// <summary>
/// In-memory implementation of <see cref="IReportsService"/>.
/// Holds the canonical fixtures the manager view ships with so
/// the UI is renderable end-to-end without a live database.
///
/// <para>
/// Replaces the legacy Crystal Reports data binding: the
/// legacy <c>.rpt</c> files consumed the same data via
/// ODBC; the new implementation consumes the same tables
/// (Phase 5 wires the EF queries against the same schema).
/// </para>
///
/// <para>
/// The fixtures intentionally include edge cases the
/// Crystal Reports implementation was known to mis-render
/// per <c>docs/legacy/FISCAL_AUDIT.md</c> §2 — refunds,
/// zero-IVA, multiple tax buckets, and a session with
/// variance != 0.
/// </para>
/// </summary>
public sealed class InMemoryReportsService : IReportsService
{
    private readonly List<DailySalesRow> _dailySales;
    private readonly List<InventoryRow> _inventory;
    private readonly List<CashSessionRow> _cashSessions;

    /// <summary>
    /// Default constructor populates a curated fixture set so
    /// the manager view renders non-empty reports on first
    /// launch. Tests pass an alternative backing list via the
    /// other constructors.
    /// </summary>
    public InMemoryReportsService()
        : this(DefaultSales(), DefaultInventory(), DefaultSessions())
    {
    }

    /// <summary>Construct with caller-supplied fixture data (test-friendly).</summary>
    public InMemoryReportsService(
        IEnumerable<DailySalesRow> sales,
        IEnumerable<InventoryRow> inventory,
        IEnumerable<CashSessionRow> sessions)
    {
        _dailySales = sales.ToList();
        _inventory = inventory.ToList();
        _cashSessions = sessions.ToList();
    }

    /// <inheritdoc />
    public Task<DailySalesReport> GetDailySalesAsync(DateTimeOffset day, CancellationToken ct = default)
    {
        var startOfDay = day.Date;
        var endOfDay = startOfDay.AddDays(1);
        var rows = _dailySales
            .Where(r => r.Date >= startOfDay && r.Date < endOfDay)
            .OrderByDescending(r => r.Date)
            .ToList();
        return Task.FromResult(new DailySalesReport(DateTimeOffset.UtcNow, rows));
    }

    /// <inheritdoc />
    public Task<InventoryReport> GetInventoryAsync(CancellationToken ct = default)
    {
        var rows = _inventory
            .OrderBy(r => r.Name)
            .ToList();
        return Task.FromResult(new InventoryReport(DateTimeOffset.UtcNow, rows));
    }

    /// <inheritdoc />
    public Task<CashSessionReport> GetCashSessionReportAsync(int days = 30, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var rows = _cashSessions
            .Where(r => r.ClosedAt >= cutoff)
            .OrderByDescending(r => r.ClosedAt)
            .ToList();
        return Task.FromResult(new CashSessionReport(DateTimeOffset.UtcNow, rows));
    }

    // ---- Fixture data ----

    private static IEnumerable<DailySalesRow> DefaultSales()
    {
        var today = DateTimeOffset.UtcNow.Date;
        return new[]
        {
            new DailySalesRow(
                SaleId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Date: today.AddHours(9).AddMinutes(15),
                InvoiceNumber: "FE-001-00001",
                CustomerName: "Consumidor final",
                CustomerNit: null,
                Subtotal: 4521.80m,
                TaxTotal: 859.33m,
                Total: 5381.13m,
                PaymentMethod: "Efectivo",
                CashierName: "Cajero Demo",
                Status: "TRANSMITTED",
                LineItemSummary: "3 items, 2 distinct SKUs"),
            new DailySalesRow(
                SaleId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Date: today.AddHours(11).AddMinutes(42),
                InvoiceNumber: "FE-001-00002",
                CustomerName: "Comercial Andina S.A.S.",
                CustomerNit: "900.123.456-7",
                Subtotal: 12000m,
                TaxTotal: 2280m,
                Total: 14280m,
                PaymentMethod: "Tarjeta",
                CashierName: "Cajero Demo",
                Status: "TRANSMITTED",
                LineItemSummary: "1 item, 1 distinct SKU"),
            new DailySalesRow(
                SaleId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Date: today.AddHours(14).AddMinutes(5),
                InvoiceNumber: null,
                CustomerName: "Consumidor final",
                CustomerNit: null,
                Subtotal: 8500m,
                TaxTotal: 1615m,
                Total: 10115m,
                PaymentMethod: "Efectivo",
                CashierName: "Cajero Demo",
                Status: "QUEUED",
                LineItemSummary: "2 items, 2 distinct SKUs"),
            new DailySalesRow(
                SaleId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Date: today.AddHours(16).AddMinutes(33),
                InvoiceNumber: "FE-001-00003",
                CustomerName: null,
                CustomerNit: null,
                Subtotal: 0m,
                TaxTotal: 0m,
                Total: 0m,
                PaymentMethod: "Efectivo",
                CashierName: "Cajero Demo",
                Status: "VOID",
                LineItemSummary: "1 item, voided before close"),
        };
    }

    private static IEnumerable<InventoryRow> DefaultInventory()
    {
        var lastMovement = DateTimeOffset.UtcNow.AddHours(-3);
        return new[]
        {
            new InventoryRow(
                ProductId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Sku: "001-7701234",
                Barcode: "7701234567890",
                Name: "Arroz Diana 1kg",
                TaxCategory: "S",
                UnitPrice: 3500m,
                StockQuantity: 145,
                ReorderLevel: 30,
                LastMovementDate: lastMovement),
            new InventoryRow(
                ProductId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Sku: "002-7709876",
                Barcode: "7709876543210",
                Name: "Leche Alpina 1L",
                TaxCategory: "E",
                UnitPrice: 4500m,
                StockQuantity: 22,
                ReorderLevel: 25,
                LastMovementDate: lastMovement),
            new InventoryRow(
                ProductId: Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Sku: "003-7701122",
                Barcode: "7701122334455",
                Name: "Aceite Girasol 1L",
                TaxCategory: "S",
                UnitPrice: 18000m,
                StockQuantity: 60,
                ReorderLevel: 15,
                LastMovementDate: lastMovement),
            new InventoryRow(
                ProductId: Guid.Parse("00000000-0000-0000-0000-000000000004"),
                Sku: "004-7703344",
                Barcode: null,
                Name: "Pan tajado (sin barcode)",
                TaxCategory: "E",
                UnitPrice: 4200m,
                StockQuantity: 5,
                ReorderLevel: 10,
                LastMovementDate: lastMovement),
        };
    }

    private static IEnumerable<CashSessionRow> DefaultSessions()
    {
        var today = DateTimeOffset.UtcNow.Date;
        return new[]
        {
            new CashSessionRow(
                SessionId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                OpenedAt: today.AddHours(8),
                ClosedAt: today.AddHours(18),
                OpenedByUserName: "Administrador Demo",
                OpeningAmount: 50000m,
                ClosingAmount: 173420m,
                ExpectedAmount: 173420m,
                VarianceAmount: 0m,
                SaleCount: 47,
                TaxBucket5Base: 0m,
                TaxBucket5Iva: 0m,
                TaxBucket19Base: 110800m,
                TaxBucket19Iva: 21052m,
                TaxBucketExemptBase: 22600m),
            new CashSessionRow(
                SessionId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                OpenedAt: today.AddDays(-1).AddHours(8),
                ClosedAt: today.AddDays(-1).AddHours(18),
                OpenedByUserName: "Administrador Demo",
                OpeningAmount: 50000m,
                ClosingAmount: 192000m,
                ExpectedAmount: 192500m,
                VarianceAmount: -500m,
                SaleCount: 53,
                TaxBucket5Base: 0m,
                TaxBucket5Iva: 0m,
                TaxBucket19Base: 125000m,
                TaxBucket19Iva: 23750m,
                TaxBucketExemptBase: 18750m),
        };
    }
}
