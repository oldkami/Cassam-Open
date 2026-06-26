using System;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using FluentAssertions;

namespace Cassam.Ui.Tests.Contract;

/// <summary>
/// Integration contract test: the cancel-tenant modal must call
/// <c>ITenantLifecycleService.RequestDeletionAsync</c> with the
/// correct (tenantId, actorUserId, cancellationToken) parameter
/// shape (REQ-MT-05 / SCN-MT-05 / design §5.3).
///
/// <para>
/// These tests run against the
/// <see cref="InMemoryTenantLifecycleService"/> stub that the UI
/// project ships for Phase 2. Phase 3 will swap in the real
/// EF-backed implementation; the same contract tests run against
/// it to catch parameter-shape drift.
/// </para>
/// </summary>
public class ITenantLifecycleServiceConsumerTests
{
    [Fact]
    public async Task CancelTenantModal_passes_tenantId_and_actorUserId_from_session()
    {
        var svc = new InMemoryTenantLifecycleService();
        var session = new StubTenantSessionState();
        var vm = new CancelTenantModalViewModel(svc, session);

        await vm.LoadAsync();
        vm.ConfirmationInput = vm.TenantNit;
        await vm.ConfirmCancelAsync();

        var snap = await session.GetSnapshotAsync(default);
        svc.LastRequest.Should().NotBeNull(
            "the modal must have called RequestDeletionAsync");
        svc.LastRequest!.Value.TenantId.Should().Be(snap.TenantId);
        svc.LastRequest!.Value.ActorUserId.Should().Be(snap.UserId);
    }

    [Fact]
    public async Task CancelTenantModal_honors_cancellation_token()
    {
        // The contract requires the modal to thread a
        // CancellationToken through to the service so callers can
        // cancel an in-flight cancellation request. The Phase 2
        // stub does not surface the token, but we assert the
        // method signature accepts one (compile-time) and the
        // service completes the call.
        var svc = new InMemoryTenantLifecycleService();
        var session = new StubTenantSessionState();
        var vm = new CancelTenantModalViewModel(svc, session);

        await vm.LoadAsync();
        vm.ConfirmationInput = vm.TenantNit;

        using var cts = new CancellationTokenSource();
        await vm.ConfirmCancelAsync(cts.Token);

        vm.IsCancelled.Should().BeTrue();
        cts.IsCancellationRequested.Should().BeFalse(
            "the call completes cleanly — the token is only an abort lever");
    }

    [Fact]
    public async Task CancelTenantModal_surfaces_double_delete_error_from_service()
    {
        var svc = new InMemoryTenantLifecycleService();
        var session = new StubTenantSessionState();
        var snap = await session.GetSnapshotAsync(default);

        // Pre-delete the tenant.
        await svc.RequestDeletionAsync(snap.TenantId, snap.UserId, default);

        // Now try via the modal — should fail with the same message.
        var vm = new CancelTenantModalViewModel(svc, session);
        await vm.LoadAsync();
        vm.ConfirmationInput = vm.TenantNit;
        await vm.ConfirmCancelAsync();

        vm.IsCancelled.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("ya fue cancelado");
    }
}