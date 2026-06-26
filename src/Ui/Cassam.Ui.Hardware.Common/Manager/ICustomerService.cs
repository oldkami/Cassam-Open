using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// Lightweight customer descriptor surfaced in the manager customer
/// list. Phase 2 only carries the basics — name, document, email,
/// phone — because the cashier flow never needs more.
/// </summary>
/// <param name="Id">Customer FK.</param>
/// <param name="DocumentNumber">Colombian Cédula / NIT / external id.</param>
/// <param name="Name">Full name (person) or Razón Social (company).</param>
/// <param name="Email">Optional contact email.</param>
/// <param name="Phone">Optional contact phone.</param>
/// <param name="CreatedAt">UTC timestamp the customer was first registered.</param>
public sealed record CustomerListItem(
    Guid Id,
    string DocumentNumber,
    string Name,
    string Email,
    string Phone,
    DateTime CreatedAt);

/// <summary>
/// Edit buffer for a customer row. Declared as a class (not a
/// record) so the XAML two-way bindings can mutate the individual
/// fields — see the rationale on <see cref="ProductEditModel"/>.
/// </summary>
public sealed class CustomerEditModel
{
    public Guid Id { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    public CustomerEditModel() { }

    public CustomerEditModel(Guid id, string documentNumber, string name, string email, string phone)
    {
        Id = id;
        DocumentNumber = documentNumber;
        Name = name;
        Email = email;
        Phone = phone;
    }
}

/// <summary>
/// Manager-flow customer CRUD. Phase 2 ships
/// <see cref="InMemoryCustomerService"/>; Phase 3 wires the
/// EF-backed implementation against the <c>customers</c> table.
/// </summary>
public interface ICustomerService
{
    /// <summary>List all customers for the active tenant.</summary>
    Task<IReadOnlyList<CustomerListItem>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Case-insensitive partial-match across document number and
    /// name. Empty <paramref name="searchText"/> returns the same
    /// set as <see cref="ListAsync"/>.
    /// </summary>
    Task<IReadOnlyList<CustomerListItem>> SearchAsync(string searchText, CancellationToken ct);

    /// <summary>Persist an edit (insert when <see cref="CustomerEditModel.Id"/> is empty).</summary>
    Task SaveAsync(CustomerEditModel model, CancellationToken ct);

    /// <summary>Soft-delete (or hard-delete, Phase 3 TBD) a customer by id.</summary>
    Task DeleteAsync(Guid customerId, CancellationToken ct);
}