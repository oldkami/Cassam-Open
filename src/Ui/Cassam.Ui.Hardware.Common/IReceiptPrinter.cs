using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Hardware abstraction for a thermal receipt printer. The interface
/// accepts pre-rendered ESC/POS bytes; the receipt content is built
/// by <c>ReceiptRenderer</c> (added in PR 9, T2.09). Cash-drawer kick
/// is exposed here because, in retail, the printer is almost always
/// the device that drives the RJ12 pulse — separate
/// <see cref="ICashDrawer"/> is reserved for the rare direct-attached
/// drawer (design §6.2, R-UI-11).
/// </summary>
public interface IReceiptPrinter
{
    /// <summary>
    /// Print a fully rendered ESC/POS byte stream. Implementations
    /// may block until the device acknowledges the write; the cashier
    /// flow is timed end-to-end against this method (SCN-UI-02).
    /// </summary>
    Task PrintAsync(PrinterDevice device, byte[] escposBytes, CancellationToken ct);

    /// <summary>
    /// Cut the paper. Optional — some low-cost thermal printers do not
    /// support cut. Implementations should be no-op when unsupported.
    /// </summary>
    Task CutPaperAsync(PrinterDevice device, CancellationToken ct);

    /// <summary>
    /// Send the standard RJ12 cash-drawer kick pulse
    /// (<c>ESC p 0 25 250</c>). Called by the cashier flow AFTER
    /// <see cref="PrintAsync"/> completes so the customer never sees
    /// the drawer open before the paper (R-UI-11).
    /// </summary>
    Task KickCashDrawerAsync(PrinterDevice device, CancellationToken ct);
}
