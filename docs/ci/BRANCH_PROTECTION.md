# GitHub Branch Protection Setup

> Status: **DOCUMENTED, NOT YET APPLIED** — requires a remote GitHub repository.

This document captures the exact branch-protection settings required for the `cassam-modernization` change. Apply them **once the repository is published to GitHub** and **before any PR merges to `feature/cassam-modernization`**.

## Why this matters

Phase 2 ships hardware-integrated code (barcode scanners, ESC/POS printers, BT pairing, multi-platform UI). A bad merge without CI green could break the cashier flow for paying customers. Branch protection with required status checks is the safety net.

## Prerequisites

1. The repository is published on GitHub (currently local-only at `C:\D\Cassam`)
2. The `feature/cassam-modernization` branch is pushed to the remote
3. The CI workflow at `.github/workflows/cassam-modernization.yml` has run at least once so GitHub knows the job names
4. The user has admin access to the GitHub repo

## Required status checks

After the first CI run completes, the following jobs will be available as required checks. Configure **all 7** in branch protection:

| Job name | Purpose |
|---|---|
| `backend` | Phase 1 backend build + EF migrations + Testcontainers tests (3-OS matrix) |
| `ui-windows` | Windows HAL build + Windows-targeted UI tests |
| `ui-linux` | Linux HAL build + UI tests (with libevdev/libx11/libei) |
| `ui-macos` | macOS HAL build (maccatalyst TFM) |
| `ui-android` | Android HAL build (with android workload) |
| `ui-wasm` | WebAssembly HAL build (with wasm-tools workload) |
| `migration-drift` | EF migration idempotency check |

## Settings to apply (GitHub UI)

Navigate to: **GitHub.com → `<OWNER>/<REPO>` → Settings → Branches → Add rule**

**Branch name pattern**: `feature/cassam-modernization`

**Required settings**:
- ☑ Require a pull request before merging
  - ☑ Require approvals: **0** (solo el owner trabaja en esto por ahora)
  - ☑ Dismiss stale pull request approvals when new commits are pushed
  - ☑ Require review from Code Owners (only after CODEOWNERS file is added)
- ☑ Require status checks to pass before merging
  - ☑ Require branches to be up to date before merging
  - **Search and select all 7 checks above**
- ☑ Require conversation resolution before merging
- ☑ Require signed commits (optional but recommended for compliance)
- ☑ Require linear history (recommended — keeps `git log --graph` clean)
- ☐ Include administrators (recommended ON — enforces the rules on admins too)
- ☑ Allow force pushes: **disabled**
- ☑ Allow deletions: **disabled**
- ☑ Block creations: **disabled** (we want feature branches)

## Settings to apply (GitHub CLI — if `gh` is installed)

After pushing to GitHub and the first CI run completes:

```bash
# Install gh CLI: https://cli.github.com
gh auth login

# Verify the repo remote
gh repo view

# Set branch protection (replace OWNER/REPO with actual values)
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  /repos/OWNER/REPO/branches/feature/cassam-modernization/protection \
  -f required_status_checks='{"strict":true,"contexts":["backend","ui-windows","ui-linux","ui-macos","ui-android","ui-wasm","migration-drift"]}' \
  -F enforce_admins=true \
  -F required_linear_history=true \
  -F allow_force_pushes=false \
  -F allow_deletions=false \
  -F block_creations=false \
  -F required_conversation_resolution=true
```

## Verification

After applying, verify with:

```bash
gh api /repos/OWNER/REPO/branches/feature/cassam-modernization/protection | jq
```

Expected output includes:
- `required_status_checks.strict: true`
- `required_status_checks.contexts`: 7 entries
- `enforce_admins.enabled: true`
- `allow_force_pushes.enabled: false`
- `allow_deletions.enabled: false`

## Related

- `.github/workflows/cassam-modernization.yml` — the CI workflow that produces the 7 status checks
- `README.md` — has the CI status badge that links to the workflow runs
- Phase 2 design §14.3 — the CI matrix strategy this implements

## Open question (for sponsor/ops)

When the repo is published, what is the OWNER/REPO slug? Update the README badge URL and this document.