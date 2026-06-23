using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Cassam.Core.Persistence;

namespace Cassam.Core.Tests;

/// <summary>
/// Soft-delete contract tests. These verify the C# side of the
/// soft-delete pattern (the global query filter is added in
/// <see cref="CassamDbContext.OnModelCreating"/>). Database-backed
/// round-trip tests ship in PR 2 (task T1.10) once the Testcontainers
/// PostgreSQL harness lands.
/// </summary>
public class SoftDeleteTests
{
    [Theory]
    [InlineData(typeof(User))]
    [InlineData(typeof(Product))]
    [InlineData(typeof(Customer))]
    [InlineData(typeof(Sale))]
    [InlineData(typeof(CashSession))]
    public void Tenant_scoped_entity_implements_ISoftDeletable(Type entityType)
    {
        typeof(ISoftDeletable).IsAssignableFrom(entityType)
            .Should().BeTrue($"{entityType.Name} must support soft-delete per REQ-CORE-02");
    }

    [Fact]
    public void Tenant_does_not_implement_ISoftDeletable_because_it_is_the_root()
    {
        // Per SCN-CORE-13 + REQ-MT-05, the tenant itself transitions to
        // TenantStatus.Deleted instead of using a DeletedAt column.
        typeof(ISoftDeletable).IsAssignableFrom(typeof(Tenant))
            .Should().BeFalse("Tenant deletion is modeled via TenantStatus.Deleted, not soft-delete");
    }

    [Fact]
    public void Newly_created_soft_deletable_entity_has_null_DeletedAt()
    {
        var user = new User { TenantId = Guid.CreateVersion7() };

        user.DeletedAt.Should().BeNull();
        ((ISoftDeletable)user).DeletedAt.Should().BeNull();
    }

    [Fact]
    public void DbContext_applies_global_query_filter_to_soft_deletable_entities()
    {
        // The EF Core global query filter is what makes the soft-delete
        // behavior effective at query time. We verify the model is
        // configured correctly without touching a database.
        using var context = new CassamDbContext(
            new DbContextOptionsBuilder<CassamDbContext>()
                .UseInMemoryDatabase($"soft-delete-test-{Guid.NewGuid():N}")
                .Options);

        var userEntity = context.Model.FindEntityType(typeof(User));
        userEntity.Should().NotBeNull();
        userEntity!.GetDeclaredQueryFilters().Should().NotBeEmpty(
            "User must have a global query filter that excludes rows where DeletedAt is non-null");

        var productEntity = context.Model.FindEntityType(typeof(Product));
        productEntity!.GetDeclaredQueryFilters().Should().NotBeEmpty();

        var customerEntity = context.Model.FindEntityType(typeof(Customer));
        customerEntity!.GetDeclaredQueryFilters().Should().NotBeEmpty();

        var saleEntity = context.Model.FindEntityType(typeof(Sale));
        saleEntity!.GetDeclaredQueryFilters().Should().NotBeEmpty();

        var cashSessionEntity = context.Model.FindEntityType(typeof(CashSession));
        cashSessionEntity!.GetDeclaredQueryFilters().Should().NotBeEmpty();
    }

    [Fact]
    public void DbContext_does_not_apply_query_filter_to_non_soft_deletable_entities()
    {
        // SaleLineItem and Payment are NOT soft-deletable — they're
        // cascade-deleted with their parent Sale.
        using var context = new CassamDbContext(
            new DbContextOptionsBuilder<CassamDbContext>()
                .UseInMemoryDatabase($"no-filter-test-{Guid.NewGuid():N}")
                .Options);

        var lineItemEntity = context.Model.FindEntityType(typeof(SaleLineItem));
        lineItemEntity!.GetDeclaredQueryFilters().Should().BeEmpty(
            "SaleLineItem is owned by Sale and is not soft-deletable");

        var paymentEntity = context.Model.FindEntityType(typeof(Payment));
        paymentEntity!.GetDeclaredQueryFilters().Should().BeEmpty(
            "Payment is owned by Sale and is not soft-deletable");
    }

    [Fact]
    public void Tenant_scoped_entity_implements_ITenantScoped()
    {
        // Strong-typed compile-time check: every tenant-scoped entity
        // must declare ITenantScoped so the global filter / convention
        // surface in PR 2 (T1.06) can find them via reflection.
        var tenantScopedTypes = new[]
        {
            typeof(User),
            typeof(Product),
            typeof(Customer),
            typeof(Sale),
            typeof(CashSession),
        };

        foreach (var type in tenantScopedTypes)
        {
            typeof(ITenantScoped).IsAssignableFrom(type)
                .Should().BeTrue($"{type.Name} must implement ITenantScoped for DD-05 RLS");
        }

        // Counter-examples: Tenant is the root of tenancy and is NOT scoped.
        typeof(ITenantScoped).IsAssignableFrom(typeof(Tenant))
            .Should().BeFalse();
        // SaleLineItem / Payment are not directly tenant-scoped — they
        // inherit tenant context through their parent Sale / CashSession.
        typeof(ITenantScoped).IsAssignableFrom(typeof(SaleLineItem)).Should().BeFalse();
        typeof(ITenantScoped).IsAssignableFrom(typeof(Payment)).Should().BeFalse();
    }
}
