namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Colombian national identity document type used on
/// <c>customers.document_type</c>. Drives FE Venta vs DEE POS
/// dispatch per <c>pos-fiscal-fe-venta</c> REQ-FEVENTA-02
/// and <c>pos-core-modern-stack</c> SCN-CORE-12.
/// </summary>
public enum CustomerDocumentType
{
    /// <summary>Cédula de Ciudadanía — natural person (Colombian citizens).</summary>
    Cc = 0,

    /// <summary>Número de Identificación Tributaria — legal entities (companies).</summary>
    Nit = 1,

    /// <summary>Cédula de Extranjería — foreign residents.</summary>
    Ce = 2,

    /// <summary>Other (passport, foreign ID, etc.).</summary>
    Other = 3,
}
