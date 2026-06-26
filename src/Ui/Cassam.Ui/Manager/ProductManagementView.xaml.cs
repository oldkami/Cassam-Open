using Cassam.Ui.Hardware.Common.Manager;

namespace Cassam.Ui.Manager;

/// <summary>
/// Code-behind for <see cref="ProductManagementView"/>. Resolves
/// the view-model through DI and triggers the initial load on
/// page activation.
/// </summary>
public sealed partial class ProductManagementView : Page
{
    /// <summary>The product-management view-model (singleton, DI-resolved).</summary>
    public ProductManagementViewModel ViewModel { get; }

    public ProductManagementView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<ProductManagementViewModel>();
        this.DataContext = ViewModel;

        this.Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }
}