using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// One tender against a <see cref="Sale"/>. A single sale may have
/// multiple payments (split tender: cash + card). Only
/// <see cref="PaymentMethod.Cash"/> requires
/// <see cref="TenderedAmount"/> / <see cref="ChangeAmount"/>; non-cash
/// methods populate <see cref="Reference"/> (authorization code, etc.)
/// per SCN-CORE-11.
/// </summary>
public class Payment : Entity
{
    /// <summary>Foreign key to the parent <see cref="Sale"/>.</summary>
    public Guid SaleId { get; set; }

    /// <summary>
    /// Foreign key to the <see cref="CashSession"/> the payment was
    /// booked into. Required so that the session's expected cash can
    /// be incremented per SCN-CORE-11.
    /// </summary>
    public Guid CashSessionId { get; set; }

    /// <summary>Tender type.</summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>Net amount applied to the sale in COP.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Amount handed over by the customer. Required for
    /// <see cref="PaymentMethod.Cash"/>, optional otherwise.
    /// </summary>
    public decimal? TenderedAmount { get; set; }

    /// <summary>
    /// Change returned to the customer. Always zero or positive;
    /// nullable for non-cash payments.
    /// </summary>
    public decimal? ChangeAmount { get; set; }

    /// <summary>
    /// Authorization / voucher reference (card slip, transfer ID,
    /// reference number). Required for non-cash payments per
    /// Colombian fiscal traceability.
    /// </summary>
    public string? Reference { get; set; }
}
