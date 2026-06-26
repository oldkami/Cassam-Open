using System;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using FluentAssertions;

namespace Cassam.Ui.Tests.Manager;

/// <summary>
/// Unit tests for <see cref="CashSessionManagementViewModel"/>'s
/// open/close + variance state machine (T2.09.c, REQ-CORE-10).
/// </summary>
public class CashSessionManagementViewModelTests
{
    private static CashSessionManagementViewModel BuildVm(
        out InMemoryCashSessionService service,
        out StubTenantSessionState session)
    {
        service = new InMemoryCashSessionService();
        session = new StubTenantSessionState();
        return new CashSessionManagementViewModel(service, session);
    }

    [Fact]
    public async Task RefreshAsync_reports_no_open_session_initially()
    {
        var vm = BuildVm(out _, out _);

        await vm.RefreshAsync();

        vm.OpenSession.Should().BeNull();
        vm.RecentSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task BeginOpen_then_confirm_opens_session()
    {
        var vm = BuildVm(out _, out _);

        vm.BeginOpen();
        vm.OpeningAmountInput = 5000m;
        await vm.ConfirmOpenAsync();

        vm.OpenSession.Should().NotBeNull();
        vm.OpenSession!.OpeningAmount.Should().Be(5000m);
        vm.OpenSession!.OpenedByUserName.Should().Be("Administrador Demo");
        vm.StatusMessage.Should().Contain("abierta");
    }

    [Fact]
    public async Task ConfirmOpen_rejects_negative_amount()
    {
        var vm = BuildVm(out _, out _);

        vm.BeginOpen();
        vm.OpeningAmountInput = -100m;
        await vm.ConfirmOpenAsync();

        vm.OpenSession.Should().BeNull();
        vm.StatusMessage.Should().Contain("negativo");
    }

    [Fact]
    public async Task BeginClose_then_confirm_closes_with_variance_zero_when_closing_equals_opening()
    {
        var vm = BuildVm(out _, out _);

        vm.BeginOpen();
        vm.OpeningAmountInput = 5000m;
        await vm.ConfirmOpenAsync();

        vm.BeginClose();
        vm.ClosingAmountInput = 5000m;
        await vm.ConfirmCloseAsync();

        vm.OpenSession.Should().BeNull("the session is closed after ConfirmClose");
        vm.StatusMessage.Should().Contain("Cuadre exacto");
        vm.RecentSessions.Should().HaveCount(1);
        vm.RecentSessions[0].VarianceAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ConfirmClose_with_shortage_surfaces_faltante()
    {
        var vm = BuildVm(out _, out _);

        vm.BeginOpen();
        vm.OpeningAmountInput = 5000m;
        await vm.ConfirmOpenAsync();

        vm.BeginClose();
        vm.ClosingAmountInput = 4000m;
        await vm.ConfirmCloseAsync();

        vm.StatusMessage.Should().Contain("Faltante de 1.000");
        vm.RecentSessions[0].VarianceAmount.Should().Be(-1000m);
    }

    [Fact]
    public async Task ConfirmClose_with_overage_surfaces_sobrante()
    {
        var vm = BuildVm(out _, out _);

        vm.BeginOpen();
        vm.OpeningAmountInput = 5000m;
        await vm.ConfirmOpenAsync();

        vm.BeginClose();
        vm.ClosingAmountInput = 6000m;
        await vm.ConfirmCloseAsync();

        vm.StatusMessage.Should().Contain("Sobrante de 1.000");
        vm.RecentSessions[0].VarianceAmount.Should().Be(1000m);
    }

    [Fact]
    public async Task ConfirmClose_with_no_open_session_surfaces_error()
    {
        var vm = BuildVm(out _, out _);

        vm.BeginClose();
        vm.ClosingAmountInput = 1000m;
        await vm.ConfirmCloseAsync();

        vm.StatusMessage.Should().Contain("No hay");
    }

    [Fact]
    public async Task BeginOpen_while_a_session_is_open_surfaces_error()
    {
        var vm = BuildVm(out _, out _);

        vm.BeginOpen();
        vm.OpeningAmountInput = 1000m;
        await vm.ConfirmOpenAsync();

        // Try opening a second session.
        vm.BeginOpen();
        vm.OpeningAmountInput = 2000m;
        await vm.ConfirmOpenAsync();

        vm.StatusMessage.Should().Contain("Ya existe");
    }

    [Fact]
    public void CancelOpen_clears_dialog_state()
    {
        var vm = BuildVm(out _, out _);

        vm.BeginOpen();
        vm.CancelOpen();

        vm.IsOpening.Should().BeFalse();
    }

    [Fact]
    public void CancelClose_clears_dialog_state()
    {
        var vm = BuildVm(out _, out _);

        vm.BeginClose();
        vm.CancelClose();

        vm.IsClosing.Should().BeFalse();
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var act1 = () => new CashSessionManagementViewModel(null!, new StubTenantSessionState());
        var act2 = () => new CashSessionManagementViewModel(new InMemoryCashSessionService(), null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}