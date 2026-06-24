using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Exceptions;
using Cassam.Core.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Persistence.Services;

/// <summary>
/// PostgreSQL-backed <see cref="INumberingService"/>. Reserves the next
/// sequential number by acquiring a row-level lock on the active
/// resolución (<c>SELECT … FOR UPDATE</c>) and incrementing
/// <c>current_number</c> inside an explicit transaction per
/// <c>pos-core-modern-stack</c> REQ-CORE-06 / SCN-CORE-07.
///
/// <para>
/// Concurrency model:
/// </para>
/// <list type="number">
///   <item>An initial non-locking lookup locates the id of the active
///         resolución for the (tenant, document type) pair. This is
///         cheap and lets us throw the right exception
///         (<see cref="ResolucionNotActiveException"/>) without paying
///         for a transaction round-trip when no active range exists.</item>
///   <item>An explicit transaction is opened (<see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction"/>).
///         PostgreSQL holds the lock until <c>Commit</c> or
///         <c>Rollback</c>.</item>
///   <item>A raw SQL <c>SELECT * FROM resoluciones WHERE id = … FOR UPDATE</c>
///         acquires the row lock. Any concurrent reservation against the
///         same resolución blocks at the database level until the first
///         transaction commits.</item>
///   <item>The increment is applied via the tracked entity. EF Core's
///         optimistic-concurrency token (<c>version</c>) provides a
///         belt-and-braces check on top of the row lock: even if a
///         caller manages to skip the locking path (e.g. against an
///         InMemory provider), the <c>WHERE version = @old_version</c>
///         predicate detects a stale read.</item>
///   <item><c>SaveChangesAsync</c> + <c>CommitAsync</c> flushes and
///         releases the lock.</item>
/// </list>
///
/// <para>
/// Provider portability: the <c>FOR UPDATE</c> clause is PostgreSQL
/// syntax. The InMemory test provider silently ignores it; concurrency
/// tests against InMemory verify the application logic only and the
/// real serialization guarantee is exercised by the Testcontainers
/// integration tests in <c>NumberingConcurrencyTests</c>.
/// </para>
/// </summary>
public sealed class NumberingService : INumberingService
{
    private readonly CassamDbContext _dbContext;

    /// <summary>
    /// Constructs the service around the caller's <see cref="CassamDbContext"/>.
    /// The DbContext is captured; the caller owns its lifetime.
    /// </summary>
    /// <param name="dbContext">
    /// DbContext that owns the active transaction. Passing the same
    /// context the dispatch use case is already using keeps the row
    /// lock in scope for the whole numbering reservation.
    /// </param>
    public NumberingService(CassamDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    /// <remarks>
    /// See the class-level remarks for the full concurrency walkthrough.
    /// </remarks>
    public async Task<long> ReserveNextNumberAsync(
        Guid tenantId,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);

        cancellationToken.ThrowIfCancellationRequested();

        // ---- Step 1: locate the active resolución (non-locking read) ----
        var activeId = await _dbContext.Resoluciones
            .Where(r => r.TenantId == tenantId
                     && r.DocumentType == documentType
                     && r.Status == ResolucionStatus.Active)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeId == Guid.Empty)
        {
            throw new ResolucionNotActiveException(tenantId, documentType);
        }

        // ---- Step 2: lock + increment inside one transaction ----
        // BeginTransactionAsync is explicit so FOR UPDATE actually holds
        // the lock until commit. SaveChanges on its own would only lock
        // at UPDATE time, releasing immediately after — which would
        // race two concurrent increments between SELECT and UPDATE.
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // FromSqlInterpolated carries parameter values as bind variables,
        // not string concatenation — safe against injection.
        // We include `deleted_at IS NULL` explicitly because EF Core's
        // global query filter is not auto-applied to FromSql queries;
        // the dispatcher must never increment a soft-deleted row.
        var resolucion = await _dbContext.Resoluciones
            .FromSqlInterpolated($@"
                SELECT * FROM resoluciones
                WHERE id = {activeId}
                  AND deleted_at IS NULL
                FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (resolucion is null)
        {
            // The row disappeared between Step 1 and Step 2 — concurrent
            // soft-delete or hard delete. Treat as "no active resolución"
            // since the observable state to the caller is identical.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new ResolucionNotActiveException(tenantId, documentType);
        }

        if (resolucion.CurrentNumber >= resolucion.RangeEnd)
        {
            // Range exhausted — the caller should consult the next active
            // resolución (if any) or surface the exception to the operator.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new ResolucionExhaustedException(
                resolucion.Id,
                resolucion.CurrentNumber,
                resolucion.RangeEnd);
        }

        // ---- Step 3: increment + flush ----
        resolucion.CurrentNumber += 1;
        resolucion.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return resolucion.CurrentNumber;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Read-only path — no transaction, no lock. The pre-flight check is
    /// best-effort: between this call and a subsequent
    /// <see cref="ReserveNextNumberAsync"/> the range may exhaust. The
    /// authoritative answer always comes from the reservation itself.
    /// </remarks>
    public async Task<bool> HasAvailableNumbersAsync(
        Guid tenantId,
        DocumentType documentType,
        int howMany,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(howMany);

        cancellationToken.ThrowIfCancellationRequested();

        // Project directly to the two fields we need — the dispatcher
        // never asks for the full row here. EF Core emits a lean query.
        var snapshot = await _dbContext.Resoluciones
            .Where(r => r.TenantId == tenantId
                     && r.DocumentType == documentType
                     && r.Status == ResolucionStatus.Active)
            .Select(r => new { r.CurrentNumber, r.RangeEnd })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            // No active resolución — there are no available numbers at all.
            return false;
        }

        var remaining = snapshot.RangeEnd - snapshot.CurrentNumber;
        return remaining >= howMany;
    }
}
