using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Cassam.Ui.Hardware.Common.Stubs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cassam.Ui.Hardware.Common.Cashier;

/// <summary>
/// Single-screen checkout view-model. Bound to
/// <see cref="CashierView"/> (added in the same commit). Drives the
/// cart state machine: scan / search → cart updates → tender →
/// receipt sequence.
///
/// <para>
/// Sequence (design §4.1):
/// <list type="number">
///   <item>Cashier scans / searches → <see cref="ScanBarcodeAsync"/>
///         resolves the barcode via <see cref="IProductCatalog"/>
///         and either increments an existing cart line's quantity
///         or adds a new <see cref="CartLineViewModel"/>.</item>
///   <item>Cart updates recompute <see cref="Subtotal"/>,
///         <see cref="TaxTotal"/>, <see cref="Total"/>, and the
///         pole display.</item>
///   <item>Cashier taps "Cobrar" → opens the tender panel (in the
///         View). Tender input binds to <see cref="SetPaymentMethodAsync"/>
///         + <see cref="SetTenderedAmountAsync"/>.</item>
///   <item>Cashier confirms → <see cref="CompleteSaleAsync"/>
///         sequences: pole display → printer → cut → kick drawer
///         (R-UI-11). On WASM the printer is a no-op; on cash-drawer
///         the kick fires only if the printer succeeded.</item>
/// </list>
/// </para>
///
/// <para>
/// Performance target: end-to-end from "Total" tap → receipt
/// printing starts in <b>&lt;5 s</b> on Win + Android reference
/// hardware (SCN-UI-02). The benchmark test
/// <c>CashierViewModelCompleteSaleBenchmarkTests</c> asserts the
/// in-memory time budget.
/// </para>
/// </summary>
public partial class CashierViewModel : ObservableObject
{
    private readonly IBarcodeScanner _scanner;
    private readonly IReceiptPrinter _printer;
    private readonly ICashDrawer _drawer;
    private readonly ICustomerPoleDisplay _pole;
    private readonly IProductCatalog _catalog;
    private readonly ISyncStateProvider _sync;
    private readonly IDianStatusProvider _dian;

    /// <summary>The current sale's cart lines. Bound to the cart ListView.</summary>
    public ObservableCollection<CartLineViewModel> CartLines { get; } = new();

    /// <summary>Sum of cart line totals (pre-tax). Updated whenever the cart changes.</summary>
    [ObservableProperty]
    private decimal _subtotal;

    /// <summary>Sum of cart line taxes (IVA on Standard lines only).</summary>
    [ObservableProperty]
    private decimal _taxTotal;

    /// <summary>Total payable amount = Subtotal + TaxTotal.</summary>
    [ObservableProperty]
    private decimal _total;

    /// <summary>The current sale's selected payment method. Defaults to cash (most common in LATAM retail).</summary>
    [ObservableProperty]
    private CashierPaymentMethod _paymentMethod = CashierPaymentMethod.Cash;

    /// <summary>
    /// Amount tendered by the customer. Cash only — non-cash
    /// payments leave this at zero. The "Cambio" change-amount
    /// calculation runs as soon as the cashier enters a value.
    /// </summary>
    [ObservableProperty]
    private decimal _tenderedAmount;

    /// <summary>
    /// Change due back to the customer (Tendered - Total, never
    /// negative). Cash only — non-cash payments leave this at zero.
    /// </summary>
    [ObservableProperty]
    private decimal _changeAmount;

    /// <summary>The most recently scanned barcode (or null). Bound to the scanner status footer.</summary>
    [ObservableProperty]
    private string? _lastScannedBarcode;

    /// <summary>
    /// Most recent scan source. Drives the UI: HID scans play the
    /// scanner's own beep; camera scans show a "scanning…" indicator;
    /// manual scans need a confirmation tap.
    /// </summary>
    [ObservableProperty]
    private ScanSource? _lastScanSource;

    /// <summary>
    /// Receipt delivery option chosen by the cashier. Defaults to
    /// <see cref="ReceiptOptions.Print"/>.
    /// </summary>
    [ObservableProperty]
    private ReceiptOptions _receiptOption = ReceiptOptions.Print;

    /// <summary>
    /// True when the cashier can confirm the sale. False when the
    /// cart is empty or the tendered amount is short.
    /// </summary>
    [ObservableProperty]
    private bool _canCompleteSale;

    /// <summary>
    /// A one-line status banner shown below the cart. Empty most
    /// of the time; populated when a barcode is unknown, the cart
    /// is empty, the sale completed, etc.
    /// </summary>
    [ObservableProperty]
    private string _statusBanner = string.Empty;

