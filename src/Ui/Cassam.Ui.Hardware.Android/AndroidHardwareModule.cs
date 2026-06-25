using Cassam.Ui.Hardware.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Cassam.Ui.Hardware.Android;

/// <summary>
/// Dependency-injection extension that wires the four Android HAL
/// implementations into the host's service collection. Called from
/// <c>Cassam.Ui.App.xaml.cs</c> inside the <c>#if ANDROID</c>
/// branch — see the per-platform DI commit.
///
/// <para>
/// All four bindings are singletons, matching the desktop HAL
/// rationale (the underlying handles are cheap to open per call
/// but disposing them between scans / prints would force the
/// device firmware to re-initialize).
/// </para>
/// </summary>
public static class AndroidHardwareModule
{
    /// <summary>
    /// Register every Android HAL implementation against
    /// <paramref name="services"/>. Idempotent — safe to call from
    /// multiple code paths.
    /// </summary>
    public static IServiceCollection AddAndroidHardware(this IServiceCollection services)
    {
        services.AddSingleton<IBluetoothLeAdapter, PluginBleAdapter>();
        services.AddSingleton<IBarcodeScanner, AndroidBarcodeScanner>();
        services.AddSingleton<IReceiptPrinter, AndroidEscPosReceiptPrinter>();
        services.AddSingleton<ICashDrawer, AndroidBluetoothCashDrawer>();
        services.AddSingleton<ICustomerPoleDisplay, AndroidBluetoothPoleDisplay>();
        return services;
    }
}
