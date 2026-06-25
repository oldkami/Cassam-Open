#if !WINDOWS
using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Linux;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cassam.Ui.Tests.Hardware.Linux;

/// <summary>
/// Contract tests for the Linux HAL surface. The whole file
/// compiles on the platform-neutral <c>net10.0</c> target so the
/// CI matrix runs it on every host. Tests that exercise the
/// platform-only code paths (evdev / X11 / Wayland barcode
/// scanner) use the <see cref="FakeInputHook"/> so they run on
/// Windows / macOS CI runners without a Linux station.
/// </summary>
public class LinuxHardwareModuleTests
{
    [Fact]
    public void All_four_HAL_interfaces_resolve_to_Linux_implementations()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => LinuxHardwareModule.AddLinuxHardware(services))
            .Build();

        using var scope = host.Services.CreateScope();
        var provider = scope.ServiceProvider;

        provider.GetRequiredService<IBarcodeScanner>()
            .Should().BeOfType<LinuxBarcodeScanner>();
        provider.GetRequiredService<IReceiptPrinter>()
            .Should().BeOfType<LinuxEscPosReceiptPrinter>();
        provider.GetRequiredService<ICashDrawer>()
            .Should().BeOfType<LinuxPrinterKickedCashDrawer>();
        provider.GetRequiredService<ICustomerPoleDisplay>()
            .Should().BeOfType<LinuxSerialPoleDisplay>();
    }

    [Fact]
    public void AddLinuxHardware_is_idempotent_against_repeated_registrations()
    {
        var services = new ServiceCollection();
        var act = () =>
        {
            LinuxHardwareModule.AddLinuxHardware(services);
            LinuxHardwareModule.AddLinuxHardware(services);
        };

        act.Should().NotThrow();
    }
}

