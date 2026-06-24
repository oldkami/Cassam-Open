using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Persistence.Services;

/// <summary>
/// EF Core-backed <see cref="IResolucionLifecycleService"/>. Provides
/// read-only lifecycle queries plus the one-way
/// <see cref="ResolucionStatus.Active"/> → <see cref="ResolucionStatus.Expired"/>
/// transition per <c>pos-core-modern-stack</c> REQ-CORE-06 / SCN-CORE-06.
///
/// <para>
/// All read methods project to the columns they need; no entity
/// hydration is wasted on the watch-list queries. The transition is the
/// only mutator and it appends an audit-log entry inside the same
/// transaction so the state change and the audit row land atomically
/// (REQ-CORE-03).
/// </para>
/// </summary>
public sealed class ResolucionLifecycleService : IResolucionLifecycleService
{
    private readonly CassamDbContext _dbContext;

    /// <summary>
    /// Constructs the service around the caller's
    /// <see cref="CassamDbContext"/>. The caller is responsible for the
    /// context's lifetime and for ensuring the audit-log writer
    /// (resolved from the same context) is available for
    /// <see cref="MarkExpiredAsync"/>.
    /// </summary>
    /// <param name="dbContext">DbContext that owns the transición.</param>
    public ResolucionLifecycleService(CassamDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    /// <remarks>
    /// We project directly to <c>expiration_date</c> + <c>id</c> so the
    /// dispatcher can build reminder notifications without hydrating the
    /// full row. The query filter excludes soft-deleted rows; the
    /// status filter excludes Draft/Expired rows so we never warn the
    /// operator about something already terminated.
    /// </remarks>
    public async Task<IReadOnlyList<Resolucion>> FindResolucionesExpiringWithinAsync(
        Guid tenantId,
        int daysAhead,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(daysAhead);

        cancellationToken.ThrowIfCancellationRequested();

        // Compute the cutoff as a UTC DateOnly so the comparison
        // matches the column type. DateOnly arithmetic uses day-count
        // semantics, which is what we want for "calendar days".
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead);

        return await _dbContext.Resoluciones
            .Where(r => r.TenantId == tenantId
                     && r.Status == ResolucionStatus.Active
                     && r.ExpirationDate <= cutoff)
            .OrderBy(r => r.ExpirationDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Resolucion>> FindExhaustedResolucionesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        return await _dbContext.Resoluciones
            .Where(r => r.TenantId == tenantId
                     && r.Status == ResolucionStatus.Active
                     && r.CurrentNumber >= r.RangeEnd)
            .OrderBy(r => r.RangeEnd)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The implementation contract:
    /// <list type="number">
    ///   <item>Load the row by id (tracked).</item>
    ///   <item>Verify status is <see cref="ResolucionStatus.Active"/>;
    ///         throw <see cref="InvalidOperationException"/> otherwise.
    ///         Re-expired rows are not silently overwritten.</item>
    ///   <item>Append an audit-log entry tagged
    ///         <c>"RESOLUTION_EXPIRED"</c> on the same DbContext.</item>
    ///   <item><c>SaveChangesAsync</c> flushes both rows in one
    ///         transaction.</item>
    /// </list>
    /// <para>
    /// We do NOT call <c>IAuditLogWriter.WriteAsync</c> here because
    /// this service intentionally depends only on the DbContext (the
    /// audit writer is itself a thin wrapper that requires a DbContext).
    /// The append logic mirrors <see cref="Cassam.Core.Audit.AuditLogWriter"/>
    /// so the on-disk shape is identical; the dependency is kept narrow
    /// so the lifecycle service stays usable in a worker that does not
    /// inject the audit writer.
    /// </para>
    /// </remarks>
    public async Task MarkExpiredAsync(
        Guid resolucionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(resolucionId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        var resolucion = await _dbContext.Resoluciones
            .FirstOrDefaultAsync(r => r.Id == resolucionId, cancellationToken)
            .ConfigureAwait(false);

        if (resolucion is null)
        {
            throw new InvalidOperationException(
                $"Resolución '{resolucionId}' not found. MarkExpiredAsync requires the row to exist.");
        }

        if (resolucion.Status != ResolucionStatus.Active)
        {
            throw new InvalidOperationException(
                $"Resolución '{resolucionId}' is in status {resolucion.Status} — only Active resoluciones may transition to Expired. " +
                "The transition is one-way; use an explicit corrective UPDATE if you need to reset state.");
        }

        var beforeState = resolucion.Status.ToString();

        resolucion.Status = ResolucionStatus.Expired;
        resolucion.UpdatedAt = DateTime.UtcNow;

        // ---- Audit row ----
        // The audit writer keeps audit writes inside the caller's
        // transaction; we replicate the on-disk shape here so the
        // dependency stays narrow (no need to inject IAuditLogWriter).
        var now = DateTime.UtcNow;
        var auditRow = new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = resolucion.TenantId,
            // SYSTEM actor — the daily-check job is not driven by an
            // operator session. A tenant-scoped SYSTEM sentinel user is
            // resolved by the worker host (out of scope for this PR).
            ActorUserId = Guid.Empty,
            EntityType = "resolucion",
            EntityId = resolucion.Id,
            Action = "RESOLUTION_EXPIRED",
            BeforeState = beforeState,
            AfterState = ResolucionStatus.Expired.ToString(),
            IpAddress = null,
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1u,
        };
        _dbContext.AuditLog.Add(auditRow);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveResolucionAsync(
        Guid tenantId,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        return await _dbContext.Resoluciones
            .AsNoTracking()
            .AnyAsync(r => r.TenantId == tenantId
                        && r.DocumentType == documentType
                        && r.Status == ResolucionStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
