using Cassam.Core.Audit;
using Cassam.Core.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests;

/// <summary>
/// Append-only audit-log writer tests. The append-only contract is
/// REQ-CORE-03 / SCN-CORE-03; these tests exercise the application-layer
/// half (the writer only ever adds rows) and prove a roundtrip into the
/// <c>audit_log</c> <see cref="DbSet{TEntity}"/>.
///
/// The PostgreSQL half of the contract (RLS <c>DELETE</c> denial + DB
/// trigger) is an integration test in PR 2 (T1.11) once the Testcontainers
/// harness lands.
/// </summary>
public class AuditLogWriterTests
{
    [Fact]
    public async Task WriteAsync_inserts_a_row_with_all_required_fields()
    {
        var dbName = $"audit-log-test-{Guid.NewGuid():N}";
        var tenantId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var entityId = Guid.CreateVersion7();

        await using (var ctx = NewContext(dbName))
        {
            ctx.Database.EnsureCreated();

            var writer = new AuditLogWriter(ctx);
            var entry = new AuditLogEntry(
                ActorUserId: actorId,
                TenantId: tenantId,
                EntityType: "documento_electronico",
                EntityId: entityId,
                Action: "TRANSITION_SIGNED",
                BeforeState: "DRAFT",
                AfterState: "SIGNED",
                IpAddress: "192.0.2.42");

            await writer.WriteAsync(entry);

            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext(dbName))
        {
            var saved = await ctx.AuditLog.AsNoTracking().SingleAsync();

            saved.TenantId.Should().Be(tenantId);
            saved.ActorUserId.Should().Be(actorId);
            saved.EntityType.Should().Be("documento_electronico");
            saved.EntityId.Should().Be(entityId);
            saved.Action.Should().Be("TRANSITION_SIGNED");
            saved.BeforeState.Should().Be("DRAFT");
            saved.AfterState.Should().Be("SIGNED");
            saved.IpAddress.Should().Be("192.0.2.42");
            saved.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            saved.Id.Should().NotBe(Guid.Empty);
            saved.Version.Should().Be(1u);
        }
    }

    [Fact]
    public async Task WriteAsync_supports_null_ip_address_for_system_driven_entries()
    {
        // System-driven entries (drain worker, scheduled jobs) carry
        // no IP. The audit trail must still accept them — the IP column
        // is nullable while ActorUserId is mandatory (every audit row
        // identifies the actor; system actors use the tenant's SYSTEM user).
        var dbName = $"audit-log-test-{Guid.NewGuid():N}";
        var tenantId = Guid.CreateVersion7();

        await using (var ctx = NewContext(dbName))
        {
            ctx.Database.EnsureCreated();

            var writer = new AuditLogWriter(ctx);
            await writer.WriteAsync(new AuditLogEntry(
                ActorUserId: Guid.CreateVersion7(),
                TenantId: tenantId,
                EntityType: "documento_electronico",
                EntityId: Guid.CreateVersion7(),
                Action: "DRAIN_TRANSMITTED",
                BeforeState: "QUEUED",
                AfterState: "TRANSMITTED",
                IpAddress: null));

            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext(dbName))
        {
            var saved = await ctx.AuditLog.AsNoTracking().SingleAsync();
            saved.IpAddress.Should().BeNull();
            saved.Action.Should().Be("DRAIN_TRANSMITTED");
        }
    }

    [Fact]
    public async Task WriteAsync_throws_ArgumentNullException_for_null_entry()
    {
        await using var ctx = NewContext($"audit-log-test-{Guid.NewGuid():N}");
        ctx.Database.EnsureCreated();

        var writer = new AuditLogWriter(ctx);

        var act = async () => await writer.WriteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WriteAsync_throws_ArgumentNullException_for_null_dbContext()
    {
        var act = () => _ = new AuditLogWriter(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task WriteAsync_persists_multiple_rows_for_the_same_entity_in_order()
    {
        // A document going through DRAFT → SIGNED → TRANSMITTED → ACCEPTED
        // produces three audit rows. The writer handles them independently.
        var dbName = $"audit-log-test-{Guid.NewGuid():N}";
        var tenantId = Guid.CreateVersion7();
        var entityId = Guid.CreateVersion7();

        await using (var ctx = NewContext(dbName))
        {
            ctx.Database.EnsureCreated();

            var writer = new AuditLogWriter(ctx);

            await writer.WriteAsync(new AuditLogEntry(
                Guid.NewGuid(), tenantId, "documento_electronico", entityId,
                "TRANSITION_SIGNED", "DRAFT", "SIGNED", "10.0.0.1"));
            await writer.WriteAsync(new AuditLogEntry(
                Guid.NewGuid(), tenantId, "documento_electronico", entityId,
                "TRANSITION_TRANSMITTED", "SIGNED", "TRANSMITTED", "10.0.0.1"));
            await writer.WriteAsync(new AuditLogEntry(
                Guid.NewGuid(), tenantId, "documento_electronico", entityId,
                "TRANSITION_ACCEPTED", "TRANSMITTED", "ACCEPTED", "10.0.0.1"));

            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext(dbName))
        {
            var rows = await ctx.AuditLog.AsNoTracking()
                .OrderBy(a => a.OccurredAt)
                .ToListAsync();

            rows.Should().HaveCount(3);
            rows.Select(r => r.Action).Should().ContainInOrder(
                "TRANSITION_SIGNED",
                "TRANSITION_TRANSMITTED",
                "TRANSITION_ACCEPTED");
        }
    }

    [Fact]
    public async Task WriteAsync_does_not_SaveChanges_on_its_own()
    {
        // The writer deliberately does NOT call SaveChanges — that would
        // split the audit row from the fiscal mutation. We prove this by
        // checking the row is NOT visible from a fresh context until the
        // caller flushes.
        var dbName = $"audit-log-test-{Guid.NewGuid():N}";

        await using (var ctx = NewContext(dbName))
        {
            ctx.Database.EnsureCreated();

            var writer = new AuditLogWriter(ctx);
            await writer.WriteAsync(new AuditLogEntry(
                Guid.NewGuid(), Guid.CreateVersion7(), "documento_electronico",
                Guid.CreateVersion7(), "TRANSITION_SIGNED", "DRAFT", "SIGNED", null));
            // Intentionally NOT calling SaveChangesAsync.
        }

        await using (var verifyCtx = NewContext(dbName))
        {
            var count = await verifyCtx.AuditLog.CountAsync();
            count.Should().Be(0,
                "the writer must not commit until the caller invokes SaveChanges — the mutation and audit share one transaction");
        }
    }

    private static CassamDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<CassamDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
