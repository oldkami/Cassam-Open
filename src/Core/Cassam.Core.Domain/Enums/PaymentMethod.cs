namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Payment tender type accepted by the POS. Maps directly to
/// <c>payments.payment_method</c> per <c>pos-core-modern-stack</c>
/// REQ-CORE-10. Only <see cref="Cash"/> requires
/// <c>tendered_amount</c> / <c>change_amount</c>.
/// </summary>
public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Transfer = 2,
    Other = 3,
}
