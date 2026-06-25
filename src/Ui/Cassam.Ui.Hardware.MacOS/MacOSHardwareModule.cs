using Cassam.Ui.Hardware.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Cassam.Ui.Hardware.MacOS;

/// <summary>
/// Dependency-injection extension that wires the four macOS HAL
/// implementations into the host's service collection. Called from
/// <c>Cassam.Ui.App.xaml.cs</c> inside the <c>#if MACCATALYST</c>
/// or <c>#if __APPLE__</c> branch — see the per-platform DI wiring
/// commit (PR 8 / T2.06).
///
/// <para>
/// All four bindings are singletons, matching the desktop HAL
/// rationale (the underlying handles are cheap to open per call but
/// disposing them between prints would force the device firmware to
/// re-initialize every receipt).
/// </para>
///
/// <para>
/// Production register path: the macOS head compiles the Uno
/// Skia Desktop variant with <c>UseMacOS()</c> in the host builder,
/// which sets the <c>__APPLE__</c> preprocessor symbol. The DI
/// extension uses runtime OS detection (the macOS HAL itself
/// guards each method with <see cref="OperatingSystem.IsMacOS"/>)
/// so misconfigured DI on a non-macOS build fails loud at
/// <c>StartAsync</c> / <c>PrintAsync</c>, not at process startup.
/// </para>
/// </summary>
public static class MacOSHardwareModule
{
    /// <summary>
    /// Register every macOS HAL implementation against
    /// <paramref name="services"/>. Idempotent — safe to call from
    /// multiple code paths.
    /// </summary>
    public static IServiceCollection AddMacOSHardware(this IServiceCollection services)
    {
        services.AddSingleton<IBarcodeScanner, MacOSBarcodeScanner>();
        services.AddSingleton<IReceiptPrinter, MacOSEscPosReceiptPrinter>();
        services.AddSingleton<ICashDrawer, MacOSPrinterKickedCashDrawer>();
        services.AddSingleton<ICustomerPoleDisplay, MacOSSerialPoleDisplay>();
        return services;
    }
}
