using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Services;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Phase 2 in-memory implementation of
/// <see cref="ITenantLifecycleService"/>. Used by the manager
/// cancel-tenant modal so the demo flow works end-to-end without
/// the real EF-backed implementation. Phase 3 swaps this out for
/// the production <c>Cassam.Core.Tenancy.TenantLifecycleService</c>
/// via DI.
///
/// <para>
/// Records the last requested deletion so unit tests can assert
/// the modal called the service with the correct tenant id +
/// actor user id (per the integration contract tests in
/// <c>src/Ui/Cassam.Ui.Tests/Contract/</c>).
/// </para>
/// </summary>
public sealed class InMemoryTenantLifecycleService : ITenantLifecycleService
{
    private readonly object _gate = new();
    private readonly HashSet<Guid> _deleted = new();

    /// <summary>The tenant ids that have been soft-deleted through this stub.</summary>
    public IReadOnlyCollection<Guid> DeletedTenants
    {
        get { lock (_gate) return _deleted.ToArray(); }
    }

    /// <summary>The most recent (tenantId, actorUserId) pair passed to <see cref="RequestDeletionAsync"/>.</summary>
    public (Guid TenantId, Guid ActorUserId)? LastRequest { get; private set; }

    /// <inheritdoc />
    public Task RequestDeletionAsync(
        Guid tenantId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_deleted.Contains(tenantId))
            {
                throw new InvalidOperationException(
                    $"El tenant '{tenantId}' ya fue cancelado.");
            }
            _deleted.Add(tenantId);
            LastRequest = (tenantId, actorUserId);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsFiscalRetentionExpiredAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    /// <inheritdoc />
    public Task<IReadOnlyList<Tenant>> FindDeletionPendingTenantsAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Tenant>>(Array.Empty<Tenant>());

    /// <inheritdoc />
    public Task EnforceFiscalRetentionAsync(
        Tenant tenant,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}