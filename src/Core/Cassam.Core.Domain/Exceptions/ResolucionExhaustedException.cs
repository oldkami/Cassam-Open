namespace Cassam.Core.Domain.Exceptions;

/// <summary>
/// Thrown when <c>INumberingService.ReserveNextNumberAsync</c> finds an
/// active resolución but its numbering range is exhausted
/// (<c>current_number &gt;= range_end</c>) per
/// <c>pos-core-modern-stack</c> REQ-CORE-06 / SCN-CORE-07.
///
/// <para>
/// The caller's contract is to either:
/// </para>
/// <list type="number">
///   <item>Mark the current resolución as <see cref="Enums.ResolucionStatus.Expired"/>
///         via <c>IResolucionLifecycleService.MarkExpiredAsync</c>.</item>
///   <item>Look for the next active resolución for the same document type
///         (the dispatcher already implements that fallback).</item>
///   <item>If none exists, surface the exception to the operator so a new
///         DIAN range can be uploaded.</item>
/// </list>
///
/// <para>
/// Conceptually distinct from <see cref="ResolucionNotActiveException"/>:
/// the latter is "no resolution at all in the active state", while this
/// one is "there is one but it ran out of numbers". Both share the same
/// SCN-CORE-07 dispatcher-fallback semantics but carry different
/// remediation hints.
/// </para>
/// </summary>
public sealed class ResolucionExhaustedException : InvalidOperationException
{
    /// <summary>The resolución that ran out of numbers.</summary>
    public Guid ResolucionId { get; }

    /// <summary>The last number the resolución successfully issued.</summary>
    public long CurrentNumber { get; }

    /// <summary>The inclusive upper bound of the DIAN-authorized range.</summary>
    public long RangeEnd { get; }

    /// <summary>
    /// Builds the exception with a stable message that includes the
    /// current and target numbers — useful for the operator to confirm
    /// the range against the DIAN autorización.
    /// </summary>
    /// <param name="resolucionId">Resolución that exhausted its range.</param>
    /// <param name="currentNumber">Last number actually issued.</param>
    /// <param name="rangeEnd">Inclusive upper bound of the authorized range.</param>
    public ResolucionExhaustedException(Guid resolucionId, long currentNumber, long rangeEnd)
        : base($"Resolución '{resolucionId}' exhausted its numbering range: " +
               $"current_number={currentNumber}, range_end={rangeEnd}. " +
               "Mark the resolución as Expired and either pick the next active range or upload a new one.")
    {
        ResolucionId = resolucionId;
        CurrentNumber = currentNumber;
        RangeEnd = rangeEnd;
    }
}
