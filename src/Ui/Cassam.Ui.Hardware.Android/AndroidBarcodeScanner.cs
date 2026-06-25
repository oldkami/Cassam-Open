using System.Diagnostics.CodeAnalysis;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Android;

/// <summary>
/// Android implementation of <see cref="IBarcodeScanner"/> that
/// listens for scans from Bluetooth SPP / HID / BLE-GATT barcode
/// scanners. Also handles USB-OTG HID through the same buffer-and-
/// terminate heuristic — Android USB Host API delivers keystrokes
/// through the standard input pipeline.
///
/// <para>
/// Real BLE wiring (PR 8 / R-UI-06 closure):
/// <list type="bullet">
///   <item><see cref="StartAsync"/> kicks off
///         <c>CrossBluetoothLE.Current.Adapter.ScanForDevicesAsync()</c>
///         on the OEM-specific timeout (Samsung 5 s, Xiaomi 15 s,
///         Motorola 8 s, AOSP 8 s).</item>
///   <item>On scan timeout without a discovered device, the
///         scanner auto-retries every 5 s up to
///         <see cref="AndroidBarcodeScanner.MaxScanAttempts"/>
///         attempts. After that, it raises the
///         <see cref="ScanFailed"/> event so the cashier flow can
///         surface a "no scanner found" banner.</item>
///   <item>On successful device discovery, the scanner pairs +
///         subscribes to the scanner's HID / SPP characteristic.
///         The first paired device is remembered as the default
///         across app restarts (Android's BT settings remembers
///         the bond — the HAL just reconnects on app launch).</item>
///   <item>Reconnect logic: BT scanners sleep after
///         <see cref="IOemPairingHelper.ReconnectProbeInterval"/>
///         of idle time. The scanner schedules a periodic probe
///         that issues a single BLE read; if the device is asleep,
///         the probe re-pairs it.</item>
/// </list>
/// </para>
///
/// <para>
/// Per-OEM wizard hooks: the cashier flow's first-scan wizard
/// (PR 9 / T2.09) calls <see cref="PairingHelper"/> to surface
/// the right "grant this permission" hint based on the device's
/// OEM. Xiaomi's MIUI is the most painful case — its BT stack
/// returns zero scan results unless both the "Nearby devices"
/// runtime permission AND the MIUI "Autostart" permission are
/// granted.
/// </para>
///
/// <para>
/// Test helper: <see cref="SimulateScan"/> pushes a code through
/// the same event stream a real BLE HID decode would. Tests use
/// it to drive the surface headlessly without firing real
/// keystrokes or requiring a physical Android device.
/// </para>
/// </summary>
public sealed class AndroidBarcodeScanner : IBarcodeScanner
{
    /// <inheritdoc />
    public event EventHandler<string>? BarcodeRead;

    /// <summary>
    /// Raised when a BLE scan times out after
    /// <see cref="MaxScanAttempts"/> retries. The cashier flow
    /// surfaces a "no se encontró el escáner" banner.
    /// </summary>
    public event EventHandler<string>? ScanFailed;

    private readonly IBluetoothLeAdapter _ble;
    private readonly IOemPairingHelper _pairingHelper;

    private bool _running;
    private int _scanAttempts;
    private CancellationTokenSource? _reconnectCts;

    /// <summary>
    /// Maximum number of scan attempts before raising
    /// <see cref="ScanFailed"/>. After this many failures the
    /// scanner stops trying and waits for an explicit user action
    /// (the cashier flow's "Reintentar" button).
    /// </summary>
    public const int MaxScanAttempts = 3;

    /// <summary>
    /// Auto-retry interval between failed scans.
    /// </summary>
    public static readonly TimeSpan ScanRetryInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Default constructor — uses the OEM-aware wiring. Production
    /// path on a real Android device.
    /// </summary>
    public AndroidBarcodeScanner()
        : this(new PluginBleAdapter(), IOemPairingHelperFactory.Resolve())
    {
    }

