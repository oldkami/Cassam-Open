using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Manager-flow tenant admin view-model. Surfaces the active
/// tenant's identity (legal name, NIT, subscription tier, cloud
/// transmission flag) and exposes the "Cancelar tenant" action
/// (REQ-MT-05 / SCN-MT-05).
///
/// <para>
/// The destructive workflow itself lives in
/// <see cref="CancelTenantModalViewModel"/>; this VM just owns
/// the read-only summary view + the entry-point command.
/// </para>
/// </summary>
public partial class TenantAdminViewModel : ObservableObject
{
    private readonly ITenantSessionState _session;

    /// <summary>The tenant's legal name (Razón Social).</summary>
    [ObservableProperty]
    private string _tenantLegalName = string.Empty;

    /// <summary>The tenant's NIT.</summary>
    [ObservableProperty]
    private string _tenantNit = string.Empty;

    /// <summary>The tenant's subscription tier.</summary>
    [ObservableProperty]
    private string _subscriptionTier = string.Empty;

    /// <summary>
    /// True when cloud-side DIAN transmission is enabled
    /// (REQ-MT-08). On-prem POS is unaffected when this is false.
    /// </summary>
    [ObservableProperty]
    private bool _cloudTransmissionEnabled;

    /// <summary>One-line status message ("Datos actualizados", "Cancelación solicitada", etc).</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True while the cancel-tenant modal is visible.</summary>
    [ObservableProperty]
    private bool _isCancelling;

    /// <summary>Raised by <see cref="BeginCancelCommand"/> so the View opens the cancel-tenant dialog.</summary>
    public event EventHandler? CancelRequested;

    public TenantAdminViewModel(ITenantSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <summary>Reload the snapshot from the session-state provider.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var snap = await _session.GetSnapshotAsync(ct);
        TenantLegalName = snap.TenantLegalName;
        TenantNit = snap.TenantNit;
        SubscriptionTier = snap.SubscriptionTier.ToString();
        CloudTransmissionEnabled = snap.CloudTransmissionEnabled;
    }

    /// <summary>Request the cancel-tenant workflow. The View listens for <see cref="CancelRequested"/>.</summary>
    [RelayCommand]
    public void BeginCancel()
    {
        IsCancelling = true;
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}