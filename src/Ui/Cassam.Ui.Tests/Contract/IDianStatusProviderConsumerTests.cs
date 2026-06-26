using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using Cassam.Ui.Hardware.Common.Stubs;
using FluentAssertions;

namespace Cassam.Ui.Tests.Contract;

/// <summary>
/// Integration contract test: the manager flow's DIAN status
/// panel must consume <see cref="IDianStatusProvider"/> with the
/// same shape that Phase 4b will produce (design §7.1).
///
/// <para>
/// These tests run the <see cref="DianStatusPanelViewModel"/>'s
/// <c>RefreshAsync</c> against the stub provider and assert the
/// VM's properties land in the expected shapes. The same test
/// suite will run against the Phase 4b real provider to catch
/// any contract drift between the seam and the implementation.
/// </para>
/// </summary>
public class IDianStatusProviderConsumerTests
{
    [Fact]
    public async Task DianStatusPanel_consumes_IDianStatusProvider_with_expected_shape()
    {
        var dian = new StubDianStatusProvider();
        var sync = new StubSyncStateProvider();
        var retry = new StubRetryableDianAction();
        var voidAction = new StubVoidableDianAction();
        var vm = new DianStatusPanelViewModel(dian, sync, retry, voidAction);

        await vm.RefreshAsync();

        // The shape contract: every numeric property on
        // DianStatus must surface as an int on the VM, the
        // IsInContingencyMode bool must surface as a bool, and
        // the LastTransmissionResultCode must surface as int
        // (0/1/2).
        vm.ActiveResolucionesCount.Should().Be(0);
        vm.CertificatesExpiringIn30Days.Should().Be(0);
        vm.LastTransmissionResultCode.Should().Be(0);
        vm.ContingencyQueueDepth.Should().Be(0);
        vm.IsInContingencyMode.Should().BeFalse();
    }

    [Fact]
    public async Task DianStatusPanel_handles_large_ActiveResoluciones_count()
    {
        // Phase 4b will return real counts that can exceed
        // 1000 — the VM's int-typed property must accept the
        // full range.
        var dian = new ConfigurableDianStatusProvider(
            activeResoluciones: 12_345,
            certificatesExpiringIn30Days: 7,
            lastTransmissionResultCode: 1,
            contingencyQueueDepth: 3,
            isInContingencyMode: true);
        var sync = new StubSyncStateProvider();
        var retry = new StubRetryableDianAction();
        var voidAction = new StubVoidableDianAction();
        var vm = new DianStatusPanelViewModel(dian, sync, retry, voidAction);

        await vm.RefreshAsync();

        vm.ActiveResolucionesCount.Should().Be(12_345);
        vm.IsInContingencyMode.Should().BeTrue();
    }

    /// <summary>
    /// Configurable stub for tests that need a specific
    /// <see cref="DianStatus"/> snapshot.
    /// </summary>
    private sealed class ConfigurableDianStatusProvider : IDianStatusProvider
    {
        private readonly DianStatus _fixture;

        public ConfigurableDianStatusProvider(
            int activeResoluciones,
            int certificatesExpiringIn30Days,
            int lastTransmissionResultCode,
            int contingencyQueueDepth,
            bool isInContingencyMode)
        {
            _fixture = new DianStatus(
                ActiveResoluciones: activeResoluciones,
                CertificatesExpiringIn30Days: certificatesExpiringIn30Days,
                LastTransmissionResultCode: lastTransmissionResultCode,
                ContingencyQueueDepth: contingencyQueueDepth,
                IsInContingencyMode: isInContingencyMode);
        }

        public Task<DianStatus> GetStatusAsync(CancellationToken ct) =>
            Task.FromResult(_fixture);
    }
}