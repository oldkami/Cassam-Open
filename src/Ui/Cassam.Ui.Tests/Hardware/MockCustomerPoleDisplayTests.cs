using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Common.Mock;
using FluentAssertions;

namespace Cassam.Ui.Tests.Hardware;

public class MockCustomerPoleDisplayTests
{
    private static readonly PoleDisplayDevice Device =
        new("pole-1", "Customer facing", ConnectionType.Serial, "COM3");

    [Fact]
    public async Task ShowTotalAsync_formats_in_esCO()
    {
        var pole = new MockCustomerPoleDisplay();

        await pole.ShowTotalAsync(Device, 1234567.50m, "COP", CancellationToken.None);

        // es-CO uses '.' as thousands separator and ',' as decimal.
        pole.LastDisplayedTotal.Should().Be("COP 1.234.567,50");
    }

    [Fact]
    public async Task ShowTotalAsync_handles_zero()
    {
        var pole = new MockCustomerPoleDisplay();

        await pole.ShowTotalAsync(Device, 0m, "COP", CancellationToken.None);

        pole.LastDisplayedTotal.Should().Be("COP 0,00");
    }

    [Fact]
    public async Task ClearAsync_resets_display()
    {
        var pole = new MockCustomerPoleDisplay();
        await pole.ShowTotalAsync(Device, 100m, "COP", CancellationToken.None);
        pole.LastDisplayedTotal.Should().NotBeNull();

        await pole.ClearAsync(Device, CancellationToken.None);

        pole.LastDisplayedTotal.Should().BeNull();
    }
}
