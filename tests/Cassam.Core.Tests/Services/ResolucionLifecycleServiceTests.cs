using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Services;
using Cassam.Core.Persistence;
using Cassam.Core.Persistence.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ResolucionLifecycleService"/>. The service
/// does NOT touch <c>current_number</c> — it owns expiry/exhaustion
/// detection and the one-way <c>Active → Expired</c> state transition.
///
/// <para>
/// Tests run against the EF Core InMemory provider. The provider does
/// not enforce transactions but does correctly handle the queries the
/// service emits. The audit-row verification at the end of
/// <c>MarkExpiredAsync</c> is in-memory (no PostgreSQL trigger check);
/// the trigger-side enforcement is exercised by the audit-log
/// immutability tests shipped in PR 3.
/// </para>
/// </summary>
public class ResolucionLifecycleServiceTests
{
    // ===== FindResolucionesExpiringWithinAsync ========================

    [Fact]
    public async Task FindResolucionesExpiringWithinAsync_returns_resolucions_exactly_within_window()
    {
        // Boundary test: today counts as "within 0 days" but +31 days
        // must NOT. The service uses inclusive comparison
        // (expirationDate <= cutoff).
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();

        await SeedResolucion(ctx, tenantId, expiration: DateOnly.FromDateTime(DateTime.UtcNow));
        await SeedResolucion(ctx, tenantId, expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));
        await SeedResolucion(ctx, tenantId, expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)));
        await SeedResolucion(ctx, tenantId, expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        await SeedResolucion(ctx, tenantId, expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(31)));
        await SeedResolucion(ctx, tenantId, expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(365)));

        var sut = new ResolucionLifecycleService(ctx);

        var results = await sut.FindResolucionesExpiringWithinAsync(tenantId, daysAhead: 30);

        results.Should().HaveCount(4,
            "today, +1d, +15d, and +30d all fall within a 30-day inclusive window");
        results.Select(r => r.ExpirationDate)
            .Should().BeInAscendingOrder(
                "the contract is 'most urgent first'");
    }

    [Fact]
    public async Task FindResolucionesExpiringWithinAsync_excludes_other_tenants()
    {
        await using var ctx = NewContext();
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await SeedResolucion(ctx, tenantA, expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)));
        await SeedResolucion(ctx, tenantB, expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)));

        var sut = new ResolucionLifecycleService(ctx);

        var tenantAResults = await sut.FindResolucionesExpiringWithinAsync(tenantA, daysAhead: 30);

        tenantAResults.Should().HaveCount(1,
            "the lookup is tenant-scoped — other tenants' resoluciones are invisible");
        tenantAResults.Single().TenantId.Should().Be(tenantA);
    }

    [Fact]
    public async Task FindResolucionesExpiringWithinAsync_excludes_non_active_resolucions()
    {
        // Only ACTIVE resoluciones are candidates for the 30-day warning.
        // Draft / Expired rows must not surface — Draft hasn't been
        // activated yet and Expired is already past its warning window.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();

        await SeedResolucion(ctx, tenantId, status: ResolucionStatus.Draft,
            expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)));
        await SeedResolucion(ctx, tenantId, status: ResolucionStatus.Active,
            expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)));
        await SeedResolucion(ctx, tenantId, status: ResolucionStatus.Expired,
            expiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)));

        var sut = new ResolucionLifecycleService(ctx);

        var results = await sut.FindResolucionesExpiringWithinAsync(tenantId, daysAhead: 30);

        results.Should().HaveCount(1,
            "only the Active resolución must surface in the expiry scan");
        results.Single().Status.Should().Be(ResolucionStatus.Active);
    }

    // ===== FindExhaustedResolucionesAsync ============================

    [Fact]
    public async Task FindExhaustedResolucionesAsync_returns_only_resolucions_past_range_end()
    {
        // An "exhausted" resolución is one whose current_number has
        // reached range_end. The dispatcher consults this list to skip
        // already-exhausted rows when searching for a fallback range.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();

        await SeedResolucion(ctx, tenantId,
            rangeStart: 1, rangeEnd: 100, currentNumber: 50);  // not exhausted
        await SeedResolucion(ctx, tenantId,
            rangeStart: 100, rangeEnd: 200, currentNumber: 200); // exhausted
        await SeedResolucion(ctx, tenantId,
            rangeStart: 1000, rangeEnd: 2000, currentNumber: 2000); // exhausted
        await SeedResolucion(ctx, tenantId,
            rangeStart: 5000, rangeEnd: 6000, currentNumber: 4000); // not exhausted

        var sut = new ResolucionLifecycleService(ctx);

        var exhausted = await sut.FindExhaustedResolucionesAsync(tenantId);

        exhausted.Should().HaveCount(2,
            "two resoluciones have current_number >= range_end");
        exhausted.Select(r => r.RangeEnd)
            .Should().BeInAscendingOrder("results are sorted smallest range first");
    }

    [Fact]
    public async Task FindExhaustedResolucionesAsync_excludes_non_active_status()
    {
        // A Draft resolución has not started issuing numbers, so it
        // cannot be "exhausted" in the meaningful sense. An Expired
        // resolución has its status flipped — it must NOT appear in the
        // active-exhausted list (the dispatcher would otherwise keep
        // re-discovering it).
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();

        await SeedResolucion(ctx, tenantId, status: ResolucionStatus.Draft,
            rangeStart: 1, rangeEnd: 10, currentNumber: 10);
        await SeedResolucion(ctx, tenantId, status: ResolucionStatus.Expired,
            rangeStart: 1, rangeEnd: 10, currentNumber: 10);

        var sut = new ResolucionLifecycleService(ctx);

        var exhausted = await sut.FindExhaustedResolucionesAsync(tenantId);

        exhausted.Should().BeEmpty();
    }

    // ===== MarkExpiredAsync ==========================================

    [Fact]
    public async Task MarkExpiredAsync_transitions_active_to_expired()
    {
        // The one-way transition: ACTIVE → EXPIRED. No other state
        // transition is allowed via this service.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var resolucion = await SeedResolucion(ctx, tenantId,
            status: ResolucionStatus.Active);

        var sut = new ResolucionLifecycleService(ctx);

        await sut.MarkExpiredAsync(resolucion.Id);

        resolucion.Status.Should().Be(ResolucionStatus.Expired,
            "the ACTIVE → EXPIRED transition is the service's sole state mutation");
    }

    [Fact]
    public async Task MarkExpiredAsync_appends_an_audit_log_row()
    {
        // REQ-CORE-03: every fiscal mutation MUST produce an audit row.
        // MarkExpiredAsync is a fiscal mutation (state transition on
        // a compliance entity), so an audit entry is mandatory.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var resolucion = await SeedResolucion(ctx, tenantId,
            status: ResolucionStatus.Active);

        var sut = new ResolucionLifecycleService(ctx);

        await sut.MarkExpiredAsync(resolucion.Id);

        var auditRow = await ctx.AuditLog.AsNoTracking()
            .SingleAsync(a => a.EntityType == "resolucion" && a.EntityId == resolucion.Id);

        auditRow.TenantId.Should().Be(tenantId);
        auditRow.Action.Should().Be("RESOLUTION_EXPIRED");
        auditRow.BeforeState.Should().Be("Active");
        auditRow.AfterState.Should().Be("Expired");
    }

    [Fact]
    public async Task MarkExpiredAsync_throws_when_resolucion_not_found()
    {
        await using var ctx = NewContext();

        var sut = new ResolucionLifecycleService(ctx);

        var act = async () => await sut.MarkExpiredAsync(Guid.CreateVersion7());

        await act.Should().ThrowAsync<InvalidOperationException>(
            "MarkExpiredAsync requires the target row to exist — silent skip would mask data corruption");
    }

    [Fact]
    public async Task MarkExpiredAsync_throws_when_resolucion_not_active()
    {
        // The transition is one-way. Trying to mark a Draft or already-
        // Expired resolución as Expired is a state-machine violation
        // and MUST throw rather than silently succeed.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();

        var draft = await SeedResolucion(ctx, tenantId, status: ResolucionStatus.Draft);
        var alreadyExpired = await SeedResolucion(ctx, tenantId, status: ResolucionStatus.Expired);

        var sut = new ResolucionLifecycleService(ctx);

        var draftAct = async () => await sut.MarkExpiredAsync(draft.Id);
        var expiredAct = async () => await sut.MarkExpiredAsync(alreadyExpired.Id);

        await draftAct.Should().ThrowAsync<InvalidOperationException>();
        await expiredAct.Should().ThrowAsync<InvalidOperationException>();
    }

    // ===== HasActiveResolucionAsync ===================================

    [Fact]
    public async Task HasActiveResolucionAsync_returns_true_when_active_exists()
    {
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        await SeedResolucion(ctx, tenantId,
            documentType: DocumentType.FeVenta, status: ResolucionStatus.Active);

        var sut = new ResolucionLifecycleService(ctx);

        var has = await sut.HasActiveResolucionAsync(tenantId, DocumentType.FeVenta);

        has.Should().BeTrue();
    }

    [Fact]
    public async Task HasActiveResolucionAsync_returns_false_when_no_row()
    {
        await using var ctx = NewContext();

        var sut = new ResolucionLifecycleService(ctx);

        var has = await sut.HasActiveResolucionAsync(
            Guid.CreateVersion7(), DocumentType.DeePos);

        has.Should().BeFalse();
    }

    [Fact]
    public async Task HasActiveResolucionAsync_returns_false_when_only_non_active_rows_exist()
    {
        // A Draft row must not satisfy the check — the dispatcher
        // should refuse to issue numbers when the only available
        // resolución is in Draft.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        await SeedResolucion(ctx, tenantId,
            documentType: DocumentType.DeePos, status: ResolucionStatus.Draft);
        await SeedResolucion(ctx, tenantId,
            documentType: DocumentType.DeePos, status: ResolucionStatus.Expired);

        var sut = new ResolucionLifecycleService(ctx);

        var has = await sut.HasActiveResolucionAsync(tenantId, DocumentType.DeePos);

        has.Should().BeFalse();
    }

    [Fact]
    public async Task HasActiveResolucionAsync_matches_exact_document_type()
    {
        // Two tenants with different document types must not collide.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        await SeedResolucion(ctx, tenantId,
            documentType: DocumentType.DeePos, status: ResolucionStatus.Active);

        var sut = new ResolucionLifecycleService(ctx);

        var hasPos = await sut.HasActiveResolucionAsync(tenantId, DocumentType.DeePos);
        var hasFe = await sut.HasActiveResolucionAsync(tenantId, DocumentType.FeVenta);

        hasPos.Should().BeTrue();
        hasFe.Should().BeFalse(
            "the DEE_POS active resolución must not satisfy an FE_Venta lookup");
    }

    // ===== Helpers =====================================================

    private static CassamDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CassamDbContext>()
            .UseInMemoryDatabase($"resolucion-lifecycle-{Guid.NewGuid():N}")
            .Options);

    private static async Task<Resolucion> SeedResolucion(
        CassamDbContext ctx,
        Guid tenantId,
        DocumentType documentType = DocumentType.DeePos,
        ResolucionStatus status = ResolucionStatus.Active,
        DateOnly? expiration = null,
        long rangeStart = 1,
        long rangeEnd = 100,
        long currentNumber = 0)
    {
        var resolucion = new Resolucion
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            DocumentType = documentType,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            CurrentNumber = currentNumber,
            ExpirationDate = expiration ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            SoftwareTechnicalKeyId = Guid.CreateVersion7(),
            Status = status,
        };
        ctx.Resoluciones.Add(resolucion);
        await ctx.SaveChangesAsync();
        return resolucion;
    }
}