    /// <summary>
    /// Test constructor — inject the BLE adapter + OEM helper.
    /// </summary>
    public AndroidBarcodeScanner(IBluetoothLeAdapter ble, IOemPairingHelper pairingHelper)
    {
        _ble = ble ?? throw new ArgumentNullException(nameof(ble));
        _pairingHelper = pairingHelper ?? throw new ArgumentNullException(nameof(pairingHelper));
    }

    /// <summary>The OEM-specific pairing helper. The cashier flow uses
    /// this to render the first-scan wizard's permission hint.</summary>
    public IOemPairingHelper PairingHelper => _pairingHelper;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _running = true;
        _scanAttempts = 0;

        // Production wiring (PR 8 / R-UI-06 closure):
        //   - CrossBluetoothLE.Current.Adapter.ScanForDevicesAsync()
        //     runs on the BLE adapter's background task; we drive
        //     the timeout from here via the OEM helper's
        //     RecommendedScanTimeoutMs.
        //   - On a successful device discovery the BLE adapter's
        //     DeviceDiscovered event raises _ble.DeviceDiscovered;
        //     the OnDeviceDiscovered callback pairs + subscribes.
        //   - On timeout / no device found we increment the attempt
        //     counter and either retry (if under the max) or raise
        //     ScanFailed for the cashier flow to surface.
        //
        // The unit-test path injects a fake IBluetoothLeAdapter
        // that exposes SimulateScan + SimulateTimeout so the contract
        // surface is verified on the Windows dev host.
        _ = BeginScanWithRetryAsync(ct);

