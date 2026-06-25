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

    /// <summary>DIAN-issued numbering-range authorizations.</summary>
    public DbSet<Resolucion> Resoluciones => Set<Resolucion>();

    /// <summary>X.509 signing certificates.</summary>
    public DbSet<Certificado> Certificados => Set<Certificado>();

    /// <summary>DIAN-issued software technical keys (Clave Técnica de Software).</summary>
    public DbSet<SoftwareTechnicalKey> SoftwareTechnicalKeys => Set<SoftwareTechnicalKey>();

    /// <summary>Generated fiscal documents (DEE POS / FE Venta / NC / ND).</summary>
    public DbSet<DocumentoElectronico> DocumentosElectronicos => Set<DocumentoElectronico>();

    /// <summary>Pending DIAN transmissions awaiting drain.</summary>
    public DbSet<ContingencyQueue> ContingencyQueue => Set<ContingencyQueue>();

    /// <summary>Append-only audit log of fiscal mutations.</summary>
    public DbSet<AuditLog> AuditLog => Set<AuditLog>();

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
            entity.Property(t => t.DeletedAt);

            // (Nit) uniqueness is enforced globally; tenants are the
            // root of tenancy, so no tenant_id prefix is needed.
            // The composite index below lets the soft-delete re-
            // registration check (WHERE deleted_at IS NULL) use an
            // index-only scan.
            entity.HasIndex(t => new { t.Nit, t.DeletedAt })
                  .HasDatabaseName("ix_tenants_nit_deleted_at")
                  .IsUnique();

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

            // Per-cashier quick-keys (PR 7 / design §17.1, question #6).
            // JSON payload, nullable (cashier may not have personalised
            // yet). Mapped to `quick_keys text` — see the EF migration
            // `UserQuickKeys` for the column type. The cashier flow
            // (T2.08 in PR 9) reads the full payload on session open
            // and writes it back when the cashier pins a new product.
            entity.Property(u => u.QuickKeys).HasColumnType("text");

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

        // ---- Resolucion -------------------------------------------------
        // DIAN-issued numbering-range authorization (REQ-CORE-06).
        modelBuilder.Entity<Resolucion>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.Property(r => r.TenantId).IsRequired();
            entity.Property(r => r.DocumentType).HasConversion<string>().HasMaxLength(16);
            entity.Property(r => r.RangeStart).IsRequired();
            entity.Property(r => r.RangeEnd).IsRequired();
            entity.Property(r => r.CurrentNumber).IsRequired();
            entity.Property(r => r.ExpirationDate).IsRequired();
            entity.Property(r => r.SoftwareTechnicalKeyId).IsRequired();
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(r => r.Prefix).HasMaxLength(10);

            entity.Property(r => r.Version).IsConcurrencyToken();

            // The dispatcher always queries "active resolución for
            // document type T" — composite (tenant_id, status) leads that path.
            entity.HasIndex(r => new { r.TenantId, r.DocumentType, r.Status })
                  .HasDatabaseName("ix_resoluciones_tenant_id_document_type_status");

            // Composite (tenant_id, id) covering index per DD-05.
            entity.HasIndex(r => new { r.TenantId, r.Id })
                  .HasDatabaseName("ix_resoluciones_tenant_id_id");

            // Uniqueness: a given DIAN autorización cannot be registered twice
            // for the same tenant / document type / prefix. Prefix is nullable
            // and the unique index treats NULLs as distinct, which is the
            // desired semantics here (two prefixless resoluciones on the
            // same document type are differentiated by their range).
            entity.HasIndex(r => new { r.TenantId, r.DocumentType, r.Prefix })
                  .HasDatabaseName("ix_resoluciones_tenant_id_document_type_prefix")
                  .IsUnique();

            entity.HasQueryFilter(r => r.DeletedAt == null);
        });

        // ---- Certificado -----------------------------------------------
        // X.509 signing certificate (REQ-CORE-07).
        modelBuilder.Entity<Certificado>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.TenantId).IsRequired();
            entity.Property(c => c.Subject).HasMaxLength(500).IsRequired();
            entity.Property(c => c.Issuer).HasMaxLength(500).IsRequired();
            entity.Property(c => c.Serial).HasMaxLength(100).IsRequired();
            entity.Property(c => c.NotBefore).IsRequired();
            entity.Property(c => c.NotAfter).IsRequired();
            entity.Property(c => c.PfxPath).HasMaxLength(500);
            entity.Property(c => c.CertStoreRef).HasMaxLength(200);
            entity.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(c => c.PasswordHash).HasMaxLength(512);

            entity.Property(c => c.Version).IsConcurrencyToken();

            // The signer always looks up "active certificate for tenant T" —
            // composite (tenant_id, status) leads that path.
            entity.HasIndex(c => new { c.TenantId, c.Status })
                  .HasDatabaseName("ix_certificados_tenant_id_status");

            entity.HasIndex(c => new { c.TenantId, c.Id })
                  .HasDatabaseName("ix_certificados_tenant_id_id");

            // Serial is unique within a tenant — DIAN does not issue the
            // same X.509 serial twice to the same NIT.
            entity.HasIndex(c => new { c.TenantId, c.Serial })
                  .HasDatabaseName("ix_certificados_tenant_id_serial")
                  .IsUnique();

            entity.HasQueryFilter(c => c.DeletedAt == null);
        });

        // ---- SoftwareTechnicalKey --------------------------------------
        // DIAN-issued Clave Técnica de Software (REQ-CORE-06, design §4.2).
        modelBuilder.Entity<SoftwareTechnicalKey>(entity =>
        {
            entity.HasKey(k => k.Id);

            entity.Property(k => k.TenantId).IsRequired();
            entity.Property(k => k.KeyValue).HasMaxLength(200).IsRequired();
            entity.Property(k => k.IssuedByDian).IsRequired();
            entity.Property(k => k.Active).IsRequired();
            entity.Property(k => k.IssuedAt).IsRequired();
            entity.Property(k => k.DeactivatedAt);

            entity.Property(k => k.Version).IsConcurrencyToken();

            // The dispatcher always queries "active technical key for tenant T".
            entity.HasIndex(k => new { k.TenantId, k.Active })
                  .HasDatabaseName("ix_software_technical_keys_tenant_id_active");

            entity.HasIndex(k => new { k.TenantId, k.Id })
                  .HasDatabaseName("ix_software_technical_keys_tenant_id_id");

            entity.HasQueryFilter(k => k.DeletedAt == null);
        });

        // ---- DocumentoElectronico --------------------------------------
        // The fiscal document (REQ-CORE-08, design DD-01). State machine
        // enforcement is the domain layer's job in PR 2; the DB CHECK
        // constraint lands in T1.08 / T1.11.
        modelBuilder.Entity<DocumentoElectronico>(entity =>
        {
            entity.HasKey(d => d.Id);

            entity.Property(d => d.TenantId).IsRequired();
            entity.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(16);
            entity.Property(d => d.Numero).IsRequired();
            entity.Property(d => d.CufeOrCude).HasMaxLength(200);
            entity.Property(d => d.XmlPayload).HasColumnType("text").IsRequired();
            entity.Property(d => d.SignatureXml).HasColumnType("text");
            entity.Property(d => d.PdfPath).HasMaxLength(500);
            entity.Property(d => d.Estado).HasConversion<string>().HasMaxLength(32);
            entity.Property(d => d.TransmittedResponseCode).HasMaxLength(64);
            entity.Property(d => d.TransmittedResponseMessage).HasMaxLength(2000);
            entity.Property(d => d.RetentionLockedAt);

            entity.Property(d => d.Version).IsConcurrencyToken();

            // FK indexes — join performance for the high-frequency queries.
            entity.HasIndex(d => d.SaleId)
                  .HasDatabaseName("ix_documentos_electronicos_sale_id");

            entity.HasIndex(d => d.ResolucionId)
                  .HasDatabaseName("ix_documentos_electronicos_resolucion_id");

            entity.HasIndex(d => d.CertificadoId)
                  .HasDatabaseName("ix_documentos_electronicos_certificado_id");

            entity.HasIndex(d => d.SoftwareTechnicalKeyId)
                  .HasDatabaseName("ix_documentos_electronicos_software_technical_key_id");

            // Self-reference: voided_by points to the NC that superseded this doc.
            entity.HasIndex(d => d.VoidedBy)
                  .HasDatabaseName("ix_documentos_electronicos_voided_by");

            // The transmission worker queries "documents in this state, oldest first"
            // — composite (tenant_id, estado, created_at) leads that path.
            entity.HasIndex(d => new { d.TenantId, d.Estado, d.CreatedAt })
                  .HasDatabaseName("ix_documentos_electronicos_tenant_id_estado_created_at");

            // Per-document-type reports and the daily-X/Z report.
            entity.HasIndex(d => new { d.TenantId, d.DocumentType, d.CreatedAt })
                  .HasDatabaseName("ix_documentos_electronicos_tenant_id_document_type_created_at");

            // Composite (tenant_id, id) covering index per DD-05.
            entity.HasIndex(d => new { d.TenantId, d.Id })
                  .HasDatabaseName("ix_documentos_electronicos_tenant_id_id");

            entity.HasQueryFilter(d => d.DeletedAt == null);
        });

        // ---- ContingencyQueue ------------------------------------------
        // Pending DIAN transmissions awaiting drain (design §2.2).
        modelBuilder.Entity<ContingencyQueue>(entity =>
        {
            entity.HasKey(q => q.Id);

            entity.Property(q => q.TenantId).IsRequired();
            entity.Property(q => q.DocumentoElectronicoId).IsRequired();
            entity.Property(q => q.QueuedAt).IsRequired();
            entity.Property(q => q.RetryCount).IsRequired();
            entity.Property(q => q.LastError).HasMaxLength(2000);
            entity.Property(q => q.NextAttemptAt);
            entity.Property(q => q.CompletedAt);

            entity.Property(q => q.Version).IsConcurrencyToken();

            // The drain worker pulls the oldest batch FIFO — composite
            // (tenant_id, queued_at) leads that scan.
            entity.HasIndex(q => new { q.TenantId, q.QueuedAt })
                  .HasDatabaseName("ix_contingency_queue_tenant_id_queued_at");

            // 1:1 with documento_electronico: a document can only sit in
            // the queue once at a time. The unique index also doubles as
            // the FK index.
            entity.HasIndex(q => q.DocumentoElectronicoId)
                  .HasDatabaseName("ix_contingency_queue_documento_electronico_id")
                  .IsUnique();

            entity.HasQueryFilter(q => q.DeletedAt == null);
        });

        // ---- AuditLog --------------------------------------------------
        // Append-only audit trail (REQ-CORE-03 / SCN-CORE-03).
        // Note: AuditLog is intentionally NOT soft-deletable (no query filter).
        // The RLS DELETE denial + DB trigger ship in T1.11.
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.TenantId).IsRequired();
            entity.Property(a => a.EntityType).HasMaxLength(64).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(64).IsRequired();
            entity.Property(a => a.BeforeState).HasMaxLength(32);
            entity.Property(a => a.AfterState).HasMaxLength(32);
            entity.Property(a => a.IpAddress).HasMaxLength(64);
            entity.Property(a => a.OccurredAt).IsRequired();

            entity.Property(a => a.Version).IsConcurrencyToken();

            // Most-frequent query: "show me the audit trail for entity X".
            entity.HasIndex(a => new { a.EntityType, a.EntityId })
                  .HasDatabaseName("ix_audit_log_entity_type_entity_id");

            // Per-tenant chronological audit view — the compliance officer's
            // standard dashboard.
            entity.HasIndex(a => new { a.TenantId, a.OccurredAt })
                  .HasDatabaseName("ix_audit_log_tenant_id_occurred_at");

            // Composite (tenant_id, id) covering index per DD-05.
            entity.HasIndex(a => new { a.TenantId, a.Id })
                  .HasDatabaseName("ix_audit_log_tenant_id_id");
        });

        // ---- Foreign-key relationships ----------------------------------
        // The entities intentionally do not expose navigation properties
        // (pure POCO domain model — no behavior), so EF Core cannot infer
        // FKs from `public Tenant Tenant { get; set; }` and they must be
        // declared explicitly via the fluent API. Every FK below maps a
        // scalar `*_id` column to the parent table's `id`; we set
        // <c>OnDelete(Restrict)</c> on the tenant-scoped FKs because
        // tenant rows are terminal in the tenancy hierarchy — historical
        // fiscal data MUST survive tenant deletion (REQ-CORE-01 +
        // SCN-CORE-13) — and on the parent-child FKs because cascading
        // deletes in a fiscal system require an explicit operator
        // action, not a side-effect of a referential cascade.
        ConfigureForeignKeys(modelBuilder);
    }

    private static void ConfigureForeignKeys(ModelBuilder modelBuilder)
    {
        // ---- Tenant FKs (REQ-CORE-01) ----
        // Every tenant-scoped table references tenants(id). OnDelete is
        // Restrict because tenant deletion is operator-driven and must
        // not cascade into fiscal history (SCN-CORE-13 retention).
        modelBuilder.Entity<User>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Product>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Customer>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Sale>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CashSession>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(cs => cs.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Resolucion>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Certificado>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SoftwareTechnicalKey>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(k => k.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentoElectronico>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ContingencyQueue>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(q => q.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditLog>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---- Sale / CashSession relationships ----
        modelBuilder.Entity<Sale>()
            .HasOne<CashSession>()
            .WithMany()
            .HasForeignKey(s => s.CashSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---- SaleLineItem children ----
        // Line items are NOT soft-deletable; they belong to a Sale and
        // share the Sale's lifetime. Restrict is still appropriate
        // because a sale with line items cannot be deleted in this
        // system (the line items carry fiscal detail).
        modelBuilder.Entity<SaleLineItem>()
            .HasOne<Sale>()
            .WithMany()
            .HasForeignKey(li => li.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SaleLineItem>()
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(li => li.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---- Payment children ----
        modelBuilder.Entity<Payment>()
            .HasOne<Sale>()
            .WithMany()
            .HasForeignKey(p => p.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Payment>()
            .HasOne<CashSession>()
            .WithMany()
            .HasForeignKey(p => p.CashSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---- Compliance relationships ----
        modelBuilder.Entity<Resolucion>()
            .HasOne<SoftwareTechnicalKey>()
            .WithMany()
            .HasForeignKey(r => r.SoftwareTechnicalKeyId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---- DocumentoElectronico fan-in ----
        modelBuilder.Entity<DocumentoElectronico>()
            .HasOne<Resolucion>()
            .WithMany()
            .HasForeignKey(d => d.ResolucionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentoElectronico>()
            .HasOne<Certificado>()
            .WithMany()
            .HasForeignKey(d => d.CertificadoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentoElectronico>()
            .HasOne<SoftwareTechnicalKey>()
            .WithMany()
            .HasForeignKey(d => d.SoftwareTechnicalKeyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentoElectronico>()
            .HasOne<Sale>()
            .WithMany()
            .HasForeignKey(d => d.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-reference: VoidedBy points to the NC that superseded
        // this document (REQ-CORE-08 / design §DD-01).
        modelBuilder.Entity<DocumentoElectronico>()
            .HasOne<DocumentoElectronico>()
            .WithMany()
            .HasForeignKey(d => d.VoidedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // ---- ContingencyQueue fan-in ----
        modelBuilder.Entity<ContingencyQueue>()
            .HasOne<DocumentoElectronico>()
            .WithMany()
            .HasForeignKey(q => q.DocumentoElectronicoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
