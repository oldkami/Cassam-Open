namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Colombian DIAN electronic document type. Shared by
/// <c>resoluciones</c>, <c>sales</c>, and <c>documentos_electronicos</c>
/// per <c>pos-core-modern-stack</c> REQ-CORE-06 / REQ-CORE-09.
/// </summary>
public enum DocumentType
{
    /// <summary>Documento Equivalente Electrónico POS (DEE POS) — Anexo 1.0.</summary>
    DeePos = 0,

    /// <summary>Factura Electrónica de Venta (FE Venta) — Anexo 1.9.</summary>
    FeVenta = 1,

    /// <summary>Nota Crédito — references an existing CUFE/CUDE.</summary>
    NotaCredito = 2,

    /// <summary>Nota Débito — references an existing CUFE/CUDE.</summary>
    NotaDebito = 3,
}
