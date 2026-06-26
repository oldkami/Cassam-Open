using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Identifies which sub-view the manager shell currently renders.
/// Strings are the page-type names so the XAML code-behind can
/// resolve them via <c>Type.GetType(...)</c> + the Uno
/// <c>Frame.Navigate(...)</c> call.
///
/// <para>
/// Why an enum and not a Type reference? The
/// <c>Cassam.Ui.Hardware.Common</c> assembly intentionally does
/// not reference <c>Cassam.Ui</c> (the platform-specific XAML
/// assembly). Routing the navigation through string keys keeps
/// the platform-neutral VM decoupled from the XAML pages while
/// still letting the View-side code resolve the right Page type.
/// </para>
/// </summary>
public enum ManagerRoute
{
    /// <summary>Manager landing / dashboard (initial route).</summary>
    Overview = 0,

    /// <summary>Product CRUD (T2.09.a).</summary>
    Products = 1,

    /// <summary>Customer CRUD (T2.09.b).</summary>
    Customers = 2,

    /// <summary>Cash session open/close + history (T2.09.c).</summary>
    CashSessions = 3,

    /// <summary>Reports navigation shell (T2.09.d — actual reports land in T2.11).</summary>
    Reports = 4,

    /// <summary>DIAN status panel — regulatory control center (T2.10).</summary>
    DianStatus = 5,

    /// <summary>Tenant admin (legal name, NIT, subscription tier, cancel-tenant button).</summary>
    TenantAdmin = 6,
}

/// <summary>
/// View-model for the manager shell (design §5.1 navigation
/// tree). Holds the active route + the session snapshot the
/// header bar renders (manager name, station, role). Sub-views
/// are reached via the <see cref="ActiveRoute"/>; the shell's
/// Frame routes them in the View.
/// </summary>
public partial class ManagerShellViewModel : ObservableObject
{
    /// <summary>The currently selected manager route. Drives which sub-view is rendered.</summary>
    [ObservableProperty]
    private ManagerRoute _activeRoute = ManagerRoute.Overview;

    /// <summary>The logged-in user's display name (header bar).</summary>
    [ObservableProperty]
    private string _managerDisplayName = string.Empty;

    /// <summary>The active tenant's legal name (header bar subtitle).</summary>
    [ObservableProperty]
    private string _tenantLegalName = string.Empty;

    /// <summary>The active station id (header bar subtitle).</summary>
    [ObservableProperty]
    private string _stationId = string.Empty;

    /// <summary>The active user's role (OWNER / ADMIN / MANAGER / CASHIER).</summary>
    [ObservableProperty]
    private string _userRole = string.Empty;

    /// <summary>True when the user is allowed to cancel the tenant (OWNER only).</summary>
    [ObservableProperty]
    private bool _canCancelTenant;

    /// <summary>
    /// Navigate to the specified sub-view. Called by the
    /// NavigationView / tab buttons in the shell.
    /// </summary>
    public void NavigateTo(ManagerRoute route) => ActiveRoute = route;

    /// <summary>
    /// Populate the header-bar fields from the supplied session
    /// snapshot. Called once on shell initialization so the VM
    /// has the active tenant / user context.
    /// </summary>
    public void ApplySession(TenantSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ManagerDisplayName = snapshot.UserDisplayName;
        TenantLegalName = snapshot.TenantLegalName;
        StationId = snapshot.StationId;
        UserRole = snapshot.UserRole;
        CanCancelTenant = snapshot.CanCancelTenant;
    }
}