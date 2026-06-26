using System;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using FluentAssertions;

namespace Cassam.Ui.Tests.Manager;

/// <summary>
/// Unit tests for <see cref="CustomerManagementViewModel"/>'s
/// CRUD state machine (T2.09.b, REQ-UI-03).
/// </summary>
public class CustomerManagementViewModelTests
{
    private static CustomerManagementViewModel BuildVm(out InMemoryCustomerService service)
    {
        service = new InMemoryCustomerService();
        return new CustomerManagementViewModel(service);
    }

    [Fact]
    public async Task RefreshAsync_populates_list_with_seeded_customers()
    {
        var vm = BuildVm(out _);

        await vm.RefreshAsync();

        vm.Customers.Should().HaveCount(3);
        vm.Customers.Should().Contain(c => c.DocumentNumber == "79123456");
    }

    [Fact]
    public async Task SearchText_filters_results_by_document_number()
    {
        var vm = BuildVm(out _);

        vm.SearchText = "900123456";
        await Task.Yield();
        await vm.RefreshAsync();

        vm.Customers.Should().ContainSingle(c => c.Name.StartsWith("Comercial"));
    }

    [Fact]
    public async Task SearchText_filters_results_by_name()
    {
        var vm = BuildVm(out _);

        vm.SearchText = "Pérez";
        await Task.Yield();
        await vm.RefreshAsync();

        vm.Customers.Should().ContainSingle(c => c.DocumentNumber == "52345678");
    }

    [Fact]
    public void BeginAdd_populates_editing_buffer()
    {
        var vm = BuildVm(out _);

        vm.BeginAdd();

        vm.IsEditing.Should().BeTrue();
        vm.EditingCustomer.Should().NotBeNull();
        vm.EditingCustomer!.Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task SaveAsync_with_valid_buffer_persists_and_reloads()
    {
        var vm = BuildVm(out var service);

        vm.BeginAdd();
        vm.EditingCustomer!.DocumentNumber = "11111111";
        vm.EditingCustomer!.Name = "Cliente Nuevo";

        await vm.SaveAsync();

        vm.IsEditing.Should().BeFalse();
        vm.StatusMessage.Should().Contain("guardado");

        var all = await service.ListAsync(default);
        all.Should().Contain(c => c.DocumentNumber == "11111111");
    }

    [Fact]
    public async Task SaveAsync_rejects_blank_document()
    {
        var vm = BuildVm(out _);

        vm.BeginAdd();
        vm.EditingCustomer!.DocumentNumber = "";
        vm.EditingCustomer!.Name = "Con nombre";

        await vm.SaveAsync();

        vm.IsEditing.Should().BeTrue();
        vm.StatusMessage.Should().Contain("obligatorios");
    }

    [Fact]
    public async Task DeleteAsync_removes_row()
    {
        var vm = BuildVm(out _);

        var row = new CustomerListItem(
            Id: Guid.Parse("20000000-0000-0000-0000-000000000001"),
            DocumentNumber: "79123456",
            Name: "María Fernanda López",
            Email: "maria@example.co",
            Phone: "+57 311 555 1234",
            CreatedAt: new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc));

        await vm.DeleteAsync(row);

        vm.StatusMessage.Should().Contain("eliminado");
        vm.Customers.Should().NotContain(c => c.Id == row.Id);
    }

    [Fact]
    public void CancelEdit_clears_state()
    {
        var vm = BuildVm(out _);

        vm.BeginAdd();
        vm.CancelEdit();

        vm.IsEditing.Should().BeFalse();
        vm.EditingCustomer.Should().BeNull();
    }

    [Fact]
    public void Constructor_rejects_null_service()
    {
        var act = () => new CustomerManagementViewModel(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}