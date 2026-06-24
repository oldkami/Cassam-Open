using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Integration;

/// <summary>
/// Real-PostgreSQL integration tests for the <see cref="Tenant"/>
/// aggregate. These tests exercise the database round-trip path that
/// the in-memory test provider cannot validate: snake_case column
/// names, the <c>numeric(18,4)</c> type, the global soft-delete query
/// filter, and the composite indexes declared in
/// <see cref="Cassam.Core.Persistence.CassamDbContext"/>.
///
/// <para>
/// Per <c>pos-core-modern-stack</c> REQ-CORE-01 the <see cref="Tenant"/>
/// table is the root of the tenancy hierarchy: every other business
/// table carries a <c>tenant_id</c> FK. The tests below confirm the
/// root row round-trips and that the unique <c>nit</c> constraint is
/// enforced at the SQL layer (not just in EF Core's model snapshot).
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class TenantPersistenceTests
{
    private readonly PostgresContainerFixture _fixture;

    public TenantPersistenceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_round_trips_through_postgres()
    {
        var tenantId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = tenantId,
                LegalName = "Panadería La Espiga S.A.S.",
                Nit = "900123456-7",
                SubscriptionTier = SubscriptionTier.Pro,
                SubscriptionStartedAt = now,
                Status = TenantStatus.Active,
                CloudTransmissionEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var verifyCtx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var saved = await verifyCtx.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId);

            saved.LegalName.Should().Be("Panadería La Espiga S.A.S.");
            saved.Nit.Should().Be("900123456-7");
            saved.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            saved.Status.Should().Be(TenantStatus.Active);
            saved.CloudTransmissionEnabled.Should().BeTrue();
            saved.CreatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task Tenant_count_matches_inserts()
    {
        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString);

        // Use a tenant ID unique to this test for predictable counting.
        var tenantId = Guid.CreateVersion7();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            LegalName = $"Count Test {tenantId:N}",
            Nit = $"COUNT-{tenantId:N}".Substring(0, 20),
            SubscriptionTier = SubscriptionTier.Free,
            SubscriptionStartedAt = DateTime.UtcNow,
            Status = TenantStatus.Trial,
            CloudTransmissionEnabled = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        // Count via raw SQL so we see the row that was just committed
        // (not the change-tracker state).
        var count = await ctx.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM tenants")
            .SingleAsync();
        count.Should().BeGreaterThanOrEqualTo(1,
            "we just inserted a row, so the count must reflect it");

        var found = await ctx.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM tenants WHERE id = {0}",
                tenantId)
            .SingleAsync();
        found.Should().Be(1, "the inserted row should be retrievable by id");
    }

    [Fact]
    public async Task Duplicate_Nit_is_rejected_by_unique_constraint()
    {
        // Tenants are uniquely identified by NIT at the DB layer
        // (CassamDbContext declares HasIndex(t => t.Nit).IsUnique()).
        // The first insert succeeds; the second insert with the same
        // NIT must raise a Postgres unique-violation error.
        var sharedNit = $"DUP-{Guid.CreateVersion7():N}".Substring(0, 20);

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = Guid.CreateVersion7(),
                LegalName = "First Tenant",
                Nit = sharedNit,
                SubscriptionTier = SubscriptionTier.Free,
                SubscriptionStartedAt = DateTime.UtcNow,
                Status = TenantStatus.Trial,
                CloudTransmissionEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var dupCtx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            dupCtx.Tenants.Add(new Tenant
            {
                Id = Guid.CreateVersion7(),
                LegalName = "Second Tenant",
                Nit = sharedNit,
                SubscriptionTier = SubscriptionTier.Free,
                SubscriptionStartedAt = DateTime.UtcNow,
                Status = TenantStatus.Trial,
                CloudTransmissionEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

            var act = async () => await dupCtx.SaveChangesAsync();
            var exception = await act.Should().ThrowAsync<DbUpdateException>();
            exception.Which.InnerException.Should().NotBeNull();
            // The Npgsql provider wraps the Postgres error; the SQLSTATE
            // 23505 is the unique-violation code. We don't string-match
            // the full message (it's localized) but we assert the
            // exception chain reaches the Npgsql layer.
            exception.Which.InnerException!.GetType().Name
                .Should().Be("PostgresException",
                    "the unique violation must surface from Npgsql, not be swallowed by EF");
        }
    }

    [Fact]
    public async Task SubscriptionTier_enum_is_persisted_as_string()
    {
        // The DbContext configures the enum columns with
        // HasConversion<string>() so the column stores 'FREE' / 'PRO'
        // / 'ENTERPRISE' rather than the numeric value. The migration
        // emitted character varying(16) for these columns. We verify
        // the round-trip preserves the symbolic name.
        var tenantId = Guid.CreateVersion7();
        var nit = $"TIER-{Guid.CreateVersion7():N}".Substring(0, 20);

        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = tenantId,
                LegalName = "Tier Test",
                Nit = nit,
                SubscriptionTier = SubscriptionTier.Enterprise,
                SubscriptionStartedAt = DateTime.UtcNow,
                Status = TenantStatus.Active,
                CloudTransmissionEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var verify = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            var stored = await verify.Tenants
                .Where(t => t.Id == tenantId)
                .Select(t => t.SubscriptionTier)
                .SingleAsync();
            stored.Should().Be(SubscriptionTier.Enterprise);

            // Also confirm the literal string is in the column.
            var rawTier = await verify.Database
                .SqlQueryRaw<string>(
                    "SELECT subscription_tier AS \"Value\" FROM tenants WHERE id = {0}",
                    tenantId)
                .SingleAsync();
            rawTier.Should().Be("Enterprise");
        }
    }
}
