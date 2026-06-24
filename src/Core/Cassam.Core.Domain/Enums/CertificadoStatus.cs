namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Lifecycle of an X.509 signing certificate per
/// <c>pos-core-modern-stack</c> REQ-CORE-07.
///
/// A certificado enters through <see cref="PendingValidation"/> while the
/// signer verifies its chain, expiration, and CRL/OCSP status (REQ-SIG-04).
/// On success it becomes <see cref="Active"/>; rotation demotes the old
/// one to <see cref="Rotated"/> atomically per SCN-CORE-08.
/// </summary>
public enum CertificadoStatus
{
    /// <summary>
    /// Certificate uploaded; chain + expiration + CRL/OCSP not yet verified.
    /// Cannot be used to sign <c>documentos_electronicos</c>.
    /// </summary>
    PendingValidation = 0,

    /// <summary>Verified and currently used for XAdES-BES signing.</summary>
    Active = 1,

    /// <summary>Past <c>not_after</c>; signing is refused.</summary>
    Expired = 2,

    /// <summary>Operator revoked (e.g. compromised key); signing is refused.</summary>
    Revoked = 3,

    /// <summary>
    /// Replaced by a newer certificate. Kept for fiscal history — the old
    /// certificate's signature can still be verified against documents that
    /// were signed before the rotation.
    /// </summary>
    Rotated = 4,
}
