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
/// Manager-flow customer CRUD view-model. Mirrors
/// <see cref="ProductManagementViewModel"/> for the customer
/// entity (T2.09.b). The cashier flow never edits customers
/// directly so this VM is the only place the CRUD commands live.
/// </summary>
public partial class CustomerManagementViewModel : ObservableObject
{
    private readonly ICustomerService _service;

    /// <summary>The customer list bound to the DataGrid. Refreshed by <see cref="RefreshAsync"/>.</summary>
    public ObservableCollection<CustomerListItem> Customers { get; } = new();

    /// <summary>The currently selected row in the DataGrid.</summary>
    [ObservableProperty]
    private CustomerListItem? _selectedCustomer;

    /// <summary>The manager's current search input.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The edit-buffer for the currently-edited customer. Null when not editing.</summary>
    [ObservableProperty]
    private CustomerEditModel? _editingCustomer;

    /// <summary>True while the edit dialog is open.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>One-line status message shown above the grid.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public CustomerManagementViewModel(ICustomerService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <inheritdoc cref="ProductManagementViewModel.RefreshAsync" />
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        IReadOnlyList<CustomerListItem> rows = string.IsNullOrWhiteSpace(SearchText)
            ? await _service.ListAsync(ct)
            : await _service.SearchAsync(SearchText, ct);

        Customers.Clear();
        foreach (var row in rows.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            Customers.Add(row);
        }
    }

    [RelayCommand]
    public void BeginAdd()
    {
        EditingCustomer = new CustomerEditModel(
            id: Guid.Empty,
            documentNumber: string.Empty,
            name: string.Empty,
            email: string.Empty,
            phone: string.Empty);
        IsEditing = true;
    }

    [RelayCommand]
    public void BeginEdit(CustomerListItem? row)
    {
        if (row is null) return;
        EditingCustomer = new CustomerEditModel(
            id: row.Id,
            documentNumber: row.DocumentNumber,
            name: row.Name,
            email: row.Email,
            phone: row.Phone);
        IsEditing = true;
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (EditingCustomer is null) return;
        if (string.IsNullOrWhiteSpace(EditingCustomer.DocumentNumber) || string.IsNullOrWhiteSpace(EditingCustomer.Name))
        {
            StatusMessage = "Número de documento y nombre son obligatorios.";
            return;
        }

        await _service.SaveAsync(EditingCustomer, ct);
        IsEditing = false;
        EditingCustomer = null;
        StatusMessage = "Cliente guardado.";
        await RefreshAsync(ct);
    }

    [RelayCommand]
    public void CancelEdit()
    {
        EditingCustomer = null;
        IsEditing = false;
    }

    [RelayCommand]
    public async Task DeleteAsync(CustomerListItem? row, CancellationToken ct = default)
    {
        if (row is null) return;
        await _service.DeleteAsync(row.Id, ct);
        StatusMessage = $"Cliente '{row.Name}' eliminado.";
        await RefreshAsync(ct);
    }

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();
}