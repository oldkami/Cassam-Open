namespace Cassam.Core.Domain.Enums;

/// <summary>
/// Colombian DIAN tax category, per Anexo Técnico (DEE POS Anexo 1.0
/// and FE Venta Anexo 1.9) and <c>pos-core-modern-stack</c> REQ-CORE-09.
/// The letter values map directly to the <c>&lt;TaxCategory&gt;</c>
/// element in UBL 2.1 XML:
///   S = Standard (Gravado) — IVA 19% (or current rate)
///   Z = Zero-rate (Tasa cero) — 0% but reportable
///   E = Exempt (Exento) — outside the tax scope
///   O = Other (Otro) — excluded / non-taxable
/// </summary>
public enum TaxCategory
{
    /// <summary>Standard / Gravado — taxed at the prevailing IVA rate.</summary>
    Standard = 0,

    /// <summary>Zero-rate / Tasa cero — taxable but at 0% (still reported).</summary>
    ZeroRate = 1,

    /// <summary>Exempt / Exento — outside the scope of the tax.</summary>
    Exempt = 2,

    /// <summary>Other / Otro — excluded or non-taxable.</summary>
    Other = 3,
}
