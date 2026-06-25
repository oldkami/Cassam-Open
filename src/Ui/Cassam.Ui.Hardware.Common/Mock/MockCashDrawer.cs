using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Mock;

/// <summary>
/// In-memory <see cref="ICashDrawer"/> for unit tests. Increments a
/// counter on every <c>KickAsync</c> so the test can assert how many
/// drawer opens happened during a sale or session.
/// </summary>
public sealed class MockCashDrawer : ICashDrawer
{
    /// <summary>Total number of kicks issued since the mock was created.</summary>
    public int KickCount { get; private set; }

    /// <inheritdoc />
    public Task KickAsync(CashDrawerDevice device, CancellationToken ct)
    {
        KickCount++;
        return Task.CompletedTask;
    }
}
