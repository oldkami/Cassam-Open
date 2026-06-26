using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Phase 2 stub for the manager shell's session-state provider.
/// Returns a deterministic fixture (Cassam SAS demo tenant, Owner
/// user, Station-01) so the manager UI renders with realistic
/// content on first launch. Phase 3 wires the real Keycloak +
/// Postgres-backed implementation.
/// </summary>
public sealed class StubTenantSessionState : ITenantSessionState
{
    private readonly TenantSessionSnapshot _fixture;

    /// <summary>
    /// Build the stub with the default fixture (Cassam SAS,
    /// Owner user, Station-01).
    /// </summary>
    public StubTenantSessionState()
        : this(new TenantSessionSnapshot(
            TenantId: Guid.Parse("30000000-0000-0000-0000-000000000001"),
            TenantLegalName: "Cassam S.A.S.",
            TenantNit: "900111222-3",
            SubscriptionTier: Core.Domain.Enums.SubscriptionTier.Pro,
            CloudTransmissionEnabled: true,
            UserId: Guid.Parse("30000000-0000-0000-0000-000000000002"),
            UserDisplayName: "Administrador Demo",
            UserRole: "OWNER",
            StationId: "STATION-01"))
    {
    }

    /// <summary>Test constructor — inject a custom snapshot.</summary>
    public StubTenantSessionState(TenantSessionSnapshot fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _fixture = fixture;
    }

    /// <inheritdoc />
    public Task<TenantSessionSnapshot> GetSnapshotAsync(CancellationToken ct)
        => Task.FromResult(_fixture);
}