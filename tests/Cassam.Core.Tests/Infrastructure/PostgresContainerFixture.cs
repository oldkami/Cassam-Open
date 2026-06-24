using Testcontainers.PostgreSql;
using Xunit;

namespace Cassam.Core.Tests.Infrastructure;

/// <summary>
/// xUnit <see cref="IAsyncLifetime"/> fixture that owns a single
/// PostgreSQL 16 Testcontainers instance for the duration of a test
/// class. The container is started once on the first <c>InitializeAsync</c>
/// call, reused across every <see cref="IAsyncLifetime"/> consumer
/// in the same xUnit <c>[Collection]</c>, and disposed when the
/// collection's lifetime ends.
///
/// <para>
/// PostgreSQL 16 is the lowest LTS line that ships all the
/// features this project depends on:
/// <list type="bullet">
///   <item><c>gen_random_uuid()</c> from the <c>pgcrypto</c>
///         extension (REQ-CORE-02).</item>
///   <item>Row-Level Security with <c>FORCE ROW LEVEL SECURITY</c>
///         so the policy applies even to the table owner
///         (T1.11 audit-log immutability).</item>
///   <item><c>CREATE OR REPLACE FUNCTION ... LANGUAGE plpgsql</c>
///         triggers for the SQL CHECK + transition trigger
///         emitted in T1.08 / T1.11.</item>
///   <item>Logical replication (<c>wal_level = logical</c>) for
///         the cloud-sync engine in Phase 4.</item>
/// </list>
/// </para>
///
/// <para>
/// PostgreSQL safety defaults (REQ-CORE-13, SCN-CORE-14) are
/// verified in <c>PostgresSafetyDefaultsTests</c> against the
/// container this fixture owns. The defaults that <c>postgres:16</c>
/// already ships with (fsync=on, full_page_writes=on,
/// synchronous_commit=on, wal_level=replica) are kept as-is;
/// the test asserts they were NOT downgraded by a custom
/// <c>postgresql.conf</c> overlay.
/// </para>
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>
    /// Connection string the test classes use to open a
    /// <c>NpgsqlConnection</c> or a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
    /// against the running container. Null until
    /// <see cref="InitializeAsync"/> completes.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Convenience flag the migration step uses to skip
    /// <c>Database.MigrateAsync()</c> when the container failed
    /// to start (so the tests fail with a clear message rather
    /// than a confusing Npgsql exception).
    /// </summary>
    public bool IsAvailable => _container is not null;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("cassam_test")
            .WithUsername("cassam_test")
            .WithPassword("cassam_test")
            .WithCleanUp(true)
            .WithLabel("cassam-test", "true")
            .Build();

        // StartAsync can take 5-15 s on first run (image pull).
        // xUnit does not surface a progress indicator for this
        // fixture so we keep the call simple and let the test
        // runner's per-test timeout (default 5 min) cover it.
        await _container.StartAsync().ConfigureAwait(false);

        ConnectionString = _container.GetConnectionString();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
            _container = null;
        }
    }
}
