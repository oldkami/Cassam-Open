using System;

namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Where the scan originated. Drives the cashier flow's UX
/// (camera scans surface a "camera" icon; HID scans play the
/// "beep" tone from the scanner itself; BT scans on Android may
/// need an explicit confirmation per OEM behavior).
/// </summary>
public enum ScanSource
{
    /// <summary>USB / Bluetooth / built-in keyboard-emulating scanner.</summary>
    Hid,

    /// <summary>Serial / Bluetooth-SPP socket stream.</summary>
    Serial,

    /// <summary>Web (WASM) camera via <c>BarcodeDetector</c> or ZXing-wasm.</summary>
    Camera,

    /// <summary>Cashier typed the code by hand (the search input).</summary>
    Manual,
}

/// <summary>
/// The result of a successful scan. The cashier flow uses
/// <see cref="Source"/> to decide whether to play a tone, render
/// a "scanning…" indicator, or require a confirmation tap.
/// </summary>
public sealed record ScanResult(
    string Code,
    DateTimeOffset Timestamp,
    ScanSource Source);
