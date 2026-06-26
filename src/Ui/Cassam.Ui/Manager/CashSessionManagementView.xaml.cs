using Cassam.Ui.Hardware.Common.Manager;

namespace Cassam.Ui.Manager;

/// <summary>
/// Code-behind for <see cref="CashSessionManagementView"/>. Resolves
/// the view-model through DI and triggers the initial load on
/// page activation.
/// </summary>
public sealed partial class CashSessionManagementView : Page
{
    /// <summary>The cash-session management view-model (singleton, DI-resolved).</summary>
    public CashSessionManagementViewModel ViewModel { get; }

    public CashSessionManagementView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<CashSessionManagementViewModel>();
        this.DataContext = ViewModel;

        this.Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }
}