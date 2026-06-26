using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// In-memory product catalog for the manager flow's Phase 2
/// preview. Seeds a small fixture so the manager view has
/// non-empty content on first launch; the production
/// implementation lands in Phase 3 behind the same
/// <see cref="IProductService"/> interface so the UI requires no
/// changes (R-UI-06 mitigation, design §7).
/// </summary>
public sealed class InMemoryProductService : IProductService
{
    private readonly List<ProductListItem> _products;

    /// <summary>Build the service with the provided seed list (test-friendly constructor).</summary>
    public InMemoryProductService(IEnumerable<ProductListItem> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _products = seed.ToList();
    }

    /// <summary>
    /// Convenience constructor with a tiny three-row fixture so the
    /// production app's first launch shows real product rows.
    /// </summary>
    public InMemoryProductService()
        : this(new[]
        {
            new ProductListItem(
                Id: Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Sku: "001-7701234",
                Name: "Arroz Diana 1kg",
                Barcode: "7701234567890",
                UnitPrice: 3500m,
                TaxCategory: Core.Domain.Enums.TaxCategory.Standard,
                StockQuantity: 120m,
                IsActive: true),
            new ProductListItem(
                Id: Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Sku: "002-7709876",
                Name: "Leche Alpina 1L",
                Barcode: "7709876543210",
                UnitPrice: 4500m,
                TaxCategory: Core.Domain.Enums.TaxCategory.Exempt,
                StockQuantity: 80m,
                IsActive: true),
            new ProductListItem(
                Id: Guid.Parse("10000000-0000-0000-0000-000000000003"),
                Sku: "003-7705555",
                Name: "Pan Bimbo Tajado",
                Barcode: "7705555111222",
                UnitPrice: 6800m,
                TaxCategory: Core.Domain.Enums.TaxCategory.Exempt,
                StockQuantity: 24m,
                IsActive: true),
        })
    {
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductListItem>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProductListItem>>(_products.ToArray());

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductListItem>> SearchAsync(string searchText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return ListAsync(ct);
        }

        var hit = _products
            .Where(p =>
                p.Sku.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                p.Barcode.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return Task.FromResult<IReadOnlyList<ProductListItem>>(hit);
    }

    /// <inheritdoc />
    public Task SaveAsync(ProductEditModel model, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);

        var idx = _products.FindIndex(p => p.Id == model.Id);
        var item = new ProductListItem(
            Id: model.Id,
            Sku: model.Sku,
            Name: model.Name,
            Barcode: model.Barcode,
            UnitPrice: model.UnitPrice,
            TaxCategory: model.TaxCategory,
            StockQuantity: model.StockQuantity,
            IsActive: model.IsActive);

        if (idx >= 0)
        {
            _products[idx] = item;
        }
        else
        {
            _products.Add(item);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid productId, CancellationToken ct)
    {
        var idx = _products.FindIndex(p => p.Id == productId);
        if (idx >= 0)
        {
            // Soft-delete: flip IsActive to false rather than removing
            // the row, so historic sales still reference a product
            // record (audit log + receipts must resolve the name).
            _products[idx] = _products[idx] with { IsActive = false };
        }
        return Task.CompletedTask;
    }
}