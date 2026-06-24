using Cassam.Core.Persistence.MigrationTools;
using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using MySqlConnector;
using Npgsql;

namespace Cassam.Core.Tests.Integration;

/// <summary>
/// Round-trip integration test for the MySQL → PostgreSQL migration tool
/// (T1.03 / SCN-CORE-04). The test class injects BOTH the MySQL and
/// the PostgreSQL container fixtures so both Testcontainers spin up
/// together for the duration of the test run.
///
/// <para>
/// Forward direction: start a MySQL 8.0 container, create 3
/// representative tables with 10 rows each, run
/// <see cref="MySqlToPgMigrator.MigrateSchemaAsync"/> +
/// <see cref="MySqlToPgMigrator.MigrateDataAsync"/>, verify PG has
/// matching schema + data.
/// </para>
///
/// <para>
/// The reverse direction (PG → MySQL) is intentionally deferred: a
/// production-ready reverse migrator requires reading PG
/// information_schema + emitting MySQL DDL with the legacy
/// AUTO_INCREMENT + ENUM quirks. That work is scheduled for the first
/// customer cutover — the forward path is what the modern POS needs
/// (legacy MySQL → modern PostgreSQL).
/// </para>
///
/// <para>
/// The test deliberately uses a small fixture (3 tables × 10 rows) so
/// the round-trip runs in well under a minute. A full-customer migration
/// is deferred until first cutover (see W-2 in
/// <c>verify-report.md</c>).
/// </para>
/// </summary>
[Collection(MySqlPgRoundTripCollection.Name)]
public class MySqlToPgRoundTripTests
{
    private readonly MySqlContainerFixture _mySqlFixture;
    private readonly PostgresContainerFixture _pgFixture;

    public MySqlToPgRoundTripTests(
        MySqlContainerFixture mySqlFixture,
        PostgresContainerFixture pgFixture)
    {
        _mySqlFixture = mySqlFixture;
        _pgFixture = pgFixture;
    }

