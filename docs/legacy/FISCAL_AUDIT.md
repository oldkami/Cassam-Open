# Fiscal audit — legacy Cassam VB.NET POS

> **Task**: T1.02 · **Date**: 2026-06-24 · **Source**: `C:\D\Cassam\Cassam\CassamProductShare\` (active SVN WC)

This document enumerates the fiscal modules in the legacy VB.NET POS, captures their tax-calculation formulas, and identifies the contract the modern C# / PostgreSQL rewrite must honor for SCN-CORE-05 (legacy VB.NET parity).

## 1. Module inventory

The legacy `CapaDatos/` (data layer) holds 28 `.vb` files. The fiscal subset:

| Module | Path | Public surface (summary) |
|---|---|---|
| `DatoDian` | `CapaDatos/DatoDian.vb` | DIAN-issued numbering-range authorization (resolución). Holds `numeroinicial`, `numerofinal`, `numeroactual`, `estado`, `fechavencimiento`, `regimen`. |
| `Factura` | `CapaDatos/Factura.vb` | Factura (invoice) header + aggregation. Public methods: `GetIva`, `GetSubTotal`, `GetOtrosImpuestos`, `GetDescuento`, `GetTotal`, `GetSobrecosto`, `GetTotalAntesImpuestos`, `GetAjusteAlTotal`. |
| `Movimiento` | `CapaDatos/Movimiento.vb` | **Line item** (movement). Holds per-line `cantidad`, `precio`, `descuento`, `sobrecosto`, `iva`, `otros_impuestos`. |
| `MovimientoTemporal` | `CapaDatos/MovimientoTemporal.vb` | In-progress sale buffer (cart). Same aggregation methods as `Factura` but queries `movimiento_temporal` instead of joined `detalle_factura + movimiento`. |
| `DetalleFactura` | `CapaDatos/DetalleFactura.vb` | Join table: `(factura_idfactura, movimiento_idmovimiento)`. |
| `Equivalencia` | `CapaDatos/Equivalencia.vb` | Product conversions (e.g. 1 box = 24 units). `(producto1, producto2, cantidad_de_p2_iguala_p1)`. |
| `Producto` | `CapaDatos/Producto.vb` | Product master. Holds `iva` (per-product tax rate), `sobrecosto` (default markup). |
| `ClienteProveedor` | `CapaDatos/ClienteProveedor.vb` | 3rd-party master (both customers and suppliers). |
| `Pago` | `CapaDatos/Pago.vb` | Payment rows (a factura has many pagos). |
| `CierreDeCaja` | `CapaDatos/CierreDeCaja.vb` | Daily X/Z report. Tax breakdown by bracket. |
| `Devolucion` | `CapaDatos/Devolucion.vb` | Returns. |
| `Empresa` | `CapaDatos/Empresa.vb` | Tenant settings: `CashDrawerEstado`, `CINCOPINES`, `SubtotalIvaIncluido`. |
| `OperacionesMySQL` | `CapaDatos/OperacionesMySQL.vb` | Low-level MySQL helpers (`spRun_MySQLCommand`, `spRetornaValor`). |

UI layer (`frm*`) wraps these modules with WinForms. The fiscal logic lives in `CapaDatos/`.

## 2. Tax category contract — IVA brackets

The legacy `CapaDatos/CierreDeCaja.vb` (line ~1185) splits IVA into four named buckets:

| `m.iva` value | Bucket (UI column) | `base_*` aggregate | `iva_*` aggregate | Maps to modern `TaxCategory` |
|---:|---|---:|---|---|
| `0` | Exento | `base_exento` | (none) | **E** (Exempt) |
| `0.05` | Gravado 5% | `base_5` | `iva_5` | **S** (Standard) — historical rate |
| `0.10` | Gravado 10% | `base_10` | `iva_10` | **S** (Standard) — historical rate |
| `0.16` | Gravado 16% | `base_16` | `iva_16` | **S** (Standard) — historical rate |
| `0.19` | Gravado 19% | `base_19` | `iva_19` | **S** (Standard) — current rate (since 2026) |
| `0.20` | Gravado 20% | `base_20` | `iva_20` | **S** (Standard) — reactivated rate (Ley 2277/2022) |

Notes:
- The current DIAN rate is 19% (since Jan 2026); the legacy code carries all five rates so historical invoices can be re-printed.
- The legacy system stores `m.iva` as a per-line **fractional rate** (e.g. `0.19`) — NOT a `TaxCategory` enum. The modern system stores the **enum** (`S`/`Z`/`E`/`O`) and computes the rate from a per-tenant tax-rate table.
- `otros_impuestos` is the **impoconsumo (INC)** — a Colombian national consumption tax on specific goods (restaurants, vehicles, telecom). It is reported as a separate bucket but added to the public line total exactly like IVA.

## 3. Per-line pricing formula

Each `Movimiento` (line item) carries:

- `cantidad` — quantity (decimal, allows fractional units like kg)
- `precio` — **base price** (sin IVA, sin INC)
- `descuento` — **fractional** discount (e.g. `0.10` = 10% off)
- `sobrecosto` — **fractional** markup (e.g. `0.05` = 5% surcharge)
- `iva` — fractional IVA rate (e.g. `0.19`)
- `otros_impuestos` — fractional INC rate (e.g. `0.08`)

The public line total (what the customer sees) is:

```
public_total_linea = cantidad
                   * (precio - precio * descuento + precio * sobrecosto)
                   * (1 + iva + otros_impuestos)
