using Cassam.Core.Domain.Common;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// One pending DIAN transmission entry. When the transmission worker
/// cannot reach DIAN at sign time, the document is left in
/// <see cref="Enums.EstadoDocumento.Queued"/> and a row is enqueued here so
/// the drain worker can retry FIFO without losing the signed XML.
///
/// The 1:1 link to <see cref="DocumentoElectronico"/> is enforced by a
/// UNIQUE constraint on <c>documento_electronico_id</c>. The
/// <c>(tenant_id, queued_at)</c> composite index lets the worker pull
/// the oldest batch efficiently.
/// </summary>
public class ContingencyQueue : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Foreign key to the <see cref="DocumentoElectronico"/> awaiting
    /// transmission. Unique — one queue entry per document.
    /// </summary>
    public Guid DocumentoElectronicoId { get; set; }

    /// <summary>UTC timestamp the document was first queued. Drives FIFO order in the drain worker.</summary>
    public DateTime QueuedAt { get; set; }

    /// <summary>Number of failed drain attempts so far. Reset on success.</summary>
    public int RetryCount { get; set; }

    /// <summary>Last error message captured from a failed drain attempt (HTTP / TLS / AppResponse parsing).</summary>
    public string? LastError { get; set; }

    /// <summary>UTC timestamp the drain worker is next allowed to attempt. Null means "ready now".</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>UTC timestamp the document was successfully drained. Null while still pending.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAt { get; set; }
}