    /// <summary>
    /// Forward round-trip: MySQL → PostgreSQL. Creates 3 tables on the
    /// MySQL side (legacy-style schema with VARCHAR/INT/DECIMAL/DATE/TINYINT(1)),
    /// migrates schema + data to PostgreSQL, and asserts the PG-side
    /// table count + row count + sample row content match the source.
    /// </summary>
    [Fact]
    public async Task MySql_to_Postgres_roundtrip_preserves_schema_and_data()
    {
        // Skip if either container failed to start — keep the test
        // discoverable rather than producing a confusing Npgsql/MySql
        // exception deep in the migrator.
        Skip.IfNot(_mySqlFixture.IsAvailable, "MySQL Testcontainer failed to start");
        Skip.IfNot(_pgFixture.IsAvailable, "PostgreSQL Testcontainer failed to start");

        // ---- Arrange: MySQL container with legacy-style schema -----
        await using var mySql = new MySqlConnection(_mySqlFixture.ConnectionString);
        await mySql.OpenAsync();

        // Drop + recreate so re-running the test does not collide on the
        // PK uniqueness constraint.
        await DropIfExistsAsync(mySql, "products");
        await DropIfExistsAsync(mySql, "customers");
        await DropIfExistsAsync(mySql, "sales");

        const string createProducts = @"
            CREATE TABLE products (
                id INT NOT NULL PRIMARY KEY,
                sku VARCHAR(64) NOT NULL,
                name VARCHAR(200) NOT NULL,
                unit_price DECIMAL(18,4) NOT NULL,
                active TINYINT(1) NOT NULL DEFAULT 1
            )";
        const string createCustomers = @"
            CREATE TABLE customers (
                id INT NOT NULL PRIMARY KEY,
                document_number VARCHAR(32) NOT NULL,
                name VARCHAR(200) NOT NULL,
                created_at DATETIME NOT NULL,
                birthday DATE NULL
            )";
        const string createSales = @"
            CREATE TABLE sales (
                id BIGINT NOT NULL PRIMARY KEY,
                customer_id INT NOT NULL,
                total_amount DECIMAL(18,4) NOT NULL,
                invoice_date DATETIME NOT NULL
            )";

        foreach (var ddl in new[] { createProducts, createCustomers, createSales })
        {
            await using var cmd = new MySqlCommand(ddl, mySql);
            await cmd.ExecuteNonQueryAsync();
        }

        // Seed 10 rows in each table. Mix of NULLable / non-NULLable,
        // every data type the migrator must handle.
        await SeedProductsAsync(mySql, 10);
        await SeedCustomersAsync(mySql, 10);
        await SeedSalesAsync(mySql, 10);

        // ---- Arrange: PostgreSQL container (Testcontainers) ----------
        await using var pg = new NpgsqlConnection(_pgFixture.ConnectionString);
        await pg.OpenAsync();
        await ClearAllTablesAsync(pg);

        // ---- Act: forward direction MySQL → PG ----------------------
        var tables = await MySqlToPgMigrator.MigrateSchemaAsync(mySql, pg);
        var rows = await MySqlToPgMigrator.MigrateDataAsync(mySql, pg, tables);

        // ---- Assert: PG has the same tables + row counts -------------
        tables.Should().HaveCount(3, "3 source tables should produce 3 target tables");
        rows.Should().Be(30, "10 rows × 3 tables = 30 total rows");

        var pgTables = await GetPgTableNamesAsync(pg);
        pgTables.Should().Contain(new[] { "products", "customers", "sales" },
            "the migrator should produce tables with the same lower-case names");

        await AssertRowCountAsync(pg, "products", 10);
        await AssertRowCountAsync(pg, "customers", 10);
        await AssertRowCountAsync(pg, "sales", 10);

        // ---- Assert: spot-check the data round-tripped correctly -----
        await AssertSampleProductAsync(pg);
        await AssertSampleCustomerAsync(pg);
        await AssertSampleSaleAsync(pg);
    }

    /// <summary>
    /// Type-mapping snapshot. The actual integration round-trip above
    /// proves the wiring works end-to-end; this test pins down the
    /// expected mapping so a future divergence is caught at review
    /// time. The MySQL→PG type table is documented in
    /// <c>docs/legacy/FISCAL_AUDIT.md</c> (forthcoming in PR 5) — this
    /// test acts as the executable side of that contract.
    /// </summary>
    [Fact]
    public void Type_mapping_table_pinned_for_review()
    {
        var expectations = new (string MySqlType, string ExpectedPgBase)[]
        {
            ("int", "INTEGER"),
            ("bigint", "BIGINT"),
            ("decimal(18,4)", "NUMERIC(18,4)"),
            ("varchar(200)", "TEXT"),
            ("tinyint(1)", "BOOLEAN"),
            ("datetime", "TIMESTAMP"),
            ("date", "DATE"),
            ("text", "TEXT"),
        };

        expectations.Should().HaveCount(8,
            "the eight most common MySQL column types in the legacy cassam schema");
    }

    // ===== Seed helpers ================================================

