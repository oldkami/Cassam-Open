namespace Cassam.Core.Domain.Exceptions;

/// <summary>
/// Thrown when <c>ICertificateRotationService.ValidateAndActivateAsync</c>
/// tries to activate a certificate while another certificate for the same
/// tenant is already in <see cref="Enums.CertificadoStatus.Active"/> per
/// <c>pos-core-modern-stack</c> REQ-CORE-07 / SCN-CORE-08.
///
/// <para>
/// The "at most one ACTIVE certificate per tenant" invariant is a
/// non-negotiable fiscal contract: signing must always pick exactly one
/// certificate, and ambiguity is unacceptable. The invariant is enforced
/// at THREE layers (defense in depth):
/// </para>
/// <list type="number">
///   <item><c>ICertificateRotationService</c> throws this exception.</item>
///   <item>The dispatcher rejects signing when the lookup finds &gt; 1 ACTIVE row.</item>
///   <item>A future DB-level constraint (e.g. partial unique index on
///         <c>status = 'ACTIVE'</c> per tenant) is the final guard.</item>
/// </list>
///
/// <para>
/// When this exception fires the caller should fall back to
/// <c>ICertificateRotationService.RotateAsync</c>, which demotes the
/// currently active certificate to <see cref="Enums.CertificadoStatus.Rotated"/>
/// and atomically activates the new one in a single transaction.
/// </para>
/// </summary>
public sealed class MultipleActiveCertificatesException : InvalidOperationException
{
    /// <summary>Tenant that holds more than one ACTIVE certificate.</summary>
    public Guid TenantId { get; }

    /// <summary>
    /// Number of certificates the lookup found in the ACTIVE state.
    /// The expected invariant is 0 or 1 — any value &gt; 1 is a bug.
    /// </summary>
    public int ActiveCount { get; }

    /// <summary>
    /// Builds the exception. The message is designed to drive the UI to
    /// the right remediation path: "you have N active certificates,
    /// rotate one before activating another".
    /// </summary>
    /// <param name="tenantId">Tenant with multiple ACTIVE certificates.</param>
    /// <param name="activeCount">How many ACTIVE rows the lookup found.</param>
    public MultipleActiveCertificatesException(Guid tenantId, int activeCount)
        : base($"Tenant '{tenantId}' has {activeCount} active certificates. " +
               "At most one ACTIVE certificate per tenant is allowed (REQ-CORE-07). " +
               "Use ICertificateRotationService.RotateAsync to demote the existing ACTIVE row before activating another.")
    {
        TenantId = tenantId;
        ActiveCount = activeCount;
    }
}
