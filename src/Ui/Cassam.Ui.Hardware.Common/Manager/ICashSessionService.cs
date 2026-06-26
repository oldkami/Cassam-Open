using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Summary of a single cash session for the manager history list.
/// </summary>
/// <param name="Id">CashSession FK.</param>
/// <param name="OpenedAt">UTC timestamp the shift opened.</param>
/// <param name="ClosedAt">UTC timestamp the shift closed (null while open).</param>
/// <param name="OpenedByUserName">Display name of the cashier who opened the shift.</param>
/// <param name="OpeningAmount">Till amount at open.</param>
/// <param name="ClosingAmount">Till amount at close (operator-counted).</param>
/// <param name="ExpectedAmount">System-computed expected amount at close.</param>
/// <param name="VarianceAmount">
/// <c>ClosingAmount - ExpectedAmount</c>. Negative = shortage,
/// surfaced red per REQ-AR-04.
/// </param>
/// <param name="Status">Lifecycle state (Open / Closed / Reconciled).</param>
public sealed record CashSessionSummary(
    Guid Id,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    string OpenedByUserName,
    decimal OpeningAmount,
    decimal? ClosingAmount,
    decimal? ExpectedAmount,
    decimal? VarianceAmount,
    Core.Domain.Enums.CashSessionStatus Status);

/// <summary>
/// Manager-flow cash-session CRUD. Open / close / list-history per
/// REQ-CORE-10 / SCN-CORE-10. The actual money math (expected
/// amount) is computed from sales + payments; Phase 2 ships a
/// stub that returns the operator-supplied closing amount with a
/// zero variance so the UI is renderable. Phase 3 wires the real
/// expected-amount calculation.
/// </summary>
public interface ICashSessionService
{
    /// <summary>
    /// Return the currently-open cash session for the active
    /// tenant, or <c>null</c> when no session is open. The manager
    /// UI uses this to decide whether the "Abrir caja" or "Cerrar
    /// caja" button is enabled.
    /// </summary>
    Task<CashSessionSummary?> GetOpenSessionAsync(CancellationToken ct);

    /// <summary>List the most recent <paramref name="take"/> closed sessions (most-recent-first).</summary>
    Task<IReadOnlyList<CashSessionSummary>> ListRecentAsync(int take, CancellationToken ct);

    /// <summary>
    /// Open a new cash session. Throws
    /// <see cref="InvalidOperationException"/> when a session is
    /// already open for the active tenant.
    /// </summary>
    Task<CashSessionSummary> OpenSessionAsync(
        string openedByUserName,
        decimal openingAmount,
        CancellationToken ct);

    /// <summary>
    /// Close the currently-open session with the operator-counted
    /// amount. Computes the variance against the (Phase 2 stub)
    /// expected amount and returns the closed-session summary.
    /// Throws <see cref="InvalidOperationException"/> when no
    /// session is open.
    /// </summary>
    Task<CashSessionSummary> CloseSessionAsync(
        decimal closingAmount,
        CancellationToken ct);
}