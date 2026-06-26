using System;
using Cassam.Ui.Hardware.Common.Manager;

namespace Cassam.Ui.Manager;

/// <summary>
/// Code-behind for <see cref="TenantAdminView"/>. Resolves the
/// view-model through DI, refreshes on load, and opens the
/// cancel-tenant dialog when the user taps the destructive
/// action button.
/// </summary>
public sealed partial class TenantAdminView : Page
{
    /// <summary>The tenant-admin view-model (singleton, DI-resolved).</summary>
    public TenantAdminViewModel ViewModel { get; }

    /// <summary>
    /// Raised when the user taps "Cancelar tenant". The shell
    /// forwards this to the App root which opens the
    /// <see cref="CancelTenantDialog"/>.
    /// </summary>
    public event EventHandler? CancelTenantRequested;

    public TenantAdminView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<TenantAdminViewModel>();
        this.DataContext = ViewModel;

        this.Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }

    private void OnCancelTenantClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.BeginCancel();
        CancelTenantRequested?.Invoke(this, EventArgs.Empty);
    }
}