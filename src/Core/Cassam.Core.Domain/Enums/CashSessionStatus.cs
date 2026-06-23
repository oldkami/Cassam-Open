namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Cash session lifecycle status. Drives expected-vs-actual variance
/// reporting per <c>pos-core-modern-stack</c> REQ-CORE-10 and
/// <c>cloud-analytics-reporting</c> REQ-AR-04.
/// </summary>
public enum CashSessionStatus
{
    /// <summary>Open and accepting sales.</summary>
    Open = 0,

    /// <summary>Closed but not yet reconciled against bank deposits / statements.</summary>
    Closed = 1,

    /// <summary>Reconciled — closing amount matches expected amount (or variance acknowledged).</summary>
    Reconciled = 2,
}
