namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Lifecycle of a <c>documento_electronico</c> row per
/// <c>pos-core-modern-stack</c> REQ-CORE-08 and design DD-01.
///
/// Canonical transitions (also enforced by <c>DocumentoElectronicoStateMachine</c>):
/// <list type="bullet">
///   <item><c>(none) → Draft</c> — generator inserts the row.</item>
///   <item><c>Draft → Signed</c> — XAdES-BES signer completes (REQ-SIG-01).</item>
///   <item><c>Signed → Queued</c> — DIAN is unreachable; the contingency worker takes over.</item>
///   <item><c>Signed → Transmitted</c> — Transmission worker POST succeeds.</item>
///   <item><c>Transmitted → Accepted | Rejected | Pending</c> — ApplicationResponse codes.</item>
///   <item><c>Queued → Transmitted</c> — Drain worker succeeds.</item>
///   <item><c>any pre-Accepted → Voided</c> — operator "Anular" with Nota Crédito.</item>
///   <item><c>any → RequiresRehabilitacion</c> — module version breaks XML / CUFE / QR.</item>
///   <item><c>RequiresRehabilitacion → Signed</c> — re-habilitación passes.</item>
/// </list>
///
/// <see cref="Accepted"/> and <see cref="Voided"/> are terminal (no outgoing transitions).
/// <see cref="RequiresRehabilitacion"/> is a blocking sub-state: production transmission is
/// refused until the operator clears it via re-habilitación.
/// </summary>
public enum EstadoDocumento
{
    /// <summary>Generator inserted the row; XML is not yet signed.</summary>
    Draft = 0,

    /// <summary>XAdES-BES signature applied; document is ready to transmit.</summary>
    Signed = 1,

    /// <summary>
    /// Transmission failed (DIAN unreachable); document sits in
    /// <c>contingency_queue</c> until the drain worker succeeds.
    /// </summary>
    Queued = 2,

    /// <summary>
    /// POSTed to DIAN; awaiting the synchronous Application Response.
    /// <c>transmitted_at</c> is set; <c>transmitted_response_code/message</c>
    /// will be filled on response parse.
    /// </summary>
    Transmitted = 3,

    /// <summary>DIAN returned ApplicationResponse = VALIDATED. Terminal.</summary>
    Accepted = 4,

    /// <summary>DIAN returned ApplicationResponse = REJECTED. Terminal.</summary>
    Rejected = 5,

    /// <summary>
    /// DIAN returned ApplicationResponse = PENDING (operator must follow up
    /// outside the system, then re-transmit or void).
    /// </summary>
    Pending = 6,

    /// <summary>
    /// Operator "Anular" issued a Nota Crédito that supersedes this document.
    /// Terminal — the document is preserved for fiscal history but no longer
    /// represents a live fiscal receipt.
    /// </summary>
    Voided = 7,

    /// <summary>
    /// Blocking sub-state: the active Anexo Técnico / DIAN module version
    /// is broken (e.g. an upstream Anexo change invalidated the cached
    /// canonicalization, CUFE/QR generation, or XSL transform). Production
    /// transmission is refused until re-habilitación passes.
    /// </summary>
    RequiresRehabilitacion = 8,
}
