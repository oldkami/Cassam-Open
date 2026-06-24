using Cassam.Core.Domain.Common;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// One append-only audit row recording a fiscal mutation per
/// <c>pos-core-modern-stack</c> REQ-CORE-03 / SCN-CORE-03. The
/// <see cref="Cassam.Core.Audit.IAuditLogWriter"/> contract guarantees a single
/// <c>INSERT</c> per state transition; <c>UPDATE</c> and <c>DELETE</c> are
/// denied by a PostgreSQL RLS policy plus a <c>BEFORE UPDATE OR DELETE</c>
/// trigger emitted in a later migration (T1.11).
///
/// <see cref="BeforeState"/> / <see cref="AfterState"/> are the entity's
/// <c>estado</c> values (e.g. <c>"DRAFT"</c>, <c>"SIGNED"</c>) — not the
/// raw row JSON — so audit reads are cheap and the column stays narrow.
///
/// Audit rows are intentionally NOT soft-deletable: the immutability
/// contract (REQ-CORE-03) outlives a row "delete" request. A soft-delete
/// of an audit trail would defeat regulatory inspection.
/// </summary>
public class AuditLog : Entity, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// User that triggered the mutation. System-driven entries
    /// (drain worker, scheduled jobs) reference the tenant's sentinel
    /// <c>SYSTEM</c> user — every audit row carries an actor per REQ-CORE-03.
    /// </summary>
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Logical entity name (e.g. <c>"documento_electronico"</c>,
    /// <c>"certificado"</c>, <c>"resolucion"</c>).
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Primary key of the affected row.</summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Stable action verb (e.g. <c>"TRANSITION_QUEUED"</c>,
    /// <c>"CERTIFICATE_ROTATED"</c>, <c>"RESOLOLUTION_ACTIVATED"</c>).
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>State name before the mutation. Null on create-only entries.</summary>
    public string? BeforeState { get; set; }

    /// <summary>State name after the mutation. Null is allowed only for failure events.</summary>
    public string? AfterState { get; set; }

    /// <summary>Client IP captured at the audit boundary. Null for background workers.</summary>
    public string? IpAddress { get; set; }

    /// <summary>UTC timestamp the audit entry was written. Mirrors <see cref="Entity.CreatedAt"/>.</summary>
    public DateTime OccurredAt { get; set; }
}
