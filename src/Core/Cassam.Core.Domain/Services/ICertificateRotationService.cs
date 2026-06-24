using Cassam.Core.Domain.Entities;

namespace Cassam.Core.Domain.Services;

/// <summary>
/// X.509 certificate lifecycle operations per
/// <c>pos-core-modern-stack</c> REQ-CORE-07 / SCN-CORE-07 / SCN-CORE-08.
///
/// <para>
/// Three responsibilities:
/// </para>
/// <list type="number">
///   <item><b>Validation</b> —
///         <see cref="ValidateAndActivateAsync"/> loads a
///         <c>.pfx</c> blob, parses the X.509 certificate, builds the
///         chain, and (on success) flips the
///         <see cref="Certificado.Status"/> from
///         <see cref="Enums.CertificadoStatus.PendingValidation"/> to
///         <see cref="Enums.CertificadoStatus.Active"/>. The
///         "at most one ACTIVE per tenant" invariant is enforced here.</item>
///   <item><b>Rotation</b> — <see cref="RotateAsync"/> atomically
///         demotes the currently ACTIVE certificate to
///         <see cref="Enums.CertificadoStatus.Rotated"/> AND activates
///         the new one in a single transaction (SCN-CORE-08 atomic
///         rotation invariant).</item>
///   <item><b>Expiry detection</b> —
///         <see cref="FindCertificatesExpiringWithinAsync"/> returns
///         the certificates that need a heads-up warning to the
///         operator (analogous to the resolución 30-day warning).</item>
/// </list>
///
/// <para>
/// All operations are tenant-scoped: a rotation for tenant A cannot
/// disturb tenant B's certificates. The invariant is enforced at the
/// lookup level (the queries filter by <c>tenant_id</c>) and at the
/// activation level (the "no second ACTIVE per tenant" check).
/// </para>
/// </summary>
public interface ICertificateRotationService
{
    /// <summary>
    /// Loads the certificate blob referenced by
    /// <paramref name="certificadoId"/>, validates the X.509 chain,
    /// and — on success — transitions the entity from
    /// <see cref="Enums.CertificadoStatus.PendingValidation"/> to
    /// <see cref="Enums.CertificadoStatus.Active"/>.
    ///
    /// <para>
    /// The "at most one ACTIVE per tenant" invariant is enforced
    /// before the activation: if another ACTIVE row exists for the
    /// same tenant the method throws
    /// <see cref="Exceptions.MultipleActiveCertificatesException"/>.
    /// Callers that hit this should fall back to
    /// <see cref="RotateAsync"/> which performs the demotion + activation
    /// atomically.
    /// </para>
    /// </summary>
    /// <param name="certificadoId">Certificate to validate and activate.</param>
    /// <param name="pfxPassword">Password for the PKCS#12 blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The activated <see cref="Certificado"/> with
    /// <see cref="Certificado.Status"/> = <see cref="Enums.CertificadoStatus.Active"/>.
    /// </returns>
    /// <exception cref="Exceptions.CertificateValidationException">
    /// The .pfx is missing, the password is wrong, the certificate is
    /// expired, or the X.509 chain does not build.
    /// </exception>
    /// <exception cref="Exceptions.MultipleActiveCertificatesException">
    /// Another ACTIVE certificate already exists for the same tenant.
    /// </exception>
    Task<Certificado> ValidateAndActivateAsync(
        Guid certificadoId,
        string pfxPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates <paramref name="newCertificadoId"/>, then atomically
    /// demotes the tenant's currently ACTIVE certificate (if any) to
    /// <see cref="Enums.CertificadoStatus.Rotated"/> AND activates the
    /// new one in a single database transaction per SCN-CORE-08.
    ///
    /// <para>
    /// Atomicity is the key invariant: a partial outcome (old
    /// ROTATED without new ACTIVE, or new ACTIVE alongside the old)
    /// would leave the tenant without a signing key OR with two
    /// signing keys — both are fiscal violations. The
    /// implementation MUST wrap both operations in one
    /// <c>SaveChangesAsync</c> (or one explicit transaction) so that
    /// a failure on the activation side rolls back the demotion.
    /// </para>
    ///
    /// <para>
    /// If no current ACTIVE certificate exists the method skips the
    /// demotion and behaves like <see cref="ValidateAndActivateAsync"/>.
    /// </para>
    /// </summary>
    /// <param name="newCertificadoId">New certificate to validate + activate.</param>
    /// <param name="pfxPassword">Password for the new certificate's blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly activated <see cref="Certificado"/>.</returns>
    /// <exception cref="Exceptions.CertificateValidationException">
    /// Validation of the new certificate failed.
    /// </exception>
    Task<Certificado> RotateAsync(
        Guid newCertificadoId,
        string pfxPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every <see cref="Enums.CertificadoStatus.Active"/>
    /// certificate for the tenant whose <c>not_after</c> falls on or
    /// before <c>now + daysAhead</c>. Used by the certificate-expiry
    /// warning workflow.
    /// </summary>
    /// <param name="tenantId">Tenant to scan.</param>
    /// <param name="daysAhead">Look-ahead window in days.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Expiring certificates, earliest <c>not_after</c> first.</returns>
    Task<IReadOnlyList<Certificado>> FindCertificatesExpiringWithinAsync(
        Guid tenantId,
        int daysAhead,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cheap boolean check for whether the tenant has at least one
    /// <see cref="Enums.CertificadoStatus.Active"/> certificate.
    /// </summary>
    /// <param name="tenantId">Tenant to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> when at least one ACTIVE certificate exists;
    /// <c>false</c> otherwise.
    /// </returns>
    Task<bool> HasActiveCertificateAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
