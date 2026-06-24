namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Lifecycle of a DIAN-issued <c>resolución</c> (numbering-range
/// authorization) per <c>pos-core-modern-stack</c> REQ-CORE-06.
///
/// A resolución progresses from <see cref="Draft"/> (uploaded but not yet
/// accepted by the operator) through <see cref="Active"/> (issuing documents)
/// until <see cref="Expired"/> (DIAN expiry reached or range exhausted).
/// </summary>
public enum ResolucionStatus
{
    /// <summary>Uploaded but not yet activated by the operator.</summary>
    Draft = 0,

    /// <summary>Currently issuing numbers in its <c>range_start..range_end</c>.</summary>
    Active = 1,

    /// <summary>
    /// Past <c>expiration_date</c> or range exhausted (<c>current_number &gt; range_end</c>).
    /// The dispatcher must look for the next active resolución per SCN-CORE-07.
    /// </summary>
    Expired = 2,
}
