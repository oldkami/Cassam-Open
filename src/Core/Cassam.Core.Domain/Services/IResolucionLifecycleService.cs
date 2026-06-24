using Cassam.Core.Domain.Entities;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Services;

/// <summary>
/// Lifecycle operations on <see cref="Resolucion"/> per
/// <c>pos-core-modern-stack</c> REQ-CORE-06 / SCN-CORE-06 / SCN-CORE-07.
///
/// <para>
/// Responsibilities split:
/// </para>
/// <list type="bullet">
///   <item>Expiry detection: locate resoluciones that are about to expire
///         (so the UI can warn the operator with a 30-day heads-up per
///         SCN-CORE-06).</item>
///   <item>Exhaustion detection: locate resoluciones whose
///         <c>current_number</c> has reached <c>range_end</c> so the
///         dispatcher can stop issuing from them.</item>
///   <item>State transition: <see cref="MarkExpiredAsync"/> flips a
///         resolución from <see cref="ResolucionStatus.Active"/> to
///         <see cref="ResolucionStatus.Expired"/> — a one-way transition.</item>
///   <item>Active-resolution lookup: <see cref="HasActiveResolucionAsync"/>
///         is the dispatcher's cheap pre-flight check before the actual
///         <c>INumberingService.ReserveNextNumberAsync</c> call.</item>
/// </list>
///
/// <para>
/// None of these operations mutate <c>current_number</c> — that
/// belongs to <see cref="INumberingService"/>. The split lets us test
/// expiry queries without standing up the locking machinery.
/// </para>
/// </summary>
public interface IResolucionLifecycleService
{
    /// <summary>
    /// Returns every <see cref="ResolucionStatus.Active"/> resolución
    /// for the tenant whose <c>expiration_date</c> falls on or before
    /// <c>now + daysAhead</c>. Used by the daily-check job to raise the
    /// SCN-CORE-06 30-day warning to the operator.
    /// </summary>
    /// <param name="tenantId">Tenant to scan.</param>
    /// <param name="daysAhead">
    /// Look-ahead window in days. A value of 30 returns resoluciones
    /// expiring in the next 30 calendar days.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Resoluciones expiring soon, in ascending
    /// <c>expiration_date</c> order (most urgent first).
    /// </returns>
    Task<IReadOnlyList<Resolucion>> FindResolucionesExpiringWithinAsync(
        Guid tenantId,
        int daysAhead,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every <see cref="ResolucionStatus.Active"/> resolución
    /// whose numbering range is exhausted
    /// (<c>current_number &gt;= range_end</c>) per SCN-CORE-07. The
    /// dispatcher consults this list to skip already-exhausted
    /// resoluciones when searching for a fallback range.
    /// </summary>
    /// <param name="tenantId">Tenant to scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Exhausted resoluciones, in <c>range_end</c> ascending order
    /// (smaller ranges first — usually the ones that ran out first).
    /// </returns>
    Task<IReadOnlyList<Resolucion>> FindExhaustedResolucionesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions the given resolución from
    /// <see cref="ResolucionStatus.Active"/> to
    /// <see cref="ResolucionStatus.Expired"/>. The transition is
    /// one-way — the caller cannot "un-expire" a resolución via this
    /// service. The implementation MUST:
    /// <list type="number">
    ///   <item>Confirm the row is currently <see cref="ResolucionStatus.Active"/>;
    ///         throw <see cref="InvalidOperationException"/> otherwise.</item>
    ///   <item>Append an audit-log entry tagged
    ///         <c>"RESOLUTION_EXPIRED"</c> in the same transaction.</item>
    /// </list>
    /// </summary>
    /// <param name="resolucionId">Resolución to mark expired.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkExpiredAsync(
        Guid resolucionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cheap boolean check for whether the tenant has at least one
    /// <see cref="ResolucionStatus.Active"/> resolución for the given
    /// document type. Used by the dispatcher as a pre-flight gate so
    /// it can fail fast with a friendly error before the actual
    /// numbering call.
    /// </summary>
    /// <param name="tenantId">Tenant to check.</param>
    /// <param name="documentType">Document type to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> when at least one active resolución exists for the
    /// (tenant, document type) pair; <c>false</c> otherwise.
    /// </returns>
    Task<bool> HasActiveResolucionAsync(
        Guid tenantId,
        DocumentType documentType,
        CancellationToken cancellationToken = default);
}
