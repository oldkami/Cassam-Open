using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Hardware abstraction for a cash drawer connected directly to the
/// station (USB-driven, or serial). Most retail cash drawers are
/// driven through the printer's RJ12 port — see
/// <see cref="IReceiptPrinter.KickCashDrawerAsync"/>. This interface
/// is reserved for the rare direct-attached case (USB-driven drawers
/// sold by some Asian OEMs) and is implemented per-platform in
/// PR 8 (T2.05..T2.07).
/// </summary>
public interface ICashDrawer
{
    /// <summary>
    /// Open the drawer. The pulse parameters (typically 25 ms ON,
    /// 250 ms OFF) are baked into the device's firmware — the
    /// abstraction only signals intent.
    /// </summary>
    Task KickAsync(CashDrawerDevice device, CancellationToken ct);
}
