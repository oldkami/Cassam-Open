using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Exceptions;
using Cassam.Core.Domain.Services;
using Cassam.Core.Persistence.Services;
using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Integration;

/// <summary>
/// Real-PostgreSQL integration tests for <see cref="CertificateRotationService"/>.
/// These verify the SCN-CORE-08 atomic rotation invariant at the
/// database level — after a rotation, exactly one ACTIVE row remains
/// per tenant and the demoted row is marked ROTATED.
///
/// <para>
/// The unit tests cover the application-layer rotation logic against
/// the InMemory provider; only these integration tests verify that
/// the same logic, against a real PostgreSQL instance, produces the
/// expected database state across two SaveChanges invocations on
/// separate connections.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class CertificateRotationIntegrationTests : IDisposable
{
    private readonly PostgresContainerFixture _fixture;
    private readonly List<string> _tempFiles = new();

    public CertificateRotationIntegrationTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RotateAsync_persists_old_rotated_and_new_active_in_real_postgres()
    {
        // The SCN-CORE-08 invariant: after rotation the database holds
        // BOTH rows — old (ROTATED) and new (ACTIVE). Verify both are
        // present and in the right state.
        var tenantId = await CreateTenantAsync(_fixture.ConnectionString);

        var oldPfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);
        var newPfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);

        var oldCertId = Guid.CreateVersion7();
        var newCertId = Guid.CreateVersion7();
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantId, oldCertId, oldPfxPath,
            status: CertificadoStatus.Active);
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantId, newCertId, newPfxPath,
            status: CertificadoStatus.PendingValidation);

        // Run the rotation.
        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var sut = new CertificateRotationService(ctx, TestChainPolicy());
            await sut.RotateAsync(newCertId, pfxPassword: TestPassword);
        }

        // Verify from a fresh DbContext — proves the changes were
        // committed, not just sitting in the change tracker.
        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var oldRow = await verify.Certificados.AsNoTracking()
                .SingleAsync(c => c.Id == oldCertId);
            var newRow = await verify.Certificados.AsNoTracking()
                .SingleAsync(c => c.Id == newCertId);

            oldRow.Status.Should().Be(CertificadoStatus.Rotated,
                "the previously-active row must be ROTATED in PostgreSQL");
            newRow.Status.Should().Be(CertificadoStatus.Active,
                "the new row must be ACTIVE in PostgreSQL");

            var activeCount = await verify.Certificados
                .CountAsync(c => c.TenantId == tenantId
                              && c.Status == CertificadoStatus.Active);
            activeCount.Should().Be(1,
                "exactly one ACTIVE row must remain per tenant — SCN-CORE-08 invariant");
        }
    }

    [Fact]
    public async Task RotateAsync_appends_two_audit_rows_with_correct_actions_in_real_postgres()
    {
        // REQ-CORE-03: every fiscal mutation must produce an audit
        // row. Rotation produces two state changes (demotion +
        // activation) so we expect two audit entries.
        var tenantId = await CreateTenantAsync(_fixture.ConnectionString);

        var oldPfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);
        var newPfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);

        var oldCertId = Guid.CreateVersion7();
        var newCertId = Guid.CreateVersion7();
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantId, oldCertId, oldPfxPath,
            status: CertificadoStatus.Active);
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantId, newCertId, newPfxPath,
            status: CertificadoStatus.PendingValidation);

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var sut = new CertificateRotationService(ctx, TestChainPolicy());
            await sut.RotateAsync(newCertId, pfxPassword: TestPassword);
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var auditRows = await verify.AuditLog.AsNoTracking()
                .Where(a => a.TenantId == tenantId
                         && a.EntityType == "certificado"
                         && (a.EntityId == oldCertId || a.EntityId == newCertId))
                .OrderBy(a => a.OccurredAt)
                .ToListAsync();

            auditRows.Should().HaveCount(2);
            auditRows.Select(a => a.Action).Should().ContainInOrder(
                "CERTIFICATE_ROTATED_OUT",
                "CERTIFICATE_ROTATED_IN");
        }
    }

    [Fact]
    public async Task RotateAsync_respects_tenant_boundary_against_real_postgres()
    {
        // Rotation for tenant A MUST NOT touch any certificado owned
        // by tenant B, even though both rows live in the same table.
        // This proves the tenant_id filter on every read AND write.
        var tenantA = await CreateTenantAsync(_fixture.ConnectionString);
        var tenantB = await CreateTenantAsync(_fixture.ConnectionString);

        var aOldPfx = await CreateSelfSignedPfxAsync(validityDays: 365);
        var aNewPfx = await CreateSelfSignedPfxAsync(validityDays: 365);

        var aOldId = Guid.CreateVersion7();
        var aNewId = Guid.CreateVersion7();
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantA, aOldId, aOldPfx,
            status: CertificadoStatus.Active);
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantA, aNewId, aNewPfx,
            status: CertificadoStatus.PendingValidation);

        // Tenant B has its own ACTIVE cert that must stay ACTIVE.
        var bPfx = await CreateSelfSignedPfxAsync(validityDays: 365);
        var bActiveId = Guid.CreateVersion7();
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantB, bActiveId, bPfx,
            status: CertificadoStatus.Active);

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var sut = new CertificateRotationService(ctx, TestChainPolicy());
            await sut.RotateAsync(aNewId, pfxPassword: TestPassword);
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var bRow = await verify.Certificados.AsNoTracking()
                .SingleAsync(c => c.Id == bActiveId);
            bRow.Status.Should().Be(CertificadoStatus.Active,
                "tenant B's certificate must remain ACTIVE — the rotation was scoped to tenant A");
        }
    }

    [Fact]
    public async Task MarkExpiredAsync_persists_status_change_and_audit_row_in_real_postgres()
    {
        // The ResolucionLifecycleService.MarkExpiredAsync path is
        // simpler than rotation but must still produce an audit row
        // that lands in the database. We pair it here to keep the
        // cert rotation suite cohesive.
        var tenantId = await CreateTenantAsync(_fixture.ConnectionString);

        var resolucionId = Guid.CreateVersion7();
        var stkId = Guid.CreateVersion7();
        await using (var seed = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            seed.SoftwareTechnicalKeys.Add(new SoftwareTechnicalKey
            {
                Id = stkId,
                TenantId = tenantId,
                KeyValue = $"CRT-STK-{stkId:N}",
                IssuedByDian = true,
                Active = true,
                IssuedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1,
            });
            seed.Resoluciones.Add(new Resolucion
            {
                Id = resolucionId,
                TenantId = tenantId,
                DocumentType = DocumentType.DeePos,
                RangeStart = 1,
                RangeEnd = 100,
                CurrentNumber = 0,
                ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SoftwareTechnicalKeyId = stkId,
                Status = ResolucionStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1,
            });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var sut = new ResolucionLifecycleService(ctx);
            await sut.MarkExpiredAsync(resolucionId);
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var row = await verify.Resoluciones.AsNoTracking()
                .SingleAsync(r => r.Id == resolucionId);
            row.Status.Should().Be(ResolucionStatus.Expired,
                "the persisted row must reflect the Active → Expired transition");

            var auditRow = await verify.AuditLog.AsNoTracking()
                .SingleAsync(a => a.EntityId == resolucionId
                              && a.Action == "RESOLUTION_EXPIRED");
            auditRow.BeforeState.Should().Be("Active");
            auditRow.AfterState.Should().Be("Expired");
        }
    }

    [Fact]
    public async Task ValidateAndActivateAsync_persists_status_change_and_active_count_equals_one()
    {
        // The simpler activation path — single tenant, no prior
        // ACTIVE certificate. The end state must have exactly one
        // ACTIVE row.
        var tenantId = await CreateTenantAsync(_fixture.ConnectionString);
        var pfxPath = await CreateSelfSignedPfxAsync(validityDays: 365);
        var certId = Guid.CreateVersion7();
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantId, certId, pfxPath,
            status: CertificadoStatus.PendingValidation);

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var sut = new CertificateRotationService(ctx, TestChainPolicy());
            await sut.ValidateAndActivateAsync(certId, pfxPassword: TestPassword);
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var row = await verify.Certificados.AsNoTracking()
                .SingleAsync(c => c.Id == certId);
            row.Status.Should().Be(CertificadoStatus.Active);

            var activeCount = await verify.Certificados
                .CountAsync(c => c.TenantId == tenantId
                              && c.Status == CertificadoStatus.Active);
            activeCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task ValidateAndActivateAsync_throws_when_another_active_cert_exists_against_real_postgres()
    {
        // Cross-DbContext invariant: the "at most one ACTIVE per
        // tenant" rule holds even when the check and the activation
        // run in different DbContext lifecycles (which is exactly
        // what the dispatcher does in production).
        var tenantId = await CreateTenantAsync(_fixture.ConnectionString);
        var firstPfx = await CreateSelfSignedPfxAsync(validityDays: 365);
        var secondPfx = await CreateSelfSignedPfxAsync(validityDays: 365);

        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantId, firstId, firstPfx,
            status: CertificadoStatus.Active);
        await SeedCertificadoAsync(_fixture.ConnectionString, tenantId, secondId, secondPfx,
            status: CertificadoStatus.PendingValidation);

        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);
        var sut = new CertificateRotationService(ctx, TestChainPolicy());

        var act = async () => await sut.ValidateAndActivateAsync(secondId, pfxPassword: TestPassword);

        await act.Should().ThrowAsync<MultipleActiveCertificatesException>(
            "the invariant must be enforced against the live database, not just the change tracker");
    }

    // ===== Helpers =====================================================

    private const string TestPassword = "integration-pfx-password";

    private static X509ChainPolicy TestChainPolicy() => new()
    {
        RevocationMode = X509RevocationMode.NoCheck,
        VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority,
    };

    private static async Task<Guid> CreateTenantAsync(string connectionString)
    {
        var tenantId = Guid.CreateVersion7();
        var nit = $"CRT-{tenantId:N}".Substring(0, 20);

        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            connectionString);

        var exists = await ctx.Tenants.AnyAsync(t => t.Id == tenantId);
        if (exists)
        {
            return tenantId;
        }

        var now = DateTime.UtcNow;
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            LegalName = $"CertRotation Test {tenantId:N}",
            Nit = nit,
            SubscriptionTier = SubscriptionTier.Pro,
            SubscriptionStartedAt = now,
            Status = TenantStatus.Active,
            CloudTransmissionEnabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private static async Task SeedCertificadoAsync(
        string connectionString,
        Guid tenantId,
        Guid certificadoId,
        string pfxPath,
        CertificadoStatus status)
    {
        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            connectionString);
        var now = DateTime.UtcNow;
        ctx.Certificados.Add(new Certificado
        {
            Id = certificadoId,
            TenantId = tenantId,
            Subject = "CN=Cassam Integration Test",
            Issuer = "CN=Cassam Test CA",
            Serial = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            NotBefore = now.AddDays(-1),
            NotAfter = now.AddYears(1),
            PfxPath = pfxPath,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<string> CreateSelfSignedPfxAsync(int validityDays)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=Cassam Integration Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                critical: true));

        var notBefore = DateTime.UtcNow.AddDays(-1);
        var notAfter = DateTime.UtcNow.AddDays(validityDays);
        using var cert = req.CreateSelfSigned(notBefore, notAfter);

        var pfxBytes = cert.Export(X509ContentType.Pfx, TestPassword);
        var path = Path.Combine(Path.GetTempPath(), $"cassam-int-{Guid.NewGuid():N}.pfx");
        await File.WriteAllBytesAsync(path, pfxBytes);
        _tempFiles.Add(path);
        return path;
    }

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