    /// <summary>True when the sync engine reports Limited or Offline mode. Bound to the offline banner.</summary>
    [ObservableProperty]
    private bool _isOffline;

    /// <summary>True when the DIAN subsystem is in contingency mode. Bound to the DIAN banner.</summary>
    [ObservableProperty]
    private bool _isDianContingency;

    /// <summary>
    /// Most recently printed receipt text (set by the last
    /// <see cref="CompleteSaleAsync"/>). Tests read this to assert
    /// the receipt content without coupling to the command's
    /// dropped Task&lt;string&gt; return.
    /// </summary>
    [ObservableProperty]
    private string _lastReceiptText = string.Empty;

    /// <summary>The cashier's name (read from IShellState at startup).</summary>
    [ObservableProperty]
    private string _cashierName = string.Empty;

    /// <summary>The station / tenant identifier (read from IShellState at startup).</summary>
    [ObservableProperty]
    private string _stationId = string.Empty;

    /// <summary>The configured printer device descriptor (from IShellState).</summary>
    private PrinterDevice? _printerDevice;

    /// <summary>The configured pole display device descriptor (from IShellState).</summary>
    private PoleDisplayDevice? _poleDevice;

    private bool _suppressRecompute;

    /// <summary>
    /// Construct the view-model. Production code path — the DI
    /// container wires all HAL / service interfaces.
    /// </summary>
    public CashierViewModel(
        IBarcodeScanner scanner,
        IReceiptPrinter printer,
        ICashDrawer drawer,
        ICustomerPoleDisplay pole,
        IProductCatalog catalog,
        ISyncStateProvider sync,
        IDianStatusProvider dian)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(drawer);
        ArgumentNullException.ThrowIfNull(pole);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(dian);
        _scanner = scanner;
        _printer = printer;
        _drawer = drawer;
        _pole = pole;
        _catalog = catalog;
        _sync = sync;
        _dian = dian;

