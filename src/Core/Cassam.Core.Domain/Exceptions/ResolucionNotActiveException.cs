namespace Cassam.Core.Domain.Exceptions;

/// <summary>
/// Thrown when <c>INumberingService.ReserveNextNumberAsync</c> cannot
/// locate an <see cref="Enums.ResolucionStatus.Active"/> resolución for
/// the requested (tenant, document type) pair per
/// <c>pos-core-modern-stack</c> REQ-CORE-06.
///
/// <para>
/// Typical causes:
/// </para>
/// <list type="bullet">
///   <item>The operator never uploaded the DIAN autorización.</item>
///   <item>All uploaded resoluciones are in <see cref="Enums.ResolucionStatus.Draft"/>
///         (still awaiting operator activation).</item>
///   <item>Every resolución has reached <see cref="Enums.ResolucionStatus.Expired"/>
///         and a new one has not been registered.</item>
/// </list>
///
/// The dispatcher's contract is to surface this exception to the caller
/// so the operator can be prompted to upload the next DIAN-issued range.
/// The exception is recoverable: it is NOT a bug, it is a state of the
/// business that the UI must communicate to a human.
/// </summary>
public sealed class ResolucionNotActiveException : InvalidOperationException
{
    /// <summary>
    /// The tenant that attempted the reservation. Useful for the audit
    /// log and for the UI error message ("no active resolución for
    /// tenant X").
    /// </summary>
    public Guid TenantId { get; }

    /// <summary>
    /// The document type that triggered the lookup. The dispatcher
    /// queries (tenant, document_type, status=Active) so this is the
    /// missing column.
    /// </summary>
    public Enums.DocumentType DocumentType { get; }

    /// <summary>
    /// Builds the exception with a stable message suitable for both UI
    /// surfacing and audit log persistence.
    /// </summary>
    /// <param name="tenantId">Tenant that requested the next number.</param>
    /// <param name="documentType">Document type the dispatcher was issuing.</param>
    public ResolucionNotActiveException(Guid tenantId, Enums.DocumentType documentType)
        : base($"No active resolución found for tenant '{tenantId}' and document type '{documentType}'. " +
               "Upload a new DIAN-issued range and activate it before continuing.")
    {
        TenantId = tenantId;
        DocumentType = documentType;
    }
}
