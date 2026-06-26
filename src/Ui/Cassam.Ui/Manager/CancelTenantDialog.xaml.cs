using Cassam.Ui.Hardware.Common.Manager;

namespace Cassam.Ui.Manager;

/// <summary>
/// Code-behind for <see cref="CancelTenantDialog"/>. Resolves the
/// view-model through DI, loads the tenant snapshot on
/// construction, and forwards the "Confirm" tap to the VM's
/// <c>ConfirmCancelAsync</c> command which calls the Phase 1
/// <c>ITenantLifecycleService.RequestDeletionAsync</c>.
/// </summary>
public sealed partial class CancelTenantDialog : ContentDialog
{
    /// <summary>The cancel-tenant modal view-model (singleton, DI-resolved).</summary>
    public CancelTenantModalViewModel ViewModel { get; }

    public CancelTenantDialog()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<CancelTenantModalViewModel>();
        this.DataContext = ViewModel;

        _ = ViewModel.LoadAsync();
    }

    private async void OnConfirmClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.ConfirmCancelAsync();
            if (ViewModel.IsCancelled)
            {
                // Keep the dialog open so the user sees the
                // success state + retention expiry date. The host
                // closes the dialog when the user taps the close
                // button (which remains available).
                args.Cancel = true;
                this.IsPrimaryButtonEnabled = false;
            }
            else
            {
                // Validation failure (NIT mismatch / service
                // error): keep the dialog open so the operator
                // can correct + retry.
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}