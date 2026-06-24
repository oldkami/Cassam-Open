using Testcontainers.MySql;
using Xunit;

namespace Cassam.Core.Tests.Infrastructure;

/// <summary>
/// xUnit <see cref="IAsyncLifetime"/> fixture that owns a single MySQL 8.0
/// Testcontainers instance for the duration of a test class. The fixture
/// is shared across tests in the same xUnit <c>[Collection]</c> via
/// <see cref="MySqlCollection"/>.
///
/// <para>
/// MySQL 8.0 is the lowest LTS line that ships the metadata the
/// <c>MySqlToPgMigrator</c> (T1.03) reads from
/// <c>information_schema</c>:
/// <list type="bullet">
///   <item><c>information_schema.tables</c> + <c>information_schema.columns</c>
///         with the <c>COLUMN_TYPE</c>, <c>IS_NULLABLE</c>, and
///         <c>COLUMN_DEFAULT</c> columns the schema mapper depends on.</item>
///   <item><c>utf8mb4_0900_ai_ci</c> collation for accurate column-type
///         mapping to PostgreSQL <c>text</c>.</item>
///   <item><c>utf8mb4</c> character set — required for the legacy
///         <c>Cassam</c> schema's Spanish text (NIT, dirección,
///         observaciones).</item>
/// </list>
/// </para>
///
/// <para>
/// The fixture deliberately does NOT initialize any schema — the test
/// class seeds its own tables so the round-trip test exercises a known
/// shape end to end (T1.03 SCN-CORE-04).
/// </para>
/// </summary>
public sealed class MySqlContainerFixture : IAsyncLifetime
{
    private MySqlContainer? _container;

    /// <summary>
    /// Connection string the test classes use to open a
    /// <c>MySqlConnection</c> against the running container. Null until
    /// <see cref="InitializeAsync"/> completes.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Convenience flag the migration step uses to skip the round-trip
    /// test when the container failed to start (so the test fails with a
    /// clear message rather than a confusing MySQL exception).
    /// </summary>
    public bool IsAvailable => _container is not null;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _container = new MySqlBuilder()
            .WithImage("mysql:8.0")
            .WithDatabase("cassam_legacy_test")
            .WithUsername("cassam_test")
            .WithPassword("cassam_test")
            .WithCleanUp(true)
            .WithLabel("cassam-test", "true")
            .Build();

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