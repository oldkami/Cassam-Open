namespace Cassam.Core.Audit;

/// <summary>
/// Captures the fields required by spec
/// <c>pos-core-modern-stack</c> REQ-CORE-03 for an audit-log entry
/// on a fiscal-mutation state transition.
/// </summary>
/// <param name="ActorUserId">User id that triggered the transition.</param>
/// <param name="TenantId">Tenant under which the transition ran.</param>
/// <param name="EntityType">Logical entity name (e.g. <c>"documento_electronico"</c>).</param>
/// <param name="EntityId">Primary key of the affected row.</param>
/// <param name="Action">
/// Stable action verb (e.g. <c>"TRANSITION_QUEUED"</c>,
/// <c>"TRANSITION_SIGNED"</c>, <c>"CERTIFICATE_ROTATED"</c>).
/// </param>
/// <param name="BeforeState">State name before the transition (null on create).</param>
/// <param name="AfterState">State name after the transition.</param>
/// <param name="IpAddress">Client IP captured at the audit boundary.</param>
public sealed record AuditLogEntry(
    Guid ActorUserId,
    Guid TenantId,
    string EntityType,
    Guid EntityId,
    string Action,
    string? BeforeState,
    string? AfterState,
    string? IpAddress);

/// <summary>
/// Append-only audit-log writer contract. Every fiscal mutation
/// (state transition on <c>documentos_electronicos</c>, certificate
/// rotation, resolución rollover, tenant soft-delete) calls
/// <see cref="WriteAsync"/> to insert an immutable row.
///
/// The PostgreSQL implementation denies <c>UPDATE</c> and
/// <c>DELETE</c> via RLS policy + DB trigger per SCN-CORE-03.
/// The concrete writer ships in PR 3 (task T1.07); this PR declares
/// the contract so call sites compile.
/// </summary>
public interface IAuditLogWriter
{
    /// <summary>
    /// Persists a single audit row. Implementations MUST run inside
    /// the same database transaction as the mutation being audited
    /// so that an audit failure rolls back the fiscal change.
    /// </summary>
    Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
