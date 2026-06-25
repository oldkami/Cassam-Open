using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Common.Mock;
using FluentAssertions;

namespace Cassam.Ui.Tests.Hardware;

public class MockCashDrawerTests
{
    private static readonly CashDrawerDevice Device =
        new("drawer-1", "Front counter", ConnectionType.Usb, "/dev/usb/hiddev0");

    [Fact]
    public async Task KickAsync_starts_at_zero()
    {
        var drawer = new MockCashDrawer();
        drawer.KickCount.Should().Be(0);
        await drawer.KickAsync(Device, CancellationToken.None);
        drawer.KickCount.Should().Be(1);
    }

    [Fact]
    public async Task KickAsync_increments_per_call()
    {
        var drawer = new MockCashDrawer();
        await drawer.KickAsync(Device, CancellationToken.None);
        await drawer.KickAsync(Device, CancellationToken.None);
        await drawer.KickAsync(Device, CancellationToken.None);

        drawer.KickCount.Should().Be(3);
    }
}
