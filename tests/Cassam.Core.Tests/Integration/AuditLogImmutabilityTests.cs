using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Integration;

/// <summary>
/// Tests for the database-side enforcement of the audit_log
/// append-only contract (REQ-CORE-03 / SCN-CORE-03). The
/// application-layer half lives in
/// <see cref="Cassam.Core.Audit.AuditLogWriter"/>; this suite
/// exercises the migration's BEFORE UPDATE / BEFORE DELETE triggers
/// and the RLS tenant_isolation policy.
///
/// <para>
/// All tests set the <c>app.current_tenant_id</c> session variable
/// to the row's tenant_id before reading. This is the same pattern
/// the production ITenantContext impl (T3.02) will follow when
/// request-scoped connections are opened.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class AuditLogImmutabilityTests
{
    private readonly PostgresContainerFixture _fixture;

    public AuditLogImmutabilityTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Inserting_an_audit_row_persists_it()
    {
        var tenantId = await EnsureTenantAsync();
        var now = DateTime.UtcNow;

        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        ctx.AuditLog.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ActorUserId = Guid.CreateVersion7(),
            EntityType = "documento_electronico",
            EntityId = Guid.CreateVersion7(),
            Action = "TRANSITION_SIGNED",
            BeforeState = "DRAFT",
            AfterState = "SIGNED",
            IpAddress = "192.0.2.42",
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });
        await ctx.SaveChangesAsync();

        var count = await ctx.AuditLog.CountAsync();
        count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Update_on_audit_log_is_rejected_by_trigger()
    {
        var tenantId = await EnsureTenantAsync();
        var auditId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            ctx.AuditLog.Add(new AuditLog
            {
                Id = auditId,
                TenantId = tenantId,
                ActorUserId = Guid.CreateVersion7(),
                EntityType = "documento_electronico",
                EntityId = Guid.CreateVersion7(),
                Action = "TRANSITION_SIGNED",
                BeforeState = "DRAFT",
                AfterState = "SIGNED",
                IpAddress = null,
                OccurredAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
            await ctx.SaveChangesAsync();
        }

        // Attempt to UPDATE via raw SQL. The BEFORE UPDATE trigger
        // raises 'audit_log is append-only (SCN-CORE-03): UPDATE denied'.
        await using var ctx2 = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        var conn = ctx2.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE audit_log SET after_state = 'TAMPERED' WHERE id = '{auditId}'";

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var exception = await act.Should().ThrowAsync<Npgsql.PostgresException>();
        exception.Which.MessageText
            .Should().Contain("audit_log is append-only",
                "the BEFORE UPDATE trigger must reject any UPDATE attempt on audit_log");
    }

    [Fact]
    public async Task Delete_on_audit_log_is_rejected_by_trigger()
    {
        var tenantId = await EnsureTenantAsync();
        var auditId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            ctx.AuditLog.Add(new AuditLog
            {
                Id = auditId,
                TenantId = tenantId,
                ActorUserId = Guid.CreateVersion7(),
                EntityType = "documento_electronico",
                EntityId = Guid.CreateVersion7(),
                Action = "TRANSITION_ACCEPTED",
                BeforeState = "TRANSMITTED",
                AfterState = "ACCEPTED",
                IpAddress = null,
                OccurredAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        var conn = ctx2.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM audit_log WHERE id = '{auditId}'";

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var exception = await act.Should().ThrowAsync<Npgsql.PostgresException>();
        exception.Which.MessageText
            .Should().Contain("audit_log is append-only",
                "the BEFORE DELETE trigger must reject any DELETE attempt on audit_log");
    }

    [Fact]
    public async Task Multiple_audit_rows_for_same_entity_return_in_insertion_order()
    {
        // The audit log is the audit trail; reading it back must
        // return rows in the order they were written so a
        // compliance officer can reconstruct the timeline.
        var tenantId = await EnsureTenantAsync();
        var entityId = Guid.CreateVersion7();

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var baseTime = DateTime.UtcNow.AddMinutes(-10);
            for (int i = 0; i < 3; i++)
            {
                ctx.AuditLog.Add(new AuditLog
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    ActorUserId = Guid.CreateVersion7(),
                    EntityType = "documento_electronico",
                    EntityId = entityId,
                    Action = $"STEP_{i}",
                    BeforeState = i == 0 ? null : $"STATE_{i - 1}",
                    AfterState = $"STATE_{i}",
                    IpAddress = null,
                    OccurredAt = baseTime.AddMinutes(i),
                    CreatedAt = baseTime.AddMinutes(i),
                    UpdatedAt = baseTime.AddMinutes(i),
                    Version = 1,
                });
            }
            await ctx.SaveChangesAsync();
        }

        // Set the tenant session var so the RLS policy allows
        // the SELECT to return rows for this tenant.
        await using var readCtx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        var conn = readCtx.Database.GetDbConnection();
        await conn.OpenAsync();
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = $"SET app.current_tenant_id = '{tenantId}'";
            await setCmd.ExecuteNonQueryAsync();
        }

        var rows = await readCtx.AuditLog
            .Where(a => a.EntityId == entityId)
            .OrderBy(a => a.OccurredAt)
            .Select(a => a.Action)
            .ToListAsync();

        rows.Should().ContainInOrder("STEP_0", "STEP_1", "STEP_2");
    }

    [Fact]
    public async Task RLS_policy_isolates_audit_rows_by_tenant_session_variable()
    {
        // Set up two tenants with one audit row each. When the
        // session variable is set to tenant A's id, only A's
        // audit rows should be visible — RLS enforces cross-tenant
        // isolation at the database layer.
        //
        // The Testcontainers harness creates the cassam_test role
        // with BYPASSRLS (it's the superuser the entrypoint sets
        // up), so we need to create a separate non-superuser
        // "cassam_app" role for the RLS verification — production
        // deployments will use the same pattern (an app role that
        // does NOT have BYPASSRLS).
        var tenantA = await EnsureTenantAsync();
        var tenantB = await EnsureTenantAsync();
        var now = DateTime.UtcNow;

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            foreach (var t in new[] { tenantA, tenantB })
            {
                ctx.AuditLog.Add(new AuditLog
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = t,
                    ActorUserId = Guid.CreateVersion7(),
                    EntityType = "documento_electronico",
                    EntityId = Guid.CreateVersion7(),
                    Action = $"INSERT_FOR_{t:N}".Substring(0, 32),
                    BeforeState = null,
                    AfterState = "ACTIVE",
                    IpAddress = null,
                    OccurredAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1,
                });
            }
            await ctx.SaveChangesAsync();
        }

        // Create a non-superuser app role that does NOT bypass RLS.
        // We do this in the test rather than the migration because
        // the production deployment may use a different role name
        // (cassam_app, app_user, etc.); the migration only declares
        // the RLS policy and lets ops create the role.
        const string appRole = "cassam_app";
        await using (var setup = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var setupConn = setup.Database.GetDbConnection();
            await setupConn.OpenAsync();
            await using (var dropCmd = setupConn.CreateCommand())
            {
                // Drop if it exists from a previous test run.
                dropCmd.CommandText = $"DROP ROLE IF EXISTS {appRole}";
                await dropCmd.ExecuteNonQueryAsync();
            }
            await using (var createCmd = setupConn.CreateCommand())
            {
                // NOSUPERUSER + NOBYPASSRLS are explicit defaults
                // but spelled out for documentation.
                createCmd.CommandText =
                    $"CREATE ROLE {appRole} LOGIN PASSWORD 'cassam_app' NOSUPERUSER NOBYPASSRLS";
                await createCmd.ExecuteNonQueryAsync();
            }
            await using (var grantCmd = setupConn.CreateCommand())
            {
                grantCmd.CommandText =
                    $"GRANT CONNECT ON DATABASE cassam_test TO {appRole}; " +
                    $"GRANT USAGE ON SCHEMA public TO {appRole}; " +
                    $"GRANT SELECT, INSERT ON audit_log TO {appRole}; " +
                    $"GRANT SELECT ON tenants TO {appRole};";
                await grantCmd.ExecuteNonQueryAsync();
            }
            await setupConn.CloseAsync();
        }

        // Build a connection string for the cassam_app role.
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Username = appRole,
            Password = "cassam_app",
        };
        var appConnectionString = builder.ConnectionString;

        // Helper: run a function in the cassam_app context and
        // return the count of audit rows visible after applying
        // the RLS policy.
        async Task<int> VisibleCountForTenantAsync(Guid tenantId)
        {
            await using var conn = new Npgsql.NpgsqlConnection(appConnectionString);
            await conn.OpenAsync();

            await using (var setCmd = conn.CreateCommand())
            {
                setCmd.CommandText = $"SET app.current_tenant_id = '{tenantId}'";
                await setCmd.ExecuteNonQueryAsync();
            }

            await using var countCmd = conn.CreateCommand();
            countCmd.CommandText =
                "SELECT COUNT(*)::int FROM audit_log " +
                "WHERE tenant_id = ANY(@tenants)";
            countCmd.Parameters.AddWithValue("tenants", new[] { tenantA, tenantB });

            return (int)(await countCmd.ExecuteScalarAsync())!;
        }

        // As cassam_app with tenant A's session var: only A's row visible.
        var aCount = await VisibleCountForTenantAsync(tenantA);
        aCount.Should().Be(1,
            "RLS must scope SELECT to the session's current_tenant_id (cassam_app role)");

        // As cassam_app with tenant B's session var: only B's row visible.
        var bCount = await VisibleCountForTenantAsync(tenantB);
        bCount.Should().Be(1,
            "RLS must re-evaluate on each query, not cache the first tenant");

        // As cassam_app with NO tenant set: zero rows visible
        // (the CASE in the policy evaluates to FALSE for unset sessions).
        async Task<int> VisibleCountWithNoTenantAsync()
        {
            await using var conn = new Npgsql.NpgsqlConnection(appConnectionString);
            await conn.OpenAsync();
            await using var countCmd = conn.CreateCommand();
            countCmd.CommandText =
                "SELECT COUNT(*)::int FROM audit_log " +
                "WHERE tenant_id = ANY(@tenants)";
            countCmd.Parameters.AddWithValue("tenants", new[] { tenantA, tenantB });
            return (int)(await countCmd.ExecuteScalarAsync())!;
        }

        var noneCount = await VisibleCountWithNoTenantAsync();
        noneCount.Should().Be(0,
            "with app.current_tenant_id unset, the RLS policy USING clause's CASE evaluates to FALSE and matches nothing");
    }

    /// <summary>
    /// Inserts a tenant row if one with <paramref name="tenantId"/>
    /// doesn't already exist, and returns the id. The audit_log
    /// FK to tenants(id) requires a parent row to exist; the
    /// shared collection fixture means a prior test in this class
    /// may already have created the tenant.
    /// </summary>
    private async Task<Guid> EnsureTenantAsync()
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        if (await ctx.Tenants.AnyAsync(t => t.Id == tenantId))
        {
            return tenantId;
        }

        var now = DateTime.UtcNow;
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            LegalName = $"Audit Test {tenantId:N}",
            Nit = $"AUD-{tenantId:N}".Substring(0, 20),
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
