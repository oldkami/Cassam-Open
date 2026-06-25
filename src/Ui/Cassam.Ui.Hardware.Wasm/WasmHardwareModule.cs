using Cassam.Ui.Hardware.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Cassam.Ui.Hardware.Wasm;

/// <summary>
/// Dependency-injection extension that wires the four WASM HAL
/// implementations into the host's service collection. Called from
/// <c>Cassam.Ui.App.xaml.cs</c> inside the <c>#if __WASM__</c>
/// branch — see the per-platform DI wiring commit (PR 8 / T2.07).
///
/// <para>
/// All four bindings are singletons, matching the desktop HAL
/// rationale. The NO-OP printer / drawer / pole display bindings
/// still resolve cleanly so the cashier flow's call sites are
/// platform-neutral.
/// </para>
///
/// <para>
/// DI strategy: this extension is the Web head's only HAL
/// registration. It does NOT need an <c>#if</c> branch in
/// <c>App.xaml.cs</c> because the project's compile-time symbols
/// (<c>__WASM__</c>) gate the call site. Tests resolve against the
/// Mock surface (PR 7 default) so the WASM HAL stays free of
/// test-only branches.
/// </para>
/// </summary>
public static class WasmHardwareModule
{
    /// <summary>
    /// Register every WASM HAL implementation against
    /// <paramref name="services"/>. Idempotent — safe to call from
    /// multiple code paths.
    /// </summary>
    public static IServiceCollection AddWasmHardware(this IServiceCollection services)
    {
        services.AddSingleton<IBarcodeScanner, WasmBarcodeScanner>();
        services.AddSingleton<IReceiptPrinter, WasmReceiptPrinter>();
        services.AddSingleton<ICashDrawer, WasmCashDrawer>();
        services.AddSingleton<ICustomerPoleDisplay, WasmCustomerPoleDisplay>();
        return services;
    }
}
