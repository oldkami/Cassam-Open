using Cassam.Ui.Hardware.Common;
using Cassam.Ui.Hardware.Common.Cashier;
using Cassam.Ui.Hardware.Common.Mock;
using Cassam.Ui.Hardware.Common.Stubs;
using FluentAssertions;

namespace Cassam.Ui.Tests.Cashier;

/// <summary>
/// Integration tests for the full cashier-flow end-to-end signal
/// path: barcode scan → cart update → tender → receipt sequence.
/// These tests assert the surface exactly the way the cashier
/// sees it (per design §4.1):
///
/// <list type="number">
///   <item>The scanner fires a barcode (HID or camera source).</item>
///   <item>The VM resolves the barcode, adds a cart line,
///         recomputes totals, and updates the pole display.</item>
///   <item>The cashier enters a tendered amount + selects payment
///         method.</item>
///   <item>The cashier taps "Cobrar" → the receipt sequence runs:
///         Print → Cut → Kick (R-UI-11).</item>
/// </list>
/// </summary>
public class CashierFlowIntegrationTests
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

    [Fact]
    public async Task EndToEnd_scan_to_complete_sale_runs_full_sequence()
    {
        var scanner = new MockBarcodeScanner();
        var printer = new MockReceiptPrinter();
        var drawer = new MockCashDrawer();
        var pole = new MockCustomerPoleDisplay();

        var catalog = new InMemoryProductCatalog(new Dictionary<string, ProductSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["7701234567890"] = Arroz,
            ["7709876543210"] = Leche,
        });

        var vm = new CashierViewModel(scanner, printer, drawer, pole, catalog, new StubSyncStateProvider(), new StubDianStatusProvider());
        await vm.InitializeAsync(
            cashierName: "Integration Cashier",
            stationId: "STATION-INT",
            printerDevice: new PrinterDevice("p-int", "Integration printer", ConnectionType.Lan, "192.168.1.10:9100"),
            poleDevice: new PoleDisplayDevice("d-int", "Integration pole", ConnectionType.Serial, "/dev/ttyUSB1"),
            ct: default);

        // 1. Scan two products.
        scanner.SimulateScan("7701234567890");
        scanner.SimulateScan("7701234567890");
        scanner.SimulateScan("7709876543210");

        vm.CartLines.Should().HaveCount(2);
        vm.CartLines[0].Quantity.Should().Be(2m);
        vm.CartLines[0].Name.Should().Be("Arroz Diana 1kg");
        vm.CartLines[1].Name.Should().Be("Leche Alpina 1L");

        // 2. Verify totals.
        // Line 1: 2 * 3500 - 0 = 7000 (Standard, IVA 19% → 1330)
        // Line 2: 1 * 4500 - 0 = 4500 (Exempt, no IVA)
        // Subtotal = 11500; Tax = 1330; Total = 12830.
        vm.Subtotal.Should().Be(11500m);
        vm.TaxTotal.Should().Be(1330m);
        vm.Total.Should().Be(12830m);

        // 3. Pole display shows the running total (es-CO formatted).
        pole.LastDisplayedTotal.Should().Contain("COP");
        pole.LastDisplayedTotal.Should().Contain("12.830,00");

        // 4. Tender cash → change.
        vm.SetTenderedAmountCommand.Execute(20_000m);
        vm.PaymentMethod.Should().Be(CashierPaymentMethod.Cash);
        vm.ChangeAmount.Should().Be(7170m);
        vm.CanCompleteSale.Should().BeTrue();

        // 5. Complete sale → R-UI-11 sequence: Print → Cut → Kick.
        await vm.CompleteSaleCommand.ExecuteAsync(null);
        var receipt = vm.LastReceiptText;

        receipt.Should().Contain("CASSAM");
        receipt.Should().Contain("SON:");
        receipt.Should().Contain("M/CTE");

        printer.PrintedBytes.Should().ContainSingle(
            "R-UI-11: PrintAsync fires exactly once per sale — never twice even if Cut/Kick retry");
        printer.LastCutRequested.Should().BeTrue();
        printer.LastKickRequested.Should().BeTrue();
        drawer.KickCount.Should().Be(1);

        // Cart is reset so the next sale starts fresh.
        vm.CartLines.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteSale_for_non_cash_payment_skips_tendered_but_kicks_drawer()
    {
        // Card payments don't require tendered amount / change, but
        // the cash drawer still kicks after the receipt prints —
        // that's how retail works even when the customer paid by
        // card (the cashier drops change in the drawer for the
        // till-count at end of shift).
        var scanner = new MockBarcodeScanner();
        var printer = new MockReceiptPrinter();
        var drawer = new MockCashDrawer();
        var pole = new MockCustomerPoleDisplay();
        var catalog = new InMemoryProductCatalog(new Dictionary<string, ProductSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["7701234567890"] = Arroz,
        });
        var vm = new CashierViewModel(scanner, printer, drawer, pole, catalog, new StubSyncStateProvider(), new StubDianStatusProvider());

        await vm.InitializeAsync(
            cashierName: "Card Cashier",
            stationId: "STATION-01",
            printerDevice: new PrinterDevice("p-1", "Test", ConnectionType.Usb, "test://"),
            poleDevice: new PoleDisplayDevice("d-1", "Test", ConnectionType.Serial, "/dev/ttyUSB1"),
            ct: default);

        scanner.SimulateScan("7701234567890");
        vm.SetPaymentMethodCommand.Execute(CashierPaymentMethod.Card);

        // Non-cash + qty>0 → can complete immediately.
        vm.CanCompleteSale.Should().BeTrue(
            "card payments do not require tendered amount; the receipt prints as soon as the cart has lines");
        vm.TenderedAmount.Should().Be(0m);
        vm.ChangeAmount.Should().Be(0m);

        await vm.CompleteSaleCommand.ExecuteAsync((string?)null);

        printer.PrintedBytes.Should().ContainSingle();
        printer.LastKickRequested.Should().BeTrue();
        drawer.KickCount.Should().Be(1);
    }

    [Fact]
    public async Task CompleteSale_fails_when_printer_not_configured()
    {
        // Misconfiguration safety net: if the operator forgot to
        // configure a printer in user_settings, CompleteSaleAsync
        // returns an empty receipt rather than throwing mid-flow.
        var scanner = new MockBarcodeScanner();
        var printer = new MockReceiptPrinter();
        var drawer = new MockCashDrawer();
        var pole = new MockCustomerPoleDisplay();
        var catalog = new InMemoryProductCatalog(new Dictionary<string, ProductSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["7701234567890"] = Arroz,
        });
        var vm = new CashierViewModel(scanner, printer, drawer, pole, catalog, new StubSyncStateProvider(), new StubDianStatusProvider());

        // InitializeAsync is intentionally NOT called so _printerDevice
        // remains null. The cart still builds (via direct Add) so the
        // "no printer configured" guard fires.
        vm.CartLines.Add(new CartLineViewModel(Arroz.Id, Arroz.Sku, Arroz.Name, Arroz.UnitPrice, Arroz.TaxCategory));
        vm.SetTenderedAmountCommand.Execute(10_000m);

        var receipt = string.Empty;
        await vm.CompleteSaleCommand.ExecuteAsync(null);
        receipt = vm.LastReceiptText;

        receipt.Should().BeEmpty(
            "the VM refuses to print without a configured printer device — the operator sees the status banner instead");
        vm.StatusBanner.Should().Contain("impresora");
        printer.PrintedBytes.Should().BeEmpty("no bytes are written when the printer device is unconfigured");
        drawer.KickCount.Should().Be(0);
    }
}

