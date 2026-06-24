# Legacy VB.NET fiscal fixture corpus

> **Task**: T1.02 · **REQs**: REQ-CORE-05, SCN-CORE-05 · **Source**: `Cassam/CassamProductShare/CapaDatos/`

This directory holds a small, representative sample of the legacy VB.NET POS data shapes and tax-calculation results. The fixtures feed the legacy-parity harness (T4b.14) and exist so the modern C# generators can regression-test themselves against known legacy outputs.

## Files

| File | Purpose | Legacy code path |
|---|---|---|
| `sample_sales.json` | 8 representative sales (cash, credit, mixed IVA, with discount, with surcharge, with INC, with multiple payment methods, with ReteIVA). Each has the legacy-computed expected totals. | `Factura.vb` aggregations + `CierreDeCaja.vb` tax brackets |
| `sample_products.json` | 12 products covering all 5 IVA brackets (0, 0.05, 0.10, 0.16, 0.19, 0.20) + exento + INC (restaurants, telephony). | `Producto.vb` |
| `sample_resoluciones.json` | 3 DIAN resoluciones (active, near-expiry, exhausted). Each maps to modern `Resolucion` entity shape. | `DatoDian.vb` |
| `sample_fiscal_calculations.json` | 24 per-line (input, expected) pairs for the tax math: pure IVA, pure INC, mixed, with discount, with surcharge, with both, with cash rounding. Plus 4 cash-rounding (ajuste_al_total) cases. | `Factura.vb` lines 634-848 |
| `README.md` | This file. | — |

## How a parity test uses these

The T4b.14 harness will:

1. Load `sample_fiscal_calculations.json`.
2. For each entry, run the modern C# tax calculator (`ITaxCalculator.CalculateAsync` — TBD module) on the same input.
3. Assert the modern `pvp` / `subtotal` / `iva` / `inc` values match the legacy `expected.*` within ±1 peso (rounding tolerance).

```csharp
// Pseudo-code for T4b.14 parity test:
[Theory]
[JsonFileData("Fixtures/legacy-vb/sample_fiscal_calculations.json")]
public async Task Modern_tax_calculator_matches_legacy_vb(FiscalCalcCase testCase)
{
    var modern = await _taxCalculator.CalculateAsync(testCase.Input);
    modern.Pvp.Should().BeApproximately(testCase.Expected.Pvp, 1);
    modern.Subtotal.Should().BeApproximately(testCase.Expected.Subtotal, 1);
    modern.Iva.Should().BeApproximately(testCase.Expected.Iva, 1);
    modern.Inc.Should().BeApproximately(testCase.Expected.Inc, 1);
}
```

## Why these fixtures and not the full data set

The legacy POS has thousands of historical invoices; copying them all would balloon the test repo and force the modern schema to absorb legacy quirks (`contador_compra`/`contador_venta` split, `cliente_proveedor` dual-role table, single-factura-covers-compra-y-venta, etc.) that the modern system deliberately does NOT model.

This corpus is **representative**, not **exhaustive**:
- Every distinct IVA bracket (5/10/16/19/20/0).
- Every distinct INC scenario (none, restaurants, telephony).
- Every distinct discount/surcharge combination.
- Every distinct payment scenario (cash, card, mixed).
- The cash-rounding artifact (`ajuste_al_total`).
- The full DIAN lifecycle (active / near-expiry / exhausted).

A modern generator that matches these fixtures plus 1-2 unit tests for each edge case will have full coverage of the legacy contract. The full historical dataset is held in the customer's MySQL dump (deferred until T1.03 customer cutover).

## Cross-references

- `docs/legacy/PRODUCTSHARE_RESOLUTION.md` — which folder is canonical
- `docs/legacy/FISCAL_AUDIT.md` — per-module documentation + formulas
- `openspec/changes/cassam-modernization/tasks.md` T1.02, T4b.14 rows
- `src/Core/Cassam.Core.Domain/Enums/TaxCategory.cs` — modern `S/Z/E/O` enum

## Versioning

`schemaVersion: "1.0"` — fixtures are immutable until the legacy schema changes. Any update bumps the version and adds a migration note.

## Maintenance

Adding new fixtures:

1. Identify the legacy code path (`CapaDatos/<module>.vb` line range).
2. Compute the expected totals BY HAND from the formula in `FISCAL_AUDIT.md` §3.
3. Run the legacy VB.NET POS against the same inputs (if available) and cross-check.
4. Add to the appropriate file with a unique id, scenario, and `notes` describing the legacy code path.
5. Bump `schemaVersion` if the schema shape changes.