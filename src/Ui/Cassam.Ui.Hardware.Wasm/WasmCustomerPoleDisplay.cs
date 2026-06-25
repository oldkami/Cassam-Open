using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Wasm;

/// <summary>
/// Web (WASM) implementation of <see cref="ICustomerPoleDisplay"/>.
/// NO-OP — web POS does not have a customer-facing pole display.
/// The running total is shown on the cashier's screen only; the
/// customer (when present, e.g. during a back-office receipt
/// printing session) reads the total from the receipt itself.
/// </summary>
public sealed class WasmCustomerPoleDisplay : ICustomerPoleDisplay
{
    /// <inheritdoc />
    public Task ShowTotalAsync(PoleDisplayDevice device, decimal total, string currency, CancellationToken ct) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task ClearAsync(PoleDisplayDevice device, CancellationToken ct) => Task.CompletedTask;
}
