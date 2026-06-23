namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Tenant lifecycle status. The <see cref="Deleted"/> terminal state
/// preserves fiscal history for the regulatory retention window
/// (5 years default) per <c>cloud-saas-multi-tenant</c> REQ-MT-05.
/// </summary>
public enum TenantStatus
{
    Trial = 0,
    Active = 1,
    Suspended = 2,
    Deleted = 3,
}
