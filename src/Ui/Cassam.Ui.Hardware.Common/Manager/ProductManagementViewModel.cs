using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Manager-flow product CRUD view-model. Drives the product list
/// + search + add/edit/delete dialogs (T2.09.a, REQ-UI-03,
/// SCN-UI-03, SCN-UI-08).
///
/// <para>
/// Sequence (design §5.1):
/// <list type="number">
///   <item>Open the page → <see cref="LoadAsync"/> reads the
///         product list via <see cref="IProductService.ListAsync"/>
///         and populates <see cref="Products"/>.</item>
///   <item>Manager types in the search box →
///         <see cref="SearchText"/> setter triggers
///         <see cref="RefreshAsync"/> which calls
///         <see cref="IProductService.SearchAsync"/>.</item>
///   <item>Manager taps "Agregar" or selects a row + "Editar" →
///         <see cref="BeginAddCommand"/> /
///         <see cref="BeginEditCommand"/> populate
///         <see cref="EditingProduct"/> + set
///         <see cref="IsEditing"/>.</item>
///   <item>Manager confirms → <see cref="SaveCommand"/> calls
///         <see cref="IProductService.SaveAsync"/> + reloads.</item>
///   <item>Manager taps "Eliminar" →
///         <see cref="DeleteCommand"/> soft-deletes via the
///         service + reloads.</item>
/// </list>
/// </para>
/// </summary>
public partial class ProductManagementViewModel : ObservableObject
{
    private readonly IProductService _service;

    /// <summary>The product list bound to the DataGrid. Refreshed by <see cref="RefreshAsync"/>.</summary>
    public ObservableCollection<ProductListItem> Products { get; } = new();

    /// <summary>The currently selected row in the DataGrid. Null when the grid is unselected.</summary>
    [ObservableProperty]
    private ProductListItem? _selectedProduct;

    /// <summary>The manager's current search input. Setter triggers a debounced refresh.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The edit-buffer for the currently-edited product. Null when not editing.</summary>
    [ObservableProperty]
    private ProductEditModel? _editingProduct;

    /// <summary>True while the edit dialog is open (controls the overlay's visibility).</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>One-line status message shown above the grid (last action result).</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Available tax categories for the edit form's ComboBox.</summary>
    public IReadOnlyList<Core.Domain.Enums.TaxCategory> TaxCategories { get; } =
        Enum.GetValues<Core.Domain.Enums.TaxCategory>();

    /// <summary>Test + DI constructor.</summary>
    public ProductManagementViewModel(IProductService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>
    /// Pull the latest list from the service. Called on page
    /// load and after every save/delete.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ProductListItem> rows = string.IsNullOrWhiteSpace(SearchText)
            ? await _service.ListAsync(ct)
            : await _service.SearchAsync(SearchText, ct);

        Products.Clear();
        foreach (var row in rows.OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase))
        {
            Products.Add(row);
        }
    }

    /// <summary>Open the edit dialog in "create new" mode.</summary>
    [RelayCommand]
    public void BeginAdd()
    {
        EditingProduct = new ProductEditModel(
            id: Guid.Empty,
            sku: string.Empty,
            name: string.Empty,
            barcode: string.Empty,
            unitPrice: 0m,
            taxCategory: Core.Domain.Enums.TaxCategory.Standard,
            stockQuantity: 0m,
            isActive: true);
        IsEditing = true;
    }

    /// <summary>Open the edit dialog with the selected row's values pre-populated.</summary>
    [RelayCommand]
    public void BeginEdit(ProductListItem? row)
    {
        if (row is null) return;
        EditingProduct = new ProductEditModel(
            id: row.Id,
            sku: row.Sku,
            name: row.Name,
            barcode: row.Barcode,
            unitPrice: row.UnitPrice,
            taxCategory: row.TaxCategory,
            stockQuantity: row.StockQuantity,
            isActive: row.IsActive);
        IsEditing = true;
    }

    /// <summary>Persist the current edit buffer + reload the list.</summary>
    [RelayCommand]
    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (EditingProduct is null) return;
        if (string.IsNullOrWhiteSpace(EditingProduct.Sku) || string.IsNullOrWhiteSpace(EditingProduct.Name))
        {
            StatusMessage = "SKU y nombre son obligatorios.";
            return;
        }
        if (EditingProduct.UnitPrice < 0m)
        {
            StatusMessage = "El precio unitario no puede ser negativo.";
            return;
        }

        await _service.SaveAsync(EditingProduct, ct);
        IsEditing = false;
        EditingProduct = null;
        StatusMessage = "Producto guardado.";
        await RefreshAsync(ct);
    }

    /// <summary>Cancel the edit dialog without saving.</summary>
    [RelayCommand]
    public void CancelEdit()
    {
        EditingProduct = null;
        IsEditing = false;
    }

    /// <summary>Soft-delete the supplied row (sets IsActive=false) + reload.</summary>
    [RelayCommand]
    public async Task DeleteAsync(ProductListItem? row, CancellationToken ct = default)
    {
        if (row is null) return;
        await _service.DeleteAsync(row.Id, ct);
        StatusMessage = $"Producto '{row.Name}' archivado.";
        await RefreshAsync(ct);
    }

    /// <summary>
    /// Optional debounce hook. The XAML side can call this when
    /// the search box settles (the actual debounce is implemented
    /// in the View via <c>DispatcherTimer</c>; the VM stays
    /// single-shot).
    /// </summary>
    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();
}