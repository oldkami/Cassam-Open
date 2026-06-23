using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// Tenant user account. Authentication is performed by Keycloak
/// (<c>cloud-saas-multi-tenant</c> DD-04), but this row stores the
/// canonical display metadata plus the legacy <c>password_hash</c>
/// field used during the on-prem / cutover phase.
/// </summary>
public class User : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Entities.Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Login email. Unique within a tenant (composite index
    /// <c>(tenant_id, lower(email))</c>); case-insensitive.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Password hash. Stored as Argon2id during the on-prem phase;
    /// once Keycloak is the source of truth (Phase 3) this column is
    /// deprecated and the JWT signature is the only credential.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Operator display name shown in the UI / receipts.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Authorization role within the tenant.</summary>
    public UserRole Role { get; set; } = UserRole.Cashier;

    /// <summary>False when the operator is suspended but not deleted.</summary>
    public bool Active { get; set; } = true;

    /// <inheritdoc />
    public DateTime? DeletedAt { get; set; }
}
