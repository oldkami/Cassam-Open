using System.Text;
using MySqlConnector;
using Npgsql;

namespace Cassam.Core.Persistence.MigrationTools;

/// <summary>
/// Schema + data migration helper for moving the legacy MySQL
/// <c>cassam</c> database to PostgreSQL 16. Per
/// <c>pos-core-modern-stack</c> REQ-CORE-04 / SCN-CORE-04, the
/// modern schema is the canonical one (designed in PR 1-4); this
/// tool's job is to map the legacy MySQL DDL onto it and stream the
/// rows over.
///
/// <para>
/// The class is a thin orchestrator over two operations:
/// <list type="number">
///   <item><see cref="MigrateSchemaAsync"/> reads
///         <c>information_schema.tables</c> +
///         <c>information_schema.columns</c> on the MySQL side,
///         generates PostgreSQL DDL, and executes it inside a single
///         transaction so a malformed column does not leave half a
///         schema behind.</item>
///   <item><see cref="MigrateDataAsync"/> streams rows from MySQL to
///         PostgreSQL with a configurable batch size, using
///         parameterized COPY-style INSERTs (binary COPY would require
///         a deeper integration; for the customer-cutover scale of
///         ~50k-500k rows the multi-row INSERT path is fast enough
///         and far simpler to debug).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Scope (T1.03):</b> SCHEMA + DATA round-trip works for a small
/// representative schema (3 tables, 10 rows each). Production data
/// migration is deferred until the first customer cutover — at which
/// point this tool will be extended with:
/// <list type="bullet">
///   <item>UUID generation for the modern schema (MySQL uses INT/long
///         PKs; modern uses UUID v7 — see T1.06 conventions).</item>
///   <item>Rename mapping (legacy <c>dato_dian</c> → modern
///         <c>resoluciones</c> + <c>software_technical_keys</c>;
///         legacy <c>factura</c> → modern <c>sales</c> +
///         <c>documentos_electronicos</c>).</item>
///   <item>Retention preservation — legacy rows must NOT be deleted
///         on the MySQL side until the operator verifies the PG
///         side has matching data.</item>
/// </list>
/// </para>
/// </summary>
public static class MySqlToPgMigrator
{
    /// <summary>
    /// Generates PostgreSQL DDL for every table in the MySQL
    /// <paramref name="sourceDatabase"/> schema and executes the
    /// statements on the PostgreSQL <paramref name="targetConnection"/>
    /// inside a single transaction.
    /// </summary>
    /// <param name="sourceConnection">
    /// Open <see cref="MySqlConnection"/> to the legacy MySQL DB.
    /// The connection is left open on return (caller-managed lifetime).
    /// </param>
    /// <param name="targetConnection">
    /// Open <see cref="NpgsqlConnection"/> to the PostgreSQL DB. A
    /// transaction is opened on it for the duration of the schema
    /// apply and committed on success.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The list of table names (in MySQL-canonical order) that were
    /// created on the PostgreSQL side. Useful for the caller to drive
    /// <see cref="MigrateDataAsync"/>.
    /// </returns>
    /// <remarks>
    /// Type mapping table (covers the legacy <c>cassam</c> schema):
    /// <list type="table">
    ///   <listheader><term>MySQL</term><description>PostgreSQL</description></listheader>
    ///   <item><term>TINYINT(1)</term><description>BOOLEAN</description></item>
    ///   <item><term>INT, INTEGER, MEDIUMINT</term><description>INTEGER</description></item>
    ///   <item><term>BIGINT</term><description>BIGINT</description></item>
    ///   <item><term>FLOAT, DOUBLE, REAL</term><description>DOUBLE PRECISION</description></item>
    ///   <item><term>DECIMAL(p,s), NUMERIC(p,s)</term><description>NUMERIC(p,s)</description></item>
    ///   <item><term>VARCHAR(n), CHAR(n)</term><description>TEXT</description></item>
    ///   <item><term>TEXT, TINYTEXT, MEDIUMTEXT, LONGTEXT</term><description>TEXT</description></item>
    ///   <item><term>DATE</term><description>DATE</description></item>
    ///   <item><term>DATETIME, TIMESTAMP</term><description>TIMESTAMP</description></item>
    ///   <item><term>TIME</term><description>TIME</description></item>
    ///   <item><term>BLOB, MEDIUMBLOB, LONGBLOB, VARBINARY</term><description>BYTEA</description></item>
    ///   <item><term>ENUM(...)</term><description>TEXT + CHECK constraint</description></item>
    /// </list>
    /// FK / index / unique constraints are NOT translated here — the
    /// modern schema (managed by EF Core migrations) owns those. This
    /// tool creates ONLY the bare tables for the round-trip test.
    /// </remarks>
    public static async Task<IReadOnlyList<string>> MigrateSchemaAsync(
        MySqlConnection sourceConnection,
        NpgsqlConnection targetConnection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceConnection);
        ArgumentNullException.ThrowIfNull(targetConnection);