/// <summary>
/// Contract tests for the offline + DIAN banners. The cashier flow
/// reads <see cref="ISyncStateProvider"/> +
/// <see cref="IDianStatusProvider"/> at startup; this test
/// asserts the property plumbing wires the banner state
/// correctly.
/// </summary>
public class ConnectivityBannerTests
{
    [Fact]
    public async Task Online_sync_state_sets_IsOffline_false()
    {
        var vm = new CashierViewModel(
            new MockBarcodeScanner(),
            new MockReceiptPrinter(),
            new MockCashDrawer(),
            new MockCustomerPoleDisplay(),
            new InMemoryProductCatalog(new Dictionary<string, ProductSearchResult>()),
            new StubSyncStateProvider(),
            new StubDianStatusProvider());

        await vm.InitializeAsync(
            cashierName: "Cashier",
            stationId: "STATION",
            printerDevice: new PrinterDevice("p", "Test", ConnectionType.Usb, "test://"),
            poleDevice: new PoleDisplayDevice("d", "Test", ConnectionType.Serial, "/dev/ttyUSB1"),
            ct: default);

        vm.IsOffline.Should().BeFalse(
            "the StubSyncStateProvider returns Online — the banner stays green");
        vm.IsDianContingency.Should().BeFalse(
            "the StubDianStatusProvider returns clean — the DIAN banner stays neutral");
    }
}
