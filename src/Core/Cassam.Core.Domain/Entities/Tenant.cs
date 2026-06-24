using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// Root of the tenancy hierarchy. Every other business entity that
/// implements <see cref="ITenantScoped"/> references this table via
/// its <c>tenant_id</c> foreign key, and PostgreSQL Row-Level Security
/// (DD-05) filters every query by the session-set
/// <c>app.current_tenant_id</c>.
///
/// Note: <see cref="Tenant"/> is intentionally NOT
/// <see cref="ISoftDeletable"/> — it is the tenancy root. Deletion is
/// modeled via the <see cref="TenantStatus.Deleted"/> terminal status
/// AND the <see cref="DeletedAt"/> timestamp, which together preserve
/// fiscal history for the regulatory retention window (5 years default)
/// per <c>cloud-saas-multi-tenant</c> REQ-MT-05 and SCN-CORE-13.
///
/// <para>
/// <see cref="DeletedAt"/> is separate from <see cref="Entity.UpdatedAt"/>
/// because it has different semantics: it is the moment the tenant
/// transitioned to <see cref="TenantStatus.Deleted"/> and is the
/// anchor for the 5-year fiscal retention lock enforced by the
/// <c>documentos_electronicos_retention_lock</c> trigger.
/// </para>
/// </summary>
public class Tenant : Entity
{
    /// <summary>Registered legal name of the business (Razón Social).</summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>
    /// Colombian tax identification number (Número de Identificación
    /// Tributaria). Unique per tenant — the composite unique index
    /// <c>(nit, deleted_at IS NULL)</c> prevents accidental
    /// re-registration after a soft-delete.
    /// </summary>
    public string Nit { get; set; } = string.Empty;

    /// <summary>Subscription tier — drives metering and limits (REQ-MT-06).</summary>
    public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.Free;

    /// <summary>UTC timestamp when the tenant first subscribed.</summary>
    public DateTime SubscriptionStartedAt { get; set; }

    /// <summary>Lifecycle status.</summary>
    public TenantStatus Status { get; set; } = TenantStatus.Trial;

    /// <summary>
    /// UTC timestamp the tenant transitioned to
    /// <see cref="TenantStatus.Deleted"/>. Null for active tenants.
    /// The 5-year fiscal retention lock activates
    /// <c>deleted_at + interval '5 years'</c> — after which
    /// <c>documentos_electronicos</c> UPDATE is denied by the
    /// retention-lock trigger emitted in the
    /// <c>TenantDeletedAt</c> migration.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Per-tenant DIAN transmission feature flag (REQ-MT-08). When
    /// <c>false</c> the cloud forwards analytics only and does not
    /// transmit documents to DIAN — on-prem POS is unaffected.
    /// </summary>
    public bool CloudTransmissionEnabled { get; set; }
}
