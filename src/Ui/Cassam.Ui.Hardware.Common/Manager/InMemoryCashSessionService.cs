using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// In-memory cash-session service for Phase 2 preview. Models the
/// "one open session per tenant" invariant with a single nullable
/// slot. The production implementation lands in Phase 3 behind the
/// same <see cref="ICashSessionService"/> interface.
/// </summary>
public sealed class InMemoryCashSessionService : ICashSessionService
{
    private readonly object _gate = new();
    private CashSessionSummary? _open;
    private readonly List<CashSessionSummary> _history = new();

    /// <inheritdoc />
    public Task<CashSessionSummary?> GetOpenSessionAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_open);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CashSessionSummary>> ListRecentAsync(int take, CancellationToken ct)
    {
        lock (_gate)
        {
            IReadOnlyList<CashSessionSummary> snapshot = _history
                .OrderByDescending(s => s.OpenedAt)
                .Take(Math.Max(0, take))
                .ToArray();
            return Task.FromResult(snapshot);
        }
    }

    /// <inheritdoc />
    public Task<CashSessionSummary> OpenSessionAsync(
        string openedByUserName,
        decimal openingAmount,
        CancellationToken ct)
    {
        if (openingAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(openingAmount),
                openingAmount,
                "El monto de apertura no puede ser negativo.");
        }

        lock (_gate)
        {
            if (_open is not null)
            {
                throw new InvalidOperationException(
                    "Ya existe una sesión de caja abierta. Ciérrela antes de abrir una nueva.");
            }

            _open = new CashSessionSummary(
                Id: Guid.NewGuid(),
                OpenedAt: DateTime.UtcNow,
                ClosedAt: null,
                OpenedByUserName: openedByUserName,
                OpeningAmount: openingAmount,
                ClosingAmount: null,
                ExpectedAmount: null,
                VarianceAmount: null,
                Status: Core.Domain.Enums.CashSessionStatus.Open);
            return Task.FromResult(_open);
        }
    }

    /// <inheritdoc />
    public Task<CashSessionSummary> CloseSessionAsync(
        decimal closingAmount,
        CancellationToken ct)
    {
        if (closingAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(closingAmount),
                closingAmount,
                "El monto de cierre no puede ser negativo.");
        }

        lock (_gate)
        {
            if (_open is null)
            {
                throw new InvalidOperationException(
                    "No hay una sesión de caja abierta para cerrar.");
            }

            // Phase 2 stub: expected amount = opening amount (we
            // have no sales/payments wired yet — that lands in
            // Phase 3 with the real expected-amount calculation).
            var expected = _open.OpeningAmount;
            var variance = closingAmount - expected;
            var closed = _open with
            {
                ClosedAt = DateTime.UtcNow,
                ClosingAmount = closingAmount,
                ExpectedAmount = expected,
                VarianceAmount = variance,
                Status = Core.Domain.Enums.CashSessionStatus.Closed,
            };
            _history.Add(closed);
            _open = null;
            return Task.FromResult(closed);
        }
    }
}