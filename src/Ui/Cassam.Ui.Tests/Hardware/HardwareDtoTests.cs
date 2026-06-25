using System;
using Cassam.Ui.Hardware.Common;
using FluentAssertions;

namespace Cassam.Ui.Tests.Hardware;

public class HardwareDtoTests
{
    [Fact]
    public void ScanResult_is_value_equality()
    {
        var ts = DateTimeOffset.UtcNow;
        var a = new ScanResult("7701234567890", ts, ScanSource.Hid);
        var b = new ScanResult("7701234567890", ts, ScanSource.Hid);
        var c = new ScanResult("7701234567890", ts, ScanSource.Camera);

        (a == b).Should().BeTrue("records compare by value");
        a.Equals(b).Should().BeTrue();
        (a == c).Should().BeFalse("different Source makes them unequal");
    }

    [Fact]
    public void PrintJob_carries_width_separately_from_content()
    {
        var job = new PrintJob("hello", LineCount: 1, Width: PaperWidth.Mm80);

        job.Content.Should().Be("hello");
        job.LineCount.Should().Be(1);
        job.Width.Should().Be(PaperWidth.Mm80);
    }

    [Fact]
    public void PrinterDevice_is_value_equality()
    {
        var a = new PrinterDevice("id-1", "Front", ConnectionType.Usb, "/dev/usb/lp0");
        var b = new PrinterDevice("id-1", "Front", ConnectionType.Usb, "/dev/usb/lp0");

        a.Should().Be(b);
    }

    [Fact]
    public void ConnectionType_covers_all_per_platform_paths()
    {
        // Each value must be present so the per-platform HAL
        // implementations in PR 8 can map to it without falling
        // through to an unknown default.
        Enum.GetValues<ConnectionType>()
            .Should()
            .BeEquivalentTo(new[]
            {
                ConnectionType.Usb,
                ConnectionType.Serial,
                ConnectionType.Lan,
                ConnectionType.Bluetooth,
                ConnectionType.Camera,
            });
    }

    [Fact]
    public void KickReason_carries_audit_metadata()
    {
        var reason = new KickReason(KickSource.SaleComplete);
        reason.Source.Should().Be(KickSource.SaleComplete);
    }
}
