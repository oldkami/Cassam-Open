using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using FluentAssertions;

namespace Cassam.Core.Tests;

/// <summary>
/// Compliance-entity tests for <see cref="Certificado"/> per
/// <c>pos-core-modern-stack</c> REQ-CORE-07.
/// </summary>
public class CertificadoTests
{
    [Fact]
    public void New_certificado_starts_in_PENDING_VALIDATION()
    {
        var cert = new Certificado
        {
            TenantId = Guid.CreateVersion7(),
            Subject = "CN=Cassam Test",
            Issuer = "CN=AC Intermedia",
            Serial = "01ABCDEF",
            NotBefore = DateTime.UtcNow,
            NotAfter = DateTime.UtcNow.AddYears(1),
            PfxPath = "/secure/certs/test.pfx",
        };

        cert.Status.Should().Be(CertificadoStatus.PendingValidation,
            "every new certificate enters the queue and awaits chain/expiry/CRL/OCSP validation");
    }

    [Fact]
    public void Certificado_can_use_OS_certificate_store_instead_of_pfx_path()
    {
        var cert = new Certificado
        {
            TenantId = Guid.CreateVersion7(),
            Subject = "CN=Production",
            Issuer = "CN=AC Intermedia",
            Serial = "02DEADBEEF",
            NotBefore = DateTime.UtcNow,
            NotAfter = DateTime.UtcNow.AddYears(1),
            CertStoreRef = "StoreName=My;StoreLocation=LocalMachine;Thumbprint=ABC123",
        };

        cert.CertStoreRef.Should().NotBeNullOrWhiteSpace(
            "OS certificate-store reference is a valid alternative to a PKCS#12 file path");
        cert.PfxPath.Should().BeNull(
            "when stored in the OS keystore the pfx path is null — the two are mutually exclusive");
    }

    [Fact]
    public void Certificado_password_hash_is_nullable_when_using_OS_store()
    {
        var cert = new Certificado
        {
            TenantId = Guid.CreateVersion7(),
            Subject = "CN=Production",
            Issuer = "CN=AC Intermedia",
            Serial = "03CAFE",
            NotBefore = DateTime.UtcNow,
            NotAfter = DateTime.UtcNow.AddYears(1),
            CertStoreRef = "StoreName=My",
        };

        cert.PasswordHash.Should().BeNull(
            "OS-stored certificates have no .pfx password; the hash column is null");
    }

    [Fact]
    public void Certificado_status_transitions_through_full_lifecycle()
    {
        var cert = new Certificado
        {
            TenantId = Guid.CreateVersion7(),
            Subject = "CN=Test",
            Issuer = "CN=CA",
            Serial = "04ROTATE",
            NotBefore = DateTime.UtcNow,
            NotAfter = DateTime.UtcNow.AddYears(1),
            Status = CertificadoStatus.PendingValidation,
        };

        // PENDING_VALIDATION → ACTIVE → ROTATED per SCN-CORE-08.
        cert.Status.Should().Be(CertificadoStatus.PendingValidation);

        cert.Status = CertificadoStatus.Active;
        cert.Status.Should().Be(CertificadoStatus.Active);

        cert.Status = CertificadoStatus.Rotated;
        cert.Status.Should().Be(CertificadoStatus.Rotated,
            "after rotation the old certificate moves to ROTATED so documents signed before stay verifiable");
    }

    [Fact]
    public void Certificado_expiry_is_a_DateTime_pair_NotBefore_lt_NotAfter()
    {
        var notBefore = DateTime.UtcNow.AddDays(-1);
        var notAfter = DateTime.UtcNow.AddYears(1);

        var cert = new Certificado
        {
            TenantId = Guid.CreateVersion7(),
            Subject = "CN=Test",
            Issuer = "CN=CA",
            Serial = "05EXP",
            NotBefore = notBefore,
            NotAfter = notAfter,
        };

        cert.NotBefore.Should().BeBefore(cert.NotAfter,
            "X.509 NotBefore must precede NotAfter — a misconfigured pair is rejected at validation time");
    }

    [Fact]
    public void SoftwareTechnicalKey_starts_active_and_issued_by_dian()
    {
        var key = new SoftwareTechnicalKey
        {
            TenantId = Guid.CreateVersion7(),
            KeyValue = "abc12345-issued-by-dian",
            IssuedAt = DateTime.UtcNow,
        };

        key.IssuedByDian.Should().BeTrue();
        key.Active.Should().BeTrue();
        key.DeactivatedAt.Should().BeNull();
    }

    [Fact]
    public void SoftwareTechnicalKey_can_be_deactivated_when_rotated()
    {
        var key = new SoftwareTechnicalKey
        {
            TenantId = Guid.CreateVersion7(),
            KeyValue = "old-key",
            IssuedAt = DateTime.UtcNow.AddYears(-1),
        };

        key.Active = false;
        key.DeactivatedAt = DateTime.UtcNow;

        key.Active.Should().BeFalse();
        key.DeactivatedAt.Should().NotBeNull(
            "the deactivated timestamp records when the key stopped issuing — old documents remain verifiable");
    }
}
