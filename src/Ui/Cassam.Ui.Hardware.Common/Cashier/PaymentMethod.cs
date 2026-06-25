namespace Cassam.Ui.Hardware.Common.Cashier;

/// <summary>
/// Local PaymentMethod enum for the cashier view-model. Maps to
/// <see cref="Cassam.Core.Domain.Enums.PaymentMethod"/> at
/// persist time. Kept separate so the UI layer does not depend on
/// the domain assembly's enum members (the domain can evolve
/// without XAML recompiles).
/// </summary>
public enum CashierPaymentMethod
{
    /// <summary>Cash — requires tendered amount + change calculation.</summary>
    Cash = 0,

    /// <summary>Card — captures authorization code + brand.</summary>
    Card = 1,

    /// <summary>Bank transfer — captures reference number.</summary>
    Transfer = 2,

    /// <summary>Other — free-form note (e.g. credit-sale to known customer).</summary>
    Other = 3,
}

/// <summary>
/// Receipt delivery option chosen by the cashier at sale
/// completion. Drives whether the printer is invoked and / or the
/// customer is asked for an email address.
/// </summary>
public enum ReceiptOptions
{
    /// <summary>Print on the thermal printer (default).</summary>
    Print = 0,

    /// <summary>Email a PDF copy to the customer (requires email).</summary>
    Email = 1,

    /// <summary>Suppress printing — the DEE POS / FE Venta is still
    /// generated and transmitted to DIAN, just no paper (R-UI-12).</summary>
    None = 2,
}
