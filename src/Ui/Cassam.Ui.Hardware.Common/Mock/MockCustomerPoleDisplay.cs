using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Mock;

/// <summary>
/// In-memory <see cref="ICustomerPoleDisplay"/> for unit tests.
/// Stores the most recently displayed total as a formatted string
/// (e.g. <c>"COP 14.495,00"</c>) so assertions can be made without
/// a real VFD. The format intentionally uses the customer-facing
/// shape — currency code first, then the amount with a thousands
/// separator — and the implementation is not involved in the
/// receipt byte stream.
/// </summary>
public sealed class MockCustomerPoleDisplay : ICustomerPoleDisplay
{
    /// <summary>The most recent formatted total, or <c>null</c> after <c>ClearAsync</c>.</summary>
    public string? LastDisplayedTotal { get; private set; }

    /// <inheritdoc />
    public Task ShowTotalAsync(PoleDisplayDevice device, decimal total, string currency, CancellationToken ct)
    {
        // Use the customer's locale (es-CO) to format thousands.
        // The decimal separator is the customer-facing one.
        var culture = CultureInfo.GetCultureInfo("es-CO");
        LastDisplayedTotal = $"{currency} {total.ToString("N2", culture)}";
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(PoleDisplayDevice device, CancellationToken ct)
    {
        LastDisplayedTotal = null;
        return Task.CompletedTask;
    }
}
