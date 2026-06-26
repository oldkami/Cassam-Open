using System;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Stubs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Manager-flow DIAN status panel view-model (T2.10,
/// REQ-UI-03, REQ-UI-08, REQ-UI-10, REQ-DIAN-06, SCN-DIAN-07).
/// Owns the regulatory control center that surfaces the
/// resolucion / certificate / transmission / contingency state.
///
/// <para>
/// The panel polls every <see cref="PollInterval"/> seconds while
/// visible. The polling is done with a plain
/// <see cref="System.Threading.Timer"/> rather than the
/// dispatcher-timer because the VM is platform-neutral and the
/// timer marshals onto the UI thread via the property setters
/// (CommunityToolkit.Mvvm's source-generated notifications use
/// the synchronization context when present).
/// </para>
///
/// <para>
/// The actions in design §5.2 ("Rotar certificado",
/// "Corregir+Retransmitir", "Anular+NC") land in PR 11+
/// alongside the Phase 4b DIAN implementation. PR 10 ships
/// stub commands that record the intent and surface a
/// "Disponible tras habilitar" banner so the UX is complete.
/// </para>
/// </summary>
public partial class DianStatusPanelViewModel : ObservableObject, IDisposable
{
    private readonly IDianStatusProvider _dian;
    private readonly ISyncStateProvider _sync;
    private readonly IRetryableDianAction _retryAction;
    private readonly IVoidableDianAction _voidAction;

    private Timer? _pollTimer;
    private bool _disposed;

    /// <summary>How often the panel polls the providers while visible.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // ---- DIAN state -----------------------------------------------------

    /// <summary>Number of active resoluciones for the active tenant.</summary>
    [ObservableProperty]
    private int _activeResolucionesCount;

    /// <summary>Number of certificates expiring in the next 30 days. Drives the warning badge (REQ-DIAN-06).</summary>
    [ObservableProperty]
    private int _certificatesExpiringIn30Days;

    /// <summary>
    /// Last DIAN transmission result code (0 = Validated, 1 = Rejected, 2 = Pending).
    /// </summary>
    [ObservableProperty]
    private int _lastTransmissionResultCode;

    /// <summary>Human-readable message DIAN returned with the last transmission.</summary>
    [ObservableProperty]
    private string _lastTransmissionResultMessage = string.Empty;

    /// <summary>Number of documents waiting in the contingency queue.</summary>
    [ObservableProperty]
    private int _contingencyQueueDepth;

    /// <summary>True when the DIAN subsystem is in contingency mode. Drives the badge color.</summary>
    [ObservableProperty]
    private bool _isInContingencyMode;

    // ---- Sync state -----------------------------------------------------

    /// <summary>Number of operations waiting in the sync queue.</summary>
    [ObservableProperty]
    private int _syncQueueDepth;

    /// <summary>UTC timestamp the last successful sync completed. Null when never synced.</summary>
    [ObservableProperty]
    private DateTimeOffset? _lastSyncAt;

    /// <summary>Sync engine mode (Online / LimitedConnectivity / Offline).</summary>
    [ObservableProperty]
    private SyncMode _syncMode = SyncMode.Online;

    // ---- UI state -------------------------------------------------------

    /// <summary>One-line status message ("Actualizado", "Reintentando...", "Disponible tras habilitar").</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>UTC timestamp of the most recent successful refresh.</summary>
    [ObservableProperty]
    private DateTimeOffset _lastRefreshedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Color for the transmission-result badge. Green for validated,
    /// red for rejected, amber for pending.
    /// </summary>
    public string LastTransmissionBadgeColor => LastTransmissionResultCode switch
    {
        0 => "Green",
        1 => "Red",
        2 => "Orange",
        _ => "Gray",
    };

    public DianStatusPanelViewModel(
        IDianStatusProvider dian,
        ISyncStateProvider sync,
        IRetryableDianAction retryAction,
        IVoidableDianAction voidAction)
    {
        ArgumentNullException.ThrowIfNull(dian);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(retryAction);
        ArgumentNullException.ThrowIfNull(voidAction);
        _dian = dian;
        _sync = sync;
        _retryAction = retryAction;
        _voidAction = voidAction;
    }

    /// <summary>
    /// One-shot pull of the current DIAN + sync state. Called on
    /// page load + every <see cref="PollInterval"/>.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var dian = await _dian.GetStatusAsync(ct);
            ActiveResolucionesCount = dian.ActiveResoluciones;
            CertificatesExpiringIn30Days = dian.CertificatesExpiringIn30Days;
            LastTransmissionResultCode = dian.LastTransmissionResultCode;
            // The stub does not currently expose a message string;
            // render a friendly message based on the code so the
            // panel is not blank.
            LastTransmissionResultMessage = dian.LastTransmissionResultCode switch
            {
                0 => "Validación OK",
                1 => "Documento rechazado por la DIAN",
                2 => "Pendiente de transmisión",
                _ => "Sin transmisiones registradas",
            };
            ContingencyQueueDepth = dian.ContingencyQueueDepth;
            IsInContingencyMode = dian.IsInContingencyMode;

            var syncState = await _sync.GetStateAsync(ct);
            SyncQueueDepth = syncState.QueueDepth;
            LastSyncAt = syncState.LastSyncAt;
            SyncMode = syncState.Mode;

            LastRefreshedAt = DateTimeOffset.UtcNow;
            OnPropertyChanged(nameof(LastTransmissionBadgeColor));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al actualizar: {ex.Message}";
        }
    }

    /// <summary>
    /// Start the 5-second polling timer. Called by the View when
    /// the panel becomes visible.
    /// </summary>
    public void StartPolling()
    {
        if (_pollTimer is not null) return;
        _pollTimer = new Timer(
            _ => _ = RefreshAsync(),
            state: null,
            dueTime: PollInterval,
            period: PollInterval);
    }

    /// <summary>Stop the polling timer. Called by the View when the panel is hidden.</summary>
    public void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    /// <summary>
    /// Action: "Reintentar" (retry the last rejected transmission).
    /// Stub returns "Disponible tras habilitar" until Phase 4b
    /// wires the real retry command.
    /// </summary>
    [RelayCommand]
    public async Task RetryLastRejectedAsync(CancellationToken ct = default)
    {
        try
        {
            await _retryAction.RetryLastRejectedAsync(ct);
            StatusMessage = "Reintento en cola.";
        }
        catch (NotImplementedException)
        {
            StatusMessage = "Disponible tras habilitar (Fase 4b).";
        }
        await RefreshAsync(ct);
    }

    /// <summary>
    /// Action: "Anular último" (void the last rejected document —
    /// issues a Nota Crédito referencing the original CUFE per
    /// REQ-DIAN-05). Stub returns "Disponible tras habilitar"
    /// until Phase 4b wires the real void command.
    /// </summary>
    [RelayCommand]
    public async Task VoidLastRejectedAsync(CancellationToken ct = default)
    {
        try
        {
            await _voidAction.VoidLastRejectedAsync(ct);
            StatusMessage = "Anulación en cola.";
        }
        catch (NotImplementedException)
        {
            StatusMessage = "Disponible tras habilitar (Fase 4b).";
        }
        await RefreshAsync(ct);
    }

    partial void OnLastTransmissionResultCodeChanged(int value)
        => OnPropertyChanged(nameof(LastTransmissionBadgeColor));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Stable seam for the "Reintentar" action. The Phase 2 stub
