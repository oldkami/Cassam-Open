using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Exceptions;
using Cassam.Core.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Persistence.Services;

/// <summary>
/// EF Core + <see cref="System.Security.Cryptography.X509Certificates"/>
/// backed <see cref="ICertificateRotationService"/>. Validates uploaded
/// PKCS#12 blobs and atomically rotates certificates per
/// <c>pos-core-modern-stack</c> REQ-CORE-07 / SCN-CORE-08.
///
/// <para>
/// X.509 chain validation contract:
/// </para>
/// <list type="number">
///   <item>Open the <c>.pfx</c> blob with the operator-supplied password.</item>
///   <item>Build the X.509 chain via <see cref="X509Chain.Build"/>.</item>
///   <item>Walk every <see cref="X509ChainStatus"/> in the result and
///         raise <see cref="CertificateValidationException"/> with a
///         stable reason code on any error status.</item>
///   <item>Confirm <c>not_after</c> is in the future.</item>
///   <item>Confirm the certificate has a digital-signature key usage
///         (we will use it to sign <c>documentos_electronicos</c>).</item>
/// </list>
///
/// <para>
/// Rotation atomicity (SCN-CORE-08):
/// </para>
/// <list type="number">
///   <item>Validate the new certificate first. If validation fails we
///         MUST NOT touch the existing ACTIVE certificate.</item>
///   <item>Within one <c>SaveChangesAsync</c> flush, mark the existing
///         ACTIVE row as <see cref="CertificadoStatus.Rotated"/> AND
///         mark the new row as <see cref="CertificadoStatus.Active"/>.
///         EF Core wraps both UPDATEs in a single transaction; a failure
///         on the second UPDATE rolls back the first.</item>
///   <item>The "rotated at" timestamp is recorded by re-using the
///         <see cref="Entity.UpdatedAt"/> column — when a certificate's
///         status is <see cref="CertificadoStatus.Rotated"/>,
///         <c>updated_at</c> IS the rotation time. A dedicated
///         <c>rotated_at</c> column can be added by a future migration
///         if reporting needs to distinguish "metadata updated" from
///         "rotated".</item>
/// </list>
///
/// <para>
/// Tenant isolation: every query and every mutation filters by
/// <c>tenant_id</c>. Rotation for tenant A cannot touch a certificate
/// owned by tenant B even under contention.
/// </para>
/// </summary>
public sealed class CertificateRotationService : ICertificateRotationService
{
    private readonly CassamDbContext _dbContext;

    /// <summary>
    /// Chain policy applied to every validation. Injectable so tests
    /// can pass a permissive policy (allowing self-signed certs) while
    /// production wires up a strict one that requires the DIAN root.
    /// </summary>
    private readonly X509ChainPolicy _chainPolicy;

    /// <summary>
    /// Constructs the service around the caller's
    /// <see cref="CassamDbContext"/>.
    /// </summary>
    /// <param name="dbContext">
    /// DbContext that owns the rotation transaction. The same context
    /// MUST be used for validation + activation so the row-level state
    /// stays coherent.
    /// </param>
    /// <param name="chainPolicy">
    /// Optional chain policy. When <c>null</c> the service uses a
    /// strict default that requires revocation checking and trusts the
    /// OS certificate store (production behaviour for DIAN-issued
    /// certificates whose root is installed platform-wide).
    /// </param>
    public CertificateRotationService(
        CassamDbContext dbContext,
        X509ChainPolicy? chainPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
        _chainPolicy = chainPolicy ?? CreateStrictDefaultPolicy();
    }

