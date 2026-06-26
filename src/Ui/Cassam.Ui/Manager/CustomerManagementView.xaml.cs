using Cassam.Ui.Hardware.Common.Manager;

namespace Cassam.Ui.Manager;

/// <summary>
/// Code-behind for <see cref="CustomerManagementView"/>. Resolves
/// the view-model through DI and triggers the initial load on
/// page activation.
/// </summary>
public sealed partial class CustomerManagementView : Page
{
    /// <summary>The customer-management view-model (singleton, DI-resolved).</summary>
    public CustomerManagementViewModel ViewModel { get; }

    public CustomerManagementView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<CustomerManagementViewModel>();
        this.DataContext = ViewModel;

        this.Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }
}