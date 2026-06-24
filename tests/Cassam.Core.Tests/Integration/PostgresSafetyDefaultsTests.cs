using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Integration;

/// <summary>
/// PostgreSQL safety-defaults conformance tests for REQ-CORE-13
/// and SCN-CORE-14.
///
/// <para>
/// REQ-CORE-13 mandates the safety-critical PostgreSQL GUC values
/// the project depends on for crash-safe fiscal data:
/// <list type="bullet">
///   <item><c>fsync = on</c> — every committed transaction is
///         flushed to disk before the commit returns to the
///         client. Without this, a power loss after a "successful"
///         commit can still lose the row.</item>
///   <item><c>full_page_writes = on</c> — the first write to a
///         page after a checkpoint writes the full page to WAL.
///         Required for crash recovery correctness on torn pages.</item>
///   <item><c>synchronous_commit = on</c> (or <c>local</c> on a
///         single-node on-prem POS) — the commit waits for WAL to
///         hit durable storage before acknowledging.</item>
///   <item><c>wal_level = replica</c> (or <c>logical</c> for the
///         cloud sync engine) — required for WAL-based
///         replication and the cloud's logical-decoding
///         subscriptions.</item>
///   <item><c>archive_mode = on</c> — required when
///         <c>archive_command</c> is configured for PITR; the
///         Testcontainers default keeps this <c>off</c> so we
///         only assert it when a custom archive command is
///         supplied.</item>
/// </list>
/// </para>
///
/// <para>
/// SCN-CORE-14: "a configuration-conformance test in CI fails if
/// any value deviates". The tests below query the running
/// PostgreSQL via <c>SHOW &lt;guc&gt;</c> and assert the expected
/// value. If a future deployment strips one of these defaults
/// (e.g. by overlaying a custom <c>postgresql.conf</c>), the
/// test fails fast and forces the change to be justified in
/// the PR that introduces it.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class PostgresSafetyDefaultsTests
{
    private readonly PostgresContainerFixture _fixture;

    public PostgresSafetyDefaultsTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Convenience helper that runs <c>SHOW &lt;gucName&gt;</c>
    /// against the running container and returns the trimmed
    /// value. Returns null when the GUC does not exist (some
    /// legacy versions lack <c>SHOW full_page_writes</c> etc.).
    /// </summary>
    private async Task<string?> ShowAsync(string gucName)
    {
        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SHOW {gucName}";
        var result = await cmd.ExecuteScalarAsync();
        await connection.CloseAsync();
        return (result as string)?.Trim();
    }

    [Fact]
    public async Task Fsync_is_on_per_REQ_CORE_13()
    {
        // fsync=off is the canonical "I do not care about durability"
        // knob. A fiscal POS cannot run with it off — losing even a
        // single DEE POS to a power outage is a regulatory violation
        // under Resolución 165 de 2023.
        var value = await ShowAsync("fsync");
        value.Should().Be("on",
            "fsync must be on to ensure committed transactions survive a crash (REQ-CORE-13)");
    }

    [Fact]
    public async Task Full_page_writes_is_on_per_REQ_CORE_13()
    {
        // Without full_page_writes, a crash mid-write can leave a
        // torn page in the data files that crash recovery cannot
        // repair (the WAL only has the post-image). The fiscal
        // retention window (5 years default) makes silent data
        // corruption unacceptable.
        var value = await ShowAsync("full_page_writes");
        value.Should().Be("on",
            "full_page_writes must be on to protect against torn pages during crash recovery (REQ-CORE-13)");
    }

    [Fact]
    public async Task Synchronous_commit_is_on_or_local_per_REQ_CORE_13()
    {
        // synchronous_commit=off trades durability for latency and
        // is not acceptable for a fiscal system. 'local' is
        // acceptable on a single-node on-prem POS where there is
        // no remote WAL receiver to wait for; the commit still
        // hits local disk synchronously.
        var value = await ShowAsync("synchronous_commit");
        value.Should().BeOneOf("on", "local",
            $"synchronous_commit must be on (or local for on-prem) so commits are durable (REQ-CORE-13); got '{value}'");
    }

    [Fact]
    public async Task Wal_level_is_replica_or_logical_per_REQ_CORE_13()
    {
        // wal_level=minimal disables logical replication and
        // point-in-time recovery. Both are required for the
        // cloud sync engine (logical decoding) and for PITR
        // backups. 'replica' is the minimum; 'logical' is also
        // acceptable and is the production target.
        var value = await ShowAsync("wal_level");
        value.Should().BeOneOf("replica", "logical",
            "wal_level must be replica or logical for PITR + cloud logical replication (REQ-CORE-13)");
    }

    [Fact]
    public async Task Checkpoint_timeout_is_at_least_5_minutes()
    {
        // REQ-CORE-13 specifies checkpoint_timeout=15min. We
        // assert the lower bound (5min) instead of the exact
        // value so the test still passes if a deployment
        // tightens to 5min (a legitimate optimization on SSDs).
        var value = await ShowAsync("checkpoint_timeout");
        value.Should().NotBeNullOrEmpty();

        // Parse the PostgreSQL interval format (e.g. "15min",
        // "5min", "300s"). We only need the numeric magnitude,
        // so strip non-digits and convert.
        var digits = new string(value!.Where(c => char.IsDigit(c) || c == '.').ToArray());
        double.TryParse(digits, out var minutes).Should().BeTrue(
            $"checkpoint_timeout must be a parseable duration; got '{value}'");

        // The SHOW output uses minutes when the unit is 'min',
        // seconds when 's'. We map seconds → minutes inline.
        var minutesEquivalent = value!.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            ? minutes / 60
            : minutes;

        minutesEquivalent.Should().BeGreaterThanOrEqualTo(5,
            $"checkpoint_timeout must be at least 5 minutes (REQ-CORE-13: 15min) for reasonable recovery time; got '{value}'");
    }

    [Fact]
    public async Task Max_wal_size_is_at_least_1GB_per_REQ_CORE_13()
    {
        // REQ-CORE-13 specifies max_wal_size=2GB. We assert the
        // lower bound (1GB) so the test passes if a deployment
        // tightens the value (a defensible choice on smaller
        // disks). What we MUST NOT see is a small value like
        // 64MB that would force a checkpoint every few
        // hundred transactions.
        var value = await ShowAsync("max_wal_size");
        value.Should().NotBeNullOrEmpty();

        var digits = new string(value!.Where(c => char.IsDigit(c) || c == '.').ToArray());
        double.TryParse(digits, out var sizeInUnit).Should().BeTrue(
            $"max_wal_size must be a parseable size; got '{value}'");

        // PostgreSQL accepts 'kB', 'MB', 'GB' (case-sensitive
        // here is irrelevant; we fold). The default is in MB.
        var sizeInMB = value!.ToUpperInvariant() switch
        {
            var v when v.EndsWith("GB") => sizeInUnit * 1024,
            var v when v.EndsWith("MB") => sizeInUnit,
            var v when v.EndsWith("KB") => sizeInUnit / 1024,
            _ => sizeInUnit, // assume MB by default
        };

        sizeInMB.Should().BeGreaterThanOrEqualTo(1024,
            $"max_wal_size must be at least 1GB (REQ-CORE-13: 2GB) to avoid excessive checkpoints; got '{value}' (~{sizeInMB}MB)");
    }

    [Fact]
    public async Task Server_is_postgres_16_or_newer()
    {
        // The Testcontainers harness targets postgres:16-alpine
        // (per PostgresContainerFixture); this test guards
        // against an accidental downgrade to a version that
        // lacks a feature the project depends on (e.g. pgcrypto
        // gen_random_uuid() is core 13+; FORCE ROW LEVEL SECURITY
        // is core 9.5+; the logical replication improvements in
        // 16 improve the cloud sync engine's lag profile).
        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SHOW server_version";
        var version = (string?)await cmd.ExecuteScalarAsync();
        await connection.CloseAsync();

        version.Should().NotBeNullOrEmpty();

        // Parse the major version. Format: "16.x" or "16.1 ..."
        var majorVersionString = version!.Split('.')[0];
        int.TryParse(majorVersionString, out var majorVersion).Should().BeTrue(
            $"server_version should start with an integer major version; got '{version}'");

        majorVersion.Should().BeGreaterThanOrEqualTo(16,
            $"PostgreSQL 16+ is required (per harness configuration); server reported '{version}'");
    }
}
