namespace Cassam.Core.Domain.Enums;

/// <summary>
/// User role within a tenant. Drives authorization decisions and
/// Keycloak role-claim mapping per <c>cloud-saas-multi-tenant</c>
/// REQ-MT-07 and <c>pos-core-modern-stack</c> REQ-CORE-12.
/// </summary>
public enum UserRole
{
    Cashier = 0,
    Manager = 1,
    Admin = 2,
    Owner = 3,
}
