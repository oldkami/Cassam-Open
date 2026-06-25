using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Android;

/// <summary>
/// Android implementation of <see cref="ICashDrawer"/>.
///
/// <para>
/// Same adapter pattern as Windows / Linux: forwards
/// <see cref="KickAsync"/> to <see cref="IReceiptPrinter.KickCashDrawerAsync"/>
/// because the cash drawer piggy-backs on the printer's RJ12 cable
/// (design §6.2 closing note + R-UI-11). On Android the printer is
/// almost always a BT-paired mobile unit, so the kick travels over
/// the same BLE GATT channel as the receipt body.
/// </para>
/// </summary>
public sealed class AndroidBluetoothCashDrawer : ICashDrawer
{
    private readonly IReceiptPrinter _printer;

    public AndroidBluetoothCashDrawer(IReceiptPrinter printer)
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
            Connection: ConnectionType.Bluetooth,
            Path: device.Path);
        return _printer.KickCashDrawerAsync(printerDevice, ct);
    }
}