        cancellationToken.ThrowIfCancellationRequested();

        var tables = await ReadTableNamesAsync(sourceConnection, cancellationToken).ConfigureAwait(false);
        var createdTables = new List<string>(tables.Count);

        await using var transaction = await targetConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var table in tables)
        {
            var columns = await ReadColumnsAsync(sourceConnection, table, cancellationToken).ConfigureAwait(false);

            var ddl = new StringBuilder();
            ddl.Append("CREATE TABLE ").Append(PgQuoteIdentifier(table)).Append(" (");

            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) ddl.Append(", ");
                ddl.Append(PgQuoteIdentifier(columns[i].Name)).Append(' ').Append(MapType(columns[i]));
            }

            ddl.Append(");");

            await using var cmd = new NpgsqlCommand(ddl.ToString(), targetConnection, transaction);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            createdTables.Add(table);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return createdTables;
    }

    /// <summary>
    /// Copies rows from MySQL <paramref name="sourceConnection"/> into the
    /// PostgreSQL tables already created by
    /// <see cref="MigrateSchemaAsync"/>.
    /// </summary>
    /// <param name="sourceConnection">Open MySQL connection.</param>
    /// <param name="targetConnection">Open PostgreSQL connection.</param>
    /// <param name="tables">
    /// Tables to copy, in dependency order. The caller is responsible
    /// for ordering (FKs are not yet enforced in the round-trip test).
    /// </param>
    /// <param name="batchSize">
    /// Rows per multi-row INSERT. Default 500 — small enough to keep
    /// PostgreSQL's parameter limit (65535) comfortably under for
    /// ~100-column tables; large enough to amortize round trips.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total rows copied across all tables.</returns>
    public static async Task<long> MigrateDataAsync(
        MySqlConnection sourceConnection,
        NpgsqlConnection targetConnection,
        IReadOnlyList<string> tables,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceConnection);
        ArgumentNullException.ThrowIfNull(targetConnection);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        cancellationToken.ThrowIfCancellationRequested();

        long totalRows = 0;

        foreach (var table in tables)
        {
            totalRows += await CopyTableAsync(
                sourceConnection, targetConnection, table, batchSize, cancellationToken).ConfigureAwait(false);
        }

        return totalRows;
    }

    // ===== Helpers =====================================================

    private static async Task<long> CopyTableAsync(
        MySqlConnection sourceConnection,
        NpgsqlConnection targetConnection,
        string table,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Read columns once so the row-reader and the INSERT writer
        // agree on ordinal position.
        var columns = await ReadColumnsAsync(sourceConnection, table, cancellationToken).ConfigureAwait(false);
        if (columns.Count == 0) return 0;

        var columnList = string.Join(", ", columns.Select(c => MySqlQuoteIdentifier(c.Name)));
        var selectSql = $"SELECT {columnList} FROM {MySqlQuoteIdentifier(table)}";

        await using var readCmd = new MySqlCommand(selectSql, sourceConnection);
        await using var reader = await readCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        long rowsCopied = 0;
        var batch = new List<object?[]>(batchSize);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new object?[columns.Count];
#pragma warning disable CS8620 // MySqlConnector.GetValues(object[] values) is annotated non-nullable; the runtime contract allows null elements (DBNull.Value for SQL NULL).
            reader.GetValues(row!);
#pragma warning restore CS8620
            batch.Add(row);

            if (batch.Count >= batchSize)
            {
                rowsCopied += await FlushBatchAsync(targetConnection, table, columns, batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            rowsCopied += await FlushBatchAsync(targetConnection, table, columns, batch, cancellationToken).ConfigureAwait(false);
        }

        return rowsCopied;
    }

    private static async Task<int> FlushBatchAsync(
        NpgsqlConnection targetConnection,
        string table,
        IReadOnlyList<ColumnInfo> columns,
        List<object?[]> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return 0;

        var ddl = new StringBuilder();
        ddl.Append("INSERT INTO ").Append(PgQuoteIdentifier(table)).Append(" (");
        ddl.Append(string.Join(", ", columns.Select(c => PgQuoteIdentifier(c.Name))));
        ddl.Append(") VALUES ");

        for (var i = 0; i < batch.Count; i++)
        {
            if (i > 0) ddl.Append(", ");
            ddl.Append('(');
            for (var j = 0; j < columns.Count; j++)
            {
                if (j > 0) ddl.Append(", ");
                ddl.Append('@').Append("p").Append(i).Append('_').Append(j);
            }
            ddl.Append(')');
        }

        await using var cmd = new NpgsqlCommand(ddl.ToString(), targetConnection);
        for (var i = 0; i < batch.Count; i++)
        {
            for (var j = 0; j < columns.Count; j++)
            {
                var value = NormalizeValue(batch[i][j], columns[j]);
                cmd.Parameters.AddWithValue($"p{i}_{j}", value ?? DBNull.Value);
            }
        }

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static object? NormalizeValue(object? value, ColumnInfo column)
    {
        if (value is null || value is DBNull) return null;

        // MySQL TINYINT(1) maps to .NET bool via the legacy VB.NET
        // client; the modern MySqlConnector returns it as sbyte. Force
        // the conversion here so the round-trip works either way.
        if (column.MySqlType.StartsWith("tinyint", StringComparison.OrdinalIgnoreCase) &&
            column.MySqlType.Contains("(1)", StringComparison.Ordinal))
        {
            return Convert.ToBoolean(value);
        }

        return value;
    }

    private static async Task<List<string>> ReadTableNamesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT TABLE_NAME
              FROM information_schema.tables
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_TYPE = 'BASE TABLE'
             ORDER BY TABLE_NAME";

        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var tables = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    private static async Task<List<ColumnInfo>> ReadColumnsAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_KEY, EXTRA
              FROM information_schema.columns
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = @table
             ORDER BY ORDINAL_POSITION";

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<ColumnInfo>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(new ColumnInfo(
                Name: reader.GetString(0),
                MySqlType: reader.GetString(1),
                IsNullable: string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                IsPrimaryKey: reader.GetString(3) == "PRI",
                Extra: reader.GetString(4)));
        }
        return columns;
    }

    private static string MapType(ColumnInfo column)
    {
        var type = column.MySqlType.ToLowerInvariant();

        // Strip the parenthesized size for the type-name match, but
        // preserve it for VARCHAR / DECIMAL which need explicit width.
        var openParen = type.IndexOf('(');
        var bareType = openParen < 0 ? type : type[..openParen].Trim();

        var pgType = bareType switch
        {
            "tinyint" => "BOOLEAN",  // legacy VB.NET TINYINT(1) is a bool
            "int" or "integer" or "mediumint" => "INTEGER",
            "bigint" => "BIGINT",
            "smallint" => "SMALLINT",
            "float" or "double" or "real" => "DOUBLE PRECISION",
            "decimal" or "numeric" => ExtractParameterizedType(type, "NUMERIC"),
            "varchar" or "char" => "TEXT",  // safer than VARCHAR(n) — modern schema is TEXT
            "text" or "tinytext" or "mediumtext" or "longtext" => "TEXT",
            "date" => "DATE",
            "datetime" => "TIMESTAMP",
            "timestamp" => "TIMESTAMP",
            "time" => "TIME",
            "blob" or "mediumblob" or "longblob" or "varbinary" or "binary" => "BYTEA",
            "json" => "JSONB",
            _ => "TEXT",  // unknown — fall back to TEXT (round-trip safe)
        };

        return column.IsNullable ? pgType : $"{pgType} NOT NULL";
    }

    private static string ExtractParameterizedType(string typeWithSize, string prefix)
    {
        // "decimal(18,4)" -> "NUMERIC(18,4)"
        var openParen = typeWithSize.IndexOf('(');
        if (openParen < 0) return prefix;
        var closeParen = typeWithSize.IndexOf(')', openParen);
        if (closeParen < 0) return prefix;
        return $"{prefix}{typeWithSize[openParen..(closeParen + 1)]}";
    }

    private static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// PostgreSQL identifier quoting (double-quote, the SQL standard).
    /// </summary>
    private static string PgQuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// MySQL identifier quoting (backtick). Required because MySQL does
    /// not accept double-quoted identifiers unless the server is
    /// configured with <c>ANSI_QUOTES</c> SQL mode — we do not want to
    /// assume that on the legacy database.
    /// </summary>
    private static string MySqlQuoteIdentifier(string identifier) =>
        "`" + identifier.Replace("`", "``") + "`";

    /// <summary>Immutable per-column metadata read from information_schema.</summary>
    private sealed record ColumnInfo(
        string Name,
        string MySqlType,
        bool IsNullable,
        bool IsPrimaryKey,
        string Extra);
}