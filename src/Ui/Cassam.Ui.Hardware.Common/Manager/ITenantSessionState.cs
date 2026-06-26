using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Snapshot of the tenant / user / station identity used by the
/// manager shell's header bar and by the cancel-tenant modal.
/// Phase 2 ships a stub implementation; Phase 3 wires the real
/// session-state provider that pulls from
/// <c>tenants</c> + <c>users</c> + Keycloak.
/// </summary>
/// <param name="TenantId">Active tenant id.</param>
/// <param name="TenantLegalName">Tenant legal name (Razón Social).</param>
/// <param name="TenantNit">Tenant NIT.</param>
/// <param name="SubscriptionTier">Tenant plan tier.</param>
/// <param name="CloudTransmissionEnabled">
/// True when cloud-side DIAN transmission is enabled for the
/// tenant (REQ-MT-08).
/// </param>
/// <param name="UserId">Logged-in user id.</param>
/// <param name="UserDisplayName">Logged-in user's display name.</param>
/// <param name="UserRole">Role string — "OWNER" / "ADMIN" / "MANAGER" / "CASHIER".</param>
/// <param name="StationId">Station / terminal identifier.</param>
public sealed record TenantSessionSnapshot(
    Guid TenantId,
    string TenantLegalName,
    string TenantNit,
    Core.Domain.Enums.SubscriptionTier SubscriptionTier,
    bool CloudTransmissionEnabled,
    Guid UserId,
    string UserDisplayName,
    string UserRole,
    string StationId)
{
    /// <summary>
    /// True when the user is permitted to cancel the tenant
    /// (REQ-MT-05 restricts the destructive workflow to OWNER).
    /// </summary>
    public bool CanCancelTenant =>
        string.Equals(UserRole, "OWNER", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Manager-flow session state. Returns the active tenant + user
/// + station snapshot so the manager shell, the cancel-tenant
/// modal, and the role-guard logic all read from one source of
/// truth.
/// </summary>
public interface ITenantSessionState
{
    /// <summary>Snapshot the current session. Phase 3 returns the real values.</summary>
    Task<TenantSessionSnapshot> GetSnapshotAsync(CancellationToken ct);
}