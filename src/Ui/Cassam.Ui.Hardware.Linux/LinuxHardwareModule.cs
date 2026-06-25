using Cassam.Ui.Hardware.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Cassam.Ui.Hardware.Linux;

/// <summary>
/// Dependency-injection extension that wires the four Linux HAL
/// implementations into the host's service collection. Called from
/// <c>Cassam.Ui.App.xaml.cs</c> inside the <c>#if LINUX</c>
/// branch — see the per-platform DI commit.
///
/// <para>
/// All four bindings are singletons, matching the Windows HAL
/// rationale (the underlying handles are cheap to open per call but
/// disposing them between prints would force the device firmware to
/// re-initialize every receipt).
/// </para>
/// </summary>
public static class LinuxHardwareModule
{
    /// <summary>
    /// Register every Linux HAL implementation against
    /// <paramref name="services"/>. Idempotent — safe to call from
    /// multiple code paths.
    /// </summary>
    public static IServiceCollection AddLinuxHardware(this IServiceCollection services)
    {
        services.AddSingleton<IBarcodeScanner, LinuxBarcodeScanner>();
        services.AddSingleton<IReceiptPrinter, LinuxEscPosReceiptPrinter>();
        services.AddSingleton<ICashDrawer, LinuxPrinterKickedCashDrawer>();
        services.AddSingleton<ICustomerPoleDisplay, LinuxSerialPoleDisplay>();
        return services;
    }
}
