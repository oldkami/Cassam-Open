using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.MacOS;

/// <summary>
/// macOS implementation of <see cref="ICashDrawer"/>.
///
/// <para>
/// Same adapter pattern as Windows + Linux: forwards
/// <see cref="KickAsync"/> to <see cref="IReceiptPrinter.KickCashDrawerAsync"/>
/// because the cash drawer is almost always driven through the
/// printer's RJ12 port (design §6.2 closing note + R-UI-11).
/// </para>
///
/// <para>
/// The macOS-specific path is rare: a Bluetooth-attached cash
/// drawer (e.g. the Star mC-Print Drawer) plugs directly via USB-C.
/// In that case the operator configures the drawer's USB vendor ID
/// in <c>user_settings</c> and the station routes the kick through
/// IOKit — same byte sequence (<c>ESC p 0 25 250</c>) but a
/// different transport. PR 8 ships the printer-driven path because
/// it covers 99 % of macOS retail deployments (Epson + Star
/// desktop printers with piggy-backed drawers).
/// </para>
/// </summary>
public sealed class MacOSPrinterKickedCashDrawer : ICashDrawer
{
    private readonly IReceiptPrinter _printer;

    public MacOSPrinterKickedCashDrawer(IReceiptPrinter printer)
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
