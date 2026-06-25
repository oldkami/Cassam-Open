using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Stubs;

/// <summary>
/// A snapshot of the DIAN subsystem for the manager status panel
/// (design §7.2, REQ-UI-08). Phase 2 ships a stub that returns
/// safe defaults; Phase 4b replaces the stub with a real provider
/// that reads the <c>documentos_electronicos</c> + <c>contingency_queue</c>
/// tables and emits live events. The shape of this record is part
/// of the stable interface seam and is locked by contract tests
/// (T2.08).
/// </summary>
public sealed record DianStatus(
    int ActiveResoluciones,
    int CertificatesExpiringIn30Days,
    int LastTransmissionResultCode,   // 0=validated, 1=rejected, 2=pending
    int ContingencyQueueDepth,
    bool IsInContingencyMode);

/// <summary>
/// Stable seam between the UI and the DIAN subsystem. The interface
/// is the contract that Phase 4b must honour; the UI never talks to
/// the DIAN HTTP client directly (R-UI-06).
/// </summary>
public interface IDianStatusProvider
{
    /// <summary>
    /// Snapshot the current DIAN status for the active tenant. The
    /// manager status panel (T2.09) calls this on open and again
    /// every 5 s while it is visible.
    /// </summary>
    Task<DianStatus> GetStatusAsync(CancellationToken ct);
}

/// <summary>
/// Phase 2 default implementation. Returns zeros and a clean state
/// so the UI renders without error before Phase 4b lands. The stub
/// is a contract-test fixture: the same contract tests run against
/// the Phase 4b implementation to catch interface drift.
/// </summary>
public sealed class StubDianStatusProvider : IDianStatusProvider
{
    /// <inheritdoc />
    public Task<DianStatus> GetStatusAsync(CancellationToken ct) =>
        Task.FromResult(new DianStatus(
            ActiveResoluciones: 0,
            CertificatesExpiringIn30Days: 0,
            LastTransmissionResultCode: 0,    // 0 = validated/clean
            ContingencyQueueDepth: 0,
            IsInContingencyMode: false));
}
