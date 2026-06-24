using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// DIAN-issued numbering-range authorization for one
/// <see cref="DocumentType"/>. Each resolución issues sequential numbers
/// (<see cref="Numero"/>) within <see cref="RangeStart"/>..<see cref="RangeEnd"/>;
/// once <c>current_number &gt; range_end</c> or <see cref="ExpirationDate"/>
/// passes, the dispatcher looks for the next active resolución per
/// <c>pos-core-modern-stack</c> SCN-CORE-07.
///
/// Composite uniqueness <c>(tenant_id, document_type, prefix)</c> prevents
/// the same DIAN authorization from being registered twice.
/// </summary>
public class Resolucion : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Document type this range authorizes (DEE_POS / FE_VENTA / NC / ND).</summary>
    public DocumentType DocumentType { get; set; }

    /// <summary>Inclusive lower bound of the DIAN-authorized numbering range.</summary>
    public long RangeStart { get; set; }

    /// <summary>Inclusive upper bound of the DIAN-authorized numbering range.</summary>
    public long RangeEnd { get; set; }

    /// <summary>
    /// Next number to issue. Incremented atomically when a
    /// <see cref="DocumentoElectronico"/> is generated. Defaults to
    /// <c>range_start - 1</c> so the first issuance produces
    /// <c>current_number + 1 == range_start</c>.
    /// </summary>
    public long CurrentNumber { get; set; }

    /// <summary>DIAN expiry date. <see cref="ResolucionStatus.Expired"/> is reached on or after this day.</summary>
    public DateOnly ExpirationDate { get; set; }

    /// <summary>Foreign key to the <see cref="SoftwareTechnicalKey"/> used to sign documents under this range.</summary>
    public Guid SoftwareTechnicalKeyId { get; set; }

    /// <summary>Lifecycle status — drives dispatcher behaviour per REQ-CORE-06.</summary>
    public ResolucionStatus Status { get; set; } = ResolucionStatus.Draft;

    /// <summary>
    /// DIAN-assigned prefix (e.g. <c>"POS"</c>). Optional — some
    /// resoluciones are issued without a prefix and the system generates
    /// numbers in the plain integer form.
    /// </summary>
    public string? Prefix { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAt { get; set; }
}