/// returns <see cref="NotImplementedException"/> so the UI can
/// surface the friendly "Disponible tras habilitar" message;
/// Phase 4b replaces the stub with the real retry implementation.
/// </summary>
public interface IRetryableDianAction
{
    Task RetryLastRejectedAsync(CancellationToken ct);
}

/// <summary>
/// Phase 2 default implementation of <see cref="IRetryableDianAction"/>.
/// </summary>
public sealed class StubRetryableDianAction : IRetryableDianAction
{
    /// <inheritdoc />
    public Task RetryLastRejectedAsync(CancellationToken ct) =>
        throw new NotImplementedException(
            "Retry action lands in Phase 4b alongside the real DIAN transmission worker.");
}

/// <summary>
/// Stable seam for the "Anular último" action (Nota Crédito
/// against the original CUFE per REQ-DIAN-05).
/// </summary>
public interface IVoidableDianAction
{
    Task VoidLastRejectedAsync(CancellationToken ct);
}

/// <summary>
/// Phase 2 default implementation of <see cref="IVoidableDianAction"/>.
/// </summary>
public sealed class StubVoidableDianAction : IVoidableDianAction
{
    /// <inheritdoc />
    public Task VoidLastRejectedAsync(CancellationToken ct) =>
        throw new NotImplementedException(
            "Void action lands in Phase 4b alongside the real DIAN Nota Crédito flow.");
}