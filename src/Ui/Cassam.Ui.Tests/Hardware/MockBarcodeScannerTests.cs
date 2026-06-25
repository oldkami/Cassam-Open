using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Common.Mock;
using FluentAssertions;

namespace Cassam.Ui.Tests.Hardware;

public class MockBarcodeScannerTests
{
    [Fact]
    public async Task Start_stop_toggles_running_flag_for_simulate_scan()
    {
        var scanner = new MockBarcodeScanner();

        // Before start, simulate is a no-op.
        scanner.SimulateScan("7701234567890");
        scanner.ScannedCodes.Should().BeEmpty();

        await scanner.StartAsync(CancellationToken.None);
        scanner.SimulateScan("7701234567890");
        scanner.ScannedCodes.Should().ContainSingle().Which.Should().Be("7701234567890");

        await scanner.StopAsync(CancellationToken.None);
        scanner.SimulateScan("9999999999999");
        scanner.ScannedCodes.Should().HaveCount(1, "after Stop, SimulateScan is a no-op");
    }

    [Fact]
    public async Task BarcodeRead_event_raises_with_code()
    {
        var scanner = new MockBarcodeScanner();
        await scanner.StartAsync(CancellationToken.None);

        var received = new List<string>();
        scanner.BarcodeRead += (_, code) => received.Add(code);

        scanner.SimulateScan("abc");
        scanner.SimulateScan("def");

        received.Should().Equal("abc", "def");
    }

    [Fact]
    public async Task Start_is_idempotent()
    {
        var scanner = new MockBarcodeScanner();
        await scanner.StartAsync(CancellationToken.None);
        await scanner.StartAsync(CancellationToken.None);

        scanner.SimulateScan("x");
        scanner.ScannedCodes.Should().ContainSingle();
    }
}
