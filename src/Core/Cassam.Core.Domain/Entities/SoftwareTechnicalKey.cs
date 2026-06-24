using Cassam.Core.Domain.Common;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// DIAN-issued technical key (Clave Técnica de Software) — the software
/// identification embedded in every <see cref="DocumentoElectronico"/>.
/// Each tenant may have multiple keys over time: old keys are deactivated
/// (<see cref="DeactivatedAt"/> set, <see cref="Active"/> = false) but
/// retained so documents signed under the old key remain verifiable.
///
/// A resolución references exactly one <see cref="SoftwareTechnicalKey"/>
/// via <c>software_technical_key_id</c>; that key becomes the software
/// identity carried in every document issued under the range
/// (REQ-CORE-06).
/// </summary>
public class SoftwareTechnicalKey : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>DIAN-issued key value (the alphanumeric string the operator pastes from the habilitación portal).</summary>
    public string KeyValue { get; set; } = string.Empty;

    /// <summary>True when the key was issued directly by DIAN (vs. a self-registered test key).</summary>
    public bool IssuedByDian { get; set; } = true;

    /// <summary>True while this key is the one referenced by <see cref="Resolucion"/> rows.</summary>
    public bool Active { get; set; } = true;

    /// <summary>UTC timestamp the key was registered with the tenant.</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>UTC timestamp the key was deactivated (rotation). Null while still active.</summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAt { get; set; }
}
