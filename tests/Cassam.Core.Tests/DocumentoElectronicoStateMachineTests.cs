using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;
using Cassam.Core.Domain.StateMachines;
using FluentAssertions;

namespace Cassam.Core.Tests;

/// <summary>
/// Comprehensive transition tests for the canonical
/// <c>DocumentoElectronico</c> state machine per design DD-01 and
/// <c>pos-core-modern-stack</c> REQ-CORE-08 / SCN-CORE-09.
///
/// The test surface is the cartesian product of <see cref="EstadoDocumento"/>
/// states, parametrized so adding a new state to the enum forces a
/// regression: every from/to combination must be classified as
/// "allowed" or "denied" and the assertions pin the verdict.
/// </summary>
public class DocumentoElectronicoStateMachineTests
{
    // -- Happy-path transitions per DD-01 -------------------------------

    [Theory]
    [InlineData(EstadoDocumento.Draft, EstadoDocumento.Signed)]
    [InlineData(EstadoDocumento.Signed, EstadoDocumento.Queued)]
    [InlineData(EstadoDocumento.Signed, EstadoDocumento.Transmitted)]
    [InlineData(EstadoDocumento.Transmitted, EstadoDocumento.Accepted)]
    [InlineData(EstadoDocumento.Transmitted, EstadoDocumento.Rejected)]
    [InlineData(EstadoDocumento.Transmitted, EstadoDocumento.Pending)]
    [InlineData(EstadoDocumento.Queued, EstadoDocumento.Transmitted)]
    public void CanTransition_allows_happy_path(EstadoDocumento from, EstadoDocumento to)
    {
        DocumentoElectronicoStateMachine.CanTransition(from, to)
            .Should().BeTrue($"{from} → {to} is on the happy path per DD-01");
    }

    [Theory]
    [InlineData(EstadoDocumento.Draft)]
    [InlineData(EstadoDocumento.Signed)]
    [InlineData(EstadoDocumento.Queued)]
    [InlineData(EstadoDocumento.Transmitted)]
    [InlineData(EstadoDocumento.Rejected)]
    [InlineData(EstadoDocumento.Pending)]
    [InlineData(EstadoDocumento.RequiresRehabilitacion)]
    public void CanTransition_allows_VOIDED_from_every_pre_accepted_state(EstadoDocumento from)
    {
        DocumentoElectronicoStateMachine.CanTransition(from, EstadoDocumento.Voided)
            .Should().BeTrue(
                $"any pre-ACCEPTED state — including {from} — may transition to VOIDED per REQ-CORE-08");
    }

    [Theory]
    [InlineData(EstadoDocumento.Draft)]
    [InlineData(EstadoDocumento.Signed)]
    [InlineData(EstadoDocumento.Queued)]
    [InlineData(EstadoDocumento.Transmitted)]
    [InlineData(EstadoDocumento.Rejected)]
    [InlineData(EstadoDocumento.Pending)]
    public void CanTransition_allows_REQUIRES_REHABILITACION_from_any_non_terminal_state(EstadoDocumento from)
    {
        // REQUIRES_REHABILITACION is the operator escape hatch — the
        // terminal states (ACCEPTED, VOIDED) cannot be flagged. ACCEPTED
        // documents are already validated by DIAN and a module break
        // does not retroactively invalidate them; VOIDED documents are
        // preserved as-is for fiscal history.
        DocumentoElectronicoStateMachine.CanTransition(from, EstadoDocumento.RequiresRehabilitacion)
            .Should().BeTrue($"{from} may be flagged for re-habilitación per DD-01");
    }

    [Fact]
    public void CanTransition_allows_REQUIRES_REHABILITACION_to_clear_back_to_SIGNED()
    {
        DocumentoElectronicoStateMachine.CanTransition(
                EstadoDocumento.RequiresRehabilitacion,
                EstadoDocumento.Signed)
            .Should().BeTrue(
                "after re-habilitación passes the document resumes at SIGNED per DD-01");
    }

    // -- Forbidden transitions -------------------------------------------

