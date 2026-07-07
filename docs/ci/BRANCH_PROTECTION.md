# GitHub Branch Protection Setup

> Status: ✅ **FULLY APPLIED** to `feature/cassam-modernization` on 2026-06-26 + updated 2026-07-07 with the `UI xUnit Suite (Mock-based)` check from PR 9.6. **All 8 required status checks are active.**

This document captures the exact branch-protection settings required for the `cassam-modernization` change. They are now active on `https://github.com/oldkami/Cassam-Open` for the `feature/cassam-modernization` branch.

## Current configuration (LIVE)

8 required status checks:
- `Phase 1 Backend (ubuntu-latest)`
- `UI Linux HAL`
- `UI Windows HAL`
- `UI Android HAL`
- `UI macOS HAL`
- `UI WebAssembly HAL`
- `UI xUnit Suite (Mock-based)` (added with PR 9.6)
- `EF Migration Drift Check`

Other settings: strict (branches must be up to date), enforce admins, required linear history, no force pushes, no deletions, restrictions: null (user repo).

## Why this matters

Phase 2 ships hardware-integrated code (barcode scanners, ESC/POS printers, BT pairing, multi-platform UI). A bad merge without CI green could break the cashier flow for paying customers. Branch protection with required status checks is the safety net.

## How it was applied (historical record)

```powershell
# 1. Generate JSON body
$protection = @{
  required_status_checks = @{ strict = $true; contexts = @(8 check names) }
  required_pull_request_reviews = @{ dismiss_stale_reviews = $true; require_code_owner_reviews = $false; required_approving_review_count = 0 }
  restrictions = $null
  enforce_admins = $true
  required_linear_history = $true
  allow_force_pushes = $false
  allow_deletions = $false
  required_conversation_resolution = $false
  block_creations = $false
  lock_branch = $false
}
$json = $protection | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText("$env:TEMP\bp.json", $json, [System.Text.UTF8Encoding]::new($false))

# 2. PUT to GitHub API
gh api --method PUT -H "Accept: application/vnd.github+json" `
  repos/oldkami/Cassam-Open/branches/feature/cassam-modernization/protection `
  --input "$env:TEMP\bp.json"
```

### Common gotchas

- **422 "restrictions weren't supplied"**: must include `restrictions: null` for user repos (not org)
- **422 "Only organization repositories can have users and team restrictions"**: don't put users/teams in restrictions for user repos
- **400 "Problems parsing JSON"**: BOM in JSON file — write with `UTF8Encoding.new($false)` (no BOM)
- **Push rejected with "protected branch hook declined"**: new commit needs CI to pass before push. Open a PR instead.

## Original prerequisites (satisfied)

1. ✅ The repository is published on GitHub at `https://github.com/oldkami/Cassam-Open`
2. ✅ The `feature/cassam-modernization` branch is pushed to the remote
3. ✅ The CI workflow at `.github/workflows/cassam-modernization.yml` has run multiple times; GitHub knows the 8 job names
4. ✅ The user (`oldkami`) has admin access to the GitHub repo

## Required status checks

After the first CI run completes, the following jobs will be available as required checks. Configure **all 8** in branch protection:

| Job name | Purpose |
|---|---|
| `backend` | Phase 1 backend build + EF migrations + Testcontainers tests (3-OS matrix) |
| `ui-windows` | Windows HAL build + Windows HAL contract tests (per-platform test project) |
| `ui-linux` | Linux HAL build + Linux HAL contract tests (per-platform test project) |
| `ui-macos` | macOS HAL build (maccatalyst TFM) |
| `ui-android` | Android HAL build (with android workload) |
| `ui-wasm` | WebAssembly HAL build (with wasm-tools workload) |
| `migration-drift` | EF migration idempotency check |
| `ui-tests` | Cassam.Ui.Tests xUnit suite (Mock-based, platform-neutral, ubuntu-latest). Added in PR 9.6 after the test project was split into a platform-neutral test project + per-platform test projects so cross-platform restore no longer drags in Windows / Android / WASM workloads. |

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
  - **Search and select all 8 checks above**
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
  -f required_status_checks='{"strict":true,"contexts":["backend","ui-windows","ui-linux","ui-macos","ui-android","ui-wasm","migration-drift","ui-tests"]}' \
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
- `required_status_checks.contexts`: 8 entries
- `enforce_admins.enabled: true`
- `allow_force_pushes.enabled: false`
- `allow_deletions.enabled: false`

## Related

- `.github/workflows/cassam-modernization.yml` — the CI workflow that produces the 8 status checks
- `README.md` — has the CI status badge that links to the workflow runs
- Phase 2 design §14.3 — the CI matrix strategy this implements

## Open question (for sponsor/ops)

When the repo is published, what is the OWNER/REPO slug? Update the README badge URL and this document.