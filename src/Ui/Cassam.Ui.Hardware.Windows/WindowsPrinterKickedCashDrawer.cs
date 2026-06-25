using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Windows;

/// <summary>
/// Windows implementation of <see cref="ICashDrawer"/>.
///
/// <para>
/// In retail, the cash drawer is almost always driven through the
/// printer's RJ12 port (design §6.2 closing note + R-UI-11). This
/// adapter simply forwards <see cref="KickAsync"/> to the matching
/// <see cref="IReceiptPrinter.KickCashDrawerAsync"/> so the cashier
/// flow has a single, type-safe drawer surface that resolves to the
/// right transport (USB vendor-class winspool, SerialPort, or TcpClient
/// LAN) without knowing the underlying connection.
/// </para>
///
/// <para>
/// Direct USB-driven drawers (some Asian OEMs) are not common on
/// Windows retail and are intentionally not implemented in PR 7.
/// The <see cref="ICashDrawer"/> interface is reserved for that
/// future path.
/// </para>
/// </summary>
public sealed class WindowsPrinterKickedCashDrawer : ICashDrawer
{
    private readonly IReceiptPrinter _printer;

    public WindowsPrinterKickedCashDrawer(IReceiptPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        _printer = printer;
    }

    /// <inheritdoc />
    public Task KickAsync(CashDrawerDevice device, CancellationToken ct)
    {
        // The drawer and the printer share the RJ12 cable, so the
        // device descriptor for the drawer is the SAME physical
        // device record as the printer's. The cashier flow stores
        // both side by side in user_settings; we resolve back to
        // the printer's record here by name.
        var printerDevice = new PrinterDevice(
            Id: device.Id,
            Name: device.Name,
            Connection: device.Connection,
            Path: device.Path);
        return _printer.KickCashDrawerAsync(printerDevice, ct);
    }
}
