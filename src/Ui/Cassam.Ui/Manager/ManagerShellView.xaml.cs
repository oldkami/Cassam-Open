using System;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Cassam.Ui.Manager;

/// <summary>
/// Code-behind for <see cref="ManagerShellView"/>. Owns the
/// navigation between the manager sub-views by translating
/// <see cref="ManagerRoute"/> values into Page types and driving
/// the inner <c>Frame</c>.
///
/// <para>
/// The XAML's <c>ListBox</c> drives the route selection through
/// <see cref="OnNavSelectionChanged"/>; the buttons "Volver a
/// caja" / "Cerrar sesión" forward navigation requests to the
/// application root so the shell can be replaced from a single
/// place.
/// </para>
/// </summary>
public sealed partial class ManagerShellView : Page
{
    /// <summary>The manager shell view-model (singleton, DI-resolved).</summary>
    public ManagerShellViewModel ViewModel { get; }

    /// <summary>Raised when the user clicks "Volver a caja". The App.xaml.cs listener navigates back to the cashier view.</summary>
    public event EventHandler? BackToCashierRequested;

    /// <summary>Raised when the user clicks "Cerrar sesión".</summary>
    public event EventHandler? SignOutRequested;

    /// <summary>
    /// Raised when a sub-view (e.g. <see cref="TenantAdminView"/>)
    /// requests the cancel-tenant dialog. The application root
    /// listener resolves the dialog via DI and shows it as a
    /// <see cref="ContentDialog"/>.
    /// </summary>
    public event EventHandler? CancelTenantDialogRequested;

    public ManagerShellView()
    {
        this.InitializeComponent();
        ViewModel = App.Host.Services.GetRequiredService<ManagerShellViewModel>();
        this.DataContext = ViewModel;

        // Seed the shell header with the active tenant + user
        // snapshot. Phase 3 wires the real session-state provider
        // — for now the stub returns a deterministic fixture.
        _ = InitializeAsync();

        // Navigate to the default (Overview) once the page has
        // loaded. We use Loaded (not ctor) so the Frame is part
        // of the visual tree when we call Navigate.
        this.Loaded += OnLoaded;
        SubViewFrame.Navigated += OnSubViewFrameNavigated;
    }

    private async Task InitializeAsync()
    {
        var session = App.Host.Services.GetRequiredService<ITenantSessionState>();
        var snapshot = await session.GetSnapshotAsync(default);
        ViewModel.ApplySession(snapshot);
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        NavigateToRoute(ViewModel.ActiveRoute);
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is ListBoxItem item
            && item.Tag is string tag
            && int.TryParse(tag, out var route))
        {
            ViewModel.NavigateTo((ManagerRoute)route);
            NavigateToRoute((ManagerRoute)route);
        }
    }

    private void OnSubViewFrameNavigated(object sender, NavigationEventArgs e)
    {
        // Wire the cancel-tenant request from the TenantAdminView
        // so it bubbles up to the App root which shows the
        // ContentDialog. Other sub-views do not raise this event.
        if (e.Content is TenantAdminView admin)
        {
            admin.CancelTenantRequested -= OnCancelTenantRequestedFromAdmin;
            admin.CancelTenantRequested += OnCancelTenantRequestedFromAdmin;
        }
    }

    private void OnCancelTenantRequestedFromAdmin(object? sender, EventArgs e)
        => CancelTenantDialogRequested?.Invoke(this, EventArgs.Empty);

    private void OnBackToCashierClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => BackToCashierRequested?.Invoke(this, EventArgs.Empty);

    private void OnSignOutClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => SignOutRequested?.Invoke(this, EventArgs.Empty);

    private void NavigateToRoute(ManagerRoute route)
    {
        var pageType = route switch
        {
            ManagerRoute.Products => typeof(ProductManagementView),
            ManagerRoute.Customers => typeof(CustomerManagementView),
            ManagerRoute.CashSessions => typeof(CashSessionManagementView),
            ManagerRoute.Reports => typeof(ReportsNavigationView),
            ManagerRoute.DianStatus => typeof(DianStatusPanelView),
            ManagerRoute.TenantAdmin => typeof(TenantAdminView),
            _ => typeof(ManagerOverviewView),
        };

        if (SubViewFrame.CurrentSourcePageType != pageType)
        {
            SubViewFrame.Navigate(pageType);
        }
    }
}