# Cassam ProductShare folder resolution

> **Task**: T1.02 · **Date**: 2026-06-24 · **Audit**: legacy VB.NET POS at `C:\D\Cassam\`

## Question

The workspace contains **two** folders named like the legacy product:

1. `C:\D\Cassam\Cassam ProductShare\` (with space) — at workspace root.
2. `C:\D\Cassam\Cassam\CassamProductShare\` (no space) — inside the `Cassam/` SVN working copy.

`sdd-init` flagged this as a hygiene concern (proposal Risk #13). This document is the authoritative resolution.

## Investigation

### Folder 1: `C:\D\Cassam\Cassam ProductShare\` (workspace root, with space)

```
C:\D\Cassam\Cassam ProductShare\
└── CassamProductShare\
    ├── CassamProductShare.sln
    └── CassamProductShare\
        ├── CassamProductShare.vbproj
        ├── Form1.Designer.vb
        ├── Form1.vb                              (empty starter)
        ├── bin\Debug\CassamProductShare.vshost.exe
        ├── bin\Debug\CassamProductShare.vshost.exe.manifest
        └── My Project\                           (Application.myapp, Resources.resx, Settings.settings)
```

| Property | Value |
|---|---|
| Total files | 13 |
| Oldest file | 2007-12-31 |
| Most recent file | 2014-03-13 |
| `.svn/` metadata | **Absent** |
| `CapaDatos/` layer | **Absent** |
| `CapaNegocio/` layer | **Absent** |
| `FacturacionElectronica/` layer | **Absent** |
| Fiscal modules (DatoDian, Factura, etc.) | **Absent** |

**Verdict**: This is a **starter VS project** that Visual Studio generated when someone ran *File → New Project → Windows Forms Application* and named the project `CassamProductShare`. It contains only the boilerplate `Form1.vb` template — no business logic, no data access, no DIAN module, no CapaDatos, no Compra/Venta flow.

It is **NOT the source of truth** for the legacy POS.

### Folder 2: `C:\D\Cassam\Cassam\CassamProductShare\` (no space, inside SVN working copy)

```
C:\D\Cassam\Cassam\
├── .svn\                                       (Subversion metadata — ACTIVE WC)
├── Cassam\
│   ├── Cassam.sln
│   ├── CassamProductShare.vbproj
│   ├── CapaDatos\                              (28 .vb files including fiscal modules)
│   │   ├── DatoDian.vb                         (DIAN resolución lifecycle)
│   │   ├── Factura.vb                          (factura + tax calculations)
│   │   ├── DetalleFactura.vb                   (factura ↔ movimiento join)
│   │   ├── Movimiento.vb                       (line items w/ iva + impoconsumo)
│   │   ├── MovimientoTemporal.vb              (in-progress sale buffer)
│   │   ├── Producto.vb                         (producto + tax category)
│   │   ├── ClienteProveedor.vb                 (3rd party master)
│   │   ├── Empresa.vb                          (settings: cash drawer, régimen)
│   │   ├── Equivalencia.vb                     (product conversions)
│   │   ├── Pago.vb                             (payment methods)
│   │   ├── CierreDeCaja.vb                     (X/Z report: tax breakdown)
│   │   ├── CajaMenor.vb                        (petty cash)
│   │   ├── DatosCassam.vb                      (settings singleton)
│   │   ├── Devolucion.vb                       (returns)
│   │   ├── DetalleDevolucion.vb
│   │   ├── MovimientoCuenta.vb
│   │   ├── MovimientoTemporal.vb
│   │   ├── Perfil.vb
│   │   ├── Punto.vb
│   │   ├── Remision.vb
│   │   ├── Remision_has_movimiento.vb
│   │   ├── Sobrecosto.vb
│   │   ├── Usuario.vb
│   │   ├── UsuarioHasPerfil.vb
│   │   ├── Vendedor.vb
│   │   └── OperacionesMySQL.vb                 (low-level MySQL driver)
│   ├── frmDatoDian.vb                          (DIAN settings UI)
│   ├── frmEquivalencia.vb                      (product conversion UI)
│   ├── frmFacturaBase.vb                       (invoice base form — 81 KB)
│   ├── frmFacturaCompraLista.vb
│   ├── frmFacturaCrear.vb                      (invoice create — 31 KB)
│   ├── frmFacturaLista.vb
│   ├── frmFacturaMultipleCrear.vb              (multi-line invoice — 52 KB)
│   ├── frmFacturaTotal.vb
│   ├── frmFacturaVentaLista.vb
│   ├── frmFacturaVer.vb
│   ├── frmProductoAdd.vb                       (product add — 36 KB)
│   ├── frmProductoCrear.vb                     (product create — 54 KB)
│   ├── frmProductoEditar.vb
│   ├── frmProductoLista.vb
│   ├── frmVentaPos.vb                          (POS sale screen — 27 KB)
│   ├── frmClienteLista.vb
│   ├── frmClienteProveedorEditar.vb
│   ├── frmClienteProveedorNuevo.vb
│   ├── frmClienteSeleccion.vb
│   ├── frmVentaLista.vb
│   ├── RFacturaCarta.vb                        (carta report)
│   ├── RFacturaMediaCarta.vb                   (half-letter report)
│   ├── RInventarioCardex.vb                    (inventory cardex report)
│   └── ... + Designer.vb / resx pairs
└── bin\ obj\                                    (build output — ignored)
```

| Property | Value |
|---|---|
| `.svn/` metadata | **Present** (active Subversion working copy) |
| `CapaDatos/` layer | **Present** (28 modules) |
| Fiscal modules | **Present** (DatoDian, Factura, Movimiento, Equivalencia, etc.) |
| DIAN formulario | **Present** (`frmDatoDian.vb`) |

**Verdict**: This is the **canonical, active SVN working copy** that holds the production VB.NET POS — the source of truth for any legacy-parity work.

## Resolution

| Folder | Role | Action |
|---|---|---|
| `C:\D\Cassam\Cassam ProductShare\` (workspace root, with space) | Stale VS template; 13 files; no business code; no SVN metadata | **Leave alone** (still gitignored by `.gitignore` line 61) — but document so it is not mistaken for the canonical source. |
| `C:\D\Cassam\Cassam\CassamProductShare\` (no space, inside `Cassam/`) | **Canonical legacy POS source** — full CapaDatos, fiscal modules, DIAN forms, SVN working copy | **Audit source** for T1.02 — see `FISCAL_AUDIT.md` for per-module documentation. |

The `.gitignore` (lines 61-62) excludes **both** folders:

```
Cassam ProductShare/
Cassam ProductShare/2/
```

Both folders remain on disk (untouched) for migration reference and legacy-parity work but are not tracked by the modern git repository. The modern repo's `docs/legacy/` and `tests/Cassam.Core.Tests/Fixtures/legacy-vb/` directories hold the *extracted* legacy knowledge.

## Why two folders exist

The most likely explanation: the workspace root once contained an extracted-but-not-checked-in copy of the legacy project (workspace-root `Cassam ProductShare/`), and the active SVN working copy was later relocated to `C:\D\Cassam\Cassam\CassamProductShare\` (the `.svn/` metadata is in the parent `Cassam/` directory). The root-level copy was never deleted, just abandoned.

## What T1.02 ships from this audit

1. **Resolution doc** — this file.
2. **Per-module fiscal audit** — `FISCAL_AUDIT.md` enumerates the 14 fiscal modules + tax calculation formulas + DIAN régimen contract + known workarounds.
3. **Fixture corpus** — `tests/Cassam.Core.Tests/Fixtures/legacy-vb/` exports representative samples (sales, products, resoluciones, tax calculations) so T4b.14 (legacy VB.NET fiscal parity harness) can rebuild the legacy calculators and regression-test them against new generators.

## Cross-references

- `docs/legacy/FISCAL_AUDIT.md` — per-module VB.NET documentation
- `tests/Cassam.Core.Tests/Fixtures/legacy-vb/README.md` — fixture corpus explanation
- `openspec/changes/cassam-modernization/proposal.md` §Risks #13 (Duplicated `Cassam ProductShare/` folder)