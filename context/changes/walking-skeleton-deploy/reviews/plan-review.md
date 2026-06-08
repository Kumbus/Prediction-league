<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Walking-skeleton Azure deploy (F-04)

- **Plan**: `context/changes/walking-skeleton-deploy/plan.md`
- **Mode**: Deep
- **Date**: 2026-06-07
- **Verdict**: REVISE → SOUND (all findings fixed during triage)
- **Findings**: 3 critical, 1 warning, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | WARNING (F4) |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | FAIL (F1, F2, F5) |
| Plan Completeness | WARNING (F3) |

## Grounding

6/6 paths ✓ (Program.cs, DependencyInjection.cs, Functions/Program.cs, Api.csproj, FixtureIngestTimer.cs, deployment-plan.md), config sections ✓ (Cors / ApiFootball / Authentication:Google), migrations ✓ (2 found: `20260530155119_InitialCreate`, `20260607113246_AddFootballIngestModel`), brief↔plan ✓.

## Findings

### F1 — Migrate job has no Azure credential for the firewall step

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 4 §2 (migrate job) + §1 (secrets) + plan-brief auth decision
- **Detail**: The migrate job runs `az sql server firewall-rule create/delete` (plan.md:225,232-237), which needs an authenticated `az` session (SP or OIDC). The plan's secret list carried only publish profiles + `AZURE_SQL_CONNECTION` + server/rg names. Publish profiles authenticate `azure/webapps-deploy` only — not `az`. The "Allow Azure services" 0.0.0.0 rule does not cover GitHub-hosted runners (external IPs), which is why the per-run rule exists. Line 225's "prefer direct sqlcmd to avoid an extra principal" was therefore false — the firewall bracket still needs a principal. As written the job cannot authenticate `az` and fails. Also collides with the brief's "Publish-profile secret / no AAD app needed" decision.
- **Fix A ⭐ Recommended**: Add a resource-group-scoped service principal + `azure/login`
  - Strength: Unblocks both firewall steps; `az sql` works as written; smallest deviation from existing job shape.
  - Tradeoff: Reintroduces one scoped AAD app the brief hoped to avoid (cheaper than the explicitly deferred OIDC).
  - Confidence: HIGH — standard `azure/login` pattern.
  - Blind spot: SP secret is long-lived (same rotation note as the publish profile).
- **Fix B**: Drop dynamic firewall; pre-create a standing CI rule for GitHub IP ranges
  - Strength: No `az` auth in CI — sqlcmd-only, matches the author's original intent.
  - Tradeoff: GitHub egress range is large/changing; broad standing exposure on a SQL admin login.
  - Confidence: MED — GitHub publishes ranges but they drift.
  - Blind spot: Range churn could silently break migrations later.
- **Decision**: FIXED via Fix A — `AZURE_CREDENTIALS` (rg-scoped SP) added to Phase 4 §1 secrets with rationale; migrate job uses `azure/login@v2`; snippet gained the login step; auth-decision contradiction recorded in-plan.

### F2 — EF script generation will throw on the DefaultConnection guard

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 4 §2 build job (`dotnet ef migrations script`)
- **Detail**: No `IDesignTimeDbContextFactory` exists (verified — none in `src/server`). `dotnet ef migrations script` builds the host via `Program.cs`, which calls `AddInfrastructure` (Program.cs:15); that method throws `InvalidOperationException` when `DefaultConnection` is absent (DependencyInjection.cs:25-28) during service registration, before EF reads the model. The build job has no connection string (correctly), so script generation fails. Generation only parses the string; it never connects.
- **Fix**: In the build job, set a dummy `ConnectionStrings__DefaultConnection=Server=_;Database=_;` env on the script step.
- **Decision**: FIXED — dummy connection-string env added to the build job's `dotnet ef` step, with the design-time-factory reason recorded in-plan.

### F3 — sqlcmd `-G` forces Azure AD auth but creds are SQL auth

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 4 §2 snippet (plan.md:234)
- **Detail**: `sqlcmd -S ... -G -U $SQL_USER -P "$SQL_PASS"` — `-G` selects Azure Active Directory authentication, but the plan provisions a SQL-auth admin login (Phase 2 §1), not an AAD principal. `-G` + a SQL login → login failure. The decision table commits to SQL-auth connection string.
- **Fix**: Drop `-G`; use plain SQL auth (`-U`/`-P` only).
- **Decision**: FIXED — `-G` removed from snippet and prose (resolved alongside the F1 snippet edit); both now specify SQL auth.

### F4 — Google login (F-02) is dead in prod; smoke checks don't catch it

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Phase 3 §1 (API app settings) + Phase 5 smoke checks
- **Detail**: Phase 3 injected ConnectionStrings, Cors origin, and ApiFootball key, but not `Authentication__Google__ClientId/Secret`. The Google scheme registers only when both are present (DependencyInjection.cs:68-79); absent → Google login silently off. The prod redirect URI `https://<api-host>/signin-google` also needs registering in Google Cloud. Phase 5 only checked anonymous→401, so it passes green while a delivered F-02 feature is non-functional. Local email/password still works. The omission is asymmetric vs. the included ApiFootball key — likely oversight, not intent.
- **Fix A ⭐ Recommended**: Wire Google in prod — add client settings to Phase 3, register the redirect URI, add a Phase 5 login smoke check.
- **Fix B**: Declare Google login out of scope under "What We're NOT Doing" with a revisit trigger.
- **Decision**: FIXED via Fix A — `Authentication__Google__ClientId/Secret` added to Phase 3 §1 + prod redirect-URI human step; Phase 3 and Phase 5 verification + Progress (5.4 automated, 5.5 manual) updated.

### F5 — migrate runs parallel with deploy (needs: build only)

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 4 §2 job graph
- **Detail**: migrate and deploy-api both `needs: build`, so they run concurrently; ordering of "new code boots" vs "schema migrated" is unspecified. Harmless for this first deploy (empty DB, additive migrations), but worth pinning before the pattern carries into riskier migrations.
- **Fix**: Make deploy `needs: [build, migrate]` so schema leads code (expand/contract ordering).
- **Decision**: FIXED — `deploy-api` now `needs: [build, migrate]`.

## Triage Summary

- **Fixed**: F1 (Fix A), F2, F3, F4 (Fix A), F5 — all 5
- **Verdict after fixes**: REVISE → SOUND
