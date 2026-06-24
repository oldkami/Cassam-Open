using Cassam.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Infrastructure;

/// <summary>
/// Factory for <see cref="CassamDbContext"/> instances that point at the
/// <see cref="PostgresContainerFixture"/> connection string. Centralizing
/// the configuration keeps the per-test setup short and consistent
/// (snake_case naming, Npgsql provider, Migrations assembly pinned to
/// <c>Cassam.Core.Persistence</c>) so a test never has to re-declare
/// any of those knobs.
///
/// <para>
/// Each call to <see cref="CreateContext"/> returns a NEW context
/// instance — a deliberate choice. EF Core's change tracker is not
/// thread-safe, and xUnit runs tests from a single class in parallel
/// by default. Handing out a fresh context per test (or per
/// <c>await using</c> block) keeps every test isolated.
/// </para>
///
/// <para>
/// Migrations are applied lazily on the first
/// <see cref="DbContext.Database"/> access via
/// <see cref="DatabaseFacadeExtensions.MigrateAsync"/>. This avoids
/// forcing every test to remember to call
/// <c>await dbContext.Database.MigrateAsync()</c> in its arrange
/// phase. The trade-off: a test that touches the database pays a
/// one-time migration cost on the very first call after the
/// container starts. After the first apply, EF Core's
/// <c>__EFMigrationsHistory</c> table records the work and subsequent
/// calls are no-ops.
/// </para>
/// </summary>
public static class CassamDbContextFactory
{
    /// <summary>
    /// Builds a fresh <see cref="CassamDbContext"/> wired to the
    /// Testcontainers PostgreSQL instance.
    /// </summary>
    /// <param name="connectionString">
    /// The connection string the fixture exposes. Pass it in rather
    /// than capturing it from the fixture so the factory stays
    /// purely functional and is easy to mock in unit tests of the
    /// factory itself.
    /// </param>
    /// <returns>A new, unconfigured <see cref="CassamDbContext"/>.</returns>
    public static CassamDbContext CreateContext(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new DbContextOptionsBuilder<CassamDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(CassamDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CassamDbContext(options);
    }

    /// <summary>
    /// Convenience helper: builds a context, opens the connection,
    /// and applies pending migrations. Returns the open context so
    /// the caller can immediately <c>await dbContext.SomeSet.AddAsync(...)</c>.
    /// </summary>
    public static async Task<CassamDbContext> CreateMigratedContextAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(connectionString);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        return context;
    }
}
