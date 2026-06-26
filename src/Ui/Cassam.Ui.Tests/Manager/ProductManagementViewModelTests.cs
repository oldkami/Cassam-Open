using System;
using System.Threading.Tasks;
using Cassam.Ui.Hardware.Common.Manager;
using FluentAssertions;

namespace Cassam.Ui.Tests.Manager;

/// <summary>
/// Unit tests for <see cref="ProductManagementViewModel"/>'s
/// CRUD state machine (T2.09.a, REQ-UI-03, SCN-UI-03).
/// Tests run headlessly on every host (no XAML runtime required)
/// because the VM lives in the platform-neutral
/// <c>Cassam.Ui.Hardware.Common</c> assembly.
/// </summary>
public class ProductManagementViewModelTests
{
    private static ProductManagementViewModel BuildVm(out InMemoryProductService service)
    {
        service = new InMemoryProductService();
        return new ProductManagementViewModel(service);
    }

    [Fact]
    public async Task RefreshAsync_populates_list_with_seeded_products()
    {
        var vm = BuildVm(out _);

        await vm.RefreshAsync();

        vm.Products.Should().HaveCount(3, "the in-memory service seeds three demo rows");
        vm.Products[0].Sku.Should().Be("001-7701234");
    }

    [Fact]
    public async Task SearchText_filters_results_by_sku_name_or_barcode()
    {
        var vm = BuildVm(out _);

        vm.SearchText = "arroz";
        await Task.Yield(); // Let the debounced setter fire.
        await vm.RefreshAsync();

        vm.Products.Should().ContainSingle(p => p.Name.Contains("Arroz", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchText_by_barcode_returns_match()
    {
        var vm = BuildVm(out _);

        vm.SearchText = "7705555";
        await Task.Yield();
        await vm.RefreshAsync();

        vm.Products.Should().ContainSingle(p => p.Barcode.Contains("7705555", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BeginAdd_populates_editing_buffer_with_empty_fields()
    {
        var vm = BuildVm(out _);

        vm.BeginAdd();

        vm.IsEditing.Should().BeTrue();
        vm.EditingProduct.Should().NotBeNull();
        vm.EditingProduct!.Id.Should().Be(Guid.Empty);
        vm.EditingProduct!.Sku.Should().BeEmpty();
        vm.EditingProduct!.Name.Should().BeEmpty();
    }

    [Fact]
    public void BeginEdit_populates_editing_buffer_with_row_values()
    {
        var vm = BuildVm(out _);

        vm.BeginEdit(new ProductListItem(
            Id: Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Sku: "001-7701234",
            Name: "Arroz Diana 1kg",
            Barcode: "7701234567890",
            UnitPrice: 3500m,
            TaxCategory: Core.Domain.Enums.TaxCategory.Standard,
            StockQuantity: 120m,
            IsActive: true));

        vm.IsEditing.Should().BeTrue();
        vm.EditingProduct!.Sku.Should().Be("001-7701234");
        vm.EditingProduct!.UnitPrice.Should().Be(3500m);
    }

    [Fact]
    public async Task SaveAsync_with_valid_buffer_persists_and_reloads()
    {
        var vm = BuildVm(out var service);

        vm.BeginAdd();
        vm.EditingProduct!.Sku = "999-NEW";
        vm.EditingProduct!.Name = "Nuevo Producto";
        vm.EditingProduct!.UnitPrice = 1234m;

        await vm.SaveAsync();

        vm.IsEditing.Should().BeFalse();
        vm.StatusMessage.Should().Contain("guardado");

        // The new product must now appear in the seeded + saved set.
        var added = await service.SearchAsync("nuevo", default);
        added.Should().ContainSingle(p => p.Sku == "999-NEW");
    }

    [Fact]
    public async Task SaveAsync_rejects_blank_sku()
    {
        var vm = BuildVm(out _);

        vm.BeginAdd();
        vm.EditingProduct!.Sku = "  ";
        vm.EditingProduct!.Name = "Con nombre";

        await vm.SaveAsync();

        vm.IsEditing.Should().BeTrue("the dialog stays open so the operator can fix the input");
        vm.StatusMessage.Should().Contain("obligatorios");
    }

    [Fact]
    public async Task SaveAsync_rejects_negative_price()
    {
        var vm = BuildVm(out _);

        vm.BeginAdd();
        vm.EditingProduct!.Sku = "OK";
        vm.EditingProduct!.Name = "OK";
        vm.EditingProduct!.UnitPrice = -100m;

        await vm.SaveAsync();

        vm.IsEditing.Should().BeTrue();
        vm.StatusMessage.Should().Contain("negativo");
    }

    [Fact]
    public async Task DeleteAsync_soft_deletes_and_reloads()
    {
        var vm = BuildVm(out var service);

        var row = new ProductListItem(
            Id: Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Sku: "001-7701234",
            Name: "Arroz Diana 1kg",
            Barcode: "7701234567890",
            UnitPrice: 3500m,
            TaxCategory: Core.Domain.Enums.TaxCategory.Standard,
            StockQuantity: 120m,
            IsActive: true);

        await vm.DeleteAsync(row);

        vm.StatusMessage.Should().Contain("archivado");

        var all = await service.ListAsync(default);
        all.Should().Contain(p => p.Id == row.Id && !p.IsActive,
            "the service flips IsActive to false rather than removing the row");
    }

    [Fact]
    public void CancelEdit_clears_state()
    {
        var vm = BuildVm(out _);

        vm.BeginAdd();
        vm.CancelEdit();

        vm.IsEditing.Should().BeFalse();
        vm.EditingProduct.Should().BeNull();
    }

    [Fact]
    public void Constructor_rejects_null_service()
    {
        var act = () => new ProductManagementViewModel(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}