using Cassam.Ui.Hardware.Common.Stubs;
using FluentAssertions;

namespace Cassam.Ui.Tests.Hardware;

public class StubProviderTests
{
    [Fact]
    public async Task StubDianStatusProvider_returns_clean_state()
    {
        var stub = new StubDianStatusProvider();

        var status = await stub.GetStatusAsync(CancellationToken.None);

        status.ActiveResoluciones.Should().Be(0);
        status.CertificatesExpiringIn30Days.Should().Be(0);
        status.LastTransmissionResultCode.Should().Be(0);
        status.ContingencyQueueDepth.Should().Be(0);
        status.IsInContingencyMode.Should().BeFalse();
    }

    [Fact]
    public async Task StubSyncStateProvider_returns_online()
    {
        var stub = new StubSyncStateProvider();

        var state = await stub.GetStateAsync(CancellationToken.None);

        state.QueueDepth.Should().Be(0);
        state.IsOnline.Should().BeTrue();
        state.Mode.Should().Be(SyncMode.Online);
        state.LastSyncAt.Should().NotBeNull();
    }
}