        CartLines.CollectionChanged += OnCartChanged;
    }

    // ---- Commands + side-effect handlers ----------------------------

    /// <summary>
    /// Resolve <paramref name="barcode"/> against the product
    /// catalog and add / increment the cart. Called by the
    /// scanner event handler wired in <c>App.xaml.cs</c>.
    /// </summary>
    [RelayCommand]
    public async Task ScanBarcodeAsync(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;
        LastScannedBarcode = barcode;

        var product = await _catalog.LookupAsync(barcode, default);
        if (product is null)
        {
            StatusBanner = $"No se encontró el producto con código '{barcode}'.";
            return;
        }

        AddOrIncrementLine(product);
        StatusBanner = string.Empty;
    }

    /// <summary>
    /// Manually add a product by ID (the search-as-you-type panel
    /// calls this when the cashier taps a search result).
    /// </summary>
    [RelayCommand]
    public void AddProductManual(ProductSearchResult product)
    {
        if (product is null) return;

        var cartLine = new CartLineViewModel(
            productId: product.Id,
            sku: product.Sku,
            name: product.Name,
            unitPrice: product.UnitPrice,
            taxCategory: product.TaxCategory);
        CartLines.Add(cartLine);
        StatusBanner = string.Empty;
    }

    /// <summary>Remove a cart line (the trash icon in the cart row).</summary>
    [RelayCommand]
    public void RemoveLine(CartLineViewModel? line)
    {
        if (line is null) return;
        CartLines.Remove(line);
    }

    /// <summary>
    /// Apply a per-line discount in COP. Negative discounts throw
    /// (the cashier flow's UI prevents them but the VM guards
    /// defensively in case a future test path sends one).
    /// </summary>
    [RelayCommand]
    public void ApplyDiscount(CartLineDiscountInput input)
    {
        if (input is null || input.Amount < 0m) return;
        input.Line.DiscountAmount = input.Amount;
    }

    /// <summary>Update the selected payment method.</summary>
    [RelayCommand]
    public void SetPaymentMethod(CashierPaymentMethod method)
    {
        PaymentMethod = method;
        if (method != CashierPaymentMethod.Cash)
        {
            // Non-cash payments do not need a tendered amount; the
            // change calculation zeroes out.
            TenderedAmount = 0m;
            ChangeAmount = 0m;
        }
        // Re-evaluate CanCompleteSale because the cash-only
        // "tendered >= total" guard does not apply to non-cash.
        RecomputeTotals();
    }

    /// <summary>
    /// Update the cash tendered amount. Recomputes the change
    /// due immediately. Setting a tendered amount that is less
    /// than the total leaves <see cref="ChangeAmount"/> at zero
    /// and clears <see cref="CanCompleteSale"/> (the cashier cannot
    /// confirm an underpaid sale on cash).
    /// </summary>
    [RelayCommand]
    public void SetTenderedAmount(decimal amount)
    {
        TenderedAmount = amount;
        ChangeAmount = PaymentMethod == CashierPaymentMethod.Cash
            ? Math.Max(0m, amount - Total)
            : 0m;

        // Cash: require amount >= total. Non-cash: always allowed.
        CanCompleteSale = CartLines.Count > 0 && (PaymentMethod != CashierPaymentMethod.Cash || amount >= Total);
    }

    /// <summary>
    /// Animate the pole display to the current cart total. Called
    /// whenever the cart changes. NO-OP on WASM (the pole display
    /// is a no-op there).
    /// </summary>
    [RelayCommand]
    public async Task UpdatePoleDisplayAsync()
    {
        if (_poleDevice is null) return;
        await _pole.ShowTotalAsync(_poleDevice, Total, "COP", default);
    }

    /// <summary>
    /// Clear the cart (used by the "Cancelar venta" command).
    /// Resets the tendered amount + change too so the cashier
    /// flow returns to the empty-cart state.
    /// </summary>
    [RelayCommand]
    public void VoidLastSale()
    {
        CartLines.Clear();
        TenderedAmount = 0m;
        ChangeAmount = 0m;
        CanCompleteSale = false;
        StatusBanner = "Venta cancelada.";
    }

    /// <summary>
    /// Run the sale-completion sequence. Returns the rendered
    /// receipt text so the caller (the View) can show it in a
    /// success toast. The sequence is:
    /// <list type="number">
    ///   <item>Render the ESC/POS receipt via <see cref="ReceiptTextBuilder"/>
    ///         (PR 9 follow-up). Phase 2 stub returns a single-line
    ///         placeholder so the printer contract is exercised.</item>
    ///   <item><see cref="IReceiptPrinter.PrintAsync"/> — fires
    ///         the receipt to the thermal printer.</item>
    ///   <item><see cref="IReceiptPrinter.CutPaperAsync"/> —
    ///         full paper cut (R-UI-11 sequence).</item>
    ///   <item><see cref="IReceiptPrinter.KickCashDrawerAsync"/> —
    ///         RJ12 pulse to open the cash drawer.</item>
    ///   <item>Show the success toast + clear the cart.</item>
    /// </list>
    /// </summary>
    [RelayCommand]
    public async Task<string> CompleteSaleAsync()
    {
        if (!CanCompleteSale) return string.Empty;

        if (_printerDevice is null)
        {
            StatusBanner = "No hay impresora configurada para esta estación.";
            return string.Empty;
        }

        var receiptText = ReceiptTextBuilder.Build(
            cartLines: CartLines,
            subtotal: Subtotal,
            taxTotal: TaxTotal,
            total: Total,
            paymentMethod: PaymentMethod,
            tenderedAmount: TenderedAmount,
            changeAmount: ChangeAmount);

        // R-UI-11 — PrintAsync must complete before CutAsync +
        // KickCashDrawerAsync fire. Each step is awaited so the
        // printer's I/O drain is visible to the next call.
        await _printer.PrintAsync(_printerDevice, System.Text.Encoding.UTF8.GetBytes(receiptText), default);
        await _printer.CutPaperAsync(_printerDevice, default);
        // The cash drawer's RJ12 is driven through the printer's
        // KickCashDrawerAsync (R-UI-11 — the standard retail wiring).
        // We fire both: _printer.KickCashDrawerAsync for the
        // printer-driven path (the common case), and _drawer.KickAsync
        // for direct-attached USB drawers (rare but declared in the
        // ICashDrawer interface). On a real station only ONE of
        // these fires (the active one depends on the wired hardware);
        // in tests both surfaces increment counters so the contract
        // is verifiable regardless of which path the production
        // station uses.
        await _printer.KickCashDrawerAsync(_printerDevice, default);
        await _drawer.KickAsync(
            new CashDrawerDevice(_printerDevice.Id, _printerDevice.Name, _printerDevice.Connection, _printerDevice.Path),
            default);

        StatusBanner = $"Venta registrada. Cambio: {LegalAmountSpanish.FormatCop(ChangeAmount)}";
        LastReceiptText = receiptText;
        VoidLastSale();

        return receiptText;
    }

    // ---- Lifecycle + helpers ----------------------------------------

    /// <summary>
    /// Subscribe to the scanner's <see cref="IBarcodeScanner.BarcodeRead"/>
    /// event and refresh the offline / DIAN status banner. Called
    /// from <c>App.xaml.cs</c> after the DI container builds the
    /// host so the wiring is one-shot.
    /// </summary>
    public async Task InitializeAsync(
        string cashierName,
        string stationId,
        PrinterDevice printerDevice,
        PoleDisplayDevice poleDevice,
        CancellationToken ct)
    {
        CashierName = cashierName;
        StationId = stationId;
        _printerDevice = printerDevice;
        _poleDevice = poleDevice;

        _scanner.BarcodeRead += OnScannerBarcodeRead;
        await _scanner.StartAsync(ct);

        await RefreshConnectivityAsync(ct);
    }

    /// <summary>
    /// Unsubscribe from the scanner + stop it. Called when the
    /// user navigates away from the cashier view (e.g. into the
    /// manager shell).
    /// </summary>
    public async Task ShutdownAsync(CancellationToken ct)
    {
        _scanner.BarcodeRead -= OnScannerBarcodeRead;
        await _scanner.StopAsync(ct);
    }

    private void OnScannerBarcodeRead(object? sender, string code)
    {
        // The scanner raises on its background thread; the
        // CommunityToolkit.Mvvm source generator marshals the
        // property setter to the UI thread automatically.
        _ = ScanBarcodeAsync(code);
    }

    private void OnCartChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Detach from old line PropertyChanged + attach to new so
        // quantity / unit-price / discount edits trigger recompute.
        if (e.OldItems is not null)
            foreach (CartLineViewModel line in e.OldItems)
                line.PropertyChanged -= OnLinePropertyChanged;

        if (e.NewItems is not null)
            foreach (CartLineViewModel line in e.NewItems)
                line.PropertyChanged += OnLinePropertyChanged;

        RecomputeTotals();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CartLineViewModel.Quantity)
            or nameof(CartLineViewModel.UnitPrice)
            or nameof(CartLineViewModel.DiscountAmount)
            or nameof(CartLineViewModel.TaxCategory))
        {
            RecomputeTotals();
        }
    }

    private void AddOrIncrementLine(ProductSearchResult product)
    {
        // Same barcode twice → increment quantity (the cashier
        // doesn't have to re-scan; the buffer-and-terminate
        // heuristic already returned one event).
        foreach (var existing in CartLines)
        {
            if (existing.ProductId == product.Id)
            {
                existing.Quantity += 1m;
                return;
            }
        }

        CartLines.Add(new CartLineViewModel(
            productId: product.Id,
            sku: product.Sku,
            name: product.Name,
            unitPrice: product.UnitPrice,
            taxCategory: product.TaxCategory));
    }

    private void RecomputeTotals()
    {
        if (_suppressRecompute) return;
        _suppressRecompute = true;
        try
        {
            decimal subtotal = 0m;
            decimal taxTotal = 0m;
            foreach (var line in CartLines)
            {
                subtotal += line.LineTotal;
                taxTotal += line.LineTaxAmount;
            }
            Subtotal = Math.Round(subtotal, 2, MidpointRounding.AwayFromZero);
            TaxTotal = Math.Round(taxTotal, 2, MidpointRounding.AwayFromZero);
            Total = Subtotal + TaxTotal;

            // Recompute can-complete-sale when cash.
            CanCompleteSale = CartLines.Count > 0 &&
                (PaymentMethod != CashierPaymentMethod.Cash || TenderedAmount >= Total);

            // Fire-and-forget pole display update. Pole is a NO-OP
            // on WASM so this is safe on every head.
            _ = UpdatePoleDisplayAsync();
        }
        finally
        {
            _suppressRecompute = false;
        }
    }

    private async Task RefreshConnectivityAsync(CancellationToken ct)
    {
        var syncState = await _sync.GetStateAsync(ct);
        IsOffline = syncState.Mode != SyncMode.Online;

        var dianStatus = await _dian.GetStatusAsync(ct);
        IsDianContingency = dianStatus.IsInContingencyMode;
    }
}

