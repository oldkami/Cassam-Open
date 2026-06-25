using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Android;

/// <summary>
/// Android implementation of <see cref="IReceiptPrinter"/> that
/// writes ESC/POS bytes over Bluetooth to a paired mobile receipt
/// printer (the dominant Android POS deployment in LATAM —
/// Star SM-S230i, Epson TM-P20ii BT, Bixolon SPP-R200III).
///
/// <para>
/// BT is the common Android path because:
/// <list type="bullet">
///   <item>Retail Android stations are almost always tablets
///         without built-in serial ports.</item>
///   <item>USB-OTG to ESC/POS printers is rare and OEM-specific
///         (some tablets refuse to enumerate USB devices when
///         charging through the same port).</item>
///   <item>Bluetooth is universal on every Android tablet built
///         since 2014.</item>
/// </list>
/// </para>
///
/// <para>
/// PR 7 ships the contract surface (WriteEscPosAsync) and the
/// BT-SPP / BLE-GATT transport surface. The actual
/// <c>CrossBluetoothLE.Current.ConnectToKnownDeviceAsync(...)</c>
/// call lands in PR 8 alongside the cashier flow's printer-pair
/// wizard.
/// </para>
/// </summary>
public sealed class AndroidEscPosReceiptPrinter : IReceiptPrinter
{
    private readonly IBluetoothLeAdapter _ble;

    private static readonly byte[] KickPulseBytes =
    {
        0x1B, (byte)'p', 0x00, 25, 250,
    };

    private static readonly byte[] FullCutBytes = { 0x1D, (byte)'V', 0x00 };

    public AndroidEscPosReceiptPrinter() : this(new PluginBleAdapter())
    {
    }

    public AndroidEscPosReceiptPrinter(IBluetoothLeAdapter ble)
    {
        _ble = ble ?? throw new ArgumentNullException(nameof(ble));
    }

    /// <inheritdoc />
    public Task PrintAsync(PrinterDevice device, byte[] escposBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(escposBytes);
        if (device.Connection != ConnectionType.Bluetooth)
        {
            throw new NotSupportedException(
                $"Android HAL expects Bluetooth printers (device.Connection={device.Connection}).");
        }
        // The real BLE GATT write lands in PR 8 once station hardware
        // is online. The contract surface is stable from PR 7 so the
        // cashier flow does not need to change.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CutPaperAsync(PrinterDevice device, CancellationToken ct) =>
        PrintAsync(device, FullCutBytes, ct);

    /// <inheritdoc />
    public Task KickCashDrawerAsync(PrinterDevice device, CancellationToken ct) =>
        PrintAsync(device, KickPulseBytes, ct);
}
