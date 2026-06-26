using System;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using Cassam.Ui.Hardware.Common.Stubs;
using FluentAssertions;

namespace Cassam.Ui.Tests.Manager;

/// <summary>
/// Unit tests for <see cref="DianStatusPanelViewModel"/>'s
/// polling + retry/void action state machine (T2.10, REQ-UI-08,
/// REQ-DIAN-06).
/// </summary>
public class DianStatusPanelViewModelTests
{
    private static DianStatusPanelViewModel BuildVm(
        out StubDianStatusProvider dian,
        out StubSyncStateProvider sync,
        out StubRetryableDianAction retry,
        out StubVoidableDianAction voidAction)
    {
        dian = new StubDianStatusProvider();
        sync = new StubSyncStateProvider();
        retry = new StubRetryableDianAction();
        voidAction = new StubVoidableDianAction();
        return new DianStatusPanelViewModel(dian, sync, retry, voidAction);
    }

    [Fact]
    public async Task RefreshAsync_populates_status_from_providers()
    {
        var vm = BuildVm(out _, out _, out _, out _);

        await vm.RefreshAsync();

        vm.ActiveResolucionesCount.Should().Be(0);
        vm.CertificatesExpiringIn30Days.Should().Be(0);
        vm.LastTransmissionResultCode.Should().Be(0);
        vm.LastTransmissionResultMessage.Should().Be("Validación OK");
        vm.ContingencyQueueDepth.Should().Be(0);
        vm.IsInContingencyMode.Should().BeFalse();
        vm.SyncQueueDepth.Should().Be(0);
        vm.SyncMode.Should().Be(SyncMode.Online);
        vm.LastSyncAt.Should().NotBeNull();
    }

    [Fact]
    public void LastTransmissionBadgeColor_maps_codes_to_colors()
    {
        var vm = BuildVm(out _, out _, out _, out _);

        vm.LastTransmissionResultCode = 0;
        vm.LastTransmissionBadgeColor.Should().Be("Green");

        vm.LastTransmissionResultCode = 1;
        vm.LastTransmissionBadgeColor.Should().Be("Red");

        vm.LastTransmissionResultCode = 2;
        vm.LastTransmissionBadgeColor.Should().Be("Orange");
    }

    [Fact]
    public async Task RefreshAsync_when_provider_throws_surfaces_error_in_status()
    {
        var brokenDian = new BrokenDianStatusProvider();
        var sync = new StubSyncStateProvider();
        var retry = new StubRetryableDianAction();
        var voidAction = new StubVoidableDianAction();
        var vm = new DianStatusPanelViewModel(brokenDian, sync, retry, voidAction);

        await vm.RefreshAsync();

        vm.StatusMessage.Should().Contain("Error al actualizar");
    }

    [Fact]
    public async Task RetryLastRejectedAsync_with_stub_surfaces_disponible_message()
    {
        var vm = BuildVm(out _, out _, out _, out _);

        await vm.RetryLastRejectedAsync();

        vm.StatusMessage.Should().Contain("Disponible tras habilitar");
    }

    [Fact]
    public async Task VoidLastRejectedAsync_with_stub_surfaces_disponible_message()
    {
        var vm = BuildVm(out _, out _, out _, out _);

        await vm.VoidLastRejectedAsync();

        vm.StatusMessage.Should().Contain("Disponible tras habilitar");
    }

    [Fact]
    public void StartPolling_then_StopPolling_does_not_throw()
    {
        var vm = BuildVm(out _, out _, out _, out _);

        vm.StartPolling();
        vm.StopPolling();
        // Starting again after stop should not throw.
        vm.StartPolling();
        vm.StopPolling();
    }

    [Fact]
    public void Dispose_stops_polling_and_suppresses_finalize()
    {
        var vm = BuildVm(out _, out _, out _, out _);

        vm.StartPolling();
        vm.Dispose();

        // Disposing twice is safe.
        var act = () => vm.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var dian = new StubDianStatusProvider();
        var sync = new StubSyncStateProvider();
        var retry = new StubRetryableDianAction();
        var voidAction = new StubVoidableDianAction();

        var act1 = () => new DianStatusPanelViewModel(null!, sync, retry, voidAction);
        var act2 = () => new DianStatusPanelViewModel(dian, null!, retry, voidAction);
        var act3 = () => new DianStatusPanelViewModel(dian, sync, null!, voidAction);
        var act4 = () => new DianStatusPanelViewModel(dian, sync, retry, null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
        act4.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Helper — a <see cref="IDianStatusProvider"/> that throws
    /// from <c>GetStatusAsync</c> so the VM's error-handling path
    /// can be exercised in isolation.
    /// </summary>
    private sealed class BrokenDianStatusProvider : IDianStatusProvider
    {
        public Task<DianStatus> GetStatusAsync(CancellationToken ct) =>
            throw new InvalidOperationException("simulated outage");
    }
}