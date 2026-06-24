using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cassam.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tenant soft-delete + 5-year fiscal retention lock per
            // pos-core-modern-stack REQ-CORE-12 / SCN-CORE-13.
            //
            // The PR 1 schema declared Tenant without a DeletedAt
            // timestamp because Tenant was the root of tenancy and
            // deletion was modeled via TenantStatus.Deleted. The 5-year
            // retention contract (cloud-saas-multi-tenant REQ-MT-05)
            // requires a concrete "when was it deleted" anchor so the
            // retention worker can decide which tenants are past their
            // retention window.
            //
            // This migration adds:
            //   1. tenants.deleted_at TIMESTAMP NULL — the anchor
            //   2. documentos_electronicos.retention_locked_at TIMESTAMP
            //      NULL — the per-document lock timestamp
            //   3. BEFORE UPDATE trigger on documentos_electronicos
            //      that raises an exception when retention_locked_at
            //      is non-null (SCN-CORE-13 retention invariant)
            //   4. Replace the (nit) unique index with a composite
            //      (nit, deleted_at) index so a re-registration of
            //      the same NIT after deletion is allowed.

            // ---- 1. tenants.deleted_at ----
            migrationBuilder.Sql(@"
                ALTER TABLE tenants ADD COLUMN deleted_at TIMESTAMP NULL;
            ");

            // ---- 2. documentos_electronicos.retention_locked_at ----
            migrationBuilder.Sql(@"
                ALTER TABLE documentos_electronicos ADD COLUMN retention_locked_at TIMESTAMP NULL;
            ");

            // ---- 3. retention lock trigger ----
            // After the lock timestamp is set, the document is frozen.
            // The trigger fires BEFORE UPDATE so the exception aborts
            // the transaction before any column write. We also gate
            // the trigger on (OLD.retention_locked_at IS NOT NULL AND
            // OLD.retention_locked_at IS DISTINCT FROM NEW.retention_locked_at)
            // so the lock write itself is allowed — only further
            // mutations are blocked.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION documentos_retention_lock() RETURNS trigger AS $$
                BEGIN
                    -- Setting the lock for the first time is the
                    -- intended flow; allow that one update.
                    IF OLD.retention_locked_at IS NULL AND NEW.retention_locked_at IS NOT NULL THEN
                        RETURN NEW;
                    END IF;

                    -- Any subsequent mutation on a locked row is denied.
                    IF OLD.retention_locked_at IS NOT NULL THEN
                        RAISE EXCEPTION
                            'documentos_electronicos retention-locked (SCN-CORE-13): UPDATE denied at %',
                            OLD.retention_locked_at
                            USING ERRCODE = 'P0001';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER documentos_retention_no_update
                    BEFORE UPDATE ON documentos_electronicos
                    FOR EACH ROW
                    EXECUTE FUNCTION documentos_retention_lock();
            ");

            // ---- 4. Replace (nit) unique index with partial unique ----
            // The old single-column (nit) unique index prevented
            // re-registration of the same NIT after a soft-delete.
            // The new constraint must:
            //   (a) keep ACTIVE tenants unique per NIT (one active
            //       tenant per NIT, same as the old behavior)
            //   (b) allow re-registration after soft-delete (a new
            //       active tenant can claim the same NIT once the
            //       previous one is soft-deleted)
            //
            // A composite (nit, deleted_at) index does NOT do (a)
            // because PostgreSQL treats NULL != NULL in unique indexes
            // — two active tenants with the same NIT would be allowed.
            //
            // The correct shape is a PARTIAL UNIQUE index that applies
            // ONLY to non-deleted rows (deleted_at IS NULL). The
            // constraint is then naturally lifted once the row is
            // soft-deleted.
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ix_tenants_nit;
                CREATE UNIQUE INDEX ix_tenants_nit_active
                    ON tenants (nit)
                    WHERE deleted_at IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order: drop trigger, function, retention column,
            // then deleted_at column, then restore the (nit) index.
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS documentos_retention_no_update ON documentos_electronicos;
                DROP FUNCTION IF EXISTS documentos_retention_lock();
                ALTER TABLE documentos_electronicos DROP COLUMN IF EXISTS retention_locked_at;
                ALTER TABLE tenants DROP COLUMN IF EXISTS deleted_at;

                DROP INDEX IF EXISTS ix_tenants_nit_active;
                CREATE UNIQUE INDEX ix_tenants_nit ON tenants (nit);
            ");
        }
    }
}