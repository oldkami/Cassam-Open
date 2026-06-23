using Cassam.Core.Domain.Common;
using Cassam.Core.Domain.Enums;

namespace Cassam.Core.Domain.Entities;

/// <summary>
/// One POS cash-drawer session, opened by a <see cref="User"/> at
/// shift start and closed at shift end. Closing records the
/// expected amount (system-computed from sale totals + payments) and
/// the actual closing amount (operator-counted); the variance is
/// surfaced in <c>cloud-analytics-reporting</c> REQ-AR-04.
/// </summary>
public class CashSession : Entity, ISoftDeletable, ITenantScoped
{
    /// <summary>Foreign key to the owning <see cref="Entities.Tenant"/>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Foreign key to the <see cref="User"/> who opened the session.</summary>
    public Guid OpenedByUserId { get; set; }

    /// <summary>UTC timestamp the session was opened.</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>Opening till amount counted by the operator at shift start.</summary>
    public decimal OpeningAmount { get; set; }

    /// <summary>UTC timestamp the session was closed. Null while open.</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>Closing till amount counted by the operator at shift end.</summary>
    public decimal? ClosingAmount { get; set; }

    /// <summary>
    /// System-computed expected cash at close (opening + cash sales −
    /// cash refunds). Populated when the session is closed.
    /// </summary>
    public decimal? ExpectedAmount { get; set; }

    /// <summary>
    /// Variance between operator-counted closing amount and the
    /// system expected amount. Negative = shortage; surfaced red in
    /// the variance report per REQ-AR-04.
    /// </summary>
    public decimal? VarianceAmount { get; set; }

    /// <summary>Lifecycle status.</summary>
    public CashSessionStatus Status { get; set; } = CashSessionStatus.Open;

    /// <summary>Soft-delete timestamp.</summary>
    public DateTime? DeletedAt { get; set; }
}
