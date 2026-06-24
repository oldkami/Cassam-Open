using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cassam.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentoElectronicoEstadoCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DocumentoElectronico state-machine enforcement at the
            // SQL layer per REQ-CORE-08 / SCN-CORE-09. The C# domain
            // guard (DocumentoElectronicoStateMachine) is the primary
            // enforcement; this migration adds the SQL-side backstop
            // so that direct UPDATE statements (from a database
            // migration script, ad-hoc DBA query, or future code
            // path that bypasses EF Core) cannot produce an invalid
            // transition either.
            //
            // Two layers:
            //   1. CHECK constraint on the column — restricts the
            //      valid estado values to the 9 enum members.
            //   2. BEFORE UPDATE trigger — validates the transition
            //      pair (OLD.estado, NEW.estado) against the DD-01
            //      transition matrix; raises an exception on invalid
            //      transitions.
            //
            // The CHECK is enforced on INSERT and UPDATE; the
            // trigger is enforced on UPDATE only (the CHECK already
            // gates INSERT).

            // 1. CHECK constraint.
            migrationBuilder.Sql(@"
                ALTER TABLE documentos_electronicos
                  ADD CONSTRAINT ck_documentos_estado_valid
                  CHECK (estado IN (
                    'DRAFT', 'SIGNED', 'QUEUED', 'TRANSMITTED',
                    'ACCEPTED', 'REJECTED', 'PENDING',
                    'VOIDED', 'REQUIRES_REHABILITACION'
                  ));
            ");

            // 2. Trigger function. Maps the DD-01 transition matrix
            // (also encoded in C# DocumentoElectronicoStateMachine)
            // in plpgsql. We keep the matrix in source comments so
            // divergence is caught at code review time.
            //
            // DD-01 transition matrix (9 states, ~14 valid pairs):
            //
            //   DRAFT                  → SIGNED
            //   SIGNED                 → QUEUED, TRANSMITTED, REQUIRES_REHABILITACION
            //   SIGNED                 ← REQUIRES_REHABILITACION (re-habilitación passes)
            //   QUEUED                 → TRANSMITTED, REQUIRES_REHABILITACION
            //   TRANSMITTED            → ACCEPTED, REJECTED, PENDING,
            //                            VOIDED, REQUIRES_REHABILITACION
            //   PENDING                → ACCEPTED, REJECTED, VOIDED,
            //                            REQUIRES_REHABILITACION
            //   REQUIRES_REHABILITACION → SIGNED
            //   (any pre-ACCEPTED)     → VOIDED (operator NC)
            //   (any non-VOIDED)       → REQUIRES_REHABILITACION
            //
            // ACCEPTED and VOIDED are terminal — no outgoing transitions.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION documentos_estado_transition_check() RETURNS trigger AS $$
                DECLARE
                    valid_transition BOOLEAN := FALSE;
                BEGIN
                    -- No-op: same estado, no transition. Always allow.
                    IF OLD.estado IS NOT DISTINCT FROM NEW.estado THEN
                        RETURN NEW;
                    END IF;

                    -- Map of valid (from, to) pairs per DD-01.
                    valid_transition := CASE
                        WHEN OLD.estado = 'DRAFT' AND NEW.estado = 'SIGNED' THEN TRUE
                        WHEN OLD.estado = 'SIGNED' AND NEW.estado IN ('QUEUED', 'TRANSMITTED', 'REQUIRES_REHABILITACION') THEN TRUE
                        WHEN OLD.estado = 'QUEUED' AND NEW.estado IN ('TRANSMITTED', 'REQUIRES_REHABILITACION') THEN TRUE
                        WHEN OLD.estado = 'TRANSMITTED' AND NEW.estado IN ('ACCEPTED', 'REJECTED', 'PENDING', 'VOIDED', 'REQUIRES_REHABILITACION') THEN TRUE
                        WHEN OLD.estado = 'PENDING' AND NEW.estado IN ('ACCEPTED', 'REJECTED', 'VOIDED', 'REQUIRES_REHABILITACION') THEN TRUE
                        WHEN OLD.estado = 'REQUIRES_REHABILITACION' AND NEW.estado = 'SIGNED' THEN TRUE
                        ELSE FALSE
                    END;

                    IF NOT valid_transition THEN
                        RAISE EXCEPTION
                            'Invalid estado transition for documentos_electronicos (DD-01): % -> %',
                            OLD.estado, NEW.estado
                            USING ERRCODE = 'P0001';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER documentos_estado_no_invalid_transition
                    BEFORE UPDATE ON documentos_electronicos
                    FOR EACH ROW
                    WHEN (OLD.estado IS DISTINCT FROM NEW.estado)
                    EXECUTE FUNCTION documentos_estado_transition_check();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order: drop trigger, then function, then CHECK.
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS documentos_estado_no_invalid_transition ON documentos_electronicos;
                DROP FUNCTION IF EXISTS documentos_estado_transition_check();
                ALTER TABLE documentos_electronicos DROP CONSTRAINT IF EXISTS ck_documentos_estado_valid;
            ");
        }
    }
}
