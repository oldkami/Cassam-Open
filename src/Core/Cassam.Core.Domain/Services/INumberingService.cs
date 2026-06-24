using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Services;

/// <summary>
/// DIAN numbering dispatcher. Reserves the next sequential number for a
/// (tenant, document type) pair by atomically incrementing the active
/// <see cref="Entities.Resolucion"/>'s <c>current_number</c> counter per
/// <c>pos-core-modern-stack</c> REQ-CORE-06 / SCN-CORE-07.
///
/// <para>
/// Threading + concurrency model: the implementation MUST acquire a row
/// lock on the active resolución before reading <c>current_number</c> so
/// two concurrent dispatches never observe the same number. PostgreSQL
/// row locks (<c>SELECT … FOR UPDATE</c>) are the canonical primitive;
/// optimistic concurrency via the <c>version</c> column is a fallback
/// when row locking is impractical (e.g. against an in-memory provider).
/// </para>
///
/// <para>
/// Exhaustion handling: <see cref="ReserveNextNumberAsync"/> throws
/// <see cref="Exceptions.ResolucionExhaustedException"/> when the
/// counter has reached <c>range_end</c>, and
/// <see cref="Exceptions.ResolucionNotActiveException"/> when no
/// <see cref="ResolucionStatus.Active"/> resolución exists. Callers
/// (the document-dispatch use case) treat both as recoverable
/// conditions and surface them to the operator via the UI.
/// </para>
/// </summary>
public interface INumberingService
{
    /// <summary>
    /// Reserves and returns the next sequential number from the active
    /// resolución for the given (tenant, document type).
    /// </summary>
    /// <param name="tenantId">Tenant that owns the numbering range.</param>
    /// <param name="documentType">
    /// Document type (DEE POS / FE Venta / NC / ND). Each type has its
    /// own independent numbering range per REQ-CORE-06.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The next number, which is guaranteed to be unique across all
    /// concurrent callers within this (tenant, document type) tuple.
    /// </returns>
    /// <exception cref="Exceptions.ResolucionNotActiveException">
    /// No resolución is in <see cref="ResolucionStatus.Active"/> for the
    /// requested (tenant, document type) pair.
    /// </exception>
    /// <exception cref="Exceptions.ResolucionExhaustedException">
    /// The active resolución has already issued its last number
    /// (<c>current_number &gt;= range_end</c>).
    /// </exception>
    Task<long> ReserveNextNumberAsync(
        Guid tenantId,
        DocumentType documentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the active resolución for the (tenant, document
    /// type) pair still has at least <paramref name="howMany"/> numbers
    /// remaining. Cheap read — does NOT acquire a row lock or mutate
    /// state. Used by pre-flight checks (e.g. "warn the operator 100
    /// numbers before exhaustion").
    /// </summary>
    /// <param name="tenantId">Tenant to check.</param>
    /// <param name="documentType">Document type to check.</param>
    /// <param name="howMany">Minimum number of free slots required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> when an active resolución exists AND
    /// <c>(range_end - current_number) &gt;= howMany</c>;
    /// <c>false</c> otherwise (including when no active resolución exists).
    /// </returns>
    Task<bool> HasAvailableNumbersAsync(
        Guid tenantId,
        DocumentType documentType,
        int howMany,
        CancellationToken cancellationToken = default);
}
