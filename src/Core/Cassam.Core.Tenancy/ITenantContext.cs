namespace Cassam.Core.Tenancy;

/// <summary>
/// Ambient tenant identity for the current request / transaction.
/// Implementations are responsible for pushing <c>TenantId</c>
/// into the PostgreSQL session variable <c>app.current_tenant_id</c>
/// before any tenant-scoped query runs (DD-05).
///
/// The cloud-side implementation lives in Phase 3 (T3.02); the
/// on-prem single-tenant implementation is wired in Phase 2.
/// This PR ships the contract only.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The tenant id under which the current unit of work executes.
    /// <c>null</c> is permitted only for system-level operations
    /// (migrations, background reconciliation jobs) running under
    /// the <c>BYPASSRLS</c> PostgreSQL role.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// True when the connection is operating under the
    /// <c>BYPASSRLS</c> role attribute — RLS policies are skipped,
    /// and the caller is responsible for explicit <c>tenant_id</c>
    /// filtering. Should be true only for migration tooling and
    /// administrative scripts (DD-05, REQ-MT-02).
    /// </summary>
    bool BypassesRls { get; }
}
