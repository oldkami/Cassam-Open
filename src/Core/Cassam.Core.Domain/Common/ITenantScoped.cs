namespace Cassam.Core.Domain.Common;

/// <summary>
/// Marker interface for entities that belong to a tenant and therefore
/// require PostgreSQL Row-Level Security filtering (DD-05).
///
/// The composite index requirement
/// (<c>(tenant_id, ...)</c> with <c>tenant_id</c> as the leading column)
/// applies to every entity implementing this interface.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
