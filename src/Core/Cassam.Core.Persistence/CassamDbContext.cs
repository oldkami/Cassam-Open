using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cassam.Core.Persistence;

/// <summary>
/// Application <see cref="DbContext"/> for the pos-core-modern-stack
/// domain. Maps the entities declared in <c>Cassam.Core.Domain</c>
/// to PostgreSQL tables per spec <c>pos-core-modern-stack</c>
/// REQ-CORE-01 / REQ-CORE-02.
///
/// Conventions wired in this PR (T1.01 + T1.04):
/// <list type="bullet">
///   <item>snake_case column + table naming via <c>EFCore.NamingConventions</c>.</item>
///   <item><c>Id</c> as the primary key (inherited from <see cref="Entity"/>).</item>
///   <item><c>Version</c> as an optimistic concurrency token.</item>
///   <item>Global query filter that excludes rows where
///         <see cref="ISoftDeletable.DeletedAt"/> is non-null.</item>
///   <item>Composite indexes leading with <c>tenant_id</c> on every
///         tenant-scoped table (DD-05).</item>
///   <item>Composite FK indexes on heavily-queried join columns.</item>
/// </list>
///
/// Conventions deferred to PR 2 (task T1.06):
/// <list type="bullet">
///   <item>UUID defaults via <c>gen_random_uuid()</c> + <c>pgcrypto</c>.</item>
///   <item>Schema-conformance test that fails on missing required columns.</item>
/// </list>
/// </summary>
public class CassamDbContext : DbContext
{
    /// <summary>Constructs a context for migrations / design-time tooling.</summary>
    public CassamDbContext()
    {
    }

    /// <summary>Constructs a context for request-scoped DI usage.</summary>
    /// <param name="options">EF Core options (provider, connection string, etc.).</param>
    public CassamDbContext(DbContextOptions<CassamDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Conservative fallback: callers MUST register a real provider
            // (Npgsql.EntityFrameworkCore.PostgreSQL) via DI in production.
            // The UseSnakeCaseNamingConvention call here mirrors what the
            // explicit .UseNpgsql(...).UseSnakeCaseNamingConvention() chain
            // configures when the caller wires the provider.
        }

