using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Mock;

/// <summary>
/// In-memory <see cref="IReceiptPrinter"/> for unit tests. Each
/// <c>PrintAsync</c> call appends the byte stream to
/// <see cref="PrintedBytes"/> so byte-level assertions can be made
/// without physical hardware (SCN-UI-02 verifies the receipt byte
/// stream completes inside the 5 s budget using this surface).
/// </summary>
public sealed class MockReceiptPrinter : IReceiptPrinter
{
    private readonly List<byte[]> _printed = new();

    /// <summary>
    /// The byte streams that have been "printed" so far, in order.
    /// </summary>
    public IReadOnlyList<byte[]> PrintedBytes => _printed;

    /// <summary>True after a <c>CutPaperAsync</c> call.</summary>
    public bool LastCutRequested { get; private set; }

    /// <summary>True after a <c>KickCashDrawerAsync</c> call.</summary>
    public bool LastKickRequested { get; private set; }

    /// <inheritdoc />
    public Task PrintAsync(PrinterDevice device, byte[] escposBytes, CancellationToken ct)
    {
        _printed.Add(escposBytes);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CutPaperAsync(PrinterDevice device, CancellationToken ct)
    {
        LastCutRequested = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task KickCashDrawerAsync(PrinterDevice device, CancellationToken ct)
    {
        LastKickRequested = true;
        return Task.CompletedTask;
    }
}