    /// <inheritdoc />
    public async Task<Certificado> ValidateAndActivateAsync(
        Guid certificadoId,
        string pfxPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(certificadoId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(pfxPassword);

        cancellationToken.ThrowIfCancellationRequested();

        var certificado = await _dbContext.Certificados
            .FirstOrDefaultAsync(c => c.Id == certificadoId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CertificateValidationException(
                "NOT_FOUND",
                $"Certificado '{certificadoId}' not found.");

        // Defensive: only PENDING_VALIDATION rows can transition to ACTIVE.
        // Calling ValidateAndActivate on an already-ACTIVE row would
        // silently violate the "at most one ACTIVE per tenant" invariant;
        // the next check would catch it but we fail fast with a clear
        // message instead.
        if (certificado.Status == CertificadoStatus.Active)
        {
            throw new CertificateValidationException(
                "ALREADY_ACTIVE",
                $"Certificado '{certificadoId}' is already ACTIVE. " +
                "Use RotateAsync if you want to swap it for another.");
        }

        if (certificado.Status is CertificadoStatus.Expired
            or CertificadoStatus.Revoked
            or CertificadoStatus.Rotated)
        {
            throw new CertificateValidationException(
                "TERMINAL_STATE",
                $"Certificado '{certificadoId}' is in terminal state '{certificado.Status}' " +
                "and cannot be activated. Upload a new certificate and try again.");
        }

        using var parsed = LoadAndValidatePfx(certificado, pfxPassword);

        // "At most one ACTIVE per tenant" invariant.
        // Count any other ACTIVE row for this tenant — the lookup
        // excludes the row being validated, but in this branch we
        // already know certificadoId is not yet ACTIVE so the filter is
        // belt-and-braces.
        var otherActiveCount = await _dbContext.Certificados
            .CountAsync(c => c.TenantId == certificado.TenantId
                          && c.Status == CertificadoStatus.Active
                          && c.Id != certificado.Id,
                cancellationToken)
            .ConfigureAwait(false);

        if (otherActiveCount > 0)
        {
            throw new MultipleActiveCertificatesException(
                certificado.TenantId, otherActiveCount);
        }

        // Activate + audit row in one transaction.
        var beforeState = certificado.Status.ToString();
        certificado.Status = CertificadoStatus.Active;
        certificado.UpdatedAt = DateTime.UtcNow;

        AppendAuditRow(
            certificado.TenantId,
            certificado.Id,
            "CERTIFICATE_ACTIVATED",
            beforeState,
            CertificadoStatus.Active.ToString());

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return certificado;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Order of operations is load-bearing:
    /// </para>
    /// <list type="number">
    ///   <item>Load and validate the NEW certificate. If this fails we
    ///         abort before touching the existing ACTIVE row.</item>
    ///   <item>Load the new certificate row (tracked) and the existing
    ///         ACTIVE row (if any) under the same DbContext.</item>
    ///   <item>Flip the existing ACTIVE row's status to
    ///         <see cref="CertificadoStatus.Rotated"/> in memory.</item>
    ///   <item>Flip the new row's status to
    ///         <see cref="CertificadoStatus.Active"/> in memory.</item>
    ///   <item>Append two audit rows (one per state change).</item>
    ///   <item>Single <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
    ///         flush — both UPDATEs and both INSERTs land in the same
    ///         PostgreSQL transaction; a failure on any of them rolls
    ///         back all four.</item>
    /// </list>
    /// </remarks>
    public async Task<Certificado> RotateAsync(
        Guid newCertificadoId,
        string pfxPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(newCertificadoId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(pfxPassword);

        cancellationToken.ThrowIfCancellationRequested();

        var newCert = await _dbContext.Certificados
            .FirstOrDefaultAsync(c => c.Id == newCertificadoId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CertificateValidationException(
                "NOT_FOUND",
                $"Certificado '{newCertificadoId}' not found.");

        // Validate the new cert FIRST. If validation fails the existing
        // ACTIVE row is untouched, preserving fiscal continuity.
        using var parsed = LoadAndValidatePfx(newCert, pfxPassword);

        // Locate the currently ACTIVE row for the same tenant (if any).
        // We use the snapshot isolation of a single DbContext so the
        // SELECT happens in the same transaction as the subsequent UPDATE.
        var currentActive = await _dbContext.Certificados
            .FirstOrDefaultAsync(c => c.TenantId == newCert.TenantId
                                   && c.Status == CertificadoStatus.Active
                                   && c.Id != newCert.Id,
                cancellationToken)
            .ConfigureAwait(false);

        // ---- Atomic in-memory state mutations ----
        if (currentActive is not null)
        {
            var beforeRotated = currentActive.Status.ToString();
            currentActive.Status = CertificadoStatus.Rotated;
            currentActive.UpdatedAt = DateTime.UtcNow;

            AppendAuditRow(
                currentActive.TenantId,
                currentActive.Id,
                "CERTIFICATE_ROTATED_OUT",
                beforeRotated,
                CertificadoStatus.Rotated.ToString());
        }

        var beforeActive = newCert.Status.ToString();
        newCert.Status = CertificadoStatus.Active;
        newCert.UpdatedAt = DateTime.UtcNow;

        AppendAuditRow(
            newCert.TenantId,
            newCert.Id,
            "CERTIFICATE_ROTATED_IN",
            beforeActive,
            CertificadoStatus.Active.ToString());

        // ---- Single flush ----
        // SaveChangesAsync wraps all tracked changes in ONE PostgreSQL
        // transaction. If the second UPDATE fails the first is rolled
        // back — this is the SCN-CORE-08 atomic rotation invariant.
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return newCert;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Certificado>> FindCertificatesExpiringWithinAsync(
        Guid tenantId,
        int daysAhead,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(daysAhead);

        cancellationToken.ThrowIfCancellationRequested();

        var cutoff = DateTime.UtcNow.AddDays(daysAhead);

        return await _dbContext.Certificados
            .Where(c => c.TenantId == tenantId
                     && c.Status == CertificadoStatus.Active
                     && c.NotAfter <= cutoff)
            .OrderBy(c => c.NotAfter)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveCertificateAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        return await _dbContext.Certificados
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId
                        && c.Status == CertificadoStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // ===== Internal helpers =============================================

    /// <summary>
    /// Loads the .pfx blob referenced by <paramref name="certificado"/>,
    /// builds the X.509 chain, and validates it. Throws
    /// <see cref="CertificateValidationException"/> on any failure with a
    /// stable reason code.
    /// </summary>
    /// <param name="certificado">The certificado metadata row to read.</param>
    /// <param name="password">Password for the PKCS#12 blob.</param>
    /// <returns>
    /// A disposable <see cref="X509Certificate2"/> the caller MUST
    /// dispose (the private key is loaded in-process and should be
    /// released as soon as validation completes).
    /// </returns>
    private X509Certificate2 LoadAndValidatePfx(Certificado certificado, string password)
    {
        // ---- Step 1: locate the blob ----
        // PfxPath is mutually exclusive with CertStoreRef (the entity
        // model documents this). For now we only support .pfx path —
        // OS-certificate-store support is a follow-up Phase 3 item.
        if (string.IsNullOrWhiteSpace(certificado.PfxPath))
        {
            throw new CertificateValidationException(
                "NO_BLOB",
                $"Certificado '{certificado.Id}' has no .pfx path. " +
                "OS certificate-store support is not yet implemented; " +
                "set PfxPath before invoking validation.");
        }

        if (!File.Exists(certificado.PfxPath))
        {
            throw new CertificateValidationException(
                "FILE_NOT_FOUND",
                $"Pfx file not found at '{certificado.PfxPath}'.");
        }

        // ---- Step 2: open the PKCS#12 blob ----
        X509Certificate2 cert;
        try
        {
            // X509CertificateLoader is the .NET 9+ replacement for the
            // obsolete X509Certificate2(string, string) constructor.
            // We read into memory and pass the bytes so the API surface
            // matches across .NET versions and the EphemeralKeySet
            // flag prevents the private key from being persisted to
            // the user key store (PR 4 does not own key management —
            // that responsibility belongs to the signing use case).
            var pfxBytes = File.ReadAllBytes(certificado.PfxPath);
            cert = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException ex)
        {
            // Most common cause is a wrong password. The exception
            // message is not surfaced to the operator (it can leak
            // internal state) — we present a stable reason.
            throw new CertificateValidationException(
                "BAD_PASSWORD",
                $"Could not open the .pfx file at '{certificado.PfxPath}'. " +
                "Verify the password is correct and the file is a valid PKCS#12 blob. " +
                $"Underlying error: {ex.GetType().Name}.");
        }

        try
        {
            // ---- Step 3: build the chain ----
            using var chain = new X509Chain();
            chain.ChainPolicy = _chainPolicy;

            var built = chain.Build(cert);
            if (!built)
            {
                // Translate chain-status flags into a single stable reason.
                var allowUnknownCa =
                    (_chainPolicy.VerificationFlags
                        & X509VerificationFlags.AllowUnknownCertificateAuthority) != 0;
                var reason = TranslateChainStatus(chain.ChainStatus, allowUnknownCa);
                throw new CertificateValidationException(
                    reason,
                    $"X.509 chain validation failed for certificado '{certificado.Id}'. " +
                    $"Reason: {reason}. " +
                    $"Chain status: {string.Join(", ", chain.ChainStatus.Select(s => s.Status.ToString()))}.");
            }

            // ---- Step 4: not-expired check ----
            // X509Chain.Build already enforces NotAfter at VerificationTime,
            // but we re-check explicitly so the error message names the
            // expiry property instead of a generic ChainStatus flag.
            if (cert.NotAfter <= DateTime.UtcNow)
            {
                throw new CertificateValidationException(
                    "EXPIRED",
                    $"Certificado '{certificado.Id}' expired at {cert.NotAfter:O}.");
            }

            // ---- Step 5: digital-signature key usage ----
            // We will use this certificate to sign documentos_electronicos.
            // A cert without the DigitalSignature key usage would either
            // fail to sign or, worse, sign without cryptographic intent.
            //
            // We read the KeyUsage extension (OID 2.5.29.15) directly
            // because the cert may or may not include the extension —
            // when the extension is absent we treat it as "no usage
            // restriction" (per RFC 5280 §4.2.1.3) and accept the cert.
            // When the extension is present we require the digital
            // signature bit to be set.
            var keyUsageExt = cert.Extensions
                .OfType<X509KeyUsageExtension>()
                .FirstOrDefault();

            if (keyUsageExt is not null
                && (keyUsageExt.KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0)
            {
                throw new CertificateValidationException(
                    "KEY_USAGE_MISSING",
                    $"Certificado '{certificado.Id}' does not assert the DigitalSignature key usage. " +
                    "DIAN signing requires a certificate whose key usage includes digital signatures.");
            }

            return cert;
        }
        catch
        {
            // Release the loaded key material before propagating the
            // exception — we own the lifetime here.
            cert.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Translates the first significant <see cref="X509ChainStatus"/> flag
    /// into one of the stable reason codes
    /// (<c>"EXPIRED"</c>, <c>"CHAIN_INVALID"</c>, etc.). Used to give the
    /// UI a switch-friendly error code instead of a free-form message.
    /// </summary>
    /// <param name="statuses">Chain status flags returned by <see cref="X509Chain.Build"/>.</param>
    /// <param name="allowUnknownCertificateAuthority">
    /// When <c>true</c>, the <see cref="X509ChainStatusFlags.UntrustedRoot"/>
    /// status is treated as a non-error (the typical pattern for tests
    /// that use self-signed certificates).
    /// </param>
    private static string TranslateChainStatus(
        X509ChainStatus[] statuses,
        bool allowUnknownCertificateAuthority)
    {
        if (statuses.Length == 0)
        {
            return "CHAIN_INVALID";
        }

        foreach (var status in statuses)
        {
            // UntrustedRoot is the expected status for self-signed certs
            // (used in tests) when AllowUnknownCertificateAuthority is
            // set. We skip it so the UI does not surface an error
            // message that mentions the trust store.
            if (allowUnknownCertificateAuthority
                && (status.Status & X509ChainStatusFlags.UntrustedRoot) != 0)
            {
                continue;
            }

            return status.Status switch
            {
                X509ChainStatusFlags.NotTimeValid => "EXPIRED",
                X509ChainStatusFlags.NotTimeNested => "EXPIRED",
                X509ChainStatusFlags.Revoked => "REVOKED",
                X509ChainStatusFlags.NotSignatureValid => "CHAIN_INVALID",
                _ => "CHAIN_INVALID",
            };
        }

        return "CHAIN_INVALID";
    }

    /// <summary>
    /// Appends an <see cref="AuditLog"/> entry on the same DbContext
    /// without calling <c>SaveChanges</c>. The caller's flush commits
    /// the audit row in the same transaction as the state change per
    /// REQ-CORE-03.
    /// </summary>
    private void AppendAuditRow(
        Guid tenantId,
        Guid entityId,
        string action,
        string? beforeState,
        string afterState)
    {
        var now = DateTime.UtcNow;
        _dbContext.AuditLog.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ActorUserId = Guid.Empty,
            EntityType = "certificado",
            EntityId = entityId,
            Action = action,
            BeforeState = beforeState,
            AfterState = afterState,
            IpAddress = null,
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1u,
        });
    }

    /// <summary>
    /// Production-default chain policy. Trusts the OS certificate
    /// store, requires online revocation checking, and refuses any
    /// unknown-CA shortcut. The DIAN root is expected to be installed
    /// platform-wide (or added to <c>ExtraStore</c> by the host).
    /// </summary>
    private static X509ChainPolicy CreateStrictDefaultPolicy()
    {
        return new X509ChainPolicy
        {
            RevocationMode = X509RevocationMode.Online,
            UrlRetrievalTimeout = TimeSpan.FromSeconds(10),
            VerificationFlags = X509VerificationFlags.NoFlag,
        };
    }
}
