#if WINDOWS
using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Windows;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cassam.Ui.Tests.Hardware.Windows;

/// <summary>
/// Contract tests for the Windows HAL surface. The whole file is
/// gated on <c>#if WINDOWS</c> because the per-platform assembly
/// only exists on a Windows TFM — on Linux / macOS / Android CI
/// runners the conditional ProjectReference in
/// <c>Cassam.Ui.Tests.csproj</c> skips it.
///
/// <para>
/// What we verify:
/// <list type="bullet">
///   <item>DI binding — every HAL interface resolves to the matching
///         Windows implementation.</item>
///   <item>Scanner contract — <see cref="SimulateScan"/> raises the
///         event so headless tests can drive the surface without
///         firing real keystrokes.</item>
///   <item>Cash-drawer adapter — <see cref="WindowsPrinterKickedCashDrawer"/>
///         forwards to the printer's <c>KickCashDrawerAsync</c> with
///         the same device descriptor.</item>
/// </list>
/// </para>
///
/// <para>
/// What we do NOT verify (no station hardware available on the
/// dev host):
/// <list type="bullet">
///   <item>The WH_KEYBOARD_LL hook firing on real HID input.</item>
///   <item>The winspool RAW write reaching a real printer.</item>
///   <item>The SerialPort / TcpClient write reaching a real device.</item>
/// </list>
/// </para>
/// </summary>
public class WindowsHardwareModuleTests
{
    [Fact]
    public void All_four_HAL_interfaces_resolve_to_Windows_implementations()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => WindowsHardwareModule.AddWindowsHardware(services))
            .Build();

        using var scope = host.Services.CreateScope();
        var provider = scope.ServiceProvider;

        provider.GetRequiredService<IBarcodeScanner>()
            .Should().BeOfType<WindowsHidKeyboardBarcodeScanner>();
        provider.GetRequiredService<IReceiptPrinter>()
            .Should().BeOfType<WindowsEscPosReceiptPrinter>();
        provider.GetRequiredService<ICashDrawer>()
            .Should().BeOfType<WindowsPrinterKickedCashDrawer>();
        provider.GetRequiredService<ICustomerPoleDisplay>()
            .Should().BeOfType<WindowsSerialPoleDisplay>();
    }

    [Fact]
    public void AddWindowsHardware_is_idempotent_against_repeated_registrations()
    {
        // Calling AddWindowsHardware twice (e.g. test harness + App)
        // must NOT throw. The last registration wins for the DI
        // resolution, but a duplicate AddSingleton is a no-op so the
        // call is safe to make from any code path.
        var services = new ServiceCollection();
        var act = () =>
        {
            WindowsHardwareModule.AddWindowsHardware(services);
            WindowsHardwareModule.AddWindowsHardware(services);
        };

        act.Should().NotThrow();
    }
}

public class WindowsHidKeyboardBarcodeScannerTests
{
    [Fact]
    public async Task SimulateScan_is_noop_before_start()
    {
        var scanner = new WindowsHidKeyboardBarcodeScanner();
        var received = new List<string>();
        scanner.BarcodeRead += (_, code) => received.Add(code);

        scanner.SimulateScan("7701234567890");

        received.Should().BeEmpty(
            "the contract says StartAsync arms the scanner; before that SimulateScan is a no-op");
        scanner.ReceivedScans.Should().BeEmpty();
    }

    [Fact]
    public async Task SimulateScan_raises_event_after_start()
    {
        var scanner = new WindowsHidKeyboardBarcodeScanner();
        var received = new List<string>();
        scanner.BarcodeRead += (_, code) => received.Add(code);

        await scanner.StartAsync(CancellationToken.None);

        scanner.SimulateScan("7701234567890");
        scanner.SimulateScan("7709876543210");

        received.Should().Equal("7701234567890", "7709876543210");
        scanner.ReceivedScans.Should().BeEquivalentTo(new[] { "7701234567890", "7709876543210" });
    }

    [Fact]
    public async Task Start_is_idempotent()
    {
        var scanner = new WindowsHidKeyboardBarcodeScanner();

        await scanner.StartAsync(CancellationToken.None);
        await scanner.StartAsync(CancellationToken.None);

        // No exception is the assertion; a second Start must not
        // double-install the global keyboard hook.
        scanner.SimulateScan("x");
        scanner.ReceivedScans.Should().ContainSingle();

        await scanner.StopAsync(CancellationToken.None);
    }
}

public class WindowsPrinterKickedCashDrawerTests
{
    [Fact]
    public async Task KickAsync_forwards_to_printer_with_same_device_descriptor()
    {
        // Cash-drawer adapter contract: KickAsync(device) calls the
        // printer's KickCashDrawerAsync with the SAME device (the
        // drawer and printer share the RJ12 cable).
        var printer = new RecordingReceiptPrinter();
        var drawer = new WindowsPrinterKickedCashDrawer(printer);

        var device = new CashDrawerDevice(
            Id: "drawer-1",
            Name: "Main Drawer",
            Connection: ConnectionType.Usb,
            Path: "EPSON TM-T20");

        await drawer.KickAsync(device, CancellationToken.None);

        printer.LastKickDevice.Should().NotBeNull();
        printer.LastKickDevice!.Id.Should().Be("drawer-1");
        printer.LastKickDevice.Name.Should().Be("Main Drawer");
        printer.LastKickDevice.Connection.Should().Be(ConnectionType.Usb);
        printer.LastKickDevice.Path.Should().Be("EPSON TM-T20");
    }

    [Fact]
    public void Constructor_rejects_null_printer()
    {
        var act = () => new WindowsPrinterKickedCashDrawer(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Minimal test stub for <see cref="IReceiptPrinter"/> — records
    /// the device descriptor from the last KickCashDrawerAsync call
    /// so the cash-drawer adapter contract can be asserted without
    /// touching real hardware.
    /// </summary>
    private sealed class RecordingReceiptPrinter : IReceiptPrinter
    {
        public PrinterDevice? LastKickDevice { get; private set; }

        public Task PrintAsync(PrinterDevice device, byte[] escposBytes, CancellationToken ct) =>
            Task.CompletedTask;

        public Task CutPaperAsync(PrinterDevice device, CancellationToken ct) =>
            Task.CompletedTask;

        public Task KickCashDrawerAsync(PrinterDevice device, CancellationToken ct)
        {
            LastKickDevice = device;
            return Task.CompletedTask;
        }
    }
}
#endif