```

The legacy code computes this in SQL with the exact ROUND/precision pattern:

```sql
ROUND(
  cantidad
  * ROUND(
      ((precio - precio*descuento + precio*sobrecosto) * (1 + iva + otros_impuestos)),
      0
    ),
  PRECISION
)
```

`PRECISION = 0` (whole pesos) by default. The per-line subtotal can be overridden (`rPrecision` parameter on `MovimientoTemporal.GetSubtotal`).

### Derived fields (from `Factura.vb` lines 666-848)

| Field | Formula |
|---|---|
| `Subtotal` (base gravada/exenta) | `sum_linea(public_total / ((1 + iva + otros_impuestos) * (1 - descuento + sobrecosto)))` |
| `IVA` | `sum_linea(public_total / (1 + iva + otros_impuestos) * iva)` |
| `OtrosImpuestos` (INC) | `sum_linea(public_total / (1 + iva + otros_impuestos) * otros_impuestos)` |
| `Descuento` | `sum_linea(subtotal * descuento)` |
| `Sobrecosto` | `sum_linea(subtotal * sobrecosto)` |
| `TotalAntesImpuestos` | `subtotal + descuento` (i.e. base + discount added back) |
| `Total` | `sum_linea(public_total)` |

### Ajuste al total (cash-rounding artifact)

`Factura.GetAjusteAlTotal(total, valor)` rounds the invoice UP to the next `valor` (e.g. the next $100) to make cash handling easier:

```vb
ldAjuste = (ldTotal / rValor)        ' e.g. 12345 / 100 = 123.45
ldAjuste = Math.Ceiling(ldAjuste)    ' 124
ldAjuste = (ldAjuste * rValor)       ' 12400
ldAjuste = (ldAjuste - ldTotal)      ' 12400 - 12345 = 55 (cash drawer keeps the 55)
```

This pattern is preserved in `MovimientoTemporal.GetAjusteAlTotal` (line 838).

## 4. DIAN régimen enum

`DatoDian.regimen` validates against:

```vb
If (value = "COMUN") Or (value = "SIMPLIFICADO") Or (value = "ESPECIAL") Then
    ' accept
Else
    Throw New System.Exception("Asignacion no valida de regimen")
