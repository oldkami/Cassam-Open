using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Hardware abstraction for a customer-facing pole display (the small
/// two-line VFD/LCD that shows the running total to the customer).
/// On desktop OSes the device is connected via USB-serial or USB-HID
/// and the implementation translates the call into the device's
/// proprietary command set (CD-5220, Epson AVD, Logic Controls, etc.).
/// On Android, the equivalent is rare in retail; the interface is
/// declared for forward-compatibility. On Web (WASM) the interface is
/// not implemented (REQ-UI-06 — back-office only).
/// </summary>
public interface ICustomerPoleDisplay
{
    /// <summary>
    /// Render the running total. <paramref name="currency"/> is the
    /// ISO-4217 code (e.g. <c>"COP"</c>) — the device formats the
    /// amount per its own internal locale table.
    /// </summary>
    Task ShowTotalAsync(PoleDisplayDevice device, decimal total, string currency, CancellationToken ct);

    /// <summary>
    /// Clear the display. Called when the sale is cancelled or after
    /// the customer leaves the counter.
    /// </summary>
    Task ClearAsync(PoleDisplayDevice device, CancellationToken ct);
}
