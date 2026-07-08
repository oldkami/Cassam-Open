# Crystal Reports → DataGrid mapping (PR 10)

> **Scope**: Design §11 (`docs/legacy/FISCAL_AUDIT.md` §2, `docs/legacy/PRODUCTSHARE_RESOLUTION.md`).
> This document records how the three legacy Crystal Reports
> `.rpt` files map onto the new DataGrid + QuestPDF
> pipeline. Intended audience: the migration lead + any
> reviewer questioning "where did the .rpt go?".

## Migration constraint

Per `REQ-UI-11` (no new reporting engine) and `SCN-UI-10` (no
Crystal Reports in the codebase), the modern stack uses:

- **`DataGrid`** for interactive views (filter, sort, scroll).
- **`QuestPDF`** (MIT, free for commercial use, per DD-08) for
  printable PDF output — laid out as a one-page report with
  header / column rows / totals footer.
- **`CsvReportExporter`**, **`ExcelReportExporter`**,
  **`JsonReportExporter`** for raw data export.

The legacy `.rpt` files are **not** imported. They live in the
gitignored `Cassam ProductShare/` working copy and are not
re-emitted in modern format.

## 1:1 mapping

| Legacy `.rpt` (path on legacy WC)               | Modern replacement (PR 10)                                                                                          | Notes |
|-------------------------------------------------|---------------------------------------------------------------------------------------------------------------------|-------|
| `Cassam/CassamProductShare/RFacturaCarta.rpt`   | `src/Ui/Cassam.Ui/Reports/QuestPdfReportRenderer.RenderFacturaAsync(saleId, pageSize: PageSize.A4)` (PDF) **or** `ReportsViewModel.ExportDailySalesAsync(ExportFormat.Excel, ...)` | The "carta" page is A4; the legacy `.rpt` had a separate design for one-per-page invoice printing. The new pipeline reuses `RFacturaMediaCarta`'s emitter with a `PageSize.A4` argument. |
| `Cassam/CassamProductShare/RFacturaMediaCarta.rpt` | `QuestPdfReportRenderer.RenderFacturaAsync(saleId, pageSize: PageSize.A5)` (PDF) + the DataGrid view in `ReportsNavigationViewModel.DailySalesRows` (interactive). | Media-carta → A5. Day-bounded DataGrid replaces the runtime preview. |
| `Cassam/CassamProductShare/RInventarioCardex.rpt` | `ReportsViewModel.InventoryRows` DataGrid + `ExportInventoryAsync(ExportFormat.Pdf, ...)`. | "Kardex" = perpetual inventory ledger; the new flow shows current on-hand only (Phase 5 may extend to historical movement ledger). |

## Pipeline files

| Format | File (in `Cassam.Ui.Hardware.Common/Reports/`) | Notes |
|---|---|---|
| PDF    | `QuestPdfReportRenderer.cs` *(in `Cassam.Ui/Reports/`)* | QuestPDF Fluent; MIT license; commercial-friendly. |
| Excel  | `ExcelReportExporter.cs` | Hand-rolled OOXML; no NuGet dependency. |
| CSV    | `CsvReportExporter.cs` | RFC 4180 + UTF-8 BOM. |
| JSON   | `JsonReportExporter.cs` | System.Text.Json, indented, camelCase. |

## Feature-gap analysis

The legacy `.rpt` files had features the new pipeline does
not yet cover. We track them here so future Phase 5 work
(items in `[…]`) can promote them:

| Legacy feature | Status | Notes |
|---|---|---|
| Drill-down sub-reports ("click row → open detail .rpt") | **Not implemented** | The DataGrid exposes the data; the operator uses keyboard / column sort. [Phase 5] may add `RowDoubleClick` → detail page. |
| Dynamic grouping by date / cashier / customer | **Not implemented** | DataGrid sorts + filters handle the small data set per design §5.3 (≤ 200 rows per page). |
| Pre-printed stationary overlay (logo + footer banner) | **Implemented** | QuestPDF renderer stamps the logo + footer banner; matches letterhead in the legacy `.rpt`. |
| Currency-locale formatting for the printed totals | **Implemented** | Same `CurrencyEsCoConverter` from PR 8 (`$ 1.234.567,50`). |
| E-mail delivery as PDF attachment | **Not implemented** | Phase 5 wires SMTP. The DataGrid toolbar exposes "Exportar → PDF" so the manager can attach manually. |
| Print-with-paper-tape (roll 80mm thermal) | **Implemented** via `IReceiptPrinter` (PR 7/8) | The receipt path is separate from reports — see `ReceiptRenderer` design. |
| Visible tax-bucket breakdown on the printed report | **Implemented** | `CashSessionRow` exposes per-bucket bases (5%, 19%, exempt) per `FISCAL_AUDIT.md` §2. |
| Cash-session X/Z report (legacy `CierreDeCaja.vb`) | **Implemented** | `ReportsViewModel.ExportCashSessionAsync`. |

## Why no Stimulsoft / Telerik / jsreport

`REQ-UI-11` explicitly forbids a new reporting engine. The
report rendering is hand-rolled C# code emitting primitives
(no designer, no `.rpt`-equivalent file). QuestPDF qualifies
as a "layout library" rather than a "reporting engine"
because it has no UI designer, no report file format, and no
runtime expression language — it is C# code calling
`Page().Column().Text(...)` style methods.

## Reviewer checklist

- [x] No `.rpt` files in the modern source tree (`grep -r '\.rpt' src/Ui/` is empty).
- [x] No Crystal Reports assemblies in any `csproj` (`CrystalDecisions.*` references).
- [x] `ReportsView.xaml` uses `DataGrid` with `AutoGenerateColumns="False"` (manual columns + a `DataGridTemplateColumn` for tax-discriminated cells per design §5.3).
- [x] `QuestPdfReportRenderer.cs` is the only PDF emitter; it ships under the QuestPDF MIT license.
- [x] CSV / Excel / JSON exporters round-trip identical row sets (cross-tested in `ExcelAndCsvReportExporterTests`).
