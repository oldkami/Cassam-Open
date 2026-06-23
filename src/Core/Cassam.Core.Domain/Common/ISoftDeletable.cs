namespace Cassam.Core.Domain.Common;

/// <summary>
/// Marker interface for entities that support soft-delete via a
/// nullable <c>deleted_at</c> column. The global query filter
/// is configured in PR 2 (task T1.06) so all queries automatically
/// exclude rows where <see cref="DeletedAt"/> is non-null.
/// </summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}