End If
```

The modern system models these on `Tenant.Nit` + a separate `Regimen` column (Phase 3 work — `cloud-saas-multi-tenant` REQ-MT-03).

| Legacy `regimen` | Modern mapping (proposed) |
|---|---|
| `COMUN` | `Regimen.Comun` — responsible for IVA, INC, retenciones |
| `SIMPLIFICADO` | `Regimen.Simplificado` — no IVA charged (replaces it with a small INC) |
| `ESPECIAL` | `Regimen.Especial` — government / non-profit / specific industries |

The `COMUN` vs `SIMPLIFICADO` distinction drives whether `iva = 0.19` should be reported as Gravado or Exento in the XML (T4b.03 / T4b.04 generator).

## 5. Resolución lifecycle (legacy)

The `DatoDian` row carries the numbering-range state machine:

| `estado` | Meaning | Legacy code path |
|---|---|---|
| (string, free-form) | Active, Expired, Exhausted | State tracked as `VARCHAR`, not constrained. |

The legacy system mutates `numeroactual` by raw SQL (`UPDATE dato_dian SET numeroactual = numeroactual + 1 WHERE iddatodian = ?`) without a row lock — see Risk #9 in `tasks.md` ("Offline-first sync complexity"). The modern system replaces this with `SELECT … FOR UPDATE` row locking (Phase 1 PR 4's `INumberingService.ReserveNextNumberAsync`).

`contador_compra` and `contador_venta` are **separate counters** for buy/sell documents on the same resolución — a Colombian DIAN edge case where one autorización can authorize both directions.

## 6. Database tables (legacy MySQL)

| Table | Purpose | Modern target |
|---|---|---|
| `dato_dian` | Resolución authorization | `resoluciones` + `software_technical_keys` |
| `factura` | Invoice header | `sales` + `documentos_electronicos` |
| `detalle_factura` | Invoice ↔ line items join | (inline `sale_line_items.sale_id`) |
| `movimiento` | Line item | `sale_line_items` |
| `movimiento_temporal` | In-progress cart | (transient, no DB row) |
| `producto` | Product master | `products` |
| `cliente_proveedor` | Customer/supplier | `customers` |
| `pago` | Payment | `payments` |
| `cierre_de_caja` | X/Z report | `cash_sessions` (close metadata) |
| `devolucion` + `detalle_devolucion` | Returns | (Phase 4a sync — out of scope for T1.02) |
| `empresa` | Tenant settings | `tenants` + per-tenant config (Phase 3) |
| `equivalencia` | Product conversions | (Phase 2 product-edit UI; no schema yet) |
| `usuario`, `perfil`, `usuario_has_perfil` | Users + roles | `users` + role claim |
| `punto`, `vendedor`, `caja_menor`, `sobrecosto`, `remision`, `remision_has_movimiento` | Misc | (deferred — not fiscal-critical) |

## 7. Known workarounds / quirks

| Quirk | Where | Modern replacement |
|---|---|---|
| `PRECISION = 0` (whole pesos) | `Factura.vb`, `MovimientoTemporal.vb` constants | Phase 4b PDF representation rounds at last moment; in-DB we keep `numeric(18,4)` and round at display |
| Concurrency via raw UPDATE (no lock) | `DatoDian` mutation paths | `INumberingService` with `SELECT … FOR UPDATE` (PR 4) |
| `regimen` stored as VARCHAR with setter-time validation | `DatoDian.regimen` setter | DB-level CHECK constraint + enum (Phase 3) |
| `estado` stored as VARCHAR (no validation) | `dato_dian.estado` | DB CHECK + state machine (`ResolucionStatus` enum) |
| IVA rates 0.05/0.10/0.16/0.19/0.20 all coexisting | `producto.iva` column | Per-tenant tax-rate table (T4b.03) — historical re-print uses frozen rates |
| `sobrecosto` and `descuento` are FRACTIONAL not absolute | `producto`, `movimiento` columns | `LineDiscountAmount` / per-line fractional keep both forms (modern) |
| `contador_compra` + `contador_venta` split counters | `dato_dian` columns | (not yet modeled in modern — TODO Phase 4b) |
| Single `factura` table covers VENTA + COMPRA + ANULACION | `factura.tipo_factura` column | Modern `documentos_electronicos.document_type` enum (more granular) |
| `cliente_proveedor` is one table for both roles | `cliente_proveedor` | Modern splits into `customers` only; suppliers deferred to Phase 4a |
| `bin/` + `obj/` checked into SVN | Visual Studio defaults | Modern repo: `.gitignore` excludes all build output |

## 8. Tests / parity harness impact (T4b.14)

`pos-core-modern-stack` SCN-CORE-05 requires the modern generators to produce byte-identical (or rounding-equivalent) totals to the legacy VB.NET calculations on a representative sample of historical sales.

The fixture corpus in `tests/Cassam.Core.Tests/Fixtures/legacy-vb/` exports:

1. **`sample_sales.json`** — 8 representative sales (mix of cash/credit, mixed IVA brackets, with and without INC, with and without discount, with and without surcharge). Each sale has line items with known inputs and the legacy-computed expected outputs.
2. **`sample_products.json`** — 12 products covering all 5 IVA brackets + 2 exento + 1 with INC + 1 with both IVA and INC.
3. **`sample_resoluciones.json`** — 3 resoluciones (one active, one expired, one exhausted) with `numeroresolucion` + `prefijo` + ranges.
4. **`sample_fiscal_calculations.json`** — 24 (input, expected) pairs for the tax math: pure IVA, pure INC, mixed, with discount, with surcharge, with both, with cash rounding (`ajuste_al_total`).
5. **`README.md`** — explains what each fixture represents and which legacy code path it exercises.

These fixtures feed T4b.14 — the legacy VB.NET fiscal parity harness — which will:
1. Load each fixture.
2. Run the modern generator over the same inputs.
3. Assert the modern totals match the legacy expected outputs within rounding tolerance.

## 9. Out of scope for T1.02

- **Cryptographic contracts** — the legacy code does not sign XML; signing is Phase 4b (T4b.01).
- **DIAN transmisión** — legacy uses paper tiquetes and on-prem MySQL; DIAN transmission is Phase 4b.
- **Multi-tenant** — legacy is single-tenant per workstation; the modern schema's `(tenant_id, ...)` composite indexes are out of scope here.
- **CUFE/CUDE** — legacy uses consecutivos only; SHA-384 hash is Phase 4b.

## 10. Cross-references

- `docs/legacy/PRODUCTSHARE_RESOLUTION.md` — which folder is canonical
- `tests/Cassam.Core.Tests/Fixtures/legacy-vb/README.md` — fixture corpus
- `openspec/changes/cassam-modernization/tasks.md` T1.02 row (REQ-CORE-05, SCN-CORE-05)
- `src/Core/Cassam.Core.Domain/Enums/TaxCategory.cs` — modern `S/Z/E/O` mapping
- `src/Core/Cassam.Core.Domain/Entities/Resolucion.cs` — modern resolución entity