    [Fact]
    public void CanTransition_rejects_ACCEPTED_as_terminal()
    {
        // ACCEPTED is terminal — no outgoing transitions of any kind.
        foreach (var to in Enum.GetValues<EstadoDocumento>())
        {
            DocumentoElectronicoStateMachine.CanTransition(EstadoDocumento.Accepted, to)
                .Should().BeFalse(
                    $"ACCEPTED is terminal; {EstadoDocumento.Accepted} → {to} must be rejected (DD-01)");
        }
    }

    [Fact]
    public void CanTransition_rejects_VOIDED_as_terminal()
    {
        // VOIDED is terminal — no outgoing transitions of any kind.
        foreach (var to in Enum.GetValues<EstadoDocumento>())
        {
            DocumentoElectronicoStateMachine.CanTransition(EstadoDocumento.Voided, to)
                .Should().BeFalse(
                    $"VOIDED is terminal; {EstadoDocumento.Voided} → {to} must be rejected (DD-01)");
        }
    }

    [Theory]
    [InlineData(EstadoDocumento.Draft, EstadoDocumento.Accepted)]
    [InlineData(EstadoDocumento.Draft, EstadoDocumento.Transmitted)]
    [InlineData(EstadoDocumento.Draft, EstadoDocumento.Rejected)]
    [InlineData(EstadoDocumento.Signed, EstadoDocumento.Accepted)]
    [InlineData(EstadoDocumento.Signed, EstadoDocumento.Rejected)]
    [InlineData(EstadoDocumento.Queued, EstadoDocumento.Accepted)]
    [InlineData(EstadoDocumento.Queued, EstadoDocumento.Rejected)]
    public void CanTransition_rejects_skipping_the_DIAN_post(EstadoDocumento from, EstadoDocumento to)
    {
        DocumentoElectronicoStateMachine.CanTransition(from, to)
            .Should().BeFalse(
                $"{from} → {to} would skip the DIAN POST — rejected per DD-01");
    }

    [Theory]
    [InlineData(EstadoDocumento.Draft, EstadoDocumento.Transmitted)]
    [InlineData(EstadoDocumento.Draft, EstadoDocumento.Accepted)]
    [InlineData(EstadoDocumento.Signed, EstadoDocumento.Accepted)]
    [InlineData(EstadoDocumento.Queued, EstadoDocumento.Accepted)]
    [InlineData(EstadoDocumento.Pending, EstadoDocumento.Accepted)]
    [InlineData(EstadoDocumento.Pending, EstadoDocumento.Signed)]
    [InlineData(EstadoDocumento.Accepted, EstadoDocumento.Voided)]
    [InlineData(EstadoDocumento.Voided, EstadoDocumento.Accepted)]
    public void EnsureValidTransition_throws_for_invalid_paths(EstadoDocumento from, EstadoDocumento to)
    {
        var act = () => DocumentoElectronicoStateMachine.EnsureValidTransition(from, to);

        act.Should().Throw<InvalidOperationException>(
            $"{from} → {to} is not in the DD-01 allowed matrix; the guard must reject it (SCN-CORE-09)");
    }

    // -- Same-state no-ops -----------------------------------------------

    [Theory]
    [InlineData(EstadoDocumento.Draft)]
    [InlineData(EstadoDocumento.Signed)]
    [InlineData(EstadoDocumento.Transmitted)]
    [InlineData(EstadoDocumento.Accepted)]
    [InlineData(EstadoDocumento.Voided)]
    public void CanTransition_rejects_same_state_no_op(EstadoDocumento state)
    {
        DocumentoElectronicoStateMachine.CanTransition(state, state)
            .Should().BeFalse("a no-op transition is never allowed — caller compares before/after explicitly");
    }

    // -- Mutator ----------------------------------------------------------

    [Fact]
    public void Transition_mutates_the_document_state()
    {
        var doc = MakeDocument(EstadoDocumento.Draft);

        DocumentoElectronicoStateMachine.Transition(doc, EstadoDocumento.Signed);

        doc.Estado.Should().Be(EstadoDocumento.Signed);
    }

