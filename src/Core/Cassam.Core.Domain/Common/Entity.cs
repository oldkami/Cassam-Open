namespace Cassam.Core.Domain.Common;

/// <summary>
/// Base class for all domain entities. Provides common identity,
/// timestamp, and optimistic-concurrency columns that every entity
/// requires per spec <c>pos-core-modern-stack</c> REQ-CORE-02.
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// Surrogate primary key. Generated as a UUID v7 (time-ordered)
    /// so that B-tree index inserts stay near the rightmost leaf,
    /// reducing page splits under sustained write load.
    /// </summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Row creation timestamp (UTC, set by the database on insert).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Row last-update timestamp (UTC, maintained by the database).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Incremented on every UPDATE.
    /// Marked with <see cref="System.ComponentModel.DataAnnotations.ConcurrencyCheckAttribute"/>
    /// so that EF Core emits the <c>WHERE version = @version</c> predicate.
    /// </summary>
    public uint Version { get; set; } = 1;
}
