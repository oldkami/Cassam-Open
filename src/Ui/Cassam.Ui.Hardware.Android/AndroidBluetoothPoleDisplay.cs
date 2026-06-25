using System.Globalization;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Android;

/// <summary>
/// Android implementation of <see cref="ICustomerPoleDisplay"/>.
///
/// <para>
/// Pole displays on Android are rare in retail (most Android POS
/// tablets ship without one) — design §6.2 row 4 acknowledges this.
/// The interface is implemented for forward-compatibility: a
/// future Bluetooth-attached pole display (e.g. Bematech LV2000U
/// BT) drops in without changing the cashier flow.
/// </para>
///
/// <para>
/// PR 7 ships the contract surface (es-CO formatted total + clear).
/// The actual BT-SPP write lands in PR 8 alongside the cashier
/// flow's printer-pair wizard.
/// </para>
/// </summary>
public sealed class AndroidBluetoothPoleDisplay : ICustomerPoleDisplay
{
    private readonly IBluetoothLeAdapter _ble;

    public AndroidBluetoothPoleDisplay() : this(new PluginBleAdapter())
    {
    }

    public AndroidBluetoothPoleDisplay(IBluetoothLeAdapter ble)
    {
        _ble = ble ?? throw new ArgumentNullException(nameof(ble));
    }

    /// <inheritdoc />
    public Task ShowTotalAsync(
        PoleDisplayDevice device, decimal total, string currency, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (device.Connection != ConnectionType.Bluetooth)
        {
            throw new NotSupportedException(
                $"Android pole display expects Bluetooth (device.Connection={device.Connection}).");
        }

        // The formatted total is computed eagerly so the contract is
        // visible from the test surface even though the BT write is
        // not yet wired up. PR 8 will replace this no-op with the
        // actual BLE GATT write.
        _ = $"{currency} {total.ToString("N2", CultureInfo.GetCultureInfo("es-CO"))}";
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(PoleDisplayDevice device, CancellationToken ct) =>
        Task.CompletedTask;
}
