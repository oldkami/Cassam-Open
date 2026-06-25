using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Linux;

/// <summary>
/// Linux implementation of <see cref="ICashDrawer"/>.
///
/// <para>
/// Same adapter pattern as Windows: forwards
/// <see cref="KickAsync"/> to <see cref="IReceiptPrinter.KickCashDrawerAsync"/>
/// because the cash drawer is almost always driven through the
/// printer's RJ12 port (design §6.2 closing note + R-UI-11).
/// </para>
/// </summary>
public sealed class LinuxPrinterKickedCashDrawer : ICashDrawer
{
    private readonly IReceiptPrinter _printer;

    public LinuxPrinterKickedCashDrawer(IReceiptPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        _printer = printer;
    }

    /// <inheritdoc />
    public Task KickAsync(CashDrawerDevice device, CancellationToken ct)
    {
        var printerDevice = new PrinterDevice(
            Id: device.Id,
            Name: device.Name,
            Connection: device.Connection,
            Path: device.Path);
        return _printer.KickCashDrawerAsync(printerDevice, ct);
    }
}
