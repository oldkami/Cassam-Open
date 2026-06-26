using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cassam.Ui.Hardware.Common.Manager;

/// <summary>
/// In-memory customer list for Phase 2 preview. Mirrors
/// <see cref="InMemoryProductService"/>: the production
/// implementation swaps in behind <see cref="ICustomerService"/>
/// without UI churn (R-UI-06).
/// </summary>
public sealed class InMemoryCustomerService : ICustomerService
{
    private readonly List<CustomerListItem> _customers;

    public InMemoryCustomerService(IEnumerable<CustomerListItem> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _customers = seed.ToList();
    }

    public InMemoryCustomerService()
        : this(new[]
        {
            new CustomerListItem(
                Id: Guid.Parse("20000000-0000-0000-0000-000000000001"),
                DocumentNumber: "79123456",
                Name: "María Fernanda López",
                Email: "maria.lopez@example.co",
                Phone: "+57 311 555 1234",
                CreatedAt: new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc)),
            new CustomerListItem(
                Id: Guid.Parse("20000000-0000-0000-0000-000000000002"),
                DocumentNumber: "900123456-7",
                Name: "Comercial Andina S.A.S.",
                Email: "facturacion@comercialandina.co",
                Phone: "+57 1 555 9876",
                CreatedAt: new DateTime(2026, 2, 4, 14, 30, 0, DateTimeKind.Utc)),
            new CustomerListItem(
                Id: Guid.Parse("20000000-0000-0000-0000-000000000003"),
                DocumentNumber: "52345678",
                Name: "Carlos Andrés Pérez",
                Email: "carlos.perez@example.co",
                Phone: "+57 320 444 7788",
                CreatedAt: new DateTime(2026, 3, 22, 11, 15, 0, DateTimeKind.Utc)),
        })
    {
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CustomerListItem>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CustomerListItem>>(_customers.ToArray());

    /// <inheritdoc />
    public Task<IReadOnlyList<CustomerListItem>> SearchAsync(string searchText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return ListAsync(ct);
        }

        var hit = _customers
            .Where(c =>
                c.DocumentNumber.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return Task.FromResult<IReadOnlyList<CustomerListItem>>(hit);
    }

    /// <inheritdoc />
    public Task SaveAsync(CustomerEditModel model, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);

        var idx = _customers.FindIndex(c => c.Id == model.Id);
        if (idx >= 0)
        {
            _customers[idx] = _customers[idx] with
            {
                DocumentNumber = model.DocumentNumber,
                Name = model.Name,
                Email = model.Email,
                Phone = model.Phone,
            };
        }
        else
        {
            _customers.Add(new CustomerListItem(
                Id: model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
                DocumentNumber: model.DocumentNumber,
                Name: model.Name,
                Email: model.Email,
                Phone: model.Phone,
                CreatedAt: DateTime.UtcNow));
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid customerId, CancellationToken ct)
    {
        var idx = _customers.FindIndex(c => c.Id == customerId);
        if (idx >= 0)
        {
            _customers.RemoveAt(idx);
        }
        return Task.CompletedTask;
    }
}