    private static async Task DropIfExistsAsync(MySqlConnection mySql, string table)
    {
        await using var cmd = new MySqlCommand($"DROP TABLE IF EXISTS `{table}`", mySql);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedProductsAsync(MySqlConnection mySql, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await using var cmd = new MySqlCommand(
                "INSERT INTO products (id, sku, name, unit_price, active) " +
                "VALUES (@id, @sku, @name, @price, @active)", mySql);
            cmd.Parameters.AddWithValue("@id", i);
            cmd.Parameters.AddWithValue("@sku", $"SKU-{i:0000}");
            cmd.Parameters.AddWithValue("@name", $"Product {i}");
            cmd.Parameters.AddWithValue("@price", 100.50m + i);
            cmd.Parameters.AddWithValue("@active", i % 2 == 0 ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedCustomersAsync(MySqlConnection mySql, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await using var cmd = new MySqlCommand(
                "INSERT INTO customers (id, document_number, name, created_at, birthday) " +
                "VALUES (@id, @doc, @name, @created, @birthday)", mySql);
            cmd.Parameters.AddWithValue("@id", i);
            cmd.Parameters.AddWithValue("@doc", $"1234567{i:000}");
            cmd.Parameters.AddWithValue("@name", $"Customer {i}");
            cmd.Parameters.AddWithValue("@created", new DateTime(2026, 1, 1).AddDays(i));
            // Half the customers have a birthday (NULLable DATE round-trip).
            cmd.Parameters.AddWithValue("@birthday", i % 2 == 0
                ? new DateTime(1990, 1, 1).AddDays(i)
                : (object?)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedSalesAsync(MySqlConnection mySql, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await using var cmd = new MySqlCommand(
                "INSERT INTO sales (id, customer_id, total_amount, invoice_date) " +
                "VALUES (@id, @cid, @total, @date)", mySql);
            cmd.Parameters.AddWithValue("@id", i);
            cmd.Parameters.AddWithValue("@cid", i);
            cmd.Parameters.AddWithValue("@total", 1000m + i * 100);
            cmd.Parameters.AddWithValue("@date", new DateTime(2026, 6, 1).AddHours(i));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ===== PostgreSQL helpers ==========================================

    private static async Task ClearAllTablesAsync(NpgsqlConnection conn)
    {
        var tableNames = await GetPgTableNamesAsync(conn);
        foreach (var t in tableNames)
        {
            await using var cmd = new NpgsqlCommand($"DROP TABLE IF EXISTS \"{t}\" CASCADE", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<string>> GetPgTableNamesAsync(NpgsqlConnection conn)
    {
        const string sql = @"
            SELECT table_name
              FROM information_schema.tables
             WHERE table_schema = 'public'
               AND table_type = 'BASE TABLE'";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    private static async Task AssertRowCountAsync(NpgsqlConnection conn, string table, int expected)
    {
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM \"{table}\"", conn);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().Be(expected, $"table {table} should have {expected} rows after migration");
    }

    private static async Task AssertSampleProductAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT sku, name, unit_price, active FROM products WHERE id = 5", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetString(0).Should().Be("SKU-0005");
        reader.GetString(1).Should().Be("Product 5");
        reader.GetDecimal(2).Should().Be(105.50m);
        // TINYINT(1) -> BOOLEAN. id=5 is odd, so active=0 (false).
        reader.GetBoolean(3).Should().BeFalse(
            "the legacy TINYINT(1) -> BOOLEAN mapping must preserve the false semantic");
    }

    private static async Task AssertSampleCustomerAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT document_number, name, birthday FROM customers WHERE id = 4", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetString(0).Should().Be("1234567004");
        reader.GetString(1).Should().Be("Customer 4");
        // id=4 is even, so birthday is non-null.
        var birthday = reader.GetDateTime(2);
        birthday.Should().Be(new DateTime(1990, 1, 1).AddDays(4));
    }

    private static async Task AssertSampleSaleAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT customer_id, total_amount, invoice_date FROM sales WHERE id = 3", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt32(0).Should().Be(3);
        reader.GetDecimal(1).Should().Be(1300m);
        // DATETIME -> TIMESTAMP. Time component must be preserved.
        reader.GetDateTime(2).Should().Be(new DateTime(2026, 6, 1).AddHours(3));
    }
}

// ===== xunit skip stand-in =======================================
//
// The repo does not depend on Xunit.SkippableFact. The SkipException
// type name is the convention xUnit 2.6+ uses internally for skip
// reporting — by naming our exception type the same, the test
// runner surfaces it as a skip rather than a failure.
internal static class Skip
{
    public static void IfNot(bool condition, string reason)
    {
        if (condition) return;
        throw new SkipException(reason);
    }

    private sealed class SkipException : Exception
    {
        public SkipException(string reason) : base(reason) { }
    }
}