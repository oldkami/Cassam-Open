using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// One POS sale. Persisted atomically with its
/// <see cref="SaleLineItem"/>s and at least one <see cref="Payment"/>.
/// The <c>documento_electronico_id</c> column is nullable until the
/// DEE POS / FE Venta is generated (Phase 4b); a NULL value simply
/// means "no fiscal document yet" per SCN-CORE-10.
/// </summary>
public class Sale : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Entities.Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Foreign key to the active <see cref="CashSession"/>.</summary>
    public Guid CashSessionId { get; set; }

    /// <summary>
    /// Colombian DIAN electronic document type. Decided at sale-completion
    /// time by the fiscal dispatcher based on the buyer's identification
    /// (<see cref="pos-fiscal-fe-venta"/> REQ-FEVENTA-02).
    /// </summary>
    public DocumentType DocumentType { get; set; }

    /// <summary>
    /// Foreign key to the generated <c>documentos_electronicos</c> row.
    /// Nullable until Phase 4b generates the DEE POS / FE Venta XML.
    /// </summary>
    public Guid? DocumentoElectronicoId { get; set; }

    /// <summary>
    /// Total amount payable in COP (line subtotals minus discounts).
    /// Stored as <c>numeric(18,4)</c> in PostgreSQL to preserve
    /// cent-level precision through currency rounding.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Sum of <c>line_tax_amount</c> across all line items.</summary>
    public decimal TaxTotal { get; set; }

    /// <summary>Soft-delete timestamp.</summary>
    public DateTime? DeletedAt { get; set; }
}
