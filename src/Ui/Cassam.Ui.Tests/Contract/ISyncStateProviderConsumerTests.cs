using System;
using System.Threading;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using Cassam.Ui.Hardware.Common.Stubs;
using FluentAssertions;

namespace Cassam.Ui.Tests.Contract;

/// <summary>
/// Integration contract test: the manager flow + the cashier
/// flow's banner must consume <see cref="ISyncStateProvider"/>
/// with the same shape that Phase 4a will produce.
/// </summary>
public class ISyncStateProviderConsumerTests
{
    [Fact]
    public async Task DianStatusPanel_consumes_ISyncStateProvider_with_expected_shape()
    {
        var dian = new StubDianStatusProvider();
        var sync = new ConfigurableSyncStateProvider(
            queueDepth: 42,
            lastSyncAt: new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero),
            mode: SyncMode.LimitedConnectivity);
        var retry = new StubRetryableDianAction();
        var voidAction = new StubVoidableDianAction();
        var vm = new DianStatusPanelViewModel(dian, sync, retry, voidAction);

        await vm.RefreshAsync();

        vm.SyncQueueDepth.Should().Be(42);
        vm.LastSyncAt.Should().Be(new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero));
        vm.SyncMode.Should().Be(SyncMode.LimitedConnectivity);
    }

    [Fact]
    public async Task DianStatusPanel_surfaces_offline_mode()
    {
        var dian = new StubDianStatusProvider();
        var sync = new ConfigurableSyncStateProvider(
            queueDepth: 0,
            lastSyncAt: null,
            mode: SyncMode.Offline);
        var retry = new StubRetryableDianAction();
        var voidAction = new StubVoidableDianAction();
        var vm = new DianStatusPanelViewModel(dian, sync, retry, voidAction);

        await vm.RefreshAsync();

        vm.SyncMode.Should().Be(SyncMode.Offline);
        vm.LastSyncAt.Should().BeNull();
    }

    [Fact]
    public async Task DianStatusPanel_handles_high_queue_depth()
    {
        var dian = new StubDianStatusProvider();
        var sync = new ConfigurableSyncStateProvider(
            queueDepth: 9_999,
            lastSyncAt: DateTimeOffset.UtcNow,
            mode: SyncMode.Online);
        var retry = new StubRetryableDianAction();
        var voidAction = new StubVoidableDianAction();
        var vm = new DianStatusPanelViewModel(dian, sync, retry, voidAction);

        await vm.RefreshAsync();

        vm.SyncQueueDepth.Should().Be(9_999);
    }

    /// <summary>
    /// Configurable stub for tests that need a specific
    /// <see cref="SyncState"/> snapshot.
    /// </summary>
    private sealed class ConfigurableSyncStateProvider : ISyncStateProvider
    {
        private readonly SyncState _fixture;

        public ConfigurableSyncStateProvider(
            int queueDepth,
            DateTimeOffset? lastSyncAt,
            SyncMode mode)
        {
            _fixture = new SyncState(
                QueueDepth: queueDepth,
                LastSyncAt: lastSyncAt,
                IsOnline: mode == SyncMode.Online,
                Mode: mode);
        }

        public Task<SyncState> GetStateAsync(CancellationToken ct) =>
            Task.FromResult(_fixture);
    }
}