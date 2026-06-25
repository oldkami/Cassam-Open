using Cassam.Ui.Hardware.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Cassam.Ui.Hardware.Windows;

/// <summary>
/// Dependency-injection extension that wires the four Windows HAL
/// implementations into the host's service collection. Called from
/// <c>Cassam.Ui.App.xaml.cs</c> inside the <c>#if WINDOWS</c>
/// branch — see the per-platform DI commit.
///
/// <para>
/// Lifetime choices:
/// <list type="bullet">
///   <item><see cref="IBarcodeScanner"/> → singleton. The scanner's
///         WH_KEYBOARD_LL hook is process-scoped (one per UI thread);
///         creating more than one is a programming error.</item>
///   <item><see cref="IReceiptPrinter"/> → singleton. The winspool /
///         SerialPort / TcpClient handles are cheap to open per call
///         and disposing them between prints would force the device
///         firmware to re-initialize every receipt (slow).</item>
///   <item><see cref="ICashDrawer"/> → singleton. Pure adapter; the
///         underlying printer is the singleton above.</item>
///   <item><see cref="ICustomerPoleDisplay"/> → singleton. Same
///         reasoning as the printer.</item>
/// </list>
/// </para>
/// </summary>
public static class WindowsHardwareModule
{
    /// <summary>
    /// Register every Windows HAL implementation against
    /// <paramref name="services"/>. Idempotent — safe to call from
    /// multiple code paths (test harness + production App).
    /// </summary>
    public static IServiceCollection AddWindowsHardware(this IServiceCollection services)
    {
        services.AddSingleton<IBarcodeScanner, WindowsHidKeyboardBarcodeScanner>();
        services.AddSingleton<IReceiptPrinter, WindowsEscPosReceiptPrinter>();
        services.AddSingleton<ICashDrawer, WindowsPrinterKickedCashDrawer>();
        services.AddSingleton<ICustomerPoleDisplay, WindowsSerialPoleDisplay>();
        return services;
    }
}
