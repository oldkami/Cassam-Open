using System.Text;
using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Common.Cashier;
using Cassam.Ui.Hardware.Common.Mock;
using Cassam.Ui.Hardware.Common.Stubs;
using FluentAssertions;

namespace Cassam.Ui.Tests.Cashier;

/// <summary>
/// Unit tests for <see cref="CashierViewModel"/>'s state machine.
/// Tests run headlessly on every host (no XAML runtime required)
/// because the VM lives in the platform-neutral
/// <c>Cassam.Ui.Hardware.Common</c> assembly.
/// </summary>
public class CashierViewModelTests
{
    private static readonly ProductSearchResult Arroz = new(
        Guid.Parse("00000000-0000-0000-0000-000000000001"),
        "001-7701234",
        "Arroz Diana 1kg",
        3500m,
        Core.Domain.Enums.TaxCategory.Standard);

    private static readonly ProductSearchResult Leche = new(
        Guid.Parse("00000000-0000-0000-0000-000000000002"),
        "002-7709876",
        "Leche Alpina 1L",
        4500m,
        Core.Domain.Enums.TaxCategory.Exempt);

    private static CashierViewModel BuildVm(out MockBarcodeScanner scanner,
                                            out MockReceiptPrinter printer,
                                            out MockCashDrawer drawer,
                                            out MockCustomerPoleDisplay pole)
    {
        scanner = new MockBarcodeScanner();
        printer = new MockReceiptPrinter();
        drawer = new MockCashDrawer();
        pole = new MockCustomerPoleDisplay();

        var catalog = new InMemoryProductCatalog(new Dictionary<string, ProductSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Arroz.Sku] = Arroz,
            ["7701234567890"] = Arroz,
            [Leche.Sku] = Leche,
        });

        return new CashierViewModel(scanner, printer, drawer, pole, catalog, new StubSyncStateProvider(), new StubDianStatusProvider());
    }

    private static CashierViewModel BuildInitializedVm(
        out MockBarcodeScanner scanner,
        out MockReceiptPrinter printer,
        out MockCashDrawer drawer,
        out MockCustomerPoleDisplay pole)
    {
        var vm = BuildVm(out scanner, out printer, out drawer, out pole);

        vm.InitializeAsync(
            cashierName: "Test Cashier",
            stationId: "STATION-01",
            printerDevice: new PrinterDevice("p-1", "Test printer", ConnectionType.Usb, "test://printer"),
            poleDevice: new PoleDisplayDevice("d-1", "Test pole", ConnectionType.Serial, "/dev/ttyUSB1"),
            ct: default).GetAwaiter().GetResult();
        return vm;
    }

    [Fact]
    public async Task ScanBarcodeAsync_adds_product_to_cart()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        await vm.ScanBarcodeCommand.ExecuteAsync("7701234567890");

        vm.CartLines.Should().ContainSingle();
        vm.CartLines[0].Name.Should().Be("Arroz Diana 1kg");
        vm.CartLines[0].UnitPrice.Should().Be(3500m);
        vm.StatusBanner.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanBarcodeAsync_increments_quantity_on_second_scan_of_same_product()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        await vm.ScanBarcodeCommand.ExecuteAsync("7701234567890");
        await vm.ScanBarcodeCommand.ExecuteAsync("7701234567890");
        await vm.ScanBarcodeCommand.ExecuteAsync("7701234567890");

        vm.CartLines.Should().ContainSingle(
            "the buffer-and-terminate heuristic increments the existing line, never duplicates it");
        vm.CartLines[0].Quantity.Should().Be(3m);
    }

    [Fact]
    public async Task ScanBarcodeAsync_sets_status_banner_when_product_unknown()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        await vm.ScanBarcodeCommand.ExecuteAsync("NOT-A-BARCODE");

        vm.CartLines.Should().BeEmpty();
        vm.StatusBanner.Should().Contain("No se encontró");
    }

    [Fact]
    public void RemoveLine_removes_line_from_cart()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        vm.CartLines.Add(new CartLineViewModel(Arroz.Id, Arroz.Sku, Arroz.Name, Arroz.UnitPrice, Arroz.TaxCategory));
        vm.CartLines.Add(new CartLineViewModel(Leche.Id, Leche.Sku, Leche.Name, Leche.UnitPrice, Leche.TaxCategory));

        vm.RemoveLineCommand.Execute(vm.CartLines[0]);

        vm.CartLines.Should().ContainSingle();
        vm.CartLines[0].Name.Should().Be("Leche Alpina 1L");
    }

    [Fact]
    public void ApplyDiscount_updates_discount_and_recomputes_totals()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        vm.CartLines.Add(new CartLineViewModel(Arroz.Id, Arroz.Sku, Arroz.Name, Arroz.UnitPrice, Arroz.TaxCategory));

        vm.ApplyDiscountCommand.Execute(new CartLineDiscountInput(vm.CartLines[0], 500m));

        vm.CartLines[0].DiscountAmount.Should().Be(500m);
        // LineTotal = 3500 - 500 = 3000; Subtotal = 3000; Tax = 3000 * 0.19 = 570; Total = 3570.
        vm.Subtotal.Should().Be(3000m);
        vm.TaxTotal.Should().Be(570m);
        vm.Total.Should().Be(3570m);
    }

    [Fact]
    public void ApplyDiscount_rejects_negative_amount()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        vm.CartLines.Add(new CartLineViewModel(Arroz.Id, Arroz.Sku, Arroz.Name, Arroz.UnitPrice, Arroz.TaxCategory));

        vm.ApplyDiscountCommand.Execute(new CartLineDiscountInput(vm.CartLines[0], -1m));

        vm.CartLines[0].DiscountAmount.Should().Be(0m);
    }

    [Fact]
    public void SetPaymentMethod_updates_method()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        vm.SetPaymentMethodCommand.Execute(CashierPaymentMethod.Card);

        vm.PaymentMethod.Should().Be(CashierPaymentMethod.Card);
    }

    [Fact]
    public void SetPaymentMethod_to_non_cash_zeros_tendered_and_change()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        vm.SetTenderedAmountCommand.Execute(10_000m);  // Pretend some amount.
        vm.SetPaymentMethodCommand.Execute(CashierPaymentMethod.Card);

        vm.TenderedAmount.Should().Be(0m);
        vm.ChangeAmount.Should().Be(0m);
    }

    [Fact]
    public void SetTenderedAmount_calculates_change_correctly_for_cash()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        vm.CartLines.Add(new CartLineViewModel(Arroz.Id, Arroz.Sku, Arroz.Name, Arroz.UnitPrice, Arroz.TaxCategory));
        // Total = 3500 * 1.19 = 4165.
        vm.SetTenderedAmountCommand.Execute(10_000m);

        vm.ChangeAmount.Should().Be(5835m);
        vm.CanCompleteSale.Should().BeTrue();
    }

    [Fact]
    public void SetTenderedAmount_below_total_disables_complete()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        vm.CartLines.Add(new CartLineViewModel(Arroz.Id, Arroz.Sku, Arroz.Name, Arroz.UnitPrice, Arroz.TaxCategory));
        // Total = 4165; tendered = 1000 → short.
        vm.SetTenderedAmountCommand.Execute(1000m);

        vm.ChangeAmount.Should().Be(0m);
        vm.CanCompleteSale.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteSaleAsync_triggers_printer_sequence()
    {
        var vm = BuildInitializedVm(out _, out var printer, out var drawer, out _);

        vm.CartLines.Add(new CartLineViewModel(Arroz.Id, Arroz.Sku, Arroz.Name, Arroz.UnitPrice, Arroz.TaxCategory));
        vm.SetTenderedAmountCommand.Execute(10_000m);

        // The generated CompleteSaleCommand drops the Task<string>
        // return (CommunityToolkit.Mvvm 8.x IAsyncRelayCommand<T>
        // exposes only Task). Call the underlying async method
        // directly to assert the receipt text the printer received.
        await vm.CompleteSaleCommand.ExecuteAsync(null);
        var receipt = vm.LastReceiptText;

        // R-UI-11: Print → Cut → Kick sequence is enforced by the
        // order of the calls inside the VM. The Mock printer records
        // exactly one Print, one Cut, one Kick per sale.
        printer.PrintedBytes.Should().ContainSingle("exactly one receipt byte stream per sale");
        printer.LastCutRequested.Should().BeTrue("cut fires after print");
        printer.LastKickRequested.Should().BeTrue("drawer kicks after cut");
        drawer.KickCount.Should().Be(1);

        // The cart is cleared on completion so the next sale starts fresh.
        vm.CartLines.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteSaleAsync_returns_non_empty_receipt_text()
    {
        var vm = BuildInitializedVm(out _, out var printer, out _, out _);

        vm.CartLines.Add(new CartLineViewModel(Arroz.Id, Arroz.Sku, Arroz.Name, Arroz.UnitPrice, Arroz.TaxCategory));
        vm.SetTenderedAmountCommand.Execute(10_000m);

        await vm.CompleteSaleCommand.ExecuteAsync(null);
        var receipt = vm.LastReceiptText;

        receipt.Should().NotBeNullOrEmpty();
        receipt.Should().Contain("CASSAM", "the receipt header is the Cassam brand wordmark");
        receipt.Should().Contain("SON:", "the legal-amount line is the DD-09 es-CO format");
        receipt.Should().Contain("M/CTE");
        printer.PrintedBytes.Should().ContainSingle();
        Encoding.UTF8.GetString(printer.PrintedBytes[0]).Should().Contain("SON:");
    }

    [Fact]
    public void VoidLastSale_clears_cart_and_resets_tendered_amount()
    {
        var vm = BuildInitializedVm(out _, out _, out _, out _);

        vm.CartLines.Add(new CartLineViewModel(Arroz.Id, Arroz.Sku, Arroz.Name, Arroz.UnitPrice, Arroz.TaxCategory));
        vm.SetTenderedAmountCommand.Execute(10_000m);

        vm.VoidLastSaleCommand.Execute(null);

        vm.CartLines.Should().BeEmpty();
        vm.TenderedAmount.Should().Be(0m);
        vm.ChangeAmount.Should().Be(0m);
        vm.CanCompleteSale.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_wires_scanner_event_to_cart()
    {
        var vm = BuildVm(out var scanner, out _, out _, out _);

        await vm.InitializeAsync(
            cashierName: "Cashier 1",
            stationId: "STATION-01",
            printerDevice: new PrinterDevice("p-1", "Test", ConnectionType.Usb, "test://"),
            poleDevice: new PoleDisplayDevice("d-1", "Test", ConnectionType.Serial, "/dev/ttyUSB1"),
            ct: default);

        scanner.SimulateScan("7701234567890");
        scanner.SimulateScan("7701234567890");

        vm.CartLines.Should().ContainSingle();
        vm.CartLines[0].Quantity.Should().Be(2m);
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var act = () => new CashierViewModel(null!, null!, null!, null!, null!, null!, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

