using System;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Wasm;

/// <summary>
/// Web (WASM) implementation of <see cref="IReceiptPrinter"/>.
/// NO-OP — web POS is back-office only (REQ-UI-06, R-UI-04).
///
/// <para>
/// Why a real implementation does not exist:
/// <list type="bullet">
///   <item>Browsers cannot reach USB / serial / Bluetooth thermal
///         printers without a vendor-supplied bridge (WebUSB + a
///         vendor-side WebSocket, or an IPP HTTP print endpoint).</item>
///   <item>Email / PDF download are the recommended web-side
///         alternatives — they land in PR 10 with the QuestPDF
///         renderer (T2.09).</item>
///   <item>The cashier flow MUST call <c>PrintAsync</c> on the
///         receipt completion path so the same code runs on every
///         platform. A NO-OP keeps the call site platform-neutral
///         while the actual delivery path is the email / PDF
///         download that the cashier flow ALSO offers.</item>
/// </list>
/// </para>
///
/// <para>
/// Operator notice: a one-time warning is logged when the printer
/// interface is first resolved so operators understand the web POS
/// does not print to paper. The cashier flow's "Sin impresión" UI
/// toggle (SCN-UI-12) is the operator-facing acknowledgement.
/// </para>
/// </summary>
public sealed class WasmReceiptPrinter : IReceiptPrinter
{
    private static int _warnedOnce;

    /// <inheritdoc />
    public Task PrintAsync(PrinterDevice device, byte[] escposBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(escposBytes);
        WarnOperatorOnce();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CutPaperAsync(PrinterDevice device, CancellationToken ct)
    {
        WarnOperatorOnce();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task KickCashDrawerAsync(PrinterDevice device, CancellationToken ct)
    {
        // No-op — there is no cash drawer on a web POS. The
        // cashier's customer pays through whatever payment
        // processor the WASM head integrates with (Stripe / etc.,
        // out of scope for Phase 2).
        return Task.CompletedTask;
    }

    private static void WarnOperatorOnce()
    {
        // Interlocked.Exchange ensures the warning is logged exactly
        // once per process even if the cashier flow invokes the
        // printer interface repeatedly. The message is intentionally
        // short — verbose logging belongs in the cashier flow's
        // status bar.
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
        {
            Console.WriteLine(
                "[Cassam.Ui.Hardware.Wasm] ReceiptPrinter is a no-op on web — " +
                "use email or PDF download for receipt delivery.");
        }
    }
}
