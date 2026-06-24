using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Integration;

/// <summary>
/// Tests that exercise the SQL-side state-machine enforcement on
/// <see cref="DocumentoElectronico"/> per REQ-CORE-08 / SCN-CORE-09.
///
/// <para>
/// The C# side of the state machine is verified in
/// <c>DocumentoElectronicoStateMachineTests</c> (PR 2). These tests
/// prove the database also rejects invalid transitions — a
/// defense-in-depth guarantee so direct UPDATE statements
/// (ad-hoc DBA queries, migration scripts, future code paths that
/// bypass EF Core) cannot produce an invalid estado value or
/// transition pair.
/// </para>
///
/// <para>
/// The trigger function <c>documentos_estado_transition_check</c>
/// and the CHECK constraint <c>ck_documentos_estado_valid</c> were
/// emitted by the <c>DocumentoElectronicoEstadoCheck</c> migration.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class DocumentoElectronicoStateTransitionDbTests
{
    private readonly PostgresContainerFixture _fixture;

    public DocumentoElectronicoStateTransitionDbTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Inserting_with_invalid_estado_value_fails_CHECK_constraint()
    {
        // The CHECK constraint restricts the column to the 9
        // EstadoDocumento members. A raw INSERT with a bogus
        // value must fail. We bypass the DbContext (which would
        // also reject via HasConversion<string>()) and use
        // raw SQL to prove the SQL layer is doing the enforcement.
        var tenantId = await EnsureTenantAsync();

        // Create the parent rows required for FK validity (uses
        // its own connection under the hood).
        var (resolucionId, certificadoId, stkId) = await CreateComplianceParentsAsync(
            tenantId, _fixture.ConnectionString);

        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        var conn = ctx.Database.GetDbConnection();
        await conn.OpenAsync();

        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = $"SET app.current_tenant_id = '{tenantId}'";
            await setCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO documentos_electronicos
              (id, tenant_id, resolucion_id, certificado_id, software_technical_key_id,
               document_type, numero, xml_payload, estado,
               created_at, updated_at, version)
            VALUES
              ('{Guid.CreateVersion7()}', '{tenantId}', '{resolucionId}',
               '{certificadoId}', '{stkId}', 'DEE_POS', 1, '<xml/>', 'BOGUS_STATE',
               NOW(), NOW(), 1);
        ";

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var exception = await act.Should().ThrowAsync<Npgsql.PostgresException>();
        exception.Which.SqlState
            .Should().Be("23514",
                "the CHECK constraint violation surfaces as SQLSTATE 23514 (check_violation)");
    }

    [Fact]
    public async Task Invalid_transition_DRAFT_to_TRANSMITTED_is_rejected_by_trigger()
    {
        var tenantId = await EnsureTenantAsync();
        var (docId, conn) = await InsertDocAsync(tenantId, EstadoDocumento.Draft);

        await using (var cmd = conn.CreateCommand())
        {
            // Skip SIGNED — invalid per DD-01.
            cmd.CommandText = $"UPDATE documentos_electronicos SET estado = 'TRANSMITTED' WHERE id = '{docId}'";

            var act = async () => await cmd.ExecuteNonQueryAsync();
            var exception = await act.Should().ThrowAsync<Npgsql.PostgresException>();
            exception.Which.MessageText
                .Should().Contain("Invalid estado transition",
                    "the BEFORE UPDATE trigger must reject any non-DD-01 transition");
            exception.Which.MessageText
                .Should().Contain("DRAFT -> TRANSMITTED",
                    "the error must surface the offending pair so the caller can fix it");
        }

        await conn.CloseAsync();
    }

    [Fact]
    public async Task Invalid_transition_ACCEPTED_to_anything_is_rejected()
    {
        // ACCEPTED is a terminal state; any transition is invalid.
        var tenantId = await EnsureTenantAsync();
        var (docId, conn) = await InsertDocAsync(tenantId, EstadoDocumento.Accepted);

        foreach (var target in new[] { "DRAFT", "SIGNED", "QUEUED", "TRANSMITTED" })
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE documentos_electronicos SET estado = '{target}' WHERE id = '{docId}'";

            var act = async () => await cmd.ExecuteNonQueryAsync();
            var exception = await act.Should().ThrowAsync<Npgsql.PostgresException>();
            exception.Which.MessageText
                .Should().Contain($"ACCEPTED -> {target}",
                    $"the trigger must reject the terminal-to-any transition (target={target})");
        }

        await conn.CloseAsync();
    }

    [Fact]
    public async Task Valid_transition_DRAFT_to_SIGNED_succeeds()
    {
        var tenantId = await EnsureTenantAsync();
        var (docId, conn) = await InsertDocAsync(tenantId, EstadoDocumento.Draft);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"UPDATE documentos_electronicos SET estado = 'SIGNED' WHERE id = '{docId}'";
            await cmd.ExecuteNonQueryAsync();
        }

        // Verify the transition was applied.
        await using (var verify = conn.CreateCommand())
        {
            verify.CommandText = $"SELECT estado FROM documentos_electronicos WHERE id = '{docId}'";
            var newEstado = (string?)await verify.ExecuteScalarAsync();
            newEstado.Should().Be("SIGNED");
        }

        await conn.CloseAsync();
    }

    [Fact]
    public async Task Valid_transition_SIGNED_to_TRANSMITTED_succeeds()
    {
        var tenantId = await EnsureTenantAsync();
        var (docId, conn) = await InsertDocAsync(tenantId, EstadoDocumento.Signed);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"UPDATE documentos_electronicos SET estado = 'TRANSMITTED' WHERE id = '{docId}'";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var verify = conn.CreateCommand())
        {
            verify.CommandText = $"SELECT estado FROM documentos_electronicos WHERE id = '{docId}'";
            var newEstado = (string?)await verify.ExecuteScalarAsync();
            newEstado.Should().Be("TRANSMITTED");
        }

        await conn.CloseAsync();
    }

    [Fact]
    public async Task Valid_transition_TRANSMITTED_to_ACCEPTED_succeeds()
    {
        var tenantId = await EnsureTenantAsync();
        var (docId, conn) = await InsertDocAsync(tenantId, EstadoDocumento.Transmitted);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"UPDATE documentos_electronicos SET estado = 'ACCEPTED' WHERE id = '{docId}'";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var verify = conn.CreateCommand())
        {
            verify.CommandText = $"SELECT estado FROM documentos_electronicos WHERE id = '{docId}'";
            var newEstado = (string?)await verify.ExecuteScalarAsync();
            newEstado.Should().Be("ACCEPTED");
        }

        await conn.CloseAsync();
    }

    [Fact]
    public async Task No_op_update_same_estado_succeeds()
    {
        // The trigger has a WHEN clause that skips evaluation when
        // OLD.estado IS NOT DISTINCT FROM NEW.estado. A redundant
        // UPDATE (e.g. updating only updated_at) must not raise.
        var tenantId = await EnsureTenantAsync();
        var (docId, conn) = await InsertDocAsync(tenantId, EstadoDocumento.Draft);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"UPDATE documentos_electronicos SET estado = 'DRAFT' WHERE id = '{docId}'";
            // Should not throw — same-state UPDATE is a no-op for
            // the trigger.
            await cmd.ExecuteNonQueryAsync();
        }

        await conn.CloseAsync();
    }

    /// <summary>
    /// Inserts a DocumentoElectronico with the given estado and
    /// returns its id plus an OPEN Npgsql connection (the caller
    /// owns and must close the connection). The connection is
    /// independent of any DbContext so it survives the helper's
    /// using-scope disposal.
    /// </summary>
    private async Task<(Guid DocId, Npgsql.NpgsqlConnection Conn)> InsertDocAsync(
        Guid tenantId, EstadoDocumento initialEstado)
    {
        var (resolucionId, certificadoId, stkId) = await CreateComplianceParentsAsync(
            tenantId, _fixture.ConnectionString);

        var docId = Guid.CreateVersion7();
        var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Set the RLS context (not strictly required for
        // documentos_electronicos since the table has no RLS
        // policy in this migration — audit_log has the only one —
        // but doing it makes the test resilient to a future
        // migration adding RLS here too).
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = $"SET app.current_tenant_id = '{tenantId}'";
            await setCmd.ExecuteNonQueryAsync();
        }

        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.CommandText = $@"
                INSERT INTO documentos_electronicos
                  (id, tenant_id, resolucion_id, certificado_id, software_technical_key_id,
                   document_type, numero, xml_payload, estado,
                   created_at, updated_at, version)
                VALUES
                  ('{docId}', '{tenantId}', '{resolucionId}',
                   '{certificadoId}', '{stkId}', 'DEE_POS',
                   {Random.Shared.Next(1, 1000000)}, '<xml/>', '{initialEstado.ToString().ToUpperInvariant()}',
                   NOW(), NOW(), 1);
            ";
            await insertCmd.ExecuteNonQueryAsync();
        }

        return (docId, conn);
    }

    private async Task<(Guid ResolucionId, Guid CertificadoId, Guid SoftwareTechnicalKeyId)>
        CreateComplianceParentsAsync(Guid tenantId, Npgsql.NpgsqlConnection existingConn)
    {
        var resolucionId = Guid.CreateVersion7();
        var certificadoId = Guid.CreateVersion7();
        var stkId = Guid.CreateVersion7();

        // Use the existing connection (don't open another one).
        var now = DateTime.UtcNow;

        // Software technical key first — resolucion references it.
        await using (var stkCmd = existingConn.CreateCommand())
        {
            stkCmd.CommandText = $@"
                INSERT INTO software_technical_keys
                  (id, tenant_id, key_value, issued_by_dian, active,
                   issued_at, created_at, updated_at, version)
                VALUES
                  ('{stkId}', '{tenantId}', 'STK-{stkId:N}', TRUE, TRUE,
                   '{now:O}', '{now:O}', '{now:O}', 1)
                ON CONFLICT (id) DO NOTHING;
            ";
            await stkCmd.ExecuteNonQueryAsync();
        }

        await using (var certCmd = existingConn.CreateCommand())
        {
            certCmd.CommandText = $@"
                INSERT INTO certificados
                  (id, tenant_id, subject, issuer, serial,
                   not_before, not_after, status,
                   created_at, updated_at, version)
                VALUES
                  ('{certificadoId}', '{tenantId}', 'CN=test-{certificadoId:N}',
                   'CN=DIAN Test CA', '{certificadoId:N}',
                   '{now.AddDays(-30):O}', '{now.AddYears(1):O}', 'ACTIVE',
                   '{now:O}', '{now:O}', 1)
                ON CONFLICT (id) DO NOTHING;
            ";
            await certCmd.ExecuteNonQueryAsync();
        }

        await using (var resCmd = existingConn.CreateCommand())
        {
            resCmd.CommandText = $@"
                INSERT INTO resoluciones
                  (id, tenant_id, document_type, range_start, range_end,
                   current_number, expiration_date, software_technical_key_id,
                   status, prefix,
                   created_at, updated_at, version)
                VALUES
                  ('{resolucionId}', '{tenantId}', 'DEE_POS', 1, 1000000,
                   0, '{DateOnly.FromDateTime(now.AddYears(2)):O}', '{stkId}',
                   'ACTIVE', 'DEE',
                   '{now:O}', '{now:O}', 1)
                ON CONFLICT (id) DO NOTHING;
            ";
            await resCmd.ExecuteNonQueryAsync();
        }

        return (resolucionId, certificadoId, stkId);
    }

    private async Task<(Guid ResolucionId, Guid CertificadoId, Guid SoftwareTechnicalKeyId)>
        CreateComplianceParentsAsync(Guid tenantId, string connectionString)
    {
        var npgsqlConn = new Npgsql.NpgsqlConnection(connectionString);
        await npgsqlConn.OpenAsync();
        try
        {
            return await CreateComplianceParentsAsync(tenantId, npgsqlConn);
        }
        finally
        {
            await npgsqlConn.CloseAsync();
        }
    }

    /// <summary>
    /// Inserts a tenant row if one with <paramref name="tenantId"/>
    /// doesn't already exist, and returns the id. The
    /// DocumentoElectronico FK to tenants(id) requires a parent row
    /// to exist; the shared collection fixture means a prior test
    /// in this class may already have created the tenant.
    /// </summary>
    private async Task<Guid> EnsureTenantAsync()
    {
        var tenantId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        if (await ctx.Tenants.AnyAsync(t => t.Id == tenantId))
        {
            return tenantId;
        }

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            LegalName = $"Estado Check {tenantId:N}",
            Nit = $"EST-{tenantId:N}".Substring(0, 20),
            SubscriptionTier = SubscriptionTier.Free,
            SubscriptionStartedAt = now,
            Status = TenantStatus.Trial,
            CloudTransmissionEnabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });
        await ctx.SaveChangesAsync();
        return tenantId;
    }
}
