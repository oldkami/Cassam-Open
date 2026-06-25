namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Why a cash-drawer kick was requested. The audit log captures
/// the reason so finance can reconcile drawer opens against
/// expected sales events (open session, sale completed, manual
/// override, etc.). The HAL does not act on the value — it is
/// carried in the audit trail only.
/// </summary>
public enum KickSource
{
    /// <summary>Sale completed — automatic kick after receipt print.</summary>
    SaleComplete,

    /// <summary>Cash session opened at the start of the shift.</summary>
    CashSessionOpen,

    /// <summary>Cash session closed at the end of the shift.</summary>
    CashSessionClose,

    /// <summary>Manager-issued no-sale (paid out, count correction, etc.).</summary>
    ManualNoSale,
}

/// <summary>
/// Audit-log wrapper for a kick event. The HAL emits this so the
/// audit subsystem can record the reason without coupling to the
/// drawer driver.
/// </summary>
public sealed record KickReason(KickSource Source);
