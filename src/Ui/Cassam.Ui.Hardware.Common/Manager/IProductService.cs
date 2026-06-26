using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Lightweight product descriptor returned by the manager-flow
/// product list / search. Mirrors the cashier flow's
/// <see cref="Cashier.ProductSearchResult"/> shape but adds the
/// <see cref="StockQuantity"/> + <see cref="IsActive"/> columns the
/// manager UI surfaces.
/// </summary>
/// <param name="Id">Product FK.</param>
/// <param name="Sku">Cashier-visible SKU.</param>
/// <param name="Name">Cashier-visible product name.</param>
/// <param name="Barcode">EAN-13 / UPC / Code-128 (may be empty for non-barcoded items).</param>
/// <param name="UnitPrice">Current unit price in COP.</param>
/// <param name="TaxCategory">DIAN tax category letter.</param>
/// <param name="StockQuantity">Current on-hand stock (Phase 3 wires the real count).</param>
/// <param name="IsActive">True while the product is sellable; false = soft-archived.</param>
public sealed record ProductListItem(
    Guid Id,
    string Sku,
    string Name,
    string Barcode,
    decimal UnitPrice,
    Core.Domain.Enums.TaxCategory TaxCategory,
    decimal StockQuantity,
    bool IsActive);

/// <summary>
/// Edit buffer for a single product row. Holds the form state while
/// the manager is editing — committed to the service via
/// <see cref="IProductService.SaveAsync"/>.
///
/// <para>
/// Declared as a class (not a record) so the XAML two-way bindings
/// can mutate the individual fields. C# 9 records with positional
/// parameters generate init-only properties which the Uno XAML
/// compiler cannot assign through <c>{x:Bind ..., Mode=TwoWay}</c>.
/// </para>
/// </summary>
public sealed class ProductEditModel
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public Core.Domain.Enums.TaxCategory TaxCategory { get; set; } = Core.Domain.Enums.TaxCategory.Standard;
    public decimal StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;

    public ProductEditModel() { }

    public ProductEditModel(
        Guid id,
        string sku,
        string name,
        string barcode,
        decimal unitPrice,
        Core.Domain.Enums.TaxCategory taxCategory,
        decimal stockQuantity,
        bool isActive)
    {
        Id = id;
        Sku = sku;
        Name = name;
        Barcode = barcode;
        UnitPrice = unitPrice;
        TaxCategory = taxCategory;
        StockQuantity = stockQuantity;
        IsActive = isActive;
    }
}

/// <summary>
/// Manager-flow product catalog. Phase 2 ships an in-memory
/// implementation (<see cref="InMemoryProductService"/>); Phase 3
/// replaces it with the EF-backed
/// <c>Cassam.Core.Services.IProductCatalogService</c> wired to the
/// <c>products</c> + <c>stock_levels</c> tables.
///
/// The interface is the contract the manager UI consumes; the
/// cashier flow's <see cref="Cashier.IProductCatalog"/> is a
/// narrower read-only view built on top of it.
/// </summary>
public interface IProductService
{
    /// <summary>
    /// List all products for the active tenant. The result is
    /// unsorted — the manager view applies its own sort.
    /// </summary>
    Task<IReadOnlyList<ProductListItem>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Case-insensitive partial-match across SKU, name, and barcode.
    /// Empty <paramref name="searchText"/> returns the same set as
    /// <see cref="ListAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ProductListItem>> SearchAsync(string searchText, CancellationToken ct);

    /// <summary>Persist an edit (insert when <see cref="ProductEditModel.Id"/> is empty).</summary>
    Task SaveAsync(ProductEditModel model, CancellationToken ct);

    /// <summary>Soft-delete (set IsActive=false) a product by id.</summary>
    Task DeleteAsync(Guid productId, CancellationToken ct);
}