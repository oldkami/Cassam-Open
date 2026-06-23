namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Customer persona type, used to dispatch FE Venta (juridica) vs
/// DEE POS (natural) per <c>pos-fiscal-fe-venta</c> REQ-FEVENTA-02
/// and <c>pos-core-modern-stack</c> REQ-CORE-11.
/// </summary>
public enum PersonType
{
    /// <summary>Natural person (cédula, etc.).</summary>
    Natural = 0,

    /// <summary>Legal entity (NIT / company).</summary>
    Juridica = 1,
}
