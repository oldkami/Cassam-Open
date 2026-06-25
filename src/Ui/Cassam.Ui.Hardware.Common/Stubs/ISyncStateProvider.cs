using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Stubs;

/// <summary>
/// How the sync engine is currently running. The cashier offline
/// banner colours itself against this enum (DD-10, SCN-UI-07).
/// </summary>
public enum SyncMode
{
    /// <summary>Fully online — last sync &lt; 30 s ago, no throttling.</summary>
    Online,

    /// <summary>Online but metered — bandwidth throttle (T4a.07) is active.</summary>
    LimitedConnectivity,

    /// <summary>No connectivity — running on local queue only.</summary>
    Offline,
}

/// <summary>
/// A snapshot of the sync engine's state. Mirrors the cashier
/// offline banner (DD-10). Phase 2 stub returns
/// <see cref="SyncMode.Online"/> with zero queue depth.
/// </summary>
public sealed record SyncState(
    int QueueDepth,
    DateTimeOffset? LastSyncAt,
    bool IsOnline,
    SyncMode Mode);

/// <summary>
/// Stable seam between the UI and the sync engine. The interface
/// is the contract that Phase 4a must honour; the UI never talks
/// to the queue table directly (R-UI-06).
/// </summary>
public interface ISyncStateProvider
{
    /// <summary>
    /// Snapshot the current sync state. The cashier banner polls
    /// every 5 s when online, immediately on state change.
    /// </summary>
    Task<SyncState> GetStateAsync(CancellationToken ct);
}

/// <summary>
/// Phase 2 default implementation. Returns
/// <see cref="SyncMode.Online"/> with zero queue depth so the UI
/// renders as fully-online before Phase 4a lands.
/// </summary>
public sealed class StubSyncStateProvider : ISyncStateProvider
{
    /// <inheritdoc />
    public Task<SyncState> GetStateAsync(CancellationToken ct) =>
        Task.FromResult(new SyncState(
            QueueDepth: 0,
            LastSyncAt: DateTimeOffset.UtcNow,
            IsOnline: true,
            Mode: SyncMode.Online));
}
