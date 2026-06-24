namespace Cassam.Core.Domain.Exceptions;

/// <summary>
/// Thrown when <c>ICertificateRotationService.ValidateAndActivateAsync</c>
/// (or <c>RotateAsync</c>) cannot validate an uploaded X.509 certificate
/// per <c>pos-core-modern-stack</c> REQ-CORE-07.
///
/// <para>
/// The validation steps that can fail and surface this exception:
/// </para>
/// <list type="bullet">
///   <item>The <c>.pfx</c> file is missing, unreadable, or the password is wrong.</item>
///   <item>The certificate's <c>not_after</c> is in the past (expired).</item>
///   <item>The X.509 chain does not build (missing intermediates, untrusted issuer).</item>
///   <item>The certificate's purpose (Digital Signature key usage) is not asserted.</item>
///   <item>For DIAN-issued certs the chain must terminate in the
///         Colombian <em>Autoridad Certificadora</em> root — a foreign CA fails here.</item>
/// </list>
///
/// <para>
/// The exception's <see cref="Reason"/> property carries a stable code
/// the UI can switch on (e.g. <c>"EXPIRED"</c>, <c>"CHAIN_INVALID"</c>,
/// <c>"BAD_PASSWORD"</c>) without parsing the message string. The message
/// itself is human-readable and intended for the operator's screen.
/// </para>
/// </summary>
public sealed class CertificateValidationException : InvalidOperationException
{
    /// <summary>
    /// Stable machine-readable reason code. One of:
    /// <c>"FILE_NOT_FOUND"</c>, <c>"BAD_PASSWORD"</c>, <c>"EXPIRED"</c>,
    /// <c>"CHAIN_INVALID"</c>, <c>"KEY_USAGE_MISSING"</c>,
    /// <c>"MALFORMED_PFX"</c>.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Builds the exception with a reason code and a human-readable
    /// message. The reason is suitable for programmatic dispatch; the
    /// message is suitable for UI display.
    /// </summary>
    /// <param name="reason">Stable error code (see <see cref="Reason"/>).</param>
    /// <param name="message">Human-readable description.</param>
    public CertificateValidationException(string reason, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }
}
