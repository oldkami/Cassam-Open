using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// One line on a <see cref="Sale"/>. Carries its own tax category so a
/// mixed sale (gravado + exento + excluido) can be expressed in a
/// single DEE POS / FE Venta XML per <c>pos-fiscal-dee-pos</c>
/// SCN-DEEPOS-02.
/// </summary>
public class SaleLineItem : Entity
{
    /// <summary>Foreign key to the parent <see cref="Sale"/>.</summary>
    public Guid SaleId { get; set; }

    /// <summary>Foreign key to the sold <see cref="Product"/>.</summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Quantity sold. Decimal to support weighted items (kg, lb)
    /// common in Colombian grocery / hardware stores.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Unit price at sale time (may differ from
    /// <see cref="Product.UnitPrice"/> if a promotion / override was applied).
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Discount applied to this line, in COP. Defaults to 0.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>Tax category letter (S / Z / E / O) per Anexo Técnico.</summary>
    public TaxCategory LineTaxCategory { get; set; } = TaxCategory.Standard;

    /// <summary>
    /// The portion of <c>(Quantity * UnitPrice - DiscountAmount)</c>
    /// that is taxable (i.e., zero for Exempt / Other).
    /// </summary>
    public decimal LineTaxableAmount { get; set; }

    /// <summary>IVA + other applicable taxes for this line, in COP.</summary>
    public decimal LineTaxAmount { get; set; }
}
