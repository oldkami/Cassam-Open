using System;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Core.Domain.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Manager-flow cancel-tenant modal view-model. Implements the
/// destructive workflow per REQ-MT-05 / SCN-MT-05 / design §5.3:
///
/// <list type="number">
///   <item>Manager opens the modal and sees the tenant's NIT.</item>
///   <item>Manager types the NIT to confirm (defense against
///         accidental clicks — see REQ-MT-05).</item>
///   <item>Manager taps "Confirmar cancelación" → the VM calls
///         <see cref="ITenantLifecycleService.RequestDeletionAsync"/>
///         with the active tenant id + actor user id.</item>
///   <item>On success the VM surfaces the tenant as DELETED with
///         the 5-year fiscal retention expiry date.</item>
///   <item>On failure (e.g. service throws) the VM surfaces the
///         error message — the operator MUST see the failure so
///         they don't assume the workflow completed.</item>
/// </list>
///
/// <para>
/// The 5-year retention window is hard-coded to match the
/// Postgres trigger's <c>deleted_at + interval '5 years'</c>
/// policy (design §11). Phase 3 surfaces the dynamic value from
/// a configuration provider.
/// </para>
/// </summary>
public partial class CancelTenantModalViewModel : ObservableObject
{
    private readonly ITenantLifecycleService _lifecycle;
    private readonly ITenantSessionState _session;

    /// <summary>The NIT the manager must type to confirm the action.</summary>
    [ObservableProperty]
    private string _confirmationInput = string.Empty;

    /// <summary>The tenant's NIT (read-only reference). Compared against <see cref="ConfirmationInput"/>.</summary>
    [ObservableProperty]
    private string _tenantNit = string.Empty;

    /// <summary>The tenant's legal name (read-only reference).</summary>
    [ObservableProperty]
    private string _tenantLegalName = string.Empty;

    /// <summary>UTC date the 5-year fiscal retention lock expires.</summary>
    [ObservableProperty]
    private DateTime? _retentionExpiresAt;

    /// <summary>True while <see cref="ConfirmCancelAsync"/> is in flight.</summary>
    [ObservableProperty]
    private bool _isSubmitting;

    /// <summary>True after a successful cancellation. Hides the form, shows the success card.</summary>
    [ObservableProperty]
    private bool _isCancelled;

    /// <summary>Error message surfaced to the operator. Empty when the modal is in its idle state.</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// True when the typed <see cref="ConfirmationInput"/> matches
    /// <see cref="TenantNit"/> (case-insensitive, trimmed). Drives
    /// the "Confirmar" button's enabled state.
    /// </summary>
    public bool CanConfirm =>
        !string.IsNullOrWhiteSpace(ConfirmationInput) &&
        string.Equals(
            ConfirmationInput.Trim(),
            TenantNit?.Trim() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

    public CancelTenantModalViewModel(
        ITenantLifecycleService lifecycle,
        ITenantSessionState session)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(session);
        _lifecycle = lifecycle;
        _session = session;
    }

    /// <summary>
    /// Load the snapshot so the modal can show the tenant
    /// identity + retention expiry date. Called by the View when
    /// the modal opens.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var snap = await _session.GetSnapshotAsync(ct);
        TenantNit = snap.TenantNit;
        TenantLegalName = snap.TenantLegalName;
        // 5-year retention window per DD-04 / REQ-MT-05.
        RetentionExpiresAt = DateTime.UtcNow.AddYears(5);
        OnPropertyChanged(nameof(CanConfirm));
    }

    /// <summary>
    /// Submit the cancellation. Resolves the tenant id + actor
    /// user id from the session snapshot and calls
    /// <see cref="ITenantLifecycleService.RequestDeletionAsync"/>.
    /// </summary>
    [RelayCommand]
    public async Task ConfirmCancelAsync(CancellationToken ct = default)
    {
        if (!CanConfirm || IsSubmitting || IsCancelled) return;

        IsSubmitting = true;
        ErrorMessage = string.Empty;
        try
        {
            var snap = await _session.GetSnapshotAsync(ct);
            await _lifecycle.RequestDeletionAsync(
                tenantId: snap.TenantId,
                actorUserId: snap.UserId,
                ct);
            IsCancelled = true;
        }
        catch (InvalidOperationException ex)
        {
            // Service throws InvalidOperationException for
            // double-delete / non-Active tenants — surface the
            // exact reason so the operator can investigate.
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            // Anything else is also surfaced — we never silently
            // dismiss a destructive workflow's failure.
            ErrorMessage = $"No se pudo cancelar el tenant: {ex.Message}";
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Notify the CanConfirm dependency so the View updates the
    /// Confirm button enable state on every keystroke.
    /// </summary>
    partial void OnConfirmationInputChanged(string value) => OnPropertyChanged(nameof(CanConfirm));
}