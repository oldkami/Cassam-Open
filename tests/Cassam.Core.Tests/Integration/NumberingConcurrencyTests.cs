using System.Collections.Concurrent;
using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Exceptions;
using Cassam.Core.Domain.Services;
using Cassam.Core.Persistence.Services;
using Cassam.Core.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Integration;

/// <summary>
/// Real-PostgreSQL integration tests for <see cref="NumberingService"/>.
/// The application-layer unit tests prove the LINQ semantics; these
/// tests prove the ROW-LOCKING contract — that two concurrent
/// <c>ReserveNextNumberAsync</c> calls against the same resolución
/// never observe the same <c>current_number</c>.
///
/// <para>
/// The InMemory provider silently serializes its queries; only the
/// Testcontainers PostgreSQL fixture reproduces the <c>SELECT … FOR
/// UPDATE</c> blocking semantics that the production dispatcher
/// depends on. Every test below uses a real Postgres container
/// started by <see cref="PostgresContainerFixture"/>.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class NumberingConcurrencyTests
{
    private readonly PostgresContainerFixture _fixture;

    public NumberingConcurrencyTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Concurrent_reservations_return_unique_numbers_no_duplicates()
    {
        // 10 concurrent tasks call ReserveNextNumberAsync against the
        // same resolución. PostgreSQL's row lock guarantees each
        // commits a unique number — the union of returned numbers must
        // match the expected range exactly, with no duplicates.
        var tenantId = await CreateTenantAsync(_fixture.ConnectionString);
        var resolucionId = Guid.CreateVersion7();
        const int totalReservations = 10;
        const long rangeStart = 10_000;

        await SeedResolucionAsync(tenantId, resolucionId, rangeStart, rangeStart + totalReservations);

        // Each task gets its OWN DbContext — DbContext is not thread-safe
        // and a single context shared across tasks would deadlock on the
        // FOR UPDATE lock.
        var tasks = Enumerable.Range(0, totalReservations)
            .Select(_ => Task.Run(async () =>
            {
                await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
                    _fixture.ConnectionString);
                var sut = new NumberingService(ctx);
                return await sut.ReserveNextNumberAsync(tenantId, DocumentType.DeePos);
            }))
            .ToList();

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(totalReservations);
        results.Should().OnlyHaveUniqueItems(
            "row-level locking must guarantee each reservation observes a distinct counter value");
        results.Should().BeEquivalentTo(
            Enumerable.Range((int)rangeStart, totalReservations).Select(i => (long)i).ToList(),
            "the 10 reservations must consume range_start..range_start + 9 with no gaps");
    }

    [Fact]
    public async Task Concurrent_reservations_at_the_exhaustion_boundary_exactly_n_succeed()
    {
        // Near-exhaustion: the range fits EXACTLY N reservations.
        // Exactly N concurrent tasks succeed; any extras throw
        // ResolucionExhaustedException. This is the boundary test
        // for SCN-CORE-07 dispatcher-fallback behaviour.
        //
        // Math note: the inclusive range [range_start, range_end]
        // contains (range_end - range_start + 1) numbers. With
        // range_end = range_start + totalReservations - 1 the range
        // contains exactly totalReservations numbers.
        var tenantId = await CreateTenantAsync(_fixture.ConnectionString);
        var resolucionId = Guid.CreateVersion7();
        const int totalReservations = 5;
        const long rangeStart = 20_000;

        await SeedResolucionAsync(tenantId, resolucionId,
            rangeStart, rangeEnd: rangeStart + totalReservations - 1);

        // Spawn MORE tasks than the range can hold.
        const int overcommit = 5; // 5 extras will see exhaustion
        var tasks = Enumerable.Range(0, totalReservations + overcommit)
            .Select(_ => Task.Run(async () =>
            {
                await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
                    _fixture.ConnectionString);
                var sut = new NumberingService(ctx);
                try
                {
                    var n = await sut.ReserveNextNumberAsync(tenantId, DocumentType.DeePos);
                    return (long?)n;
                }
                catch (ResolucionExhaustedException)
                {
                    return null;
                }
            }))
            .ToList();

        var results = await Task.WhenAll(tasks);

        var succeeded = results.Where(r => r.HasValue).Select(r => r!.Value).ToList();
        var exhausted = results.Where(r => !r.HasValue).Count();

        succeeded.Should().HaveCount(totalReservations,
            "exactly the range-fitting number of reservations must succeed");
        succeeded.Should().OnlyHaveUniqueItems();
        exhausted.Should().Be(overcommit,
            "every reservation beyond range_end must surface ResolucionExhaustedException");
    }

    [Fact]
    public async Task Concurrent_reservations_across_two_tenants_do_not_interfere()
    {
        // Two tenants with their own active resoluciones — concurrent
        // reservations across both must NOT contend on each other's
        // row locks. We use DISJOINT ranges so the cross-tenant number
        // streams never overlap, and we verify the two sets landed in
        // the expected disjoint ranges.
        var tenantA = await CreateTenantAsync(_fixture.ConnectionString);
        var tenantB = await CreateTenantAsync(_fixture.ConnectionString);

        var resolucionAId = Guid.CreateVersion7();
        var resolucionBId = Guid.CreateVersion7();

        // Tenant A uses [30_000, 30_004]; Tenant B uses [40_000, 40_004].
        // Five reservations on each → 5 numbers per tenant, no overlap.
        const int perTenant = 5;
        await SeedResolucionAsync(tenantA, resolucionAId, rangeStart: 30_000, rangeEnd: 30_004);
        await SeedResolucionAsync(tenantB, resolucionBId, rangeStart: 40_000, rangeEnd: 40_004);

        var tenantANumbers = new ConcurrentBag<long>();
        var tenantBNumbers = new ConcurrentBag<long>();

        var tasks = new List<Task>();

        for (var i = 0; i < perTenant; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
                    _fixture.ConnectionString);
                var sut = new NumberingService(ctx);
                tenantANumbers.Add(await sut.ReserveNextNumberAsync(tenantA, DocumentType.DeePos));
            }));

            tasks.Add(Task.Run(async () =>
            {
                await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
                    _fixture.ConnectionString);
                var sut = new NumberingService(ctx);
                tenantBNumbers.Add(await sut.ReserveNextNumberAsync(tenantB, DocumentType.DeePos));
            }));
        }

        await Task.WhenAll(tasks);

        var aSorted = tenantANumbers.OrderBy(x => x).ToList();
        var bSorted = tenantBNumbers.OrderBy(x => x).ToList();

        aSorted.Should().HaveCount(perTenant);
        aSorted.Should().OnlyHaveUniqueItems(
            "tenant A's reservations must serialize against A's row lock");
        aSorted.Should().BeEquivalentTo(
            Enumerable.Range(30_000, perTenant).Select(i => (long)i).ToList(),
            "tenant A's reservations must produce 30000..30004 in some order");

        bSorted.Should().HaveCount(perTenant);
        bSorted.Should().OnlyHaveUniqueItems();
        bSorted.Should().BeEquivalentTo(
            Enumerable.Range(40_000, perTenant).Select(i => (long)i).ToList(),
            "tenant B's reservations must produce 40000..40004 in some order");

        // Cross-tenant check: the two streams must NOT interfere even
        // when interleaved. We don't check exact values here because
        // the order of task scheduling is non-deterministic; we
        // already proved per-tenant uniqueness and range coverage
        // above.
    }

    // ===== Helpers =====================================================

    private static async Task<Guid> CreateTenantAsync(string connectionString)
    {
        var tenantId = Guid.CreateVersion7();
        var nit = $"CNC-{tenantId:N}".Substring(0, 20);

        await using var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            connectionString);

        // Skip if a previous test already created this tenant id.
        var exists = await ctx.Tenants.AnyAsync(t => t.Id == tenantId);
        if (exists)
        {
            return tenantId;
        }

        var now = DateTime.UtcNow;
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            LegalName = $"Concurrency Test {tenantId:N}",
            Nit = nit,
            SubscriptionTier = SubscriptionTier.Pro,
            SubscriptionStartedAt = now,
            Status = TenantStatus.Active,
            CloudTransmissionEnabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task SeedResolucionAsync(
        Guid tenantId,
        Guid resolucionId,
        long rangeStart,
        long rangeEnd)
    {
        var now = DateTime.UtcNow;

        // Resoluciones reference SoftwareTechnicalKey — create the parent row first.
        var stkId = Guid.CreateVersion7();
        await using (var ctx = await CassamDbContextFactory.CreateMigratedContextAsync(
            _fixture.ConnectionString))
        {
            ctx.SoftwareTechnicalKeys.Add(new SoftwareTechnicalKey
            {
                Id = stkId,
                TenantId = tenantId,
                KeyValue = $"CNC-STK-{stkId:N}",
                IssuedByDian = true,
                Active = true,
                IssuedAt = now,
                DeactivatedAt = null,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });

            ctx.Resoluciones.Add(new Resolucion
            {
                Id = resolucionId,
                TenantId = tenantId,
                DocumentType = DocumentType.DeePos,
                RangeStart = rangeStart,
                RangeEnd = rangeEnd,
                CurrentNumber = rangeStart - 1,
                ExpirationDate = DateOnly.FromDateTime(now.AddYears(2)),
                SoftwareTechnicalKeyId = stkId,
                Status = ResolucionStatus.Active,
                Prefix = null,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
            await ctx.SaveChangesAsync();
        }
    }
}
