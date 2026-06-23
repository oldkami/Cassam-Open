# PR Review Notes — Cassam Modernization Chain

> **Status**: PR 1 of 24 — bootstrap foundation. **Current branch**: `prs/cassam-modernization/pr-01-bootstrap-foundation`.

## Branch naming (correction from tasks.md)

The original task plan (`openspec/changes/cassam-modernization/tasks.md`) called
for PR branches named `feature/cassam-modernization/pr-NN-<slug>`. **That naming
is impossible in git** — git cannot have both `feature/cassam-modernization`
(as a leaf ref) and `feature/cassam-modernization/pr-NN-...` (as a nested ref)
because the loose-ref file and the loose-ref directory would occupy the same
filesystem path.

This repo adopts the convention below. The chain semantics are unchanged.

| Role | Branch | Notes |
|---|---|---|
| Production | `main` | Only branch that merges to production. |
| Tracker (draft / no-merge) | `feature/cassam-modernization` | Aggregates every child PR in chain order. `git revert` of this branch from `main` cleanly undoes the entire modernization if a regulator requires it. |
| Child PRs | `prs/cassam-modernization/pr-NN-<slug>` | One child PR per work unit. Targets the previous PR's branch (or the tracker for PR 1). |
| Worktrees / scratch | `tmp/<author>/<purpose>` | Optional; never merge into `main` directly. |

The full PR slicing (24 PRs across 6 phases) is in
`openspec/changes/cassam-modernization/tasks.md` §"Chained-PR Plan".

## Dependency diagram

```
main
 └── feature/cassam-modernization   (tracker — draft, no-merge)
      ├── prs/cassam-modernization/pr-01-bootstrap-foundation  ← 📍 YOU ARE HERE
      ├── prs/cassam-modernization/pr-02-ef-conventions-xunit
      ├── prs/cassam-modernization/pr-03-compliance-audit-state
      └── ... (24 PRs total)
```

## PR 1 scope (this branch)

| Task | Title | Acceptance |
|---|---|---|
| T1.01 | Bootstrap `Cassam.sln` + .NET 10 + EF Core 10 + Npgsql 10; git init workspace root; .gitignore excludes legacy SVN checkouts | REQ-CORE-02, REQ-CORE-13 |
| T1.04 | Domain entities: Sale, SaleLineItem, Payment, CashSession, Product, Customer, User, Tenant | REQ-CORE-09..12, SCN-CORE-10..12 |

## CRITICAL deviation: TFM

**This PR targets `net10.0` instead of the design's `net11.0`** because no
.NET 11 SDK is installed on the build host. The .NET 10 SDK is the only
.NET SDK installed (`C:\Program Files\dotnet\sdk` lists only 10.0.x).

Resolution paths:

1. **Install .NET 11 SDK** and update `<TargetFramework>net10.0</TargetFramework>`
   → `<TargetFramework>net11.0</TargetFramework>` in all 5 csproj files
   (find/replace, ~5 hits). EF Core 11 / Npgsql 11 may also need version bumps.
2. **Accept .NET 10** as the TFM for the entire modernization and update the
   design to reflect the change.

If option 1 is chosen, this is a 5-minute rebase on the entire branch history.

## What's out of scope (intentional)

The following are explicitly NOT in PR 1 and live in later PRs:

- T1.02 — legacy VB.NET audit / fixture corpus export (PR 4)
- T1.03 — `mysql2pg` migration tool + round-trip test (PR 4)
- T1.05 — compliance entities (Resolucion, Certificado, DocumentoElectronico, etc.) (PR 3)
- T1.06 — full EF Core conventions (UUID via pgcrypto, schema-conformance test) (PR 2)
- T1.07 — append-only audit log writer impl (PR 3)
- T1.08 — `DocumentoElectronico` state machine (PR 3)
- T1.09 — Resolución / Certificado lifecycle (PR 4)
- T1.10 — Testcontainers PG integration tests (PR 2)
- T1.11 — PostgreSQL safety defaults (PR 4)
- T1.12 — Tenant soft-delete + 5y fiscal retention lock (PR 4)

## Verification (for reviewer)

```powershell
# Should print: Compilación correcta. 0 Errores.
dotnet build Cassam.slnx

# Should print: Correctas! 25 tests, 0 failures.
dotnet test Cassam.slnx

# Should print four commits on the PR branch:
git log --oneline feature/cassam-modernization..prs/cassam-modernization/pr-01-bootstrap-foundation
```

## Reviewer focus

1. **Entity surface** — does the property shape match spec REQ-CORE-09..12
   exactly? Are nullable / required annotations right for the legacy import
   path (T1.02 will dump legacy VB.NET fixtures into this shape)?
2. **Tenant scoping** — every entity in §"T1.04" except `Tenant` itself
   implements `ITenantScoped`. SaleLineItem and Payment are intentionally
   NOT tenant-scoped (their tenant context comes from the parent Sale /
   CashSession FK).
3. **Composite (tenant_id, ...) indexes** — DD-05 requires every
   tenant-scoped table to have a composite index with `tenant_id` as the
   leading column. The configuration in `CassamDbContext.OnModelCreating`
   is hand-written here; PR 2 (T1.06) will fold these into a reflection-
   driven convention so new tenant-scoped entities don't drift.
4. **Global query filter for soft-delete** — applied via `HasQueryFilter` on
   every `ISoftDeletable` entity. Tenant is intentionally excluded.
5. **Concurrency token** — every entity uses `Version` as the concurrency
   token (inherited from `Entity`). Npgsql emits `WHERE version = @version`
   on every UPDATE.
