using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests;

/// <summary>
/// Smoke test for the Testcontainers harness. The goal is to fail fast
/// (before any real assertion tests run) if the Docker engine is
/// unavailable, the image pull fails, or the migrations do not apply.
/// These three preconditions block every other integration test, so a
/// single short suite here is cheaper than letting every test fail
/// with a confusing "connection refused" or "relation does not exist"
/// error.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PostgresHarnessSmokeTests
{
    private readonly PostgresContainerFixture _fixture;

    public PostgresHarnessSmokeTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Container_starts_and_exposes_a_connection_string()
    {
        _fixture.IsAvailable.Should().BeTrue(
            "the Testcontainers fixture should have a running container by the time tests run");
        _fixture.ConnectionString.Should().NotBeNullOrWhiteSpace(
            "the connection string is what the rest of the integration tests depend on");
    }

    [Fact]
    public async Task Migrations_apply_cleanly_to_a_fresh_database()
    {
        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        // Every entity table should exist after Migrate. The 14
        // tables here mirror the InitialCreate migration's CreateTable
        // declarations; if a new entity is added without a new
        // migration the smoke test will fail and force the author
        // to regenerate the snapshot.
        var tableNames = new[]
        {
            "tenants",
            "users",
            "products",
            "customers",
            "sales",
            "sale_line_items",
            "payments",
            "cash_sessions",
            "resoluciones",
            "certificados",
            "software_technical_keys",
            "documentos_electronicos",
            "contingency_queue",
            "audit_log",
        };

        foreach (var table in tableNames)
        {
            var exists = await ctx.Database
                .SqlQueryRaw<int>(
                    "SELECT 1 AS \"Value\" FROM information_schema.tables WHERE table_name = {0}",
                    table)
                .AnyAsync();
            exists.Should().BeTrue($"the migration must have created the {table} table");
        }
    }

    [Fact]
    public async Task EFMigrationsHistory_table_records_InitialCreate()
    {
        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        // The EFCore.NamingConventions extension normalizes the
        // __EFMigrationsHistory table AND its columns to snake_case
        // (table __efmigrationshistory, columns migration_id and
        // product_version). Query via information_schema to confirm
        // the table exists, then SELECT the migration id directly.
        var historyTableName = await ctx.Database
            .SqlQueryRaw<string>(
                "SELECT table_name AS \"Value\" FROM information_schema.tables " +
                "WHERE table_schema = current_schema() AND table_name ILIKE '%efmigrationshistory%'")
            .FirstOrDefaultAsync();

        historyTableName.Should().NotBeNullOrEmpty(
            "EF Core should have created its migration history table when MigrateAsync ran");

        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        // Quote the table name to preserve the case returned by
        // information_schema, and quote the column to be safe across
        // Npgsql version differences.
        cmd.CommandText = $"SELECT \"migration_id\" FROM \"{historyTableName}\"";
        var result = (string?)await cmd.ExecuteScalarAsync();
        await connection.CloseAsync();

        result.Should().NotBeNullOrEmpty(
            "the InitialCreate migration should be recorded as applied");
        result.Should().EndWith("_InitialCreate");
    }
}
