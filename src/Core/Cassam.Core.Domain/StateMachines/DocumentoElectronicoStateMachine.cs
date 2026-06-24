using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.StateMachines;

/// <summary>
/// Canonical <see cref="DocumentoElectronico"/> state machine per
/// <c>pos-core-modern-stack</c> REQ-CORE-08 and design DD-01.
///
/// The state machine is intentionally a static class with pure
/// functions — no dependency injection — so that:
/// <list type="bullet">
///   <item>The same logic runs in the domain guard, in the DIAN
///         transmission worker, and in any future UI guard.</item>
///   <item>Unit tests do not need a service container.</item>
///   <item>The DB <c>CHECK</c> constraint emitted in a later migration
///         (T1.08) is a re-statement of <see cref="CanTransition"/> in
///         SQL — one source of truth, two enforcement layers.</item>
/// </list>
///
/// Adding a new state means editing BOTH this file and the SQL CHECK
/// constraint in the migration that adds the constraint. Forgetting
/// either leaves the system unsafe; the <c>DocumentoElectronicoStateMachineTests</c>
/// suite catches divergence by exercising every from/to combination.
/// </summary>
public static class DocumentoElectronicoStateMachine
{
    /// <summary>
    /// Returns <c>true</c> when a row in <paramref name="from"/> may
    /// transition to <paramref name="to"/> per DD-01.
    /// </summary>
    /// <param name="from">Current state. Pass the same value as <paramref name="to"/> for no-op checks (always false).</param>
    /// <param name="to">Desired next state.</param>
    public static bool CanTransition(EstadoDocumento from, EstadoDocumento to)
    {
        // Same-state is always a no-op rejection — callers compare
        // before/after explicitly and never call Transition with
        // matching values.
        if (from == to)
        {
            return false;
        }

        // ACCEPTED and VOIDED are terminal — no outgoing transitions.
        if (from == EstadoDocumento.Accepted || from == EstadoDocumento.Voided)
        {
            return false;
        }

        // SIGNED has two valid predecessors: DRAFT (happy path) and
        // REQUIRES_REHABILITACION (re-habilitación passes). We check
        // SIGNED explicitly because the linear-happy-path switch below
        // would otherwise list it twice; this keeps the matrix readable.
        if (to == EstadoDocumento.Signed)
        {
            return from == EstadoDocumento.Draft
                || from == EstadoDocumento.RequiresRehabilitacion;
        }

        // Any pre-ACCEPTED state can transition to VOIDED (operator NC).
        if (to == EstadoDocumento.Voided)
        {
            return IsPreAccepted(from);
        }

        // Any non-VOIDED state can be flagged for re-habilitación.
        // This is the operator escape hatch when an upstream Anexo
        // change invalidates already-signed documents.
        if (to == EstadoDocumento.RequiresRehabilitacion)
        {
            return from != EstadoDocumento.Voided;
        }

        // Linear transitions along the happy path. SIGNED is intentionally
        // absent from this list — handled above so both predecessors are
        // visible in one place.
        return (from, to) switch
        {
            (EstadoDocumento.Signed, EstadoDocumento.Queued) => true,
            (EstadoDocumento.Signed, EstadoDocumento.Transmitted) => true,
            (EstadoDocumento.Transmitted, EstadoDocumento.Accepted) => true,
            (EstadoDocumento.Transmitted, EstadoDocumento.Rejected) => true,
            (EstadoDocumento.Transmitted, EstadoDocumento.Pending) => true,
            (EstadoDocumento.Queued, EstadoDocumento.Transmitted) => true,
            _ => false,
        };
    }

    /// <summary>
    /// Applies <paramref name="to"/> to <paramref name="document"/> after
    /// verifying the transition. Throws <see cref="InvalidOperationException"/>
    /// with a machine-readable message when the transition is invalid.
    /// </summary>
    /// <remarks>
    /// The transition is applied in-memory only — the caller is responsible
    /// for calling <c>SaveChanges</c> on the same <c>DbContext</c> as the
    /// underlying mutation, so the audit log row and the state change land
    /// in the same transaction.
    /// </remarks>
    public static void Transition(DocumentoElectronico document, EstadoDocumento to)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureValidTransition(document.Estado, to);
        document.Estado = to;
    }

    /// <summary>
    /// Throws when the transition is not allowed. Exposed so entity
    /// methods (e.g. <c>DocumentoElectronico.MarkSigned()</c>) can use
    /// the same guard without going through the <see cref="Transition"/>
    /// mutator when they want to set side fields first.
    /// </summary>
    public static void EnsureValidTransition(EstadoDocumento from, EstadoDocumento to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Invalid DocumentoElectronico state transition: {from} → {to} " +
                $"(per DD-01; check DocumentoElectronicoStateMachine.CanTransition for the allowed matrix).");
        }
    }

    /// <summary>
    /// True when <paramref name="state"/> has not yet reached a terminal
    /// outcome. Used by the VOIDED-allowed check and by callers that
    /// want to gate operator actions ("Anular" button enabled only while
    /// pre-accepted).
    /// </summary>
    public static bool IsPreAccepted(EstadoDocumento state) => state switch
    {
        EstadoDocumento.Draft => true,
        EstadoDocumento.Signed => true,
        EstadoDocumento.Queued => true,
        EstadoDocumento.Transmitted => true,
        EstadoDocumento.Rejected => true,
        EstadoDocumento.Pending => true,
        EstadoDocumento.RequiresRehabilitacion => true,
        EstadoDocumento.Accepted => false,
        EstadoDocumento.Voided => false,
        _ => false,
    };

    /// <summary>
    /// True when the state is fully terminal — no further transitions
    /// are possible. <see cref="EstadoDocumento.Accepted"/> and
    /// <see cref="EstadoDocumento.Voided"/> are the only true terminals;
    /// <see cref="EstadoDocumento.RequiresRehabilitacion"/> is blocking
    /// but not terminal because re-habilitación may yet clear it.
    /// </summary>
    public static bool IsTerminal(EstadoDocumento state) =>
        state == EstadoDocumento.Accepted || state == EstadoDocumento.Voided;

    /// <summary>
    /// True when the state is a blocking sub-state. Production
    /// transmission is refused while a document is in this state.
    /// </summary>
    public static bool IsBlocking(EstadoDocumento state) =>
        state == EstadoDocumento.RequiresRehabilitacion;
}
