using CommunityToolkit.Mvvm.ComponentModel;
using Cassam.Core.Domain.Entities;

namespace Cassam.Ui.Hardware.Common.Cashier;

/// <summary>
/// One line in the cashier's cart. Bound to a single row of the
/// cart ListView (design §4.3). Computed properties update as
/// the cashier changes quantity or applies a discount; the parent
/// <see cref="CashierViewModel"/> listens for property changes to
/// recompute the cart subtotal.
///
/// <para>
/// Tax math per the design §4.1 sequence:
/// <code>
/// LineTotal      = Quantity * UnitPrice - DiscountAmount
/// LineTaxAmount  = LineTotal * 0.19   (when TaxCategory == Standard)
/// </code>
/// Other tax categories (ZeroRate / Exempt / Other) carry zero tax.
/// The constants live here rather than in a settings file because
/// DIAN's current IVA rate (19 %) is in force until 2027.
/// </para>
/// </summary>
public partial class CartLineViewModel : ObservableObject
{
    /// <summary>
    /// Current Colombian IVA rate (DIAN). Hard-coded because the
    /// design treats the rate as a domain constant rather than a
    /// per-tenant setting; if Colombia changes the rate the value
    /// is updated here and the cashier flow's tax totals recompute
    /// automatically.
    /// </summary>
    public const decimal ColombianIvaRate = 0.19m;

    /// <summary>FK to the sold <see cref="Product"/>. Used for stock decrement + audit log.</summary>
    [ObservableProperty]
    private Guid _productId;

    /// <summary>Cashier-visible SKU (e.g. "001-7701234").</summary>
    [ObservableProperty]
    private string _sku = string.Empty;

    /// <summary>Cashier-visible product name.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Quantity sold. Defaults to 1 — a barcode scan adds a new
    /// line with qty 1, while repeated scans of the same barcode
    /// increment the existing line's quantity.
    /// </summary>
    [ObservableProperty]
    private decimal _quantity = 1m;

    /// <summary>Effective unit price at sale time (after manual overrides).</summary>
    [ObservableProperty]
    private decimal _unitPrice;

    /// <summary>Discount applied to this line in COP. Defaults to 0.</summary>
    [ObservableProperty]
    private decimal _discountAmount;

    /// <summary>Tax category letter from the sold product.</summary>
    [ObservableProperty]
    private Core.Domain.Enums.TaxCategory _taxCategory;

    /// <summary>
    /// Net amount = Quantity * UnitPrice - DiscountAmount. Updates
    /// when any of the three inputs change. Triggers
    /// <see cref="CartLineViewModel.PropertyChanged"/> so the
    /// parent ViewModel recomputes the cart subtotal.
    /// </summary>
    public decimal LineTotal => Quantity * UnitPrice - DiscountAmount;

    /// <summary>
    /// IVA amount for this line. Zero for non-Standard tax
    /// categories (Exempt, ZeroRate, Other).
    /// </summary>
    public decimal LineTaxAmount =>
        TaxCategory == Core.Domain.Enums.TaxCategory.Standard
            ? Math.Round(LineTotal * ColombianIvaRate, 2, MidpointRounding.AwayFromZero)
            : 0m;

    /// <summary>Constructor used by the cashier flow when a scan / search resolves to a product.</summary>
    public CartLineViewModel(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        ProductId = product.Id;
        Sku = product.Sku;
        Name = product.Name;
        UnitPrice = product.UnitPrice;
        TaxCategory = product.TaxCategory;
    }

    /// <summary>
    /// Test constructor. Allows direct construction of a cart line
    /// without a product (tests don't have a DbContext).
    /// </summary>
    public CartLineViewModel(
        Guid productId,
        string sku,
        string name,
        decimal unitPrice,
        Core.Domain.Enums.TaxCategory taxCategory = Core.Domain.Enums.TaxCategory.Standard,
        decimal quantity = 1m,
        decimal discountAmount = 0m)
    {
        ProductId = productId;
        Sku = sku;
        Name = name;
        UnitPrice = unitPrice;
        TaxCategory = taxCategory;
        Quantity = quantity;
        DiscountAmount = discountAmount;
    }
}
