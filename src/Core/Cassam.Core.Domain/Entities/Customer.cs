using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// Customer master record. The composite uniqueness key
/// <c>(tenant_id, document_type, document_number)</c> prevents the
/// same customer from being registered twice with different ID types.
/// </summary>
public class Customer : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Entities.Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Type of identity document (CC, NIT, CE, OTHER).</summary>
    public CustomerDocumentType DocumentType { get; set; } = CustomerDocumentType.Cc;

    /// <summary>Document number (no dashes / dots; trimmed).</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>Legal name (Razón Social for juridica, full name for natural).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Natural or juridica. The combination of NIT + Juridica is the
    /// dispatch signal for FE Venta per <c>pos-fiscal-fe-venta</c>
    /// REQ-FEVENTA-02 and SCN-CORE-12.
    /// </summary>
    public PersonType PersonType { get; set; } = PersonType.Natural;

    /// <summary>Optional email for electronic invoicing delivery.</summary>
    public string? Email { get; set; }

    /// <summary>Optional phone number.</summary>
    public string? Phone { get; set; }

    /// <summary>Optional postal address (required when generating FE Venta).</summary>
    public string? Address { get; set; }

    /// <summary>Soft-delete timestamp.</summary>
    public DateTime? DeletedAt { get; set; }
}
