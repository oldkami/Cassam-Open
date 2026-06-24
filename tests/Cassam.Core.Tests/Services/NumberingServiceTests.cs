using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Exceptions;
using Cassam.Core.Domain.Services;
using Cassam.Core.Persistence;
using Cassam.Core.Persistence.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="NumberingService"/>. These exercise the
/// service against the EF Core InMemory provider — sufficient for the
/// happy path, the exhaustion path, and the no-active-resolución path
/// because the InMemory provider implements all the LINQ constructs the
/// service emits.
///
/// <para>
/// The cross-process / cross-transaction row-locking contract is
/// exercised by <c>NumberingConcurrencyTests</c> in the integration
/// suite, which uses a Testcontainers PostgreSQL instance. InMemory
/// cannot reproduce the <c>SELECT … FOR UPDATE</c> semantics and would
/// silently mask a concurrency bug.
/// </para>
/// </summary>
public class NumberingServiceTests
{
    [Fact]
    public async Task ReserveNextNumberAsync_returns_range_start_on_first_call()
    {
        // The first reservation against a fresh resolución must produce
        // exactly range_start (per the documented CurrentNumber semantics:
        // defaults to range_start - 1 so the first increment produces
        // range_start).
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var resolucion = await SeedResolucionAsync(ctx, tenantId, DocumentType.DeePos,
            rangeStart: 1_000_000, rangeEnd: 2_000_000, currentNumber: 999_999);

        var sut = new NumberingService(ctx);

        var issued = await sut.ReserveNextNumberAsync(tenantId, DocumentType.DeePos);

        issued.Should().Be(1_000_000L,
            "the first issued number on a fresh range must equal range_start");
        resolucion.CurrentNumber.Should().Be(1_000_000L,
            "the increment must be persisted on the entity");
    }

    [Fact]
    public async Task ReserveNextNumberAsync_increments_sequentially_on_repeated_calls()
    {
        // Three sequential reservations produce three consecutive numbers.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var resolucion = await SeedResolucionAsync(ctx, tenantId, DocumentType.FeVenta,
            rangeStart: 100, rangeEnd: 200, currentNumber: 100);

        var sut = new NumberingService(ctx);

        var n1 = await sut.ReserveNextNumberAsync(tenantId, DocumentType.FeVenta);
        var n2 = await sut.ReserveNextNumberAsync(tenantId, DocumentType.FeVenta);
        var n3 = await sut.ReserveNextNumberAsync(tenantId, DocumentType.FeVenta);

        n1.Should().Be(101L);
        n2.Should().Be(102L);
        n3.Should().Be(103L);
        resolucion.CurrentNumber.Should().Be(103L);
    }

    [Fact]
    public async Task ReserveNextNumberAsync_succeeds_through_range_end_inclusive()
    {
        // When current_number == range_end - 1, the next reservation
        // succeeds and produces range_end. The CURRENT call must NOT
        // throw — exhaustion is signaled on the call AFTER range_end.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var resolucion = await SeedResolucionAsync(ctx, tenantId, DocumentType.DeePos,
            rangeStart: 100, rangeEnd: 102, currentNumber: 101);

        var sut = new NumberingService(ctx);

        var issued = await sut.ReserveNextNumberAsync(tenantId, DocumentType.DeePos);

        issued.Should().Be(102L,
            "issuing range_end is allowed — the boundary value belongs to the authorized range");
        resolucion.CurrentNumber.Should().Be(102L);
    }

    [Fact]
    public async Task ReserveNextNumberAsync_throws_ResolucionExhaustedException_when_current_equals_range_end()
    {
        // current_number already at range_end → next reservation must
        // refuse with the documented exhaustion exception.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var resolucion = await SeedResolucionAsync(ctx, tenantId, DocumentType.NotaCredito,
            rangeStart: 1, rangeEnd: 10, currentNumber: 10);

        var sut = new NumberingService(ctx);

        var act = async () => await sut.ReserveNextNumberAsync(tenantId, DocumentType.NotaCredito);

        var exception = await act.Should().ThrowAsync<ResolucionExhaustedException>();
        exception.Which.ResolucionId.Should().Be(resolucion.Id);
        exception.Which.CurrentNumber.Should().Be(10L);
        exception.Which.RangeEnd.Should().Be(10L);
    }

    [Fact]
    public async Task ReserveNextNumberAsync_throws_ResolucionExhaustedException_when_current_exceeds_range_end()
    {
        // Defensive: even if the data drifts past range_end (e.g. an
        // out-of-band UPDATE), the service must refuse rather than
        // issue numbers outside the authorized range.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        await SeedResolucionAsync(ctx, tenantId, DocumentType.NotaDebito,
            rangeStart: 1, rangeEnd: 10, currentNumber: 11);

        var sut = new NumberingService(ctx);

        var act = async () => await sut.ReserveNextNumberAsync(tenantId, DocumentType.NotaDebito);

        await act.Should().ThrowAsync<ResolucionExhaustedException>();
    }

    [Fact]
    public async Task ReserveNextNumberAsync_throws_ResolucionNotActiveException_when_no_active_resolucion_exists()
    {
        // No resolución at all for the (tenant, document type) pair →
        // the documented not-active exception, with the right tenant
        // and document type so the UI can route the message.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();

        var sut = new NumberingService(ctx);

        var act = async () => await sut.ReserveNextNumberAsync(tenantId, DocumentType.DeePos);

        var exception = await act.Should().ThrowAsync<ResolucionNotActiveException>();
        exception.Which.TenantId.Should().Be(tenantId);
        exception.Which.DocumentType.Should().Be(DocumentType.DeePos);
    }

    [Fact]
    public async Task ReserveNextNumberAsync_throws_ResolucionNotActiveException_when_only_draft_resolucion_exists()
    {
        // A Draft resolución must NOT serve numbers — only Active does.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var draft = new Resolucion
        {
            TenantId = tenantId,
            DocumentType = DocumentType.DeePos,
            RangeStart = 1,
            RangeEnd = 100,
            CurrentNumber = 0,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            SoftwareTechnicalKeyId = Guid.CreateVersion7(),
            Status = ResolucionStatus.Draft,
        };
        ctx.Resoluciones.Add(draft);
        await ctx.SaveChangesAsync();

        var sut = new NumberingService(ctx);

        var act = async () => await sut.ReserveNextNumberAsync(tenantId, DocumentType.DeePos);

        await act.Should().ThrowAsync<ResolucionNotActiveException>(
            "a Draft resolución is not yet issuing numbers — only ACTIVE does");
    }

    [Fact]
    public async Task ReserveNextNumberAsync_throws_ResolucionNotActiveException_when_only_expired_resolucion_exists()
    {
        // An Expired resolución cannot be revived for numbering — the
        // dispatcher must look for the next active range, not reuse
        // an expired one.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        var expired = new Resolucion
        {
            TenantId = tenantId,
            DocumentType = DocumentType.FeVenta,
            RangeStart = 1,
            RangeEnd = 100,
            CurrentNumber = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            SoftwareTechnicalKeyId = Guid.CreateVersion7(),
            Status = ResolucionStatus.Expired,
        };
        ctx.Resoluciones.Add(expired);
        await ctx.SaveChangesAsync();

        var sut = new NumberingService(ctx);

        var act = async () => await sut.ReserveNextNumberAsync(tenantId, DocumentType.FeVenta);

        await act.Should().ThrowAsync<ResolucionNotActiveException>();
    }

    [Fact]
    public async Task ReserveNextNumberAsync_targets_correct_resolucion_per_document_type()
    {
        // Two resoluciones for two different document types on the
        // same tenant — each call must hit its own range.
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        await SeedResolucionAsync(ctx, tenantId, DocumentType.DeePos,
            rangeStart: 1_000_000, rangeEnd: 1_999_999, currentNumber: 1_000_000);
        await SeedResolucionAsync(ctx, tenantId, DocumentType.FeVenta,
            rangeStart: 500, rangeEnd: 599, currentNumber: 500);

        var sut = new NumberingService(ctx);

        var pos = await sut.ReserveNextNumberAsync(tenantId, DocumentType.DeePos);
        var fe = await sut.ReserveNextNumberAsync(tenantId, DocumentType.FeVenta);

        pos.Should().Be(1_000_001L,
            "the DEE POS range must produce numbers in the 1_000_001+ range");
        fe.Should().Be(501L,
            "the FE Venta range must produce numbers in the 500+ range");
    }

    [Fact]
    public async Task HasAvailableNumbersAsync_returns_true_when_enough_remaining()
    {
        await using var ctx = NewContext();
        var tenantId = Guid.CreateVersion7();
        await SeedResolucionAsync(ctx, tenantId, DocumentType.DeePos,
            rangeStart: 1, rangeEnd: 100, currentNumber: 10);

        var sut = new NumberingService(ctx);

        var enough = await sut.HasAvailableNumbersAsync(tenantId, DocumentType.DeePos, 50);
        var exact = await sut.HasAvailableNumbersAsync(tenantId, DocumentType.DeePos, 90);
        var tooMany = await sut.HasAvailableNumbersAsync(tenantId, DocumentType.DeePos, 91);

        enough.Should().BeTrue();
        exact.Should().BeTrue(
            "requesting exactly the remaining count must succeed (>= comparison)");
        tooMany.Should().BeFalse();
    }

    [Fact]
    public async Task HasAvailableNumbersAsync_returns_false_when_no_active_resolucion_exists()
    {
        await using var ctx = NewContext();

        var sut = new NumberingService(ctx);

        var available = await sut.HasAvailableNumbersAsync(
            Guid.CreateVersion7(), DocumentType.DeePos, 1);

        available.Should().BeFalse(
            "with no active resolución the dispatcher has zero available numbers");
    }

    // ===== Helpers =====================================================

    private static CassamDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CassamDbContext>()
            .UseInMemoryDatabase($"numbering-tests-{Guid.NewGuid():N}")
            // The InMemory provider does not support transactions; the
            // production code's BeginTransactionAsync call surfaces as a
            // warning event. Suppress it — these unit tests cover the
            // application logic, NOT the row-locking contract. The
            // concurrency guarantee is exercised by
            // NumberingConcurrencyTests against Testcontainers PostgreSQL.
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<Resolucion> SeedResolucionAsync(
        CassamDbContext ctx,
        Guid tenantId,
        DocumentType documentType,
        long rangeStart,
        long rangeEnd,
        long currentNumber)
    {
        var resolucion = new Resolucion
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            DocumentType = documentType,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            CurrentNumber = currentNumber,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            SoftwareTechnicalKeyId = Guid.CreateVersion7(),
            Status = ResolucionStatus.Active,
        };
        ctx.Resoluciones.Add(resolucion);
        await ctx.SaveChangesAsync();
        return resolucion;
    }
}