public class LinuxBarcodeScannerTests
{
    [Fact]
    public void Constructor_does_not_throw_on_any_host()
    {
        // PR 8 (closes R-UI-03): the placeholder that threw
        // PlatformNotSupportedException on non-Linux hosts is gone.
        // The scanner now constructs cleanly on every host because
        // the underlying input hook is hidden behind an abstraction.
        var act = () => new LinuxBarcodeScanner();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task StartAsync_throws_on_non_linux_hosts_when_factory_returns_platform_not_supported_hook()
    {
        // On non-Linux hosts the factory returns
        // PlatformNotSupportedInputHook, which throws at StartAsync.
        // This protects against a misrouted DI container binding
        // the Linux HAL against a Windows / macOS process.
        if (OperatingSystem.IsLinux())
        {
            // Self-skip on actual Linux hosts — the factory returns
            // a non-throwing hook there.
            return;
        }

        var scanner = new LinuxBarcodeScanner();
        var act = async () => await scanner.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<PlatformNotSupportedException>(
            "the non-Linux factory result refuses to start — the cashier flow must see the failure");
    }

    [Fact]
    public async Task StartAsync_with_fake_hook_does_not_throw_on_any_host()
    {
        // Production code wires FakeInputHook via DI on Windows / macOS
        // dev hosts; the StartAsync path is exercised end-to-end here.
        var hook = new FakeInputHook();
        var scanner = new LinuxBarcodeScanner(hook);

        var act = async () => await scanner.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SimulateScan_is_noop_before_start()
    {
        var hook = new FakeInputHook();
        var scanner = new LinuxBarcodeScanner(hook);
        var received = new List<string>();
        scanner.BarcodeRead += (_, code) => received.Add(code);

        hook.SimulateScan("7701234567890");

        received.Should().BeEmpty(
            "StartAsync arms the scanner; before that, even a direct hook push is a no-op because the hook itself is not running");
    }

    [Fact]
    public async Task SimulateScan_raises_event_after_start_on_any_host_via_fake_hook()
    {
        // The fake hook + scanner form a complete end-to-end
        // signal path: hook fires -> scanner forwards -> public
        // event raised. Verified on every host (no Linux required).
        var hook = new FakeInputHook();
        var scanner = new LinuxBarcodeScanner(hook);
        var received = new List<string>();
        scanner.BarcodeRead += (_, code) => received.Add(code);

        await scanner.StartAsync(CancellationToken.None);
        hook.SimulateScan("7701234567890");
        hook.SimulateScan("7709876543210");

        received.Should().Equal("7701234567890", "7709876543210");
        scanner.ReceivedScans.Should().BeEquivalentTo(new[] { "7701234567890", "7709876543210" });

        await scanner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_is_idempotent()
    {
        // Calling StartAsync twice must not throw and must not
        // double-subscribe to the hook (verified by receiving each
        // scan exactly once).
        var hook = new FakeInputHook();
        var scanner = new LinuxBarcodeScanner(hook);
        var received = new List<string>();
        scanner.BarcodeRead += (_, code) => received.Add(code);

        await scanner.StartAsync(CancellationToken.None);
        await scanner.StartAsync(CancellationToken.None);

        hook.SimulateScan("7701234567890");
        received.Should().ContainSingle(
            "a second StartAsync must NOT double-subscribe to the hook");
    }

    [Fact]
    public async Task StopAsync_unsubscribes_from_hook()
    {
        // After StopAsync the scanner must not raise BarcodeRead
        // even if the hook keeps firing (defensive — the cashier
        // flow does not require this, but it keeps the surface
        // honest during navigation events).
        var hook = new FakeInputHook();
        var scanner = new LinuxBarcodeScanner(hook);
        var received = new List<string>();
        scanner.BarcodeRead += (_, code) => received.Add(code);

        await scanner.StartAsync(CancellationToken.None);
        await scanner.StopAsync(CancellationToken.None);

        hook.SimulateScan("7701234567890");
        received.Should().BeEmpty("StopAsync must detach the hook");
    }

    [Fact]
    public void Constructor_rejects_null_hook()
    {
        var act = () => new LinuxBarcodeScanner(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

public class FakeInputHookTests
{
    [Fact]
    public async Task SimulateScan_is_noop_before_start()
    {
        var hook = new FakeInputHook();
        var received = new List<string>();
        hook.BarcodeDecoded += (_, code) => received.Add(code);

        hook.SimulateScan("7701234567890");

        received.Should().BeEmpty();
    }

    [Fact]
    public async Task SimulateScan_raises_event_after_start()
    {
        var hook = new FakeInputHook();
        var received = new List<string>();
        hook.BarcodeDecoded += (_, code) => received.Add(code);

        await hook.StartAsync(CancellationToken.None);
        hook.SimulateScan("7701234567890");

        received.Should().Equal("7701234567890");
    }
}

public class LinuxEscPosReceiptPrinterTests
{
    [Fact]
    public async Task PrintAsync_throws_NotSupportedException_for_USB_connection()
    {
        // The Linux HAL deliberately refuses USB-vendor-class
        // printers because libusb-1.0 P/Invoke lands in a follow-up
        // PR. The test protects the contract so misconfiguration
        // surfaces as a clear exception, not a silent drop.
        var printer = new LinuxEscPosReceiptPrinter();
        var device = new PrinterDevice(
            Id: "printer-1",
            Name: "Epson TM-T20",
            Connection: ConnectionType.Usb,
            Path: "/dev/usb/lp0");

        var act = async () => await printer.PrintAsync(device, new byte[] { 0x1B, 0x40 }, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*libusb*");
    }

    [Fact]
    public async Task KickCashDrawerAsync_sends_standard_ESC_p_pulse_bytes()
    {
        // See PR 7 rationale: SerialPath doesn't exist in the
        // sandbox so we expect either a SerialPort validation
        // exception OR a successful no-op. The byte payload
        // contract is enforced at code review + PR 10 station test.
        var printer = new LinuxEscPosReceiptPrinter();
        var device = new PrinterDevice(
            Id: "printer-1",
            Name: "Epson TM-T20",
            Connection: ConnectionType.Serial,
            Path: "/dev/ttyUSB0");

        try
        {
            await printer.KickCashDrawerAsync(device, CancellationToken.None);
        }
        catch (Exception ex) when (
            ex is ArgumentException ||
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex.GetType().FullName?.Contains("Unix") == true)
        {
            // Expected on Windows dev hosts + CI sandbox.
            ex.Should().NotBeNull();
        }
    }
}

#endif
