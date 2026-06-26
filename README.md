# Cassam Modernization

<!--
  CI status badge. Replace `cassam/cassam` with the actual
  GitHub `<OWNER>/<REPO>` slug once the repo is published.
-->
[![Cassam Modernization CI](https://github.com/cassam/cassam/actions/workflows/cassam-modernization.yml/badge.svg)](https://github.com/cassam/cassam/actions/workflows/cassam-modernization.yml)

> **Branch**: `main` (initial bootstrap) · **Tracker**: `feature/cassam-modernization` · **First PR**: `feature/cassam-modernization/pr-01-bootstrap-foundation`

Cassam is a Colombian point-of-sale (POS) / inventory / invoicing desktop application
that has been in production since 2008 (legacy VB.NET 3.5 + MySQL + Subversion).

This repository hosts the **modernization**: a C#/.NET 11 + PostgreSQL 16 rewrite with
DIAN electronic-invoice compliance (DEE POS + FE Venta + XAdES-BES signing +
transmission), multi-tenant cloud SaaS (RLS + Keycloak), offline-first bidirectional
sync, and an Uno Platform 6 cross-platform UI (Windows / Linux / Android / macOS / WASM).

The legacy VB.NET codebase and Subversion checkouts at the workspace root are
**read-only migration references** — they are intentionally excluded from this git
repository (see `.gitignore`) and are NOT modified by this modernization effort.

## Quick path

1. Read `openspec/changes/cassam-modernization/proposal.md` for the modernization intent.
2. Read `openspec/changes/cassam-modernization/design.md` for the architecture decisions.
3. Read `openspec/changes/cassam-modernization/tasks.md` for the 24-PR chain plan.
4. Read `openspec/changes/cassam-modernization/specs/pos-core-modern-stack/spec.md`
   for the foundational domain + persistence contract.

## Repository Layout

The full layout is specified in `openspec/changes/cassam-modernization/design.md` §1.2.
Phase 1 (this PR) creates the foundation only:

```
src/
└── Core/
    ├── Domain/        # Cassam.Core.Domain  — entities, value objects, domain services
    ├── Persistence/   # Cassam.Core.Persistence — EF Core 11 + Npgsql, migrations
    ├── Audit/         # Cassam.Core.Audit — append-only audit log writer (interface in PR 1)
    └── Tenancy/       # Cassam.Core.Tenancy — ITenantContext, RLS session-var setter

tests/
└── Cassam.Core.Tests/ # xUnit + FluentAssertions + Testcontainers.PostgreSQL
```

## Branch Strategy

This modernization uses a **feature-branch-chain** strategy (24 chained PRs):

- `main` — the only branch that merges to production.
- `feature/cassam-modernization` — the **tracker branch** (draft/no-merge).
  Aggregates every child PR in chain order. A `git revert` of this branch from
  `main` cleanly undoes the entire modernization if a regulator requires it.
- `feature/cassam-modernization/pr-NN-<slug>` — one child PR per work unit.
  Each PR targets the previous PR's branch so a reviewer sees only the new delta.

`Cassam ProductShare/` and other legacy SVN folders at the workspace root are
intentionally **not tracked** by this git repository — see `.gitignore`. They
remain on disk as read-only references for migration.

## Build & Test

```powershell
dotnet build Cassam.sln
dotnet test Cassam.sln
```

## Continuous Integration

The [.github/workflows/cassam-modernization.yml](.github/workflows/cassam-modernization.yml)
workflow validates every PR against the per-platform matrix required by
design §14.3:

| Job | OS | Validates |
|-----|----|-----------|
| `backend` | ubuntu-latest · windows-latest · macos-latest | Phase 1 build, EF migration apply, xUnit suite (Testcontainers) |
| `ui-windows` | windows-latest | Windows HAL + UI tests (Windows TFM) |
| `ui-linux` | ubuntu-latest | Linux HAL + UI tests (net10.0) |
| `ui-macos` | macos-latest | macOS HAL + UI tests build |
| `ui-android` | ubuntu-latest | Android HAL (android workload) |
| `ui-wasm` | ubuntu-latest | WebAssembly HAL (wasm-tools workload) |
| `migration-drift` | ubuntu-latest | `dotnet ef migrations script --idempotent` is non-empty |

Branch protection on `feature/cassam-modernization` should require the
`backend` matrix plus the relevant UI job before merge.

## Documentation

- `openspec/changes/cassam-modernization/` — proposal, design, tasks, specs.
- `openspec/sdd-init/Cassam.md` — SDD-init detection report (legacy stack summary).
