using Cassam.Core.Domain.Entities;
using Cassam.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Audit;

/// <summary>
/// EF Core-backed <see cref="IAuditLogWriter"/>. The writer is the only
/// code path that inserts <see cref="AuditLog"/> rows; combined with the
/// PostgreSQL RLS <c>DELETE</c> denial + <c>BEFORE UPDATE OR DELETE</c>
/// trigger (emitted in T1.11) this satisfies the immutability contract
/// of <c>pos-core-modern-stack</c> REQ-CORE-03 / SCN-CORE-03.
///
/// Append-only enforcement at the application layer:
/// <list type="bullet">
///   <item>The writer only ever calls <see cref="EntityState.Added"/> via
///         <c>dbContext.AuditLog.Add(...)</c> — never <c>Update</c> or
///         <c>Remove</c>.</item>
///   <item>EF Core's change tracker cannot reach <see cref="EntityState.Modified"/>
///         on a row we just added without an explicit developer action;
///         the writer never performs that action.</item>
///   <item>The <c>audit_log</c> table has no <c>DeletedAt</c> column and
///         no <c>HasQueryFilter</c> — the only way to "delete" a row is
///         to issue a raw SQL <c>DELETE</c>, which the DB trigger rejects.</item>
/// </list>
///
/// Transaction contract: the writer runs inside whatever
/// <see cref="DbContext"/> instance the caller is using for the
/// mutation. Callers SHOULD invoke <c>SaveChanges</c> from that same
/// context (not from a separate context) so the audit row and the
/// fiscal mutation land in the same transaction — REQ-CORE-03's atomic
/// guarantee. This class deliberately does not own a DbContext.
/// </summary>
public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly CassamDbContext _dbContext;

    /// <summary>
    /// Constructs the writer around the caller's <see cref="CassamDbContext"/>.
    /// The instance is captured; the caller is responsible for its lifetime.
    /// </summary>
    /// <param name="dbContext">
    /// The same <see cref="CassamDbContext"/> that holds the mutation
    /// being audited. Passing the same context guarantees the audit row
    /// and the mutation share one transaction.
    /// </param>
    public AuditLogWriter(CassamDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Persists the entry by adding it to the change tracker as
    /// <see cref="EntityState.Added"/>. The caller's <c>SaveChanges</c>
    /// (or <c>SaveChangesAsync</c>) flushes it. We do NOT call
    /// <c>SaveChanges</c> here — that would split the audit row out of
    /// the fiscal-mutation transaction and break the atomicity contract.
    /// </remarks>
    public Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTime.UtcNow;

        var row = new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = entry.TenantId,
            ActorUserId = entry.ActorUserId,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            BeforeState = entry.BeforeState,
            AfterState = entry.AfterState,
            IpAddress = entry.IpAddress,
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1u,
        };

        _dbContext.AuditLog.Add(row);

        // Returned Task is already completed; the async signature exists
        // so future implementations (e.g. an out-of-process durable queue)
        // can swap in without breaking callers.
        return Task.CompletedTask;
    }
}
