using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Integration;

/// <summary>
/// Real-PostgreSQL integration tests for <see cref="DocumentoElectronico"/>.
/// These verify the round-trip of the state machine's starting state
/// (<see cref="EstadoDocumento.Draft"/>), the snake_case column naming
/// the migration emits, the composite (tenant_id, estado, created_at)
/// index declared in <see cref="Cassam.Core.Persistence.CassamDbContext"/>
/// for transmission queue queries, and the self-reference
/// (<c>voided_by</c>) used by Nota Crédito flows.
///
/// <para>
/// PR 3 ships the read-side; the write-side enforcement (DD-01
/// invalid-transition trigger, SQL CHECK constraint) is verified in
/// <c>DocumentoElectronicoStateTransitionDbTests</c>.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class DocumentoElectronicoPersistenceTests
{
    private readonly PostgresContainerFixture _fixture;

    public DocumentoElectronicoPersistenceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Draft_DocumentoElectronico_round_trips_with_snake_case_columns()
    {
        var tenantId = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        var (resolucionId, certificadoId, stkId) = await CreateComplianceParentsAsync(
            tenantId, _fixture.ConnectionString);

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            ctx.DocumentosElectronicos.Add(new DocumentoElectronico
            {
                Id = docId,
                TenantId = tenantId,
                SaleId = null,
                ResolucionId = resolucionId,
                CertificadoId = certificadoId,
                SoftwareTechnicalKeyId = stkId,
                DocumentType = DocumentType.DeePos,
                Numero = 1,
                CufeOrCude = null,
                XmlPayload = "<xml>draft</xml>",
                SignatureXml = null,
                PdfPath = null,
                Estado = EstadoDocumento.Draft,
                VoidedBy = null,
                TransmittedAt = null,
                TransmittedResponseCode = null,
                TransmittedResponseMessage = null,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var saved = await verify.DocumentosElectronicos
                .AsNoTracking()
                .SingleAsync(d => d.Id == docId);

            saved.TenantId.Should().Be(tenantId);
            saved.DocumentType.Should().Be(DocumentType.DeePos);
            saved.Estado.Should().Be(EstadoDocumento.Draft);
            saved.Numero.Should().Be(1);
            saved.XmlPayload.Should().Be("<xml>draft</xml>");
            saved.TransmittedAt.Should().BeNull();
            saved.VoidedBy.Should().BeNull();
        }
    }

    [Fact]
    public async Task Composite_index_on_tenant_estado_created_at_exists()
    {
        // The transmission worker queries "oldest Draft document for
        // tenant T". The composite index
        // ix_documentos_electronicos_tenant_id_estado_created_at is
        // declared in CassamDbContext and should exist in the
        // physical schema.
        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        var indexNames = await ctx.Database
            .SqlQueryRaw<string>(
                "SELECT indexname AS \"Value\" FROM pg_indexes " +
                "WHERE tablename = 'documentos_electronicos'")
            .ToListAsync();

        indexNames.Should().Contain("ix_documentos_electronicos_tenant_id_estado_created_at",
            "the composite (tenant_id, estado, created_at) index is required by the transmission worker query");
    }

    [Fact]
    public async Task VoidedBy_self_reference_round_trips()
    {
        // The voided_by column is a self-reference to the documentos_electronicos
        // table: when a Nota Crédito voids a previously-transmitted
        // document, voided_by points to the NC's id. We model the
        // two-row pattern (DEE POS + NC that voids it) and confirm
        // the FK round-trips.
        var tenantId = Guid.CreateVersion7();
        var deePosId = Guid.CreateVersion7();
        var notaCreditoId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        var (resolucionId, certificadoId, stkId) = await CreateComplianceParentsAsync(
            tenantId, _fixture.ConnectionString);

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var deePos = new DocumentoElectronico
            {
                Id = deePosId,
                TenantId = tenantId,
                ResolucionId = resolucionId,
                CertificadoId = certificadoId,
                SoftwareTechnicalKeyId = stkId,
                DocumentType = DocumentType.DeePos,
                Numero = 1,
                XmlPayload = "<xml>dee-pos</xml>",
                Estado = EstadoDocumento.Transmitted,
                TransmittedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };

            var notaCredito = new DocumentoElectronico
            {
                Id = notaCreditoId,
                TenantId = tenantId,
                ResolucionId = resolucionId,
                CertificadoId = certificadoId,
                SoftwareTechnicalKeyId = stkId,
                DocumentType = DocumentType.NotaCredito,
                Numero = 2,
                XmlPayload = "<xml>nc</xml>",
                Estado = EstadoDocumento.Accepted,
                TransmittedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };

            // NC voids the DEE POS.
            deePos.VoidedBy = notaCreditoId;
            deePos.Estado = EstadoDocumento.Voided;

            ctx.DocumentosElectronicos.Add(deePos);
            ctx.DocumentosElectronicos.Add(notaCredito);
            await ctx.SaveChangesAsync();
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var deePos = await verify.DocumentosElectronicos
                .AsNoTracking()
                .SingleAsync(d => d.Id == deePosId);
            var notaCredito = await verify.DocumentosElectronicos
                .AsNoTracking()
                .SingleAsync(d => d.Id == notaCreditoId);

            deePos.VoidedBy.Should().Be(notaCreditoId,
                "the DEE POS row should remember which NC voided it");
            deePos.Estado.Should().Be(EstadoDocumento.Voided);
            notaCredito.Estado.Should().Be(EstadoDocumento.Accepted);
            notaCredito.VoidedBy.Should().BeNull(
                "the NC itself is not voided by anything in this scenario");
        }
    }

    /// <summary>
    /// Helper that inserts the three compliance parent rows
    /// (<see cref="Resolucion"/>, <see cref="Certificado"/>,
    /// <see cref="SoftwareTechnicalKey"/>) a
    /// <see cref="DocumentoElectronico"/> requires. The IDs are
    /// returned so the caller can use them as FKs. Two
    /// DocumentosElectronicos that need compliance parents can
    /// share the same Resolucion / Certificado / STK since the
    /// uniqueness constraints in the schema are per-tenant
    /// (Resolucion and Certificado) or tenant-id only (STK).
    /// </summary>
    /// <remarks>
    /// Also inserts a parent <see cref="Tenant"/> row when
    /// <paramref name="tenantId"/> is not already present in the
    /// database. The test fixture is shared across all tests in
    /// the <c>Postgres collection</c>, so a prior test may have
    /// already created the tenant; we look it up first and only
    /// insert when missing.
    /// </remarks>
    private static async Task<(Guid ResolucionId, Guid CertificadoId, Guid SoftwareTechnicalKeyId)>
        CreateComplianceParentsAsync(Guid tenantId, string connectionString)
    {
        var resolucionId = Guid.CreateVersion7();
        var certificadoId = Guid.CreateVersion7();
        var stkId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            connectionString);

        // Insert the parent tenant if it doesn't exist yet.
        var tenantExists = await ctx.Tenants.AnyAsync(t => t.Id == tenantId);
        if (!tenantExists)
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = tenantId,
                LegalName = $"ComplianceParent {tenantId:N}",
                // NIT column is varchar(20) — first 20 chars of "CMP-" + 32 hex chars.
                Nit = $"CMP-{tenantId:N}".Substring(0, 20),
                SubscriptionTier = SubscriptionTier.Free,
                SubscriptionStartedAt = now,
                Status = TenantStatus.Trial,
                CloudTransmissionEnabled = false,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
            await ctx.SaveChangesAsync();
        }

        // Software technical key first — resolucion references it.
        ctx.SoftwareTechnicalKeys.Add(new SoftwareTechnicalKey
        {
            Id = stkId,
            TenantId = tenantId,
            KeyValue = $"STK-{stkId:N}",
            IssuedByDian = true,
            Active = true,
            IssuedAt = now,
            DeactivatedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });

        ctx.Certificados.Add(new Certificado
        {
            Id = certificadoId,
            TenantId = tenantId,
            Subject = $"CN=test-{certificadoId:N}",
            Issuer = "CN=DIAN Test CA",
            Serial = certificadoId.ToString("N"),
            NotBefore = now.AddDays(-30),
            NotAfter = now.AddYears(1),
            PfxPath = null,
            CertStoreRef = null,
            Status = CertificadoStatus.Active,
            PasswordHash = null,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });

        ctx.Resoluciones.Add(new Resolucion
        {
            Id = resolucionId,
            TenantId = tenantId,
            DocumentType = DocumentType.DeePos,
            RangeStart = 1,
            RangeEnd = 1000,
            CurrentNumber = 0,
            ExpirationDate = DateOnly.FromDateTime(now.AddYears(2)),
            SoftwareTechnicalKeyId = stkId,
            Status = ResolucionStatus.Active,
            Prefix = "DEE",
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });

        await ctx.SaveChangesAsync();

        return (resolucionId, certificadoId, stkId);
    }
}
