using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Services;

/// <summary>
/// Lifecycle operations on <see cref="Tenant"/> per
/// <c>pos-core-modern-stack</c> REQ-CORE-12 / SCN-CORE-13. Distinct
/// from the <see cref="IResolucionLifecycleService"/> family: this
/// service owns tenant soft-delete (transitions
/// <see cref="TenantStatus.Active"/> → <see cref="TenantStatus.Deleted"/>)
/// and the 5-year fiscal retention lock that fires
/// <c>deleted_at + interval '5 years'</c> after deletion.
///
/// <para>
/// <b>Why a separate service?</b> Tenant deletion is operationally rare
/// (it happens once per customer cancellation, typically) and the
/// regulatory implications are severe — getting it wrong means losing
/// fiscal history for a tax-paying business. Splitting the service
/// keeps the audit trail (every state transition emits an
/// <see cref="AuditLog"/> row per REQ-CORE-03) on a narrow surface that
/// future operators and compliance officers can review independently
/// of the high-frequency dispatcher services.
/// </para>
///
/// <para>
/// <b>What this service does NOT do:</b>
/// <list type="bullet">
///   <item>It does NOT soft-delete child entities (sales, customers,
///         documents). The 5-year retention requirement (REQ-MT-05)
///         says fiscal data MUST survive tenant deletion; the per-entity
///         query filters continue to surface the rows as long as the
///         tenant's own <c>deleted_at</c> is in the retention window.</item>
///   <item>It does NOT hard-delete tenants. Hard-delete happens via a
///         separate scheduled job after the retention window expires
///         (out of scope for T1.12; T3.05).</item>
///   <item>It does NOT change the tenant's <c>nit</c>. The composite
///         <c>(nit, deleted_at)</c> unique index lets a new tenant
///         re-register the same NIT after the soft-delete + retention
///         window completes.</item>
/// </list>
/// </para>
/// </summary>
public interface ITenantLifecycleService
{
    /// <summary>
    /// Transitions a tenant from <see cref="TenantStatus.Active"/> to
    /// <see cref="TenantStatus.Deleted"/>, stamps <c>deleted_at</c>,
    /// and appends an audit-log entry in the same transaction. This
    /// is the soft-delete entry point operators call from the
    /// cancel-tenant workflow.
    /// </summary>
    /// <param name="tenantId">Tenant to soft-delete.</param>
    /// <param name="actorUserId">
    /// User that triggered the deletion. Recorded on the audit row.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Throws <see cref="InvalidOperationException"/> when:
    /// <list type="bullet">
    ///   <item>The tenant does not exist.</item>
    ///   <item>The tenant is already in <see cref="TenantStatus.Deleted"/>
    ///         — double-delete is not idempotent and MUST be rejected
    ///         so the operator can investigate.</item>
    ///   <item>The tenant is in <see cref="TenantStatus.Trial"/> or
    ///         <see cref="TenantStatus.Suspended"/> — only ACTIVE
    ///         tenants can transition to Deleted.</item>
    /// </list>
    /// </remarks>
    Task RequestDeletionAsync(
        Guid tenantId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the tenant's <c>deleted_at</c> is older
    /// than 5 years ago. Used by the retention-enforcement worker to
    /// decide whether to call <see cref="EnforceFiscalRetentionAsync"/>.
    /// </summary>
    /// <param name="tenantId">Tenant to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if <c>now - deleted_at &gt;= 5 years</c>; <c>false</c>
    /// otherwise (including when the tenant is not deleted).
    /// </returns>
    Task<bool> IsFiscalRetentionExpiredAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists tenants currently in the <see cref="TenantStatus.Deleted"/>
    /// state. The retention worker scans this list daily, checks
    /// <see cref="IsFiscalRetentionExpiredAsync"/>, and calls
    /// <see cref="EnforceFiscalRetentionAsync"/> on each expired row.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Tenant>> FindDeletionPendingTenantsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the fiscal retention lock to every
    /// <see cref="DocumentoElectronico"/> belonging to the tenant
    /// whose <c>retention_locked_at</c> is null. After this call, a
    /// PostgreSQL trigger denies UPDATE on the affected rows even from
    /// a DBA session — the regulatory record is frozen.
    /// </summary>
    /// <param name="tenant">Tenant whose documents must be locked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The caller is responsible for verifying the tenant's retention
    /// window has expired via <see cref="IsFiscalRetentionExpiredAsync"/>
    /// — this method does NOT re-check. (It would be defensive to do
    /// so, but the scheduled worker would rather log + skip than
    /// throw on a clock skew bug.)
    /// </remarks>
    Task EnforceFiscalRetentionAsync(
        Tenant tenant,
        CancellationToken cancellationToken = default);
}