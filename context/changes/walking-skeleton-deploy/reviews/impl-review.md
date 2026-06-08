<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Walking-skeleton Azure deploy (F-04)

- **Plan**: context/changes/walking-skeleton-deploy/plan.md
- **Scope**: All phases (1–5)
- **Date**: 2026-06-08
- **Verdict**: APPROVED (with triaged warnings)
- **Findings**: 0 critical · 1 warning · 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS (N/A — sibling workflow is SWA auto-gen) |
| Success Criteria | PASS |

## Findings

### F1 — No concurrency group → racing migrate runs

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (reliability)
- **Location**: .github/workflows/deploy-backend.yml:11
- **Detail**: Workflow had no `concurrency:` group. Concurrent pushes to main could run two migrate jobs in parallel; sqlcmd contention on Azure SQL Basic + racing wwwroot writes on the API. Idempotent script blunts it but doesn't eliminate it.
- **Fix**: Added top-level `concurrency: { group: deploy-backend-main, cancel-in-progress: false }`. cancel-in-progress:false avoids stranding the transient firewall rule mid-run.
- **Decision**: FIXED

### F2 — No job-level timeouts

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (reliability)
- **Location**: .github/workflows/deploy-backend.yml (build/migrate/deploy-api/deploy-func)
- **Detail**: No `timeout-minutes` on any job. GH default is 6h. Hung sqlcmd / stuck deploy silently burns minutes. Current runs ~3 min each.
- **Fix**: Added `timeout-minutes: 10` to build/deploy-api/deploy-func; `timeout-minutes: 20` to migrate.
- **Decision**: FIXED

### F3 — Stale AZURE_API_PUBLISH_PROFILE GH secret

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (secret hygiene)
- **Location**: change.md:109 (audit trail)
- **Detail**: `AZURE_API_PUBLISH_PROFILE` kept in GH repo secrets though unused after SP-auth switch in `ee5dbd3`. Dead creds are still creds; presence invites a future operator to wire publish-profile back, contradicting the lessons.md rule "Don't plan for publish-profile auth on App Service in this tenant".
- **Fix**: `gh secret delete AZURE_API_PUBLISH_PROFILE`; change.md audit trail updated to record deletion.
- **Decision**: FIXED
