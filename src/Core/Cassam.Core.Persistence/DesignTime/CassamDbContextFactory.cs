using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cassam.Core.Persistence.DesignTime;

/// <summary>
/// Design-time <see cref="IDesignTimeDbContextFactory{TContext}"/> for
/// <see cref="CassamDbContext"/>. This is the entry-point the
/// <c>dotnet-ef</c> CLI uses to construct a context when no
/// <c>Program.cs</c> / <c>Startup.cs</c> is wired up (e.g. when
/// generating migrations from the class library directly).
///
/// <para>
/// Connection-string resolution order:
/// <list type="number">
///   <item><c>CASSA_DESIGNTIME_CONN</c> environment variable (preferred).</item>
///   <item>Fallback <c>Host=localhost;Database=cassam_designtime;Username=postgres;Password=postgres</c>
///         — used only when the env var is absent. Suitable for a local
///         developer PostgreSQL; CI uses the env var to point at a
///         Testcontainers-managed instance.</item>
/// </list>
/// </para>
///
/// <para>
/// The factory is intentionally in the <c>Cassam.Core.Persistence</c>
/// project (not in a separate <c>DesignTime</c> project) so that
/// <c>dotnet ef migrations add</c> from the project root picks it up
/// automatically. The class is excluded from the runtime assembly's
/// public surface by being sealed and not referenced from any
/// non-design-time code path.
/// </para>
/// </summary>
public sealed class CassamDbContextFactory : IDesignTimeDbContextFactory<CassamDbContext>
{
    private const string DesignTimeConnectionEnvVar = "CASSA_DESIGNTIME_CONN";
    private const string FallbackConnectionString =
        "Host=localhost;Database=cassam_designtime;Username=postgres;Password=postgres";

    /// <inheritdoc />
    public CassamDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(DesignTimeConnectionEnvVar)
            ?? FallbackConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<CassamDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(CassamDbContextFactory).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();

        return new CassamDbContext(optionsBuilder.Options);
    }
}
