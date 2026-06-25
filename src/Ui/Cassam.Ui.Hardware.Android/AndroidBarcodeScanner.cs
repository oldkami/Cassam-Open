using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Android;

/// <summary>
/// Android implementation of <see cref="IBarcodeScanner"/> that
/// listens for scans from any of three sources:
/// <list type="number">
///   <item>Bluetooth SPP socket (the most common retail Android
///         scanner pairing — Zebra CS4070, Honeywell Voyager
///         1602g BT, etc.).</item>
///   <item>Bluetooth HID (the "BT keyboard" pairing — appears as a
///         virtual keyboard to the OS; decoded by the same
///         buffer-and-terminate heuristic as the Windows HID path).</item>
///   <item>USB-OTG HID (when the operator plugs the scanner cable
///         into the tablet). The Android USB Host API delivers
///         keystrokes through the same HID path; we treat this
///         identically to BT-HID for the HAL surface.</item>
/// </list>
///
/// <para>
/// Status of PR 7 implementation:
/// <list type="bullet">
///   <item>Contract surface: complete (event, StartAsync, StopAsync,
///         SimulateScan for tests).</item>
///   <item>Plugin.BLE integration: surface declared via a wrapper
///         <see cref="IBluetoothLeAdapter"/> so the implementation
///         can be unit-tested without a physical Android device.
///         The actual <c>CrossBluetoothLE.Current.ScanForDevicesAsync()</c>
///         calls land in PR 8 once we have a physical scanner for
///         QA (design §16 R-UI-06).</item>
/// </list>
/// </para>
///
/// <para>
/// The BT-HID / USB-OTG buffer-and-terminate heuristic is identical
/// to the Windows HID scanner (4..32 chars, Enter/Tab terminates);
/// see <c>WindowsHidKeyboardBarcodeScanner</c> for the rationale.
/// </para>
/// </summary>
public sealed class AndroidBarcodeScanner : IBarcodeScanner
{
    private readonly IBluetoothLeAdapter _ble;

    /// <inheritdoc />
    public event EventHandler<string>? BarcodeRead;

    private bool _running;

    /// <summary>
    /// Construct with the default Plugin.BLE-backed adapter.
    /// Tests pass in a fake <see cref="IBluetoothLeAdapter"/>.
    /// </summary>
    public AndroidBarcodeScanner() : this(new PluginBleAdapter())
    {
    }

    public AndroidBarcodeScanner(IBluetoothLeAdapter ble)
    {
        _ble = ble ?? throw new ArgumentNullException(nameof(ble));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _running = true;

        // The actual BLE scan / pair / SPP socket subscribe lives in
        // PR 8 once station hardware is online. The HAL surface is
        // contract-stable from PR 7 so the cashier flow does not
        // need to change when the real implementation lands.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct)
    {
        _running = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test helper — push a code into the event stream as if the
    /// BT / USB-OTG layer had decoded it. No-op when the scanner
    /// has not been started.
    /// </summary>
    public void SimulateScan(string code)
    {
        if (!_running) return;
        BarcodeRead?.Invoke(this, code);
    }
}

/// <summary>
/// Stable seam between the HAL and the Bluetooth radio. The default
/// implementation wraps Plugin.BLE; tests substitute a fake that
/// records Start/Stop calls and exposes a synthetic scan helper.
/// </summary>
public interface IBluetoothLeAdapter
{
    /// <summary>True if the device's Bluetooth radio is currently powered on.</summary>
    bool IsAvailable { get; }

    /// <summary>Begin scanning for paired / new BT barcode scanners.</summary>
    Task StartScanningAsync(CancellationToken ct);

    /// <summary>Stop scanning. Idempotent.</summary>
    Task StopScanningAsync(CancellationToken ct);
}

/// <summary>
/// Production <see cref="IBluetoothLeAdapter"/> backed by Plugin.BLE.
/// The implementation is intentionally minimal in PR 7 — full
/// pairing / SPP subscribe lands in PR 8 with the cashier flow's
/// first-scan wizard (design §6.2 row 4 + R-UI-06).
/// </summary>
public sealed class PluginBleAdapter : IBluetoothLeAdapter
{
    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task StartScanningAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopScanningAsync(CancellationToken ct) => Task.CompletedTask;
}