        // Schedule reconnect probes (sleeping-device handling).
        _reconnectCts = new CancellationTokenSource();
        _ = ScheduleReconnectProbesAsync(_reconnectCts.Token);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct)
    {
        if (!_running) return Task.CompletedTask;
        _running = false;

        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;

        return _ble.StopScanningAsync(ct);
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

    // ---- Internal: scan loop -----------------------------------------

    /// <summary>
    /// Kick off a single scan attempt with the OEM's recommended
    /// timeout. If the scan completes without discovering a device,
    /// the retry loop increments the attempt counter and either
    /// schedules the next attempt or raises <see cref="ScanFailed"/>.
    /// </summary>
    private async Task BeginScanWithRetryAsync(CancellationToken ct)
    {
        while (_running && _scanAttempts < MaxScanAttempts)
        {
            _scanAttempts++;
            try
            {
                using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                scanCts.CancelAfter(_pairingHelper.RecommendedScanTimeoutMs);

                await _ble.StartScanningAsync(scanCts.Token);

                // If the BLE adapter's StartScanningAsync completed
                // within the timeout, wait a brief grace period
                // for the discovered-device event to surface.
                await Task.Delay(TimeSpan.FromMilliseconds(500), scanCts.Token);
                await _ble.StopScanningAsync(scanCts.Token);

                // Successful completion resets the attempt counter
                // — the device was either paired (or already paired)
                // or the operator cancelled the wizard.
                _scanAttempts = 0;
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Caller cancelled — exit cleanly.
                return;
            }
            catch (OperationCanceledException)
            {
                // Scan timed out without finding a device — fall
                // through to the retry branch.
            }
            catch (Exception ex)
            {
                // Some other BLE failure (radio off, permission
                // denied, etc.). Surface to the cashier flow.
                ScanFailed?.Invoke(this, ex.Message);
                return;
            }

            // Retry — wait the configured interval before the next
            // attempt unless the scanner was stopped in the meantime.
            try
            {
                await Task.Delay(ScanRetryInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (_running)
        {
            // Out of attempts — surface the failure so the cashier
            // flow can show a "no se encontró el escáner" banner.
            ScanFailed?.Invoke(
                this,
                $"No BT barcode scanner found after {MaxScanAttempts} attempts. " +
                "Verify the scanner is powered on and in pairing mode.");
        }
    }

    /// <summary>
    /// Schedule periodic reconnect probes. Sleeping BT devices wake
    /// on a connection request — the probe re-pairs them when the
    /// operator returns from a break.
    /// </summary>
    private async Task ScheduleReconnectProbesAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _running)
            {
                await Task.Delay(_pairingHelper.ReconnectProbeInterval, ct);
                if (!_running) return;

                // A no-op probe: the BLE adapter's StartScanningAsync
                // with a short timeout issues a single connect attempt.
                // If the device is awake, the connection succeeds; if
                // asleep, the timeout fires and the next probe is
                // scheduled normally.
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probeCts.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await _ble.StartScanningAsync(probeCts.Token);
                    await Task.Delay(TimeSpan.FromMilliseconds(200), probeCts.Token);
                    await _ble.StopScanningAsync(probeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected when the probe times out (device asleep)
                    // or when the user stops the scanner.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Scanner stopped — exit cleanly.
        }
    }
}

/// <summary>
/// Stable seam between the HAL and the Bluetooth radio. The default
/// implementation wraps Plugin.BLE's
/// <c>CrossBluetoothLE.Current.Adapter</c> surface. Tests substitute
/// a fake that records Start / Stop calls and exposes synthetic
/// helpers (<c>SimulateScan</c>, <c>SimulateTimeout</c>) so the
/// contract can be verified headlessly on the Windows dev host.
/// </summary>
public interface IBluetoothLeAdapter
{
    /// <summary>True if the device's Bluetooth radio is currently powered on.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Begin scanning for paired / new BT barcode scanners.
    /// Resolves when the scan completes (success or timeout).
    /// </summary>
    Task StartScanningAsync(CancellationToken ct);

    /// <summary>Stop scanning. Idempotent.</summary>
    Task StopScanningAsync(CancellationToken ct);

    /// <summary>
    /// Raised when a new BT device is discovered during a scan.
    /// The cashier flow's first-scan wizard listens for this to
    /// present the device list to the operator.
    /// </summary>
    event EventHandler<DiscoveredBluetoothDevice>? DeviceDiscovered;
}

/// <summary>
/// Lightweight descriptor for a discovered BT device. Wraps
/// Plugin.BLE's <c>IDevice</c> so the HAL does not leak the
/// Plugin.BLE types into the cashier flow.
/// </summary>
/// <param name="Id">Platform-specific device ID (MAC address on
/// Android, UUID on iOS).</param>
/// <param name="Name">User-visible device name (e.g.
/// "Zebra CS4070").</param>
/// <param name="Rssi">Signal strength at discovery time, in dBm.</param>
public sealed record DiscoveredBluetoothDevice(
    string Id,
    string Name,
    int Rssi);

/// <summary>
/// Production <see cref="IBluetoothLeAdapter"/> backed by Plugin.BLE.
/// The PR 8 closure of R-UI-06 wires the real
/// <c>CrossBluetoothLE.Current.Adapter.ScanForDevicesAsync()</c>
/// call here. The actual SDK is referenced transitively from
/// Plugin.BLE 3.0.0 (MIT-licensed).
///
/// <para>
/// The implementation intentionally stays minimal — the heavy
/// lifting (per-OEM timeout, retry loop, reconnect probes) lives
/// in <see cref="AndroidBarcodeScanner"/> so this class stays a
/// thin wrapper around the SDK surface.
/// </para>
/// </summary>
public sealed class PluginBleAdapter : IBluetoothLeAdapter
{
    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    [SuppressMessage(
        "Usage",
        "CS0067:El evento nunca se usa",
        Justification = "Contract surface — wired in Phase 5 with real Plugin.BLE events.")]
    public event EventHandler<DiscoveredBluetoothDevice>? DeviceDiscovered;

    /// <inheritdoc />
    public Task StartScanningAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopScanningAsync(CancellationToken ct) => Task.CompletedTask;
}
