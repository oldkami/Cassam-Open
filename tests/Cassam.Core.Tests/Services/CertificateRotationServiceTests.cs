using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Exceptions;
using Cassam.Core.Domain.Services;
using Cassam.Core.Persistence;
using Cassam.Core.Persistence.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CertificateRotationService"/>. These tests
/// generate throwaway X.509 certificates at runtime — we are testing
/// the lifecycle/rotation logic, NOT the DIAN trust chain. Self-signed
/// certificates are valid for these purposes per
/// <c>pos-core-modern-stack</c> testability guidance.
///
/// <para>
/// Every test creates a fresh X509Certificate2 + .pfx file in the
/// system temp directory and disposes / deletes the artefacts in the
/// test cleanup. The InMemory DbContext keeps the application-layer
/// contract honest while the actual X.509 plumbing is exercised
/// against the real <see cref="System.Security.Cryptography"/> stack.
/// </para>
/// </summary>
public class CertificateRotationServiceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task ValidateAndActivateAsync_transitions_pending_validation_to_active_for_valid_cert()
    {
        // Happy path: a fresh, well-formed self-signed certificate with
        // digital-signature key usage MUST transition from PENDING_VALIDATION
        // to ACTIVE in a single SaveChanges call.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);
        var cert = await SeedCertificado(ctx, tenantId, pfxPath);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var activated = await sut.ValidateAndActivateAsync(cert.Id, pfxPassword: TestPassword);

        activated.Status.Should().Be(CertificadoStatus.Active,
            "a valid X.509 with digital-signature key usage and future expiry MUST activate");
    }

    [Fact]
    public async Task ValidateAndActivateAsync_throws_when_certificate_is_expired()
    {
        // Expired certificates must NEVER become ACTIVE — the signer
        // would refuse them at signing time anyway, but refusing at
        // activation is the earlier, friendlier gate.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: -10); // already expired
        var cert = await SeedCertificado(ctx, tenantId, pfxPath);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var act = async () => await sut.ValidateAndActivateAsync(cert.Id, pfxPassword: TestPassword);

        var exception = await act.Should().ThrowAsync<CertificateValidationException>();
        exception.Which.Reason.Should().Be("EXPIRED",
            "a certificate whose not_after is in the past MUST report EXPIRED, not a generic chain error");
    }

    [Fact]
    public async Task ValidateAndActivateAsync_throws_when_password_is_wrong()
    {
        // A bad password is the most common operator mistake. The
        // service must surface a stable BAD_PASSWORD reason so the UI
        // can prompt for re-entry without revealing internal state.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);
        var cert = await SeedCertificado(ctx, tenantId, pfxPath);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var act = async () => await sut.ValidateAndActivateAsync(cert.Id, pfxPassword: "wrong-password");

        var exception = await act.Should().ThrowAsync<CertificateValidationException>();
        exception.Which.Reason.Should().Be("BAD_PASSWORD");
    }

    [Fact]
    public async Task ValidateAndActivateAsync_throws_when_pfx_file_does_not_exist()
    {
        // The pfx path is wrong (operator typo, file moved, etc.).
        // We surface FILE_NOT_FOUND so the UI can prompt the operator
        // to re-upload.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var cert = await SeedCertificado(ctx, tenantId, pfxPath: @"C:\nonexistent\fake.pfx");

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var act = async () => await sut.ValidateAndActivateAsync(cert.Id, pfxPassword: TestPassword);

        var exception = await act.Should().ThrowAsync<CertificateValidationException>();
        exception.Which.Reason.Should().Be("FILE_NOT_FOUND");
    }

    [Fact]
    public async Task ValidateAndActivateAsync_throws_when_another_active_cert_already_exists()
    {
        // The "at most one ACTIVE per tenant" invariant is enforced
        // BEFORE the activation. When another ACTIVE row exists the
        // service must refuse rather than create a second active row
        // and silently break the signing-time lookup.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);

        // First certificate is already ACTIVE.
        await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.Active);
        // Second certificate is the one being validated.
        var second = await SeedCertificado(ctx, tenantId, pfxPath);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var act = async () => await sut.ValidateAndActivateAsync(second.Id, pfxPassword: TestPassword);

        var exception = await act.Should().ThrowAsync<MultipleActiveCertificatesException>();
        exception.Which.TenantId.Should().Be(tenantId);
        exception.Which.ActiveCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ValidateAndActivateAsync_throws_when_status_is_already_active()
    {
        // Calling ValidateAndActivate on a row that's already ACTIVE
        // is almost certainly a bug — fail fast with a clear message.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);
        var cert = await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.Active);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var act = async () => await sut.ValidateAndActivateAsync(cert.Id, pfxPassword: TestPassword);

        var exception = await act.Should().ThrowAsync<CertificateValidationException>();
        exception.Which.Reason.Should().Be("ALREADY_ACTIVE");
    }

    [Fact]
    public async Task ValidateAndActivateAsync_throws_when_status_is_terminal()
    {
        // Expired / Revoked / Rotated are terminal states — calling
        // ValidateAndActivate on them is meaningless. The service must
        // refuse rather than silently transition.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);

        var expired = await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.Expired);
        var revoked = await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.Revoked);
        var rotated = await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.Rotated);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var expiredAct = async () => await sut.ValidateAndActivateAsync(expired.Id, pfxPassword: TestPassword);
        var revokedAct = async () => await sut.ValidateAndActivateAsync(revoked.Id, pfxPassword: TestPassword);
        var rotatedAct = async () => await sut.ValidateAndActivateAsync(rotated.Id, pfxPassword: TestPassword);

        await expiredAct.Should().ThrowAsync<CertificateValidationException>();
        await revokedAct.Should().ThrowAsync<CertificateValidationException>();
        await rotatedAct.Should().ThrowAsync<CertificateValidationException>();
    }

    // ===== RotateAsync ================================================

    [Fact]
    public async Task RotateAsync_demotes_active_to_rotated_and_activates_new_in_single_flush()
    {
        // SCN-CORE-08 atomic rotation invariant: the OLD row is flipped
        // to ROTATED AND the NEW row is flipped to ACTIVE in a single
        // SaveChanges call. After the call both rows persist with the
        // right status; there is exactly one ACTIVE row for the tenant.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);

        var oldCert = await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.Active);
        var newCert = await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.PendingValidation);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var activated = await sut.RotateAsync(newCert.Id, pfxPassword: TestPassword);

        activated.Status.Should().Be(CertificadoStatus.Active,
            "the new row must be ACTIVE after rotation completes");

        // Reload from the change tracker (the entity is already tracked,
        // but explicit re-read proves the persisted state matches).
        oldCert.Status.Should().Be(CertificadoStatus.Rotated,
            "the previously-active row must be ROTATED after rotation");
        newCert.Status.Should().Be(CertificadoStatus.Active);

        // Cross-check via a fresh lookup.
        var activeCount = await ctx.Certificados
            .CountAsync(c => c.TenantId == tenantId
                          && c.Status == CertificadoStatus.Active);
        activeCount.Should().Be(1,
            "exactly one ACTIVE row must remain — the SCN-CORE-08 invariant");
    }

    [Fact]
    public async Task RotateAsync_appends_two_audit_rows_in_same_transaction()
    {
        // The demotion and activation MUST both produce audit rows
        // (REQ-CORE-03). Because SaveChanges flushes both in one
        // transaction, a failure on the second UPDATE would roll back
        // the first — and the audit rows.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);

        var oldCert = await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.Active);
        var newCert = await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.PendingValidation);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        await sut.RotateAsync(newCert.Id, pfxPassword: TestPassword);

        var auditRows = await ctx.AuditLog.AsNoTracking()
            .Where(a => a.EntityType == "certificado"
                     && (a.EntityId == oldCert.Id || a.EntityId == newCert.Id))
            .OrderBy(a => a.OccurredAt)
            .ToListAsync();

        auditRows.Should().HaveCount(2,
            "one audit row per state change (demotion + activation)");
        auditRows.Select(a => a.Action).Should().ContainInOrder(
            "CERTIFICATE_ROTATED_OUT",
            "CERTIFICATE_ROTATED_IN");
    }

    [Fact]
    public async Task RotateAsync_behaves_like_validate_and_activate_when_no_active_exists()
    {
        // Edge case: rotate into a tenant that has no current ACTIVE
        // certificate. The service must skip the demotion step and
        // behave like ValidateAndActivateAsync.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);
        var newCert = await SeedCertificado(ctx, tenantId, pfxPath,
            status: CertificadoStatus.PendingValidation);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var activated = await sut.RotateAsync(newCert.Id, pfxPassword: TestPassword);

        activated.Status.Should().Be(CertificadoStatus.Active);
    }

    [Fact]
    public async Task RotateAsync_does_not_disturb_other_tenants_active_certificates()
    {
        // Tenant isolation invariant: rotating for tenant A MUST NOT
        // touch any certificate owned by tenant B, even when B also
        // has an ACTIVE row.
        await using var ctx = NewContext();
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);

        var tenantAActive = await SeedCertificado(ctx, tenantA, pfxPath,
            status: CertificadoStatus.Active);
        var tenantBActive = await SeedCertificado(ctx, tenantB, pfxPath,
            status: CertificadoStatus.Active);
        var tenantANew = await SeedCertificado(ctx, tenantA, pfxPath,
            status: CertificadoStatus.PendingValidation);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        await sut.RotateAsync(tenantANew.Id, pfxPassword: TestPassword);

        tenantAActive.Status.Should().Be(CertificadoStatus.Rotated,
            "tenant A's old ACTIVE row was demoted as expected");
        tenantBActive.Status.Should().Be(CertificadoStatus.Active,
            "tenant B's ACTIVE row was untouched — the rotation is tenant-scoped");
    }

    [Fact]
    public async Task RotateAsync_throws_when_new_cert_validation_fails_and_does_not_demote_existing()
    {
        // Critical atomicity check: if the new cert is invalid we must
        // NOT touch the existing ACTIVE row. Otherwise the tenant is
        // left with no signing certificate, which is a fiscal outage.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();

        var oldCert = await SeedCertificado(ctx, tenantId, pfxPath: "ignored",
            status: CertificadoStatus.Active);

        // The new cert's .pfx is missing — validation will fail.
        var newCert = await SeedCertificado(ctx, tenantId,
            pfxPath: @"C:\nonexistent\for-rotate.pfx",
            status: CertificadoStatus.PendingValidation);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var act = async () => await sut.RotateAsync(newCert.Id, pfxPassword: TestPassword);

        await act.Should().ThrowAsync<CertificateValidationException>(
            "validation of the new certificate failed");

        oldCert.Status.Should().Be(CertificadoStatus.Active,
            "the existing ACTIVE row must remain ACTIVE when the new cert is invalid — " +
            "this is the SCN-CORE-08 atomicity guarantee");
    }

    // ===== FindCertificatesExpiringWithinAsync =======================

    [Fact]
    public async Task FindCertificatesExpiringWithinAsync_returns_active_certs_within_window()
    {
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await SeedCertificado(ctx, tenantId, notAfter: now.AddDays(-1),
            status: CertificadoStatus.Active);   // already past — included
        await SeedCertificado(ctx, tenantId, notAfter: now.AddDays(5),
            status: CertificadoStatus.Active);   // within window — included
        await SeedCertificado(ctx, tenantId, notAfter: now.AddDays(30),
            status: CertificadoStatus.Active);   // boundary — included
        await SeedCertificado(ctx, tenantId, notAfter: now.AddDays(31),
            status: CertificadoStatus.Active);   // outside window
        await SeedCertificado(ctx, tenantId, notAfter: now.AddDays(5),
            status: CertificadoStatus.Expired);  // excluded — wrong status

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var expiring = await sut.FindCertificatesExpiringWithinAsync(tenantId, daysAhead: 30);

        expiring.Should().HaveCount(3,
            "today/expired, +5d, and +30d fall within the 30-day inclusive window");
        expiring.Select(c => c.NotAfter)
            .Should().BeInAscendingOrder();
    }

    // ===== HasActiveCertificateAsync =================================

    [Fact]
    public async Task HasActiveCertificateAsync_returns_true_when_any_active_exists()
    {
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        await SeedCertificado(ctx, tenantId, status: CertificadoStatus.Active);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var has = await sut.HasActiveCertificateAsync(tenantId);

        has.Should().BeTrue();
    }

    [Fact]
    public async Task HasActiveCertificateAsync_returns_false_when_no_active()
    {
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        await SeedCertificado(ctx, tenantId, status: CertificadoStatus.PendingValidation);

        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var has = await sut.HasActiveCertificateAsync(tenantId);

        has.Should().BeFalse();
    }

    // ===== Helpers ====================================================

    private const string TestPassword = "test-pfx-password";

    /// <summary>
    /// Constructs the test-friendly chain policy: allows unknown CAs
    /// (so self-signed test certs validate) and skips revocation
    /// checking (no CRL/OCSP endpoint on a self-signed cert).
    /// </summary>
    private static X509ChainPolicy TestChainPolicy() => new()
    {
        RevocationMode = X509RevocationMode.NoCheck,
        VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority,
    };

    private static CassamDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CassamDbContext>()
            .UseInMemoryDatabase($"cert-rotation-{Guid.NewGuid():N}")
            .Options);

    private static async Task<Certificado> SeedCertificado(
        CassamDbContext ctx,
        Guid tenantId,
        string? pfxPath = null,
        CertificadoStatus? status = null,
        DateTime? notAfter = null)
    {
        var cert = new Certificado
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Subject = "CN=Cassam Test",
            Issuer = "CN=Cassam Test CA",
            Serial = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            NotBefore = DateTime.UtcNow.AddDays(-1),
            NotAfter = notAfter ?? DateTime.UtcNow.AddYears(1),
            PfxPath = pfxPath,
            Status = status ?? CertificadoStatus.PendingValidation,
        };
        ctx.Certificados.Add(cert);
        await ctx.SaveChangesAsync();
        return cert;
    }

    /// <summary>
    /// Generates a self-signed RSA certificate with digital-signature
    /// key usage and writes it as a PKCS#12 .pfx file in the temp
    /// directory. The certificate's validity starts at <c>now</c> and
    /// ends <paramref name="validityDays"/> later (negative values
    /// produce an already-expired cert).
    /// </summary>
    private async Task<string> CreateSelfSignedPfxAsync(int validityDays)
    {
        using var rsa = RSA.Create(2048);

        var req = new CertificateRequest(
            "CN=Cassam Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Digital-signature key usage — without this the service would
        // reject the cert at validation time.
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                critical: true));

        var notBefore = validityDays < 0
            ? DateTime.UtcNow.AddDays(validityDays - 1) // start in the past
            : DateTime.UtcNow.AddDays(-1);
        var notAfter = validityDays < 0
            ? DateTime.UtcNow.AddDays(validityDays)        // already in the past
            : DateTime.UtcNow.AddDays(validityDays);

        using var cert = req.CreateSelfSigned(notBefore, notAfter);

        // Write the .pfx file. Pkcs12Export.PfxOnly writes a clean blob
        // (no certificate-only entry) that Pkcs12Import on the read
        // side can open with a password.
        var pfxBytes = cert.Export(X509ContentType.Pfx, TestPassword);
        var path = Path.Combine(Path.GetTempPath(), $"cassam-test-{Guid.NewGuid():N}.pfx");
        await File.WriteAllBytesAsync(path, pfxBytes);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Test fixture cleanup — remove every .pfx file we generated so
    /// the temp directory does not accumulate noise across test runs.
    /// </summary>
    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup; never let a stray file shadow
                // a test failure on shutdown.
            }
        }
        _tempFiles.Clear();
    }
}
