using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common;

namespace Cassam.Ui.Hardware.Wasm;

/// <summary>
/// Web (WASM) implementation of <see cref="ICashDrawer"/>. NO-OP.
/// Web POS does not have a physical cash drawer — customer pays
/// through a card / transfer processor the WASM head integrates
/// with (out of scope for Phase 2).
/// </summary>
public sealed class WasmCashDrawer : ICashDrawer
{
    /// <inheritdoc />
    public Task KickAsync(CashDrawerDevice device, CancellationToken ct) => Task.CompletedTask;
}
