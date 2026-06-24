using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// One fiscal document — DEE POS, FE Venta, Nota Crédito, or Nota Débito.
/// The row is created in <see cref="EstadoDocumento.Draft"/> by the
/// generator module (<c>pos-fiscal-dee-pos</c> / <c>pos-fiscal-fe-venta</c>)
/// and progresses through the canonical state machine per
/// <c>pos-core-modern-stack</c> REQ-CORE-08 / design DD-01.
///
/// Transitions are guarded by <c>DocumentoElectronicoStateMachine</c> at
/// the domain layer and re-enforced by a PostgreSQL <c>CHECK</c> constraint
/// emitted in a later migration (T1.08). Invalid transitions throw
/// <see cref="InvalidOperationException"/>; the DB raises the equivalent
/// SQLSTATE on the next write.
/// </summary>
public class DocumentoElectronico : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Foreign key to the originating <see cref="Sale"/>. Null for Notas
    /// that originate outside a sale (e.g. an external Nota Crédito that
    /// voids a previously transmitted document).
    /// </summary>
    public Guid? SaleId { get; set; }

    /// <summary>Foreign key to the <see cref="Resolucion"/> that authorized the numbering range.</summary>
    public Guid ResolucionId { get; set; }

    /// <summary>Foreign key to the <see cref="Certificado"/> used to sign the document.</summary>
    public Guid CertificadoId { get; set; }

    /// <summary>Foreign key to the <see cref="SoftwareTechnicalKey"/> embedded in the document header.</summary>
    public Guid SoftwareTechnicalKeyId { get; set; }

    /// <summary>Document type. Matches <see cref="Resolucion.DocumentType"/>.</summary>
    public DocumentType DocumentType { get; set; }

    /// <summary>
    /// Number assigned by <see cref="Resolucion.CurrentNumber"/> at
    /// generation time. Unique within a resolución range.
    /// </summary>
    public long Numero { get; set; }

    /// <summary>
    /// CUFE (FE Venta) or CUDE (DEE POS) — the SHA-384 hash hex of the
    /// signed XML. Null until <see cref="EstadoDocumento.Signed"/>.
    /// </summary>
    public string? CufeOrCude { get; set; }

    /// <summary>Signed UBL 2.1 XML payload (DIAN Anexo Técnico schema).</summary>
    public string XmlPayload { get; set; } = string.Empty;

    /// <summary>
    /// Detached XAdES-BES signature XML (the <c>&lt;ds:Signature&gt;</c>
    /// block). Stored alongside the signed payload so verification can
    /// re-run without re-signing.
    /// </summary>
    public string? SignatureXml { get; set; }

    /// <summary>
    /// Filesystem path to the PDF representation. Null when the operator
    /// opted out of PDF generation (DEE POS allows plain-text receipts).
    /// </summary>
    public string? PdfPath { get; set; }

    /// <summary>Current state per DD-01.</summary>
    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Draft;

    /// <summary>
    /// Self-reference: when this document is voided by a Nota Crédito,
    /// <see cref="VoidedBy"/> points to the NC. Null while still alive.
    /// </summary>
    public Guid? VoidedBy { get; set; }

    /// <summary>UTC timestamp the document was POSTed to DIAN. Null while in <see cref="EstadoDocumento.Draft"/> / <see cref="EstadoDocumento.Signed"/> / <see cref="EstadoDocumento.Queued"/>.</summary>
    public DateTime? TransmittedAt { get; set; }

    /// <summary>DIAN synchronous response code (e.g. <c>"200"</c> for HTTP OK, plus the ApplicationResponse status).</summary>
    public string? TransmittedResponseCode { get; set; }

    /// <summary>DIAN Application Response message — populated on rejection / pending for operator display.</summary>
    public string? TransmittedResponseMessage { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAt { get; set; }
}