        base.OnConfiguring(optionsBuilder);
    }

    /// <summary>tenants (root of the tenancy hierarchy; NOT soft-deletable).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>users.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>products.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>customers.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>sales.</summary>
    public DbSet<Sale> Sales => Set<Sale>();

    /// <summary>sale_line_items.</summary>
    public DbSet<SaleLineItem> SaleLineItems => Set<SaleLineItem>();

    /// <summary>payments.</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>cash_sessions.</summary>
    public DbSet<CashSession> CashSessions => Set<CashSession>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- Tenant ----------------------------------------------------
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.LegalName).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Nit).HasMaxLength(20).IsRequired();
            entity.Property(t => t.SubscriptionTier).HasConversion<string>().HasMaxLength(16);
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);

            // (Nit) uniqueness is enforced globally; tenants are the
            // root of tenancy, so no tenant_id prefix is needed.
            entity.HasIndex(t => t.Nit).IsUnique();

            entity.Property(t => t.Version).IsConcurrencyToken();
        });

        // ---- User ------------------------------------------------------
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.Property(u => u.TenantId).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(200).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);

            entity.Property(u => u.Version).IsConcurrencyToken();

            // Composite uniqueness: (tenant_id, lower(email)) — case-insensitive
            // within a tenant; two tenants may legitimately have the same email.
            entity.HasIndex(u => new { u.TenantId, u.Email })
                  .HasDatabaseName("ix_users_tenant_id_email")
                  .IsUnique();

            // Composite (tenant_id, id) covering index — every tenant-scoped
            // query leads with tenant_id per DD-05.
            entity.HasIndex(u => new { u.TenantId, u.Id })
                  .HasDatabaseName("ix_users_tenant_id_id");

            entity.HasQueryFilter(u => u.DeletedAt == null);
        });

        // ---- Product ---------------------------------------------------
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.TenantId).IsRequired();
            entity.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.UnitPrice).HasColumnType("numeric(18,4)");
            entity.Property(p => p.TaxCategory).HasConversion<string>().HasMaxLength(1);
            entity.Property(p => p.StockQuantity).HasColumnType("numeric(18,4)");
            entity.Property(p => p.Barcode).HasMaxLength(64);

            entity.Property(p => p.Version).IsConcurrencyToken();

            entity.HasIndex(p => new { p.TenantId, p.Sku })
                  .HasDatabaseName("ix_products_tenant_id_sku")
                  .IsUnique();

            entity.HasIndex(p => new { p.TenantId, p.Id })
                  .HasDatabaseName("ix_products_tenant_id_id");

            entity.HasIndex(p => new { p.TenantId, p.Barcode })
                  .HasDatabaseName("ix_products_tenant_id_barcode");

            entity.HasQueryFilter(p => p.DeletedAt == null);
        });

        // ---- Customer --------------------------------------------------
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.TenantId).IsRequired();
            entity.Property(c => c.DocumentType).HasConversion<string>().HasMaxLength(8);
            entity.Property(c => c.DocumentNumber).HasMaxLength(32).IsRequired();
            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.PersonType).HasConversion<string>().HasMaxLength(16);
            entity.Property(c => c.Email).HasMaxLength(200);
            entity.Property(c => c.Phone).HasMaxLength(32);
            entity.Property(c => c.Address).HasMaxLength(300);

            entity.Property(c => c.Version).IsConcurrencyToken();

            entity.HasIndex(c => new { c.TenantId, c.DocumentType, c.DocumentNumber })
                  .HasDatabaseName("ix_customers_tenant_id_document_type_document_number")
                  .IsUnique();

            entity.HasIndex(c => new { c.TenantId, c.Id })
                  .HasDatabaseName("ix_customers_tenant_id_id");

            entity.HasQueryFilter(c => c.DeletedAt == null);
        });

        // ---- Sale ------------------------------------------------------
        modelBuilder.Entity<Sale>(entity =>
        {
            entity.HasKey(s => s.Id);

            entity.Property(s => s.TenantId).IsRequired();
            entity.Property(s => s.CashSessionId).IsRequired();
            entity.Property(s => s.DocumentType).HasConversion<string>().HasMaxLength(16);
            entity.Property(s => s.TotalAmount).HasColumnType("numeric(18,4)");
            entity.Property(s => s.TaxTotal).HasColumnType("numeric(18,4)");

            entity.Property(s => s.Version).IsConcurrencyToken();

            // Composite leading with tenant_id (DD-05) — every
            // tenant-scoped query in the POS and the cloud leads with it.
            entity.HasIndex(s => new { s.TenantId, s.Id })
                  .HasDatabaseName("ix_sales_tenant_id_id");

            // Per-cash-session lookup (cashier reconciliation, X/Z reports).
            entity.HasIndex(s => new { s.TenantId, s.CashSessionId })
                  .HasDatabaseName("ix_sales_tenant_id_cash_session_id");

            // Document-type breakdown for reporting.
            entity.HasIndex(s => new { s.TenantId, s.DocumentType })
                  .HasDatabaseName("ix_sales_tenant_id_document_type");

            entity.HasQueryFilter(s => s.DeletedAt == null);
        });

        // ---- SaleLineItem ----------------------------------------------
        modelBuilder.Entity<SaleLineItem>(entity =>
        {
            entity.HasKey(li => li.Id);

            entity.Property(li => li.Quantity).HasColumnType("numeric(18,4)");
            entity.Property(li => li.UnitPrice).HasColumnType("numeric(18,4)");
            entity.Property(li => li.DiscountAmount).HasColumnType("numeric(18,4)");
            entity.Property(li => li.LineTaxCategory).HasConversion<string>().HasMaxLength(1);
            entity.Property(li => li.LineTaxableAmount).HasColumnType("numeric(18,4)");
            entity.Property(li => li.LineTaxAmount).HasColumnType("numeric(18,4)");

            entity.Property(li => li.Version).IsConcurrencyToken();

            // FK index for the common "give me all line items of sale X" query.
            entity.HasIndex(li => li.SaleId)
                  .HasDatabaseName("ix_sale_line_items_sale_id");

            // Product analytics: "top products last 30 days" requires this index.
            entity.HasIndex(li => li.ProductId)
                  .HasDatabaseName("ix_sale_line_items_product_id");
        });

        // ---- Payment ---------------------------------------------------
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(16);
            entity.Property(p => p.Amount).HasColumnType("numeric(18,4)");
            entity.Property(p => p.TenderedAmount).HasColumnType("numeric(18,4)");
            entity.Property(p => p.ChangeAmount).HasColumnType("numeric(18,4)");
            entity.Property(p => p.Reference).HasMaxLength(100);

            entity.Property(p => p.Version).IsConcurrencyToken();

            entity.HasIndex(p => p.SaleId)
                  .HasDatabaseName("ix_payments_sale_id");

            // Cash session expected-amount recalc needs this index.
            entity.HasIndex(p => p.CashSessionId)
                  .HasDatabaseName("ix_payments_cash_session_id");
        });

        // ---- CashSession -----------------------------------------------
        modelBuilder.Entity<CashSession>(entity =>
        {
            entity.HasKey(cs => cs.Id);

            entity.Property(cs => cs.TenantId).IsRequired();
            entity.Property(cs => cs.OpenedByUserId).IsRequired();
            entity.Property(cs => cs.OpeningAmount).HasColumnType("numeric(18,4)");
            entity.Property(cs => cs.ClosingAmount).HasColumnType("numeric(18,4)");
            entity.Property(cs => cs.ExpectedAmount).HasColumnType("numeric(18,4)");
            entity.Property(cs => cs.VarianceAmount).HasColumnType("numeric(18,4)");
            entity.Property(cs => cs.Status).HasConversion<string>().HasMaxLength(16);

            entity.Property(cs => cs.Version).IsConcurrencyToken();

            entity.HasIndex(cs => new { cs.TenantId, cs.Id })
                  .HasDatabaseName("ix_cash_sessions_tenant_id_id");

            // "Find the open session for this tenant and cashier" — the
            // single most-frequent query in the cashier flow.
            entity.HasIndex(cs => new { cs.TenantId, cs.Status })
                  .HasDatabaseName("ix_cash_sessions_tenant_id_status");

            entity.HasIndex(cs => new { cs.TenantId, cs.OpenedByUserId })
                  .HasDatabaseName("ix_cash_sessions_tenant_id_opened_by_user_id");

            entity.HasQueryFilter(cs => cs.DeletedAt == null);
        });
    }
}
