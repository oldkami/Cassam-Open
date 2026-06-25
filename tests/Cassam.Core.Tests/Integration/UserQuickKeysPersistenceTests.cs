using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Integration;

/// <summary>
/// PostgreSQL round-trip tests for the <see cref="User.QuickKeys"/>
/// JSON column added in PR 7 (design §17.1 question #6 — per-cashier
/// quick-keys, RESOLVED 2026-06-24).
///
/// <para>
/// These tests exercise the database-level guarantees that the
/// entity-only test in <c>EntityTests</c> cannot:
/// <list type="bullet">
///   <item>The column exists (the migration applied cleanly).</item>
///   <item>snake_case <c>quick_keys</c> was emitted (not PascalCase).</item>
///   <item>Postgres <c>text</c> type, nullable, round-trips UTF-8 JSON.</item>
/// </list>
/// </para>
///
/// <para>
/// Validation that the JSON contains at most 12 product IDs lives
/// with the cashier flow's serializer (PR 9 / T2.08), not here — the
/// DB layer is deliberately schema-agnostic about the payload shape.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserQuickKeysPersistenceTests
{
    private readonly PostgresContainerFixture _fixture;

    public UserQuickKeysPersistenceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task User_round_trips_quick_keys_json_payload()
    {
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        const string payload = """{"product_ids":["7700000000001","7700000000002","7700000000003"],"max":12}""";

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(_fixture.ConnectionString))
        {
            // Seed a tenant so the FK from users.tenant_id resolves.
            ctx.Tenants.Add(new Tenant
            {
                Id = tenantId,
                LegalName = "Quick-Keys Test Tenant",
                Nit = $"QK-{tenantId:N}".Substring(0, 20),
                SubscriptionTier = SubscriptionTier.Free,
                SubscriptionStartedAt = DateTime.UtcNow,
                Status = TenantStatus.Active,
                CloudTransmissionEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            ctx.Users.Add(new User
            {
                Id = userId,
                TenantId = tenantId,
                Email = $"cashier-{userId:N}@cassam.co".Substring(0, 30),
                PasswordHash = "$argon2id$dummy",
                DisplayName = "Cashier Demo",
                Role = UserRole.Cashier,
                Active = true,
                QuickKeys = payload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(_fixture.ConnectionString))
        {
            var saved = await verify.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .SingleAsync();

            saved.QuickKeys.Should().Be(payload,
                "the JSON payload must round-trip byte-for-byte through PostgreSQL text");

            // Confirm the column literally exists with the expected
            // snake_case name and that the value is the exact JSON we
            // wrote — not a normalized variant, not null, not empty.
            var rawColumn = await verify.Database
                .SqlQueryRaw<string>("SELECT quick_keys AS \"Value\" FROM users WHERE id = {0}", userId)
                .SingleAsync();
            rawColumn.Should().Be(payload,
                "the raw SQL projection must hit the snake_case quick_keys column");
        }
    }

    [Fact]
    public async Task User_with_null_quick_keys_is_accepted()
    {
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(_fixture.ConnectionString))
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = tenantId,
                LegalName = "Null Quick-Keys Tenant",
                Nit = $"NUL-{tenantId:N}".Substring(0, 20),
                SubscriptionTier = SubscriptionTier.Free,
                SubscriptionStartedAt = DateTime.UtcNow,
                Status = TenantStatus.Active,
                CloudTransmissionEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            ctx.Users.Add(new User
            {
                Id = userId,
                TenantId = tenantId,
                Email = $"newbie-{userId:N}@cassam.co".Substring(0, 30),
                PasswordHash = "$argon2id$dummy",
                DisplayName = "New Cashier",
                Role = UserRole.Cashier,
                // QuickKeys intentionally left null — cashier has not
                // personalised their keypad yet.
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(_fixture.ConnectionString))
        {
            var saved = await verify.Users
                .AsNoTracking()
                .SingleAsync(u => u.Id == userId);

            saved.QuickKeys.Should().BeNull(
                "a cashier without personalised keypad must persist with a NULL quick_keys column");
        }
    }

    [Fact]
    public async Task Quick_keys_can_store_twelve_product_ids()
    {
        // SCN-UI-02 implicitly benefits from having 12 favourites pre-
        // computed on session open. The cashier flow's serializer
        // (PR 9 / T2.08) is what enforces the <= 12 invariant; this
        // test only proves the column accepts the full payload.
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var twelveIds = Enumerable.Range(1, 12)
            .Select(i => $"\"770000000{i:D4}\"")
            .ToArray();
        var payload = "{\"product_ids\":[" + string.Join(",", twelveIds) + "],\"max\":12}";

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(_fixture.ConnectionString))
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = tenantId,
                LegalName = "Full Quick-Keys Tenant",
                Nit = $"FUL-{tenantId:N}".Substring(0, 20),
                SubscriptionTier = SubscriptionTier.Free,
                SubscriptionStartedAt = DateTime.UtcNow,
                Status = TenantStatus.Active,
                CloudTransmissionEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            ctx.Users.Add(new User
            {
                Id = userId,
                TenantId = tenantId,
                Email = $"poweruser-{userId:N}@cassam.co".Substring(0, 30),
                PasswordHash = "$argon2id$dummy",
                DisplayName = "Power Cashier",
                QuickKeys = payload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(_fixture.ConnectionString))
        {
            var saved = await verify.Users
                .AsNoTracking()
                .SingleAsync(u => u.Id == userId);

            saved.QuickKeys.Should().NotBeNull();
            saved.QuickKeys!.Should().Contain("\"product_ids\"")
                .And.Contain("\"max\":12")
                .And.Contain("\"7700000000012\"",
                    "the 12th favourite must survive the round-trip");
        }
    }
}
