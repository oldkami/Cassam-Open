using System;

namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Hardware abstraction for a barcode scanner. The interface is
/// deliberately minimal: a start/stop pair plus an event for received
/// codes. Per-platform implementations in PR 8 (T2.05..T2.07) will
/// surface the scanner through this same surface so the cashier flow
/// does not need to know whether the input came from a USB HID
/// keyboard, a Bluetooth SPP socket, or the browser camera (REQ-UI-04,
/// REQ-UI-05, REQ-UI-06).
/// </summary>
public interface IBarcodeScanner
{
    /// <summary>
    /// Raised when a barcode has been decoded and is ready to be added
    /// to the current order. The string is the raw code with no
    /// symbology prefix (e.g. <c>"7701234567890"</c> for EAN-13).
    /// </summary>
    event EventHandler<string>? BarcodeRead;

    /// <summary>
    /// Begin listening for scans. Idempotent — calling <c>StartAsync</c>
    /// on an already-started scanner is a no-op.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Stop listening. Idempotent. Pending events in the implementation
    /// buffer are flushed before this method completes.
    /// </summary>
    Task StopAsync(CancellationToken ct);
}
