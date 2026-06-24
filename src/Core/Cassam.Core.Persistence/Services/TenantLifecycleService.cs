using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Persistence.Services;

/// <summary>
/// EF Core-backed <see cref="ITenantLifecycleService"/>. Implements
/// the tenant soft-delete + 5-year fiscal retention lock per
/// <c>pos-core-modern-stack</c> REQ-CORE-12 / SCN-CORE-13.
///
/// <para>
/// <b>Time abstraction:</b> the service uses <see cref="TimeProvider"/>
/// (built-in .NET 8+ abstraction) so tests can fake-clock the
/// 5-year retention boundary without sleeping the test process. The
/// constructor takes a <see cref="TimeProvider"/> parameter so DI can
/// inject <see cref="TimeProvider.System"/> in production and a
/// <c>FakeTimeProvider</c> in unit tests.
/// </para>
///
/// <para>
/// <b>Transaction discipline:</b>
/// <list type="bullet">
///   <item><see cref="RequestDeletionAsync"/> writes the tenant status
///         flip + the audit row in a single <c>SaveChangesAsync</c>
///         so they land atomically (REQ-CORE-03).</item>
///   <item><see cref="EnforceFiscalRetentionAsync"/> updates every
///         document with an empty <c>retention_locked_at</c> via a
///         bulk <c>ExecuteUpdateAsync</c> + a tenant-audit row in the
///         same transaction. The DB trigger then freezes the rows for
///         further UPDATE.</item>
/// </list>
/// </para>
/// </summary>
public sealed class TenantLifecycleService : ITenantLifecycleService
{
    /// <summary>
    /// 5-year retention window — the DIAN (and ET) regulatory
    /// requirement per <c>cloud-saas-multi-tenant</c> REQ-MT-05.
    /// Centralized as a constant so tests can reference it.
    /// </summary>
    public static readonly TimeSpan FiscalRetentionPeriod = TimeSpan.FromDays(365 * 5);

    private readonly CassamDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Constructs the service around the caller's <see cref="CassamDbContext"/>
    /// and a <see cref="TimeProvider"/>. The DI container passes
    /// <see cref="TimeProvider.System"/> in production and a fake
    /// provider in unit tests.
    /// </summary>
    /// <param name="dbContext">DbContext that owns the transition.</param>
    /// <param name="timeProvider">
    /// Time abstraction. Defaults to <see cref="TimeProvider.System"/>
    /// when omitted so legacy call sites that only pass a DbContext
    /// continue to work.
    /// </param>
    public TenantLifecycleService(
        CassamDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Throws <see cref="InvalidOperationException"/> on:
    /// <list type="bullet">
    ///   <item>Tenant not found.</item>
    ///   <item>Tenant already in <see cref="TenantStatus.Deleted"/>
    ///         (double-delete guard).</item>
    ///   <item>Tenant in <see cref="TenantStatus.Trial"/> or
    ///         <see cref="TenantStatus.Suspended"/> (only ACTIVE can
    ///         transition to Deleted).</item>
    /// </list>
    /// </remarks>
    public async Task RequestDeletionAsync(
        Guid tenantId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        var tenant = await _dbContext.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' not found. RequestDeletionAsync requires the row to exist.");
        }

        if (tenant.Status == TenantStatus.Deleted)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' is already in Deleted status — " +
                "double-delete is rejected so the operator can investigate the workflow bug.");
        }

        if (tenant.Status != TenantStatus.Active)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' is in status {tenant.Status} — " +
                "only Active tenants may transition to Deleted. " +
                "Trial / Suspended tenants must be resolved (activated or cancelled) before deletion.");
        }

        var beforeState = tenant.Status.ToString();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        tenant.Status = TenantStatus.Deleted;
        tenant.DeletedAt = now;
        tenant.UpdatedAt = now;

        // ---- Audit row ----
        // TenantLifecycleService depends only on the DbContext (no
        // IAuditLogWriter injection) so the dependency stays narrow.
        // The audit row shape mirrors AuditLogWriter so the on-disk
        // format is identical to every other fiscal mutation.
        var auditRow = new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            ActorUserId = actorUserId,
            EntityType = "tenant",
            EntityId = tenant.Id,
            Action = "TENANT_SOFT_DELETED",
            BeforeState = beforeState,
            AfterState = TenantStatus.Deleted.ToString(),
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
    public async Task<bool> IsFiscalRetentionExpiredAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        var deletedAt = await _dbContext.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => (DateTime?)t.DeletedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deletedAt is null) return false;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return now - deletedAt.Value >= FiscalRetentionPeriod;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tenant>> FindDeletionPendingTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _dbContext.Tenants
            .Where(t => t.Status == TenantStatus.Deleted)
            .OrderBy(t => t.DeletedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Locks every <see cref="DocumentoElectronico"/> for the tenant
    /// that has <c>retention_locked_at IS NULL</c>. The DB trigger
    /// (emitted in the <c>TenantDeletedAt</c> migration) then denies
    /// any subsequent UPDATE.
    ///
    /// <para>
    /// <b>Implementation note:</b> this implementation iterates the
    /// affected rows in memory and calls <c>SaveChangesAsync</c> once
    /// at the end. <see cref="EntityFrameworkCore.EntityFrameworkQueryableExtensions.ExecuteUpdateAsync"/>
    /// would be faster for very large tenant document sets but is not
    /// supported by the EF Core InMemory provider used in unit tests.
    /// For Colombian fiscal volumes (hundreds to low thousands of
    /// documents per tenant) the loop is fast enough. A future
    /// optimization PR can swap the loop for ExecuteUpdate on the
    /// relational provider branch (see PR 5 follow-up suggestion
    /// in the verify report).
    /// </para>
    ///
    /// <para>
    /// The audit row records the bulk-lock action with the count of
    /// affected documents in <c>AfterState</c> as <c>"N docs locked"</c>
    /// (the audit log column is VARCHAR(32) so we truncate the count
    /// representation if needed).
    /// </para>
    /// </remarks>
    public async Task EnforceFiscalRetentionAsync(
        Tenant tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var unlockedDocs = await _dbContext.DocumentosElectronicos
            .Where(d => d.TenantId == tenant.Id && d.RetentionLockedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var doc in unlockedDocs)
        {
            doc.RetentionLockedAt = now;
        }

        var affectedRows = unlockedDocs.Count;

        // Tenant-level audit row so the compliance officer sees that the
        // retention lock fired, not just the per-document timestamp.
        var auditRow = new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            ActorUserId = Guid.Empty,  // SYSTEM worker
            EntityType = "tenant",
            EntityId = tenant.Id,
            Action = "FISCAL_RETENTION_LOCKED",
            BeforeState = null,
            AfterState = $"{affectedRows} docs locked",
            IpAddress = null,
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1u,
        };
        _dbContext.AuditLog.Add(auditRow);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}