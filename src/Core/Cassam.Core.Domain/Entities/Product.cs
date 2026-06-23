using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// Product / SKU catalog row. Stock is tracked here for the simple
/// on-prem POS; advanced inventory (lots, serials, multi-location)
/// is out of scope for Phase 1.
/// </summary>
public class Product : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Entities.Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Stock-keeping unit — unique within a tenant.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Display name shown on the POS line item and receipt.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Default unit price in COP. Effective price at sale time may
    /// differ (promotions, manual override) and is captured on the
    /// <see cref="SaleLineItem"/>.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Default tax category for this product.</summary>
    public TaxCategory TaxCategory { get; set; } = TaxCategory.Standard;

    /// <summary>
    /// Stock quantity. Nullable for non-inventory items (services,
    /// custom orders). Defaults to 0 so legacy import paths that omit
    /// the column map cleanly.
    /// </summary>
    public decimal? StockQuantity { get; set; }

    /// <summary>EAN-13 / UPC / Code-128 barcode; nullable for un-scanned items.</summary>
    public string? Barcode { get; set; }

    /// <summary>Soft-delete timestamp.</summary>
    public DateTime? DeletedAt { get; set; }
}
