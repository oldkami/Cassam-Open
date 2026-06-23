namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Tenant subscription tier. Drives usage metering per
/// <c>cloud-saas-multi-tenant</c> REQ-MT-06.
/// </summary>
public enum SubscriptionTier
{
    Free = 0,
    Pro = 1,
    Enterprise = 2,
}
