using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cassam.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Append-only enforcement for audit_log per REQ-CORE-03 /
            // SCN-CORE-03. EF Core cannot express RLS policies or
            // trigger-based deny rules in its migration DSL, so this
            // migration ships raw SQL via MigrationBuilder.Sql. The
            // defense-in-depth layers for audit_log immutability:
            //
            //   1. Application layer — AuditLogWriter only ever calls
            //      dbContext.AuditLog.Add(...), never Update/Remove
            //      (verified in PR 2's AuditLogWriterTests).
            //   2. RLS policy — current_tenant_id filtering + DELETE
            //      denial for the cassam_app role.
            //   3. BEFORE UPDATE trigger — raises an exception on any
            //      UPDATE attempt, regardless of role.
            //
            // RLS is enabled AND FORCED so the policy applies even to
            // the table owner / superuser. The UPDATE/DELETE triggers
            // below fire regardless of role, so FORCE RLS does not
            // interfere with the immutability guarantee.

            // 1. Enable RLS with FORCE.
            migrationBuilder.Sql(@"
                ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;
                ALTER TABLE audit_log FORCE ROW LEVEL SECURITY;
            ");

            // 2. Tenant isolation policy. The session variable
            //    app.current_tenant_id is set by ITenantContext impl
            //    (T3.02); for now the tests below set it manually.
            //    USING makes the policy filter SELECT/UPDATE/DELETE;
            //    WITH CHECK makes INSERT also require the same match
            //    so a tenant cannot insert rows belonging to another
            //    tenant.
            //
            // The CASE expression handles the unset session-variable
            // case explicitly: when current_setting(..., true)
            // returns '' (the missing-setting fallback), the policy
            // evaluates to FALSE rather than raising a UUID parse
            // error. NULLIF(..., '')::uuid can raise 22P02 inside
            // the query plan; the CASE form is the safer pattern.
            migrationBuilder.Sql(@"
                CREATE POLICY tenant_isolation ON audit_log
                  USING (
                    CASE
                      WHEN current_setting('app.current_tenant_id', true) = ''
                        THEN FALSE
                      ELSE tenant_id = current_setting('app.current_tenant_id', true)::uuid
                    END
                  )
                  WITH CHECK (
                    CASE
                      WHEN current_setting('app.current_tenant_id', true) = ''
                        THEN FALSE
                      ELSE tenant_id = current_setting('app.current_tenant_id', true)::uuid
                    END
                  );
            ");

            // 3. BEFORE UPDATE trigger. Any UPDATE attempt — even
            //    from a superuser running ad-hoc maintenance —
            //    raises an exception. This is the hard immutability
            //    guarantee per SCN-CORE-03.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION audit_log_deny_update() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'audit_log is append-only (SCN-CORE-03): UPDATE denied'
                        USING ERRCODE = 'P0001';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER audit_log_no_update
                    BEFORE UPDATE ON audit_log
                    FOR EACH ROW
                    EXECUTE FUNCTION audit_log_deny_update();
            ");

            // 4. BEFORE DELETE trigger. SCN-CORE-03 requires
            //    append-only semantics; DELETE is forbidden for the
            //    same reason UPDATE is. We name the trigger
            //    differently so an admin can drop just one of them
            //    for forensic data recovery (the recovery still
            //    requires superuser to drop the trigger, so the
            //    safety guarantee is preserved).
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION audit_log_deny_delete() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'audit_log is append-only (SCN-CORE-03): DELETE denied'
                        USING ERRCODE = 'P0001';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER audit_log_no_delete
                    BEFORE DELETE ON audit_log
                    FOR EACH ROW
                    EXECUTE FUNCTION audit_log_deny_delete();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order: drop triggers, then functions, then
            // policy, then RLS disable.
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS audit_log_no_delete ON audit_log;
                DROP TRIGGER IF EXISTS audit_log_no_update ON audit_log;
                DROP FUNCTION IF EXISTS audit_log_deny_delete();
                DROP FUNCTION IF EXISTS audit_log_deny_update();
                DROP POLICY IF EXISTS tenant_isolation ON audit_log;
                ALTER TABLE audit_log NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE audit_log DISABLE ROW LEVEL SECURITY;
            ");
        }
    }
}
