using System;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using FluentAssertions;

namespace Cassam.Ui.Tests.Manager;

/// <summary>
/// Unit tests for the cancel-tenant modal view-model
/// (T2.09, REQ-MT-05, SCN-MT-05).
/// </summary>
public class CancelTenantModalTests
{
    private static (CancelTenantModalViewModel vm, InMemoryTenantLifecycleService svc, StubTenantSessionState session) BuildVm()
    {
        var svc = new InMemoryTenantLifecycleService();
        var session = new StubTenantSessionState();
        var vm = new CancelTenantModalViewModel(svc, session);
        return (vm, svc, session);
    }

    [Fact]
    public async Task LoadAsync_populates_tenant_identity_and_retention_date()
    {
        var (vm, _, _) = BuildVm();

        await vm.LoadAsync();

        vm.TenantNit.Should().Be("900111222-3");
        vm.TenantLegalName.Should().Be("Cassam S.A.S.");
        vm.RetentionExpiresAt.Should().NotBeNull();
        vm.RetentionExpiresAt!.Value.Should().BeAfter(DateTime.UtcNow.AddYears(4));
        vm.RetentionExpiresAt!.Value.Should().BeBefore(DateTime.UtcNow.AddYears(6));
    }

    [Fact]
    public async Task CanConfirm_is_false_initially()
    {
        var (vm, _, _) = BuildVm();
        await vm.LoadAsync();
        vm.CanConfirm.Should().BeFalse();
    }

    [Fact]
    public async Task CanConfirm_becomes_true_when_typed_nit_matches()
    {
        var (vm, _, _) = BuildVm();
        await vm.LoadAsync();
        vm.ConfirmationInput = "900111222-3";
        vm.CanConfirm.Should().BeTrue();
    }

    [Fact]
    public async Task CanConfirm_is_case_insensitive()
    {
        var (vm, _, _) = BuildVm();
        await vm.LoadAsync();
        vm.ConfirmationInput = "900111222-3".ToLowerInvariant();
        vm.CanConfirm.Should().BeTrue();
    }

    [Fact]
    public async Task CanConfirm_trims_whitespace()
    {
        var (vm, _, _) = BuildVm();
        await vm.LoadAsync();
        vm.ConfirmationInput = "  900111222-3  ";
        vm.CanConfirm.Should().BeTrue();
    }

    [Fact]
    public async Task CanConfirm_is_false_for_different_nit()
    {
        var (vm, _, _) = BuildVm();
        await vm.LoadAsync();
        vm.ConfirmationInput = "999999999-9";
        vm.CanConfirm.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmCancelAsync_calls_service_and_sets_IsCancelled()
    {
        var (vm, svc, session) = BuildVm();
        await vm.LoadAsync();
        vm.ConfirmationInput = vm.TenantNit;

        await vm.ConfirmCancelAsync();

        vm.IsCancelled.Should().BeTrue();
        svc.DeletedTenants.Should().ContainSingle(
            t => t == session._snapshot_for_testing().TenantId);
        svc.LastRequest.Should().NotBeNull();
        svc.LastRequest!.Value.ActorUserId.Should().Be(session._snapshot_for_testing().UserId);
    }

    [Fact]
    public async Task ConfirmCancelAsync_does_nothing_when_nit_mismatch()
    {
        var (vm, svc, _) = BuildVm();
        await vm.LoadAsync();
        vm.ConfirmationInput = "wrong-nit";

        await vm.ConfirmCancelAsync();

        vm.IsCancelled.Should().BeFalse();
        svc.DeletedTenants.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmCancelAsync_surfaces_service_errors()
    {
        var svc = new InMemoryTenantLifecycleService();
        var session = new StubTenantSessionState();
        var vm = new CancelTenantModalViewModel(svc, session);

        await vm.LoadAsync();
        vm.ConfirmationInput = vm.TenantNit;

        // Pre-populate the deletion set to simulate an already-deleted tenant.
        await svc.RequestDeletionAsync(
            session._snapshot_for_testing().TenantId,
            session._snapshot_for_testing().UserId,
            default);

        // Now request deletion again — service should throw.
        vm.ConfirmationInput = vm.TenantNit;
        await vm.ConfirmCancelAsync();

        vm.IsCancelled.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("ya fue cancelado");
    }

    [Fact]
    public async Task ConfirmCancelAsync_is_idempotent_after_success()
    {
        var (vm, svc, _) = BuildVm();
        await vm.LoadAsync();
        vm.ConfirmationInput = vm.TenantNit;

        await vm.ConfirmCancelAsync();
        // Reset state — simulate user re-opening the dialog.
        var vm2 = new CancelTenantModalViewModel(svc, new StubTenantSessionState());
        await vm2.LoadAsync();
        vm2.ConfirmationInput = vm2.TenantNit;

        await vm2.ConfirmCancelAsync();

        vm2.IsCancelled.Should().BeFalse();
        vm2.ErrorMessage.Should().Contain("ya fue cancelado");
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var svc = new InMemoryTenantLifecycleService();
        var session = new StubTenantSessionState();

        var act1 = () => new CancelTenantModalViewModel(null!, session);
        var act2 = () => new CancelTenantModalViewModel(svc, null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}

/// <summary>
/// Test-only extension to expose the underlying fixture snapshot.
/// The InMemoryTenantLifecycleService stores the last call so
/// tests can assert the tenant id + actor user id were passed
/// correctly.
/// </summary>
internal static class StubTenantSessionStateTestExtensions
{
    public static TenantSessionSnapshot _snapshot_for_testing(this StubTenantSessionState session)
    {
        return session.GetSnapshotAsync(default).GetAwaiter().GetResult();
    }
}