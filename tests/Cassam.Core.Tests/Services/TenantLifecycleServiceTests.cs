using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.Services;
using Cassam.Core.Persistence;
using Cassam.Core.Persistence.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="TenantLifecycleService"/>. These tests
/// exercise the soft-delete entry point, the 5-year retention
/// boundary, and the per-document retention lock against the EF Core
/// InMemory provider. The DB-trigger side of the lock invariant
/// (SCN-CORE-13) is verified by the integration test in
/// <c>TenantRetentionLockTests</c> — the InMemory provider does not
/// honor raw-SQL triggers.
///
/// <para>
/// Time is controlled with <see cref="FakeTimeProvider"/> so the
/// 5-year retention boundary is exercised deterministically without
/// sleeping the test process. The service takes a
/// <see cref="TimeProvider"/> in its constructor per the standard
/// .NET 8+ DI pattern.
/// </para>
/// </summary>
public class TenantLifecycleServiceTests
{
    // ===== RequestDeletionAsync =========================================

    [Fact]
    public async Task RequestDeletionAsync_transitions_active_to_deleted_and_stamps_deleted_at()
    {
        // Happy path: an ACTIVE tenant must transition to DELETED and
        // have DeletedAt stamped. The status flip and timestamp MUST
        // land atomically in the same SaveChanges call.
        await using var ctx = NewContext();
        var tenant = await SeedTenant(ctx, status: TenantStatus.Active);
        var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero));

        var sut = new TenantLifecycleService(ctx, fakeClock);

        await sut.RequestDeletionAsync(tenant.Id, actorUserId: Guid.CreateVersion7());

        // Reload via a fresh query so we see the persisted state, not
        // the change-tracker view.
                var saved = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenant.Id);
        saved.Status.Should().Be(TenantStatus.Deleted,
            "the service must flip the status from Active to Deleted");
        saved.DeletedAt.Should().Be(fakeClock.UtcNowUtcDateTime,
            "DeletedAt is the anchor for the 5-year retention lock and must match the service clock");
    }

    [Fact]
    public async Task RequestDeletionAsync_throws_when_tenant_already_deleted()
    {
        // Double-delete guard: calling RequestDeletion twice on the
        // same tenant must throw so the operator can investigate the
        // workflow bug rather than silently no-op (which would hide
        // a second audit row).
        await using var ctx = NewContext();
        var tenant = await SeedTenant(ctx, status: TenantStatus.Active);
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        await sut.RequestDeletionAsync(tenant.Id, actorUserId: Guid.CreateVersion7());

        var act = async () => await sut.RequestDeletionAsync(
            tenant.Id, actorUserId: Guid.CreateVersion7());

        await act.Should().ThrowAsync<InvalidOperationException>(
            "double-delete is rejected so the operator can investigate");
    }

    [Fact]
    public async Task RequestDeletionAsync_throws_for_trial_tenant()
    {
        // Trial tenants must be resolved (activated or hard-cancelled)
        // before deletion. The service refuses so the operator cannot
        // accidentally orphan a Trial tenant whose subscription
        // accounting never ran.
        await using var ctx = NewContext();
        var tenant = await SeedTenant(ctx, status: TenantStatus.Trial);
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        var act = async () => await sut.RequestDeletionAsync(
            tenant.Id, actorUserId: Guid.CreateVersion7());

        await act.Should().ThrowAsync<InvalidOperationException>(
            "Trial tenants must be activated or cancelled before deletion");
    }

    [Fact]
    public async Task RequestDeletionAsync_throws_for_suspended_tenant()
    {
        // Suspended tenants must be reactivated or formally cancelled
        // before deletion. Same defensive guard as Trial.
        await using var ctx = NewContext();
        var tenant = await SeedTenant(ctx, status: TenantStatus.Suspended);
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        var act = async () => await sut.RequestDeletionAsync(
            tenant.Id, actorUserId: Guid.CreateVersion7());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RequestDeletionAsync_throws_when_tenant_does_not_exist()
    {
        await using var ctx = NewContext();
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        var act = async () => await sut.RequestDeletionAsync(
            Guid.CreateVersion7(), actorUserId: Guid.CreateVersion7());

        await act.Should().ThrowAsync<InvalidOperationException>(
            "RequestDeletion on a missing tenant must fail loud so the caller can investigate");
    }

    [Fact]
    public async Task RequestDeletionAsync_writes_audit_log_row_with_actor_and_states()
    {
        // Per REQ-CORE-03, every fiscal mutation MUST emit an audit row.
        // The deletion audit row captures the actor and the
        // before/after status so the compliance officer can replay
        // the workflow from the audit trail alone.
        await using var ctx = NewContext();
        var tenant = await SeedTenant(ctx, status: TenantStatus.Active);
        var actor = Guid.CreateVersion7();
        var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero));
        var sut = new TenantLifecycleService(ctx, fakeClock);

        await sut.RequestDeletionAsync(tenant.Id, actor);

        var auditRow = ctx.AuditLog.Single(a => a.EntityId == tenant.Id);
        auditRow.Action.Should().Be("TENANT_SOFT_DELETED",
            "the audit row action is the stable identifier the dashboard queries");
        auditRow.ActorUserId.Should().Be(actor,
            "the audit row records WHO triggered the deletion");
        auditRow.EntityType.Should().Be("tenant");
        auditRow.BeforeState.Should().Be(nameof(TenantStatus.Active));
        auditRow.AfterState.Should().Be(nameof(TenantStatus.Deleted));
        auditRow.OccurredAt.Should().Be(fakeClock.UtcNowUtcDateTime);
        auditRow.TenantId.Should().Be(tenant.Id);
    }

    [Fact]
    public async Task RequestDeletionAsync_does_not_soft_delete_child_documents()
    {
        // REQ-MT-05: fiscal data MUST survive tenant deletion for the
        // regulatory retention window. The service must NOT touch
        // child entities' DeletedAt when the tenant is soft-deleted —
        // the per-entity query filters continue to surface the rows.
        await using var ctx = NewContext();
        var tenant = await SeedTenant(ctx, status: TenantStatus.Active);
        var documento = await SeedDocumento(ctx, tenant.Id, estado: EstadoDocumento.Transmitted);
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        await sut.RequestDeletionAsync(tenant.Id, actorUserId: Guid.CreateVersion7());

                var savedDocumento = await ctx.DocumentosElectronicos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(d => d.Id == documento.Id);
        savedDocumento.DeletedAt.Should().BeNull(
            "tenant soft-delete MUST NOT cascade into child entities — the retention lock handles them separately");
    }

    // ===== IsFiscalRetentionExpiredAsync =================================

    [Fact]
    public async Task IsFiscalRetentionExpiredAsync_returns_false_when_tenant_is_active()
    {
        // An undeleted tenant has DeletedAt == null; the retention
        // window has not even started.
        await using var ctx = NewContext();
        var tenant = await SeedTenant(ctx, status: TenantStatus.Active);
        var fakeClock = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero));
        var sut = new TenantLifecycleService(ctx, fakeClock);

        var expired = await sut.IsFiscalRetentionExpiredAsync(tenant.Id);

        expired.Should().BeFalse(
            "an undeleted tenant cannot have an expired retention window");
    }

    [Fact]
    public async Task IsFiscalRetentionExpiredAsync_returns_false_when_deleted_recently()
    {
        // Deleted 1 year ago — still inside the 5-year window.
        await using var ctx = NewContext();
        var now = new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero);
        var oneYearAgo = now.AddYears(-1);
        var tenant = await SeedTenant(ctx,
            status: TenantStatus.Deleted,
            deletedAt: oneYearAgo.UtcDateTime);
        var fakeClock = new FakeTimeProvider(now);
        var sut = new TenantLifecycleService(ctx, fakeClock);

        var expired = await sut.IsFiscalRetentionExpiredAsync(tenant.Id);

        expired.Should().BeFalse(
            "1 year is well within the 5-year retention window");
    }

    [Fact]
    public async Task IsFiscalRetentionExpiredAsync_returns_true_when_deleted_5_years_ago()
    {
        // Boundary: exactly 5 years to the day. The contract uses
        // `>=` so the lock fires on the 5-year mark itself.
        await using var ctx = NewContext();
        var now = new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero);
        var fiveYearsAgo = now.AddYears(-5);
        var tenant = await SeedTenant(ctx,
            status: TenantStatus.Deleted,
            deletedAt: fiveYearsAgo.UtcDateTime);
        var fakeClock = new FakeTimeProvider(now);
        var sut = new TenantLifecycleService(ctx, fakeClock);

        var expired = await sut.IsFiscalRetentionExpiredAsync(tenant.Id);

        expired.Should().BeTrue(
            "the 5-year retention boundary is inclusive — >= 5 years fires the lock");
    }

    [Fact]
    public async Task IsFiscalRetentionExpiredAsync_returns_false_just_before_5_years()
    {
        // Off-by-one guard: 5 years - 1 day must NOT be expired.
        // Without this guard the worker would lock documents a day
        // early and confuse the compliance officer.
        await using var ctx = NewContext();
        var now = new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero);
        var almostFiveYears = now.AddYears(-5).AddDays(2);
        var tenant = await SeedTenant(ctx,
            status: TenantStatus.Deleted,
            deletedAt: almostFiveYears.UtcDateTime);
        var fakeClock = new FakeTimeProvider(now);
        var sut = new TenantLifecycleService(ctx, fakeClock);

        var expired = await sut.IsFiscalRetentionExpiredAsync(tenant.Id);

        expired.Should().BeFalse(
            "5 years - 1 day must NOT trigger the lock — the boundary is inclusive");
    }

    [Fact]
    public async Task IsFiscalRetentionExpiredAsync_returns_true_well_past_5_years()
    {
        // 10-year-old deletion is well past retention — the lock MUST
        // be applied immediately.
        await using var ctx = NewContext();
        var now = new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero);
        var decadeAgo = now.AddYears(-10);
        var tenant = await SeedTenant(ctx,
            status: TenantStatus.Deleted,
            deletedAt: decadeAgo.UtcDateTime);
        var fakeClock = new FakeTimeProvider(now);
        var sut = new TenantLifecycleService(ctx, fakeClock);

        var expired = await sut.IsFiscalRetentionExpiredAsync(tenant.Id);

        expired.Should().BeTrue();
    }

    // ===== FindDeletionPendingTenantsAsync ==============================

    [Fact]
    public async Task FindDeletionPendingTenantsAsync_returns_only_deleted_tenants()
    {
        // The retention worker iterates this list. Non-deleted tenants
        // must be filtered out so the worker does not waste cycles
        // checking their DeletedAt.
        await using var ctx = NewContext();
        var tenantA = await SeedTenant(ctx, status: TenantStatus.Deleted,
            deletedAt: DateTime.UtcNow.AddYears(-6));
        await SeedTenant(ctx, status: TenantStatus.Active);
        await SeedTenant(ctx, status: TenantStatus.Trial);
        var tenantB = await SeedTenant(ctx, status: TenantStatus.Deleted,
            deletedAt: DateTime.UtcNow.AddYears(-1));
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        var results = await sut.FindDeletionPendingTenantsAsync();

        results.Should().HaveCount(2);
        results.Select(t => t.Id).Should().BeEquivalentTo(new[] { tenantA.Id, tenantB.Id });
    }

    [Fact]
    public async Task FindDeletionPendingTenantsAsync_orders_oldest_first()
    {
        // The worker locks documents oldest-first so the most-aged
        // records freeze first — this matches the compliance officer's
        // "freeze the riskiest records first" workflow.
        await using var ctx = NewContext();
        var newest = await SeedTenant(ctx, status: TenantStatus.Deleted,
            deletedAt: DateTime.UtcNow.AddYears(-1));
        var oldest = await SeedTenant(ctx, status: TenantStatus.Deleted,
            deletedAt: DateTime.UtcNow.AddYears(-6));
        var middle = await SeedTenant(ctx, status: TenantStatus.Deleted,
            deletedAt: DateTime.UtcNow.AddYears(-3));
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        var results = await sut.FindDeletionPendingTenantsAsync();

        results.Select(t => t.Id).Should().Equal(oldest.Id, middle.Id, newest.Id);
    }

    [Fact]
    public async Task FindDeletionPendingTenantsAsync_returns_empty_when_no_deleted_tenants()
    {
        await using var ctx = NewContext();
        await SeedTenant(ctx, status: TenantStatus.Active);
        await SeedTenant(ctx, status: TenantStatus.Trial);
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        var results = await sut.FindDeletionPendingTenantsAsync();

        results.Should().BeEmpty();
    }

    // ===== EnforceFiscalRetentionAsync ===================================

    [Fact]
    public async Task EnforceFiscalRetentionAsync_stamps_retention_locked_at_on_unlocked_documents()
    {
        // The lock fires on every document with retention_locked_at =
        // NULL. After the call, the timestamp must be set AND equal to
        // the service clock (so the worker has a single anchor for
        // audit purposes).
        await using var ctx = NewContext();
        var tenant = await SeedTenant(ctx, status: TenantStatus.Deleted,
            deletedAt: DateTime.UtcNow.AddYears(-6));
        var doc1 = await SeedDocumento(ctx, tenant.Id, estado: EstadoDocumento.Transmitted);
        var doc2 = await SeedDocumento(ctx, tenant.Id, estado: EstadoDocumento.Transmitted);
        var fakeClock = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero));
        var sut = new TenantLifecycleService(ctx, fakeClock);

        await sut.EnforceFiscalRetentionAsync(tenant);

                var doc1Reloaded = await ctx.DocumentosElectronicos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(d => d.Id == doc1.Id);
        var doc2Reloaded = await ctx.DocumentosElectronicos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(d => d.Id == doc2.Id);
        doc1Reloaded.RetentionLockedAt.Should().Be(fakeClock.UtcNowUtcDateTime,
            "the lock timestamp must match the service clock — the audit row uses the same anchor");
        doc2Reloaded.RetentionLockedAt.Should().Be(fakeClock.UtcNowUtcDateTime);
    }

    [Fact]
    public async Task EnforceFiscalRetentionAsync_skips_already_locked_documents()
    {
        // Documents already locked (retention_locked_at != null) are
        // idempotently skipped — calling EnforceFiscalRetention twice
        // must not re-stamp the lock (the DB trigger would block the
        // UPDATE anyway, but the InMemory provider does not enforce
        // it; the application-level filter is the friendly gate).
        await using var ctx = NewContext();
        var lockTimestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tenant = await SeedTenant(ctx, status: TenantStatus.Deleted,
            deletedAt: DateTime.UtcNow.AddYears(-6));
        var lockedDoc = await SeedDocumento(ctx, tenant.Id, estado: EstadoDocumento.Transmitted);
        lockedDoc.RetentionLockedAt = lockTimestamp;
        await ctx.SaveChangesAsync();

        var freshDoc = await SeedDocumento(ctx, tenant.Id, estado: EstadoDocumento.Transmitted);
        var fakeClock = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero));
        var sut = new TenantLifecycleService(ctx, fakeClock);

        await sut.EnforceFiscalRetentionAsync(tenant);

                var lockedReloaded = await ctx.DocumentosElectronicos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(d => d.Id == lockedDoc.Id);
        var freshReloaded = await ctx.DocumentosElectronicos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(d => d.Id == freshDoc.Id);
        lockedReloaded.RetentionLockedAt.Should().Be(lockTimestamp,
            "already-locked documents must not be re-stamped");
        freshReloaded.RetentionLockedAt.Should().Be(fakeClock.UtcNowUtcDateTime);
    }

    [Fact]
    public async Task EnforceFiscalRetentionAsync_writes_audit_row_with_affected_count()
    {
        // The compliance officer needs to see WHEN the lock fired
        // and HOW MANY documents were frozen. The audit row carries
        // the count in AfterState as "N docs locked" so a single
        // dashboard query surfaces the answer.
        await using var ctx = NewContext();
        var tenant = await SeedTenant(ctx, status: TenantStatus.Deleted,
            deletedAt: DateTime.UtcNow.AddYears(-6));
        await SeedDocumento(ctx, tenant.Id, estado: EstadoDocumento.Transmitted);
        await SeedDocumento(ctx, tenant.Id, estado: EstadoDocumento.Transmitted);
        await SeedDocumento(ctx, tenant.Id, estado: EstadoDocumento.Transmitted);
        var fakeClock = new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero));
        var sut = new TenantLifecycleService(ctx, fakeClock);

        await sut.EnforceFiscalRetentionAsync(tenant);

        var auditRow = ctx.AuditLog.Single(a =>
            a.EntityId == tenant.Id && a.Action == "FISCAL_RETENTION_LOCKED");
        auditRow.AfterState.Should().Be("3 docs locked");
        auditRow.ActorUserId.Should().Be(Guid.Empty,
            "the retention worker is a SYSTEM actor — Guid.Empty is the sentinel");
        auditRow.TenantId.Should().Be(tenant.Id);
    }

    [Fact]
    public async Task EnforceFiscalRetentionAsync_only_affects_target_tenants_documents()
    {
        // The bulk UPDATE is filtered by tenant_id — another tenant's
        // documents must NOT be touched by a retention lock meant for
        // a different tenant. This is the per-tenant isolation
        // invariant.
        await using var ctx = NewContext();
        var deletedTenant = await SeedTenant(ctx, status: TenantStatus.Deleted,
            deletedAt: DateTime.UtcNow.AddYears(-6));
        var liveTenant = await SeedTenant(ctx, status: TenantStatus.Active);
        var deletedDoc = await SeedDocumento(ctx, deletedTenant.Id, estado: EstadoDocumento.Transmitted);
        var liveDoc = await SeedDocumento(ctx, liveTenant.Id, estado: EstadoDocumento.Transmitted);
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider(
            new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero)));

        await sut.EnforceFiscalRetentionAsync(deletedTenant);

                var deletedReloaded = await ctx.DocumentosElectronicos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(d => d.Id == deletedDoc.Id);
        var liveReloaded = await ctx.DocumentosElectronicos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(d => d.Id == liveDoc.Id);
        deletedReloaded.RetentionLockedAt.Should().NotBeNull(
            "the deleted tenant's documents must be locked");
        liveReloaded.RetentionLockedAt.Should().BeNull(
            "another tenant's documents must NOT be affected by the lock");
    }

    // ===== Argument guards ==============================================

    [Fact]
    public async Task RequestDeletionAsync_rejects_empty_tenant_id()
    {
        await using var ctx = NewContext();
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        var act = async () => await sut.RequestDeletionAsync(
            Guid.Empty, actorUserId: Guid.CreateVersion7());

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task IsFiscalRetentionExpiredAsync_rejects_empty_tenant_id()
    {
        await using var ctx = NewContext();
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        var act = async () => await sut.IsFiscalRetentionExpiredAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task EnforceFiscalRetentionAsync_rejects_null_tenant()
    {
        await using var ctx = NewContext();
        var sut = new TenantLifecycleService(ctx, new FakeTimeProvider());

        var act = async () => await sut.EnforceFiscalRetentionAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ===== Helpers ======================================================

    /// <summary>
    /// Builds a fresh InMemory <see cref="CassamDbContext"/> per test.
    /// Each test gets a unique database name so the global query
    /// filters and the InMemory event-id collision detector don't
    /// bleed between tests.
    /// </summary>
    private static CassamDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CassamDbContext>()
            .UseInMemoryDatabase($"tenant-lifecycle-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<Tenant> SeedTenant(
        CassamDbContext ctx,
        TenantStatus status = TenantStatus.Active,
        DateTime? deletedAt = null)
    {
        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            LegalName = $"Test Tenant {Guid.NewGuid():N}".Substring(0, 30),
            Nit = $"NIT-{Guid.NewGuid():N}".Substring(0, 20),
            SubscriptionTier = SubscriptionTier.Free,
            SubscriptionStartedAt = now,
            Status = status,
            DeletedAt = deletedAt,
            CloudTransmissionEnabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();
        return tenant;
    }

    private static async Task<DocumentoElectronico> SeedDocumento(
        CassamDbContext ctx,
        Guid tenantId,
        EstadoDocumento estado = EstadoDocumento.Draft)
    {
        var now = DateTime.UtcNow;
        var doc = new DocumentoElectronico
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            SaleId = null,
            ResolucionId = Guid.CreateVersion7(),
            CertificadoId = Guid.CreateVersion7(),
            SoftwareTechnicalKeyId = Guid.CreateVersion7(),
            DocumentType = DocumentType.FeVenta,
            Numero = 1,
            CufeOrCude = null,
            XmlPayload = "<xml/>",
            SignatureXml = null,
            PdfPath = null,
            Estado = estado,
            VoidedBy = null,
            TransmittedAt = estado == EstadoDocumento.Transmitted ? now : null,
            TransmittedResponseCode = null,
            TransmittedResponseMessage = null,
            RetentionLockedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        ctx.DocumentosElectronicos.Add(doc);
        await ctx.SaveChangesAsync();
        return doc;
    }
}

/// <summary>
/// Test-only <see cref="TimeProvider"/> that returns a fixed
/// <see cref="DateTimeOffset"/>. The service treats
/// <see cref="TimeProvider.GetUtcNow"/> as the canonical "now", so
/// faking it lets us step the clock forward/back without sleeping.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider()
        : this(DateTimeOffset.UtcNow)
    {
    }

    public FakeTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>The UTC wall-clock <see cref="DateTime"/> the service stamps into rows.</summary>
    public DateTime UtcNowUtcDateTime => _now.UtcDateTime;
}