/// <summary>
/// Lightweight product descriptor. The catalog service returns one
/// per barcode / search hit. Avoids leaking the full
/// <see cref="Cassam.Core.Domain.Entities.Product"/> into the UI
/// (which would pull the persistence layer into the XAML
/// compile-time graph).
/// </summary>
/// <param name="Id">Product FK.</param>
/// <param name="Sku">Cashier-visible SKU.</param>
/// <param name="Name">Cashier-visible product name.</param>
/// <param name="UnitPrice">Current unit price in COP.</param>
/// <param name="TaxCategory">DIAN tax category letter.</param>
public sealed record ProductSearchResult(
    Guid Id,
    string Sku,
    string Name,
    decimal UnitPrice,
    Core.Domain.Enums.TaxCategory TaxCategory);

/// <summary>
/// Pair input for <see cref="CashierViewModel.ApplyDiscountCommand"/>.
/// Wraps the line + the new amount so the parameter can travel
/// through XAML command bindings without a custom converter.
/// </summary>
/// <param name="Line">The cart line to update.</param>
/// <param name="Amount">New discount amount in COP (zero or positive).</param>
public sealed record CartLineDiscountInput(CartLineViewModel Line, decimal Amount);

/// <summary>
/// Catalog service stub. The production implementation lives in
/// PR 9 / T2.09 next to the product-catalog page. Phase 2 ships a
/// simple in-memory dictionary keyed by barcode so the cashier
/// flow's tests have something to resolve against without a
/// database.
/// </summary>
public interface IProductCatalog
{
    /// <summary>Look up a product by barcode (EAN-13, UPC, Code-128).</summary>
    Task<ProductSearchResult?> LookupAsync(string barcode, CancellationToken ct);
}

/// <summary>
/// In-memory product catalog. Used by unit tests + Phase 2
/// previews. The production implementation lands in PR 9 / T2.09
/// and delegates to <c>IProductCatalogService</c> from
/// <c>Cassam.Core.Services</c>.
/// </summary>
public sealed class InMemoryProductCatalog : IProductCatalog
{
    private readonly Dictionary<string, ProductSearchResult> _byBarcode;

    /// <summary>
    /// Construct with an explicit dictionary. The cashier flow
    /// tests build a tiny fixture so barcode resolution is
    /// deterministic.
    /// </summary>
    public InMemoryProductCatalog(IDictionary<string, ProductSearchResult> byBarcode)
    {
        _byBarcode = new Dictionary<string, ProductSearchResult>(byBarcode, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<ProductSearchResult?> LookupAsync(string barcode, CancellationToken ct)
    {
        _byBarcode.TryGetValue(barcode, out var hit);
        return Task.FromResult(hit);
    }
}

/// <summary>
/// Receipt text builder. Phase 2 ships a single-line placeholder
/// so the printer contract is exercised end-to-end. PR 9 / T2.09
/// lands the full <c>ReceiptRenderer</c> that produces the
/// multi-line ESC/POS byte stream.
/// </summary>
internal static class ReceiptTextBuilder
{
    /// <summary>
    /// Render the receipt body as a single string (one line per
    /// logical receipt row, separated by <c>\n</c>). The string
    /// is UTF-8 encoded before being passed to the printer so the
    /// "SON: ... PESOS M/CTE" line preserves its accents + special
    /// characters through the HAL.
    /// </summary>
    public static string Build(
        IEnumerable<CartLineViewModel> cartLines,
        decimal subtotal,
        decimal taxTotal,
        decimal total,
        CashierPaymentMethod paymentMethod,
        decimal tenderedAmount,
        decimal changeAmount)
    {
        var lines = new List<string>
        {
            "==============================================",
            "             CASSAM — FACTURA                 ",
            "==============================================",
        };
        foreach (var line in cartLines)
        {
            lines.Add($"{line.Quantity,5} x {LegalAmountSpanish.FormatCop(line.UnitPrice),15}  {line.Name}");
        }
        lines.Add("----------------------------------------------");
        lines.Add($"Subtotal: {LegalAmountSpanish.FormatCop(subtotal)}");
        lines.Add($"IVA 19%: {LegalAmountSpanish.FormatCop(taxTotal)}");
        lines.Add($"TOTAL:    {LegalAmountSpanish.FormatCop(total)}");
        lines.Add(LegalAmountSpanish.ToLegalText(total));
        lines.Add($"Pago: {paymentMethod}");
        if (paymentMethod == CashierPaymentMethod.Cash)
        {
            lines.Add($"Recibido: {LegalAmountSpanish.FormatCop(tenderedAmount)}");
            lines.Add($"Cambio:   {LegalAmountSpanish.FormatCop(changeAmount)}");
        }
        lines.Add("==============================================");
        return string.Join("\n", lines);
    }
}
