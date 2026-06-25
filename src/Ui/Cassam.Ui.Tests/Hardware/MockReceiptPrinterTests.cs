using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Common.Mock;
using FluentAssertions;

namespace Cassam.Ui.Tests.Hardware;

public class MockReceiptPrinterTests
{
    private static readonly PrinterDevice Device =
        new("printer-1", "Front counter", ConnectionType.Usb, "/dev/usb/lp0");

    [Fact]
    public async Task PrintAsync_records_byte_stream()
    {
        var printer = new MockReceiptPrinter();
        var bytes = new byte[] { 0x1B, 0x40, 0x48, 0x69 };  // ESC @ H i

        await printer.PrintAsync(Device, bytes, CancellationToken.None);

        printer.PrintedBytes.Should().ContainSingle().Which.Should().Equal(bytes);
    }

    [Fact]
    public async Task PrintAsync_preserves_order()
    {
        var printer = new MockReceiptPrinter();
        var first = new byte[] { 0x01, 0x02 };
        var second = new byte[] { 0x03, 0x04 };

        await printer.PrintAsync(Device, first, CancellationToken.None);
        await printer.PrintAsync(Device, second, CancellationToken.None);

        printer.PrintedBytes.Should().HaveCount(2);
        printer.PrintedBytes[0].Should().Equal(first);
        printer.PrintedBytes[1].Should().Equal(second);
    }

    [Fact]
    public async Task CutPaperAsync_sets_last_cut_requested()
    {
        var printer = new MockReceiptPrinter();
        printer.LastCutRequested.Should().BeFalse();

        await printer.CutPaperAsync(Device, CancellationToken.None);

        printer.LastCutRequested.Should().BeTrue();
    }

    [Fact]
    public async Task KickCashDrawerAsync_sets_last_kick_requested()
    {
        var printer = new MockReceiptPrinter();
        printer.LastKickRequested.Should().BeFalse();

        await printer.KickCashDrawerAsync(Device, CancellationToken.None);

        printer.LastKickRequested.Should().BeTrue();
    }
}
