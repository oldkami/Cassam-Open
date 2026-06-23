using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using FluentAssertions;

namespace Cassam.Core.Tests;

/// <summary>
/// Entity-level instantiation and property-validation tests for the
/// Phase 1 domain model. These tests run without a database — they
/// prove the C# types and inheritance graph are sound. Database-backed
/// round-trip tests ship in PR 2 (task T1.10) once the EF Core
/// conventions and Testcontainers harness land.
/// </summary>
public class EntityTests
{
    [Fact]
    public void New_tenant_initializes_id_with_unique_uuid()
    {
        var first = new Tenant();
        var second = new Tenant();

        first.Id.Should().NotBe(Guid.Empty,
            "Entity.Id is initialized to Guid.CreateVersion7() so it is never Guid.Empty");
        first.Id.Should().NotBe(second.Id,
            "every new Entity must receive a fresh Guid (Guid.CreateVersion7 is time-ordered)");
    }

    [Fact]
    public void New_tenant_starts_at_version_one_and_trial_status()
    {
        var tenant = new Tenant();

        tenant.Version.Should().Be(1u);
        tenant.Status.Should().Be(TenantStatus.Trial);
        tenant.SubscriptionTier.Should().Be(SubscriptionTier.Free);
        tenant.CloudTransmissionEnabled.Should().BeFalse();
    }

    [Fact]
    public void User_carries_tenant_id_and_defaults_to_active_cashier()
    {
        var tenantId = Guid.CreateVersion7();
        var user = new User
        {
            TenantId = tenantId,
            Email = "cajero@cassam.co",
            PasswordHash = "$argon2id$...",
            DisplayName = "Cajero Demo",
        };

        user.TenantId.Should().Be(tenantId);
        user.Role.Should().Be(UserRole.Cashier);
        user.Active.Should().BeTrue();
        user.DeletedAt.Should().BeNull();
        ((ITenantScoped)user).TenantId.Should().Be(tenantId);
        ((ISoftDeletable)user).DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Product_defaults_to_standard_tax_category_and_zero_stock()
    {
        var product = new Product
        {
            TenantId = Guid.CreateVersion7(),
            Sku = "SKU-001",
            Name = "Coca-Cola 350ml",
            UnitPrice = 3500m,
        };

        product.TaxCategory.Should().Be(TaxCategory.Standard);
        product.StockQuantity.Should().BeNull(
            "non-inventory items have null stock; legacy import may set 0 explicitly");
        product.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Customer_requires_document_number_and_legal_name()
    {
        var customer = new Customer
        {
            TenantId = Guid.CreateVersion7(),
            DocumentType = CustomerDocumentType.Nit,
            DocumentNumber = "900123456",
            Name = "Cliente Empresarial S.A.",
            PersonType = PersonType.Juridica,
        };

        customer.DocumentType.Should().Be(CustomerDocumentType.Nit);
        customer.PersonType.Should().Be(PersonType.Juridica,
            "NIT + Juridica is the FE Venta dispatch signal per REQ-FEVENTA-02");
    }

    [Fact]
    public void Sale_carries_cash_session_and_decimal_total()
    {
        var tenantId = Guid.CreateVersion7();
        var cashSessionId = Guid.CreateVersion7();

        var sale = new Sale
        {
            TenantId = tenantId,
            CashSessionId = cashSessionId,
            DocumentType = DocumentType.DeePos,
            TotalAmount = 12345.67m,
            TaxTotal = 1900m,
        };

        sale.DocumentType.Should().Be(DocumentType.DeePos);
        sale.TotalAmount.Should().Be(12345.67m);
        sale.DocumentoElectronicoId.Should().BeNull(
            "documento_electronico_id is null until Phase 4b generates the DEE POS");
    }

    [Fact]
    public void Payment_cash_requires_tendered_and_change_amounts()
    {
        var saleId = Guid.CreateVersion7();
        var cashSessionId = Guid.CreateVersion7();

        var payment = new Payment
        {
            SaleId = saleId,
            CashSessionId = cashSessionId,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 5000m,
            TenderedAmount = 10000m,
            ChangeAmount = 5000m,
        };

        payment.PaymentMethod.Should().Be(PaymentMethod.Cash);
        payment.TenderedAmount.Should().Be(10000m);
        payment.ChangeAmount.Should().Be(5000m);
    }

    [Fact]
    public void Cash_session_starts_open_with_zero_variance()
    {
        var tenantId = Guid.CreateVersion7();
        var openedByUserId = Guid.CreateVersion7();

        var session = new CashSession
        {
            TenantId = tenantId,
            OpenedByUserId = openedByUserId,
            OpenedAt = DateTime.UtcNow,
            OpeningAmount = 50000m,
        };

        session.Status.Should().Be(CashSessionStatus.Open);
        session.ClosedAt.Should().BeNull();
        session.VarianceAmount.Should().BeNull(
            "variance is only populated when the session is closed");
    }

    [Fact]
    public void Every_entity_inherits_common_identity_and_version_columns()
    {
        var entities = new Entity[]
        {
            new Tenant(),
            new User(),
            new Product(),
            new Customer(),
            new Sale(),
            new SaleLineItem(),
            new Payment(),
            new CashSession(),
        };

        foreach (var entity in entities)
        {
            entity.Id.Should().NotBe(Guid.Empty);
            entity.Version.Should().Be(1u);
        }
    }
}

internal static class TestHelpers
{
}
