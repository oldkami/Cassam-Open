using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// X.509 signing certificate metadata for one tenant. Either stored as
/// a PKCS#12 <c>.pfx</c> file (<see cref="PfxPath"/>) or referenced via
/// the OS certificate store (<see cref="CertStoreRef"/>) per
/// <c>pos-core-modern-stack</c> REQ-CORE-07.
///
/// At most one certificate is <see cref="CertificadoStatus.Active"/> per
/// tenant at any time. Rotation demotes the old certificate to
/// <see cref="CertificadoStatus.Rotated"/> atomically per SCN-CORE-08.
/// </summary>
public class Certificado : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>X.509 subject distinguished name.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>X.509 issuer distinguished name (the issuing CA).</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>X.509 serial number (hex, uppercase, no colons).</summary>
    public string Serial { get; set; } = string.Empty;

    /// <summary>UTC start of certificate validity.</summary>
    public DateTime NotBefore { get; set; }

    /// <summary>UTC end of certificate validity. Signing is refused after this instant.</summary>
    public DateTime NotAfter { get; set; }

    /// <summary>
    /// Filesystem path to the PKCS#12 <c>.pfx</c> blob (mutually exclusive
    /// with <see cref="CertStoreRef"/>). The blob itself lives outside
    /// PostgreSQL — the path is enough to locate it at signing time.
    /// </summary>
    public string? PfxPath { get; set; }

    /// <summary>
    /// OS certificate-store reference (Windows: <c>StoreName / StoreLocation / Thumbprint</c>).
    /// Used instead of <see cref="PfxPath"/> when the certificate is installed
    /// in the platform keystore (DPAPI / libsecret / Keychain).
    /// </summary>
    public string? CertStoreRef { get; set; }

    /// <summary>Lifecycle status — drives signing-time checks per REQ-CORE-07.</summary>
    public CertificadoStatus Status { get; set; } = CertificadoStatus.PendingValidation;

    /// <summary>
    /// Argon2id hash of the <c>.pfx</c> password. NEVER store the plaintext
    /// password — even the hash is only useful to detect operator typos when
    /// loading the blob. Null when the certificate lives in the OS keystore.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAt { get; set; }
}