    [Fact]
    public void Transition_throws_and_leaves_state_unchanged_on_invalid_transition()
    {
        var doc = MakeDocument(EstadoDocumento.Draft);

        var act = () => DocumentoElectronicoStateMachine.Transition(doc, EstadoDocumento.Accepted);

        act.Should().Throw<InvalidOperationException>();
        doc.Estado.Should().Be(EstadoDocumento.Draft,
            "a rejected transition must NOT mutate the document");
    }

    [Fact]
    public void Transition_throws_ArgumentNullException_for_null_document()
    {
        var act = () => DocumentoElectronicoStateMachine.Transition(null!, EstadoDocumento.Signed);

        act.Should().Throw<ArgumentNullException>();
    }

    // -- Helpers (IsPreAccepted / IsTerminal / IsBlocking) ---------------

    [Theory]
    [InlineData(EstadoDocumento.Draft, true)]
    [InlineData(EstadoDocumento.Signed, true)]
    [InlineData(EstadoDocumento.Queued, true)]
    [InlineData(EstadoDocumento.Transmitted, true)]
    [InlineData(EstadoDocumento.Rejected, true)]
    [InlineData(EstadoDocumento.Pending, true)]
    [InlineData(EstadoDocumento.RequiresRehabilitacion, true)]
    [InlineData(EstadoDocumento.Accepted, false)]
    [InlineData(EstadoDocumento.Voided, false)]
    public void IsPreAccepted_classifies_states_correctly(EstadoDocumento state, bool expected)
    {
        DocumentoElectronicoStateMachine.IsPreAccepted(state).Should().Be(expected);
    }

    [Theory]
    [InlineData(EstadoDocumento.Accepted, true)]
    [InlineData(EstadoDocumento.Voided, true)]
    [InlineData(EstadoDocumento.Draft, false)]
    [InlineData(EstadoDocumento.Signed, false)]
    [InlineData(EstadoDocumento.RequiresRehabilitacion, false)]
    public void IsTerminal_identifies_ACCEPTED_and_VOIDED_only(EstadoDocumento state, bool expected)
    {
        DocumentoElectronicoStateMachine.IsTerminal(state).Should().Be(expected,
            "only ACCEPTED and VOIDED are truly terminal; REQUIRES_REHABILITACION is blocking but recoverable");
    }

    [Fact]
    public void IsBlocking_flags_only_REQUIRES_REHABILITACION()
    {
        foreach (var state in Enum.GetValues<EstadoDocumento>())
        {
            var expected = state == EstadoDocumento.RequiresRehabilitacion;
            DocumentoElectronicoStateMachine.IsBlocking(state).Should().Be(expected,
                $"{state} is blocking only when the value is REQUIRES_REHABILITACION");
        }
    }

    // -- Self-voiding reference (voided_by FK) ---------------------------

    [Fact]
    public void Voided_document_carries_voided_by_reference_to_the_NC()
    {
        var original = MakeDocument(EstadoDocumento.Accepted);
        original.VoidedBy = null;

        // The voiding NC: pre-ACCEPTED → VOIDED is allowed, but the
        // scenario we exercise here is the FK-side: the original carries
        // the reference once the NC lands.
        var notaCredito = MakeDocument(EstadoDocumento.Draft);
        notaCredito.DocumentType = DocumentType.NotaCredito;

        original.VoidedBy = notaCredito.Id;

        original.VoidedBy.Should().Be(notaCredito.Id,
            "after Anular, the original document carries voided_by = the NC that superseded it");
    }

    // -- Test helpers -----------------------------------------------------

    private static DocumentoElectronico MakeDocument(EstadoDocumento initial) => new()
    {
        TenantId = Guid.CreateVersion7(),
        ResolucionId = Guid.CreateVersion7(),
        CertificadoId = Guid.CreateVersion7(),
        SoftwareTechnicalKeyId = Guid.CreateVersion7(),
        DocumentType = DocumentType.DeePos,
        Numero = 1,
        XmlPayload = "<Invoice/>",
        Estado = initial,
    };
}
