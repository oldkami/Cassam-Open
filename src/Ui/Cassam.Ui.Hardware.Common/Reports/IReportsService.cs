using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Reports;

/// <summary>
/// Data seam for the reports module (T2.11). Phase 5 wires
/// the EF-backed <c>CassamDbContext</c> queries against
/// <c>sales</c> + <c>sale_line_items</c> + <c>products</c> +
/// <c>cash_sessions</c>; Phase 2 ships the
/// <see cref="InMemoryReportsService"/> stub so the manager
/// view renders end-to-end without a live database (matches
/// the platform-neutral testing strategy from PR 6/7).
/// </summary>
public interface IReportsService
{
    /// <summary>
    /// Returns the sales rows for a specific day (UTC). The
    /// <paramref name="day"/> argument is taken as a calendar
    /// date — the service filters sales where
    /// <c>sale.created_at</c> falls in the 24-hour window
    /// starting at <paramref name="day"/>. The View coalesces
    /// by day so the timezone question is hidden in the
    /// service.
    /// </summary>
    Task<DailySalesReport> GetDailySalesAsync(DateTimeOffset day, CancellationToken ct = default);

    /// <summary>
    /// Returns the inventory snapshot at "now". The View can
    /// call <c>RefreshAsync</c> on demand; the service is free
    /// to cache if needed.
    /// </summary>
    Task<InventoryReport> GetInventoryAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the closed cash sessions whose
    /// <c>closed_at</c> falls in the last <paramref name="days"/> days
    /// (default 30). Phase 2 stub returns the in-memory
    /// fixtures; Phase 5 wires the EF query.
    /// </summary>
    Task<CashSessionReport> GetCashSessionReportAsync(int days = 30, CancellationToken ct = default);
}
