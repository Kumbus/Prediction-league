<!-- PLAN-REVIEW-REPORT -->
# Plan Review: OAuth Sign-in Scaffold (F-02)

- **Plan**: context/changes/auth-oauth-scaffold/plan.md
- **Mode**: Deep
- **Date**: 2026-06-07
- **Verdict**: SOUND (one warning skipped, carried to S-01)
- **Findings**: 0 critical, 1 warning, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | PASS |

## Grounding

8/8 paths ✓, symbols ✓ (AppDbContext `IdentityDbContext<ApplicationUser,IdentityRole<Guid>,Guid>`, `AddFootballIngest`, `IsGlobalAdmin`, `UseAuthorization`@Program.cs:50), brief↔plan ✓. Verified correct: pipeline-order claim, AddIdentity-not-AddDefaultIdentity, .NET 10 401/403 API cookie behavior, no-migration claim (AspNet* tables exist in InitialCreate).

## Findings

### F1 — Load-bearing cross-origin cookie config never verified

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 #4 (CORS + cookie) / Critical Details / all Manual Verification blocks
- **Detail**: The riskiest config — credentialed CORS + cookie `SameSite=None;Secure` (Critical Details, named the Phase 1 key risk in the brief) — is never exercised by a success criterion. F-02 ships no SPA, so every manual check hits the API same-origin via the `.http` file or browser. Origin mismatch, `AllowCredentials` vs `AllowAnyOrigin` conflict, or wrong `SameSite` ships green and only breaks in S-01. The plan flags the risk in Open Risks but never converts it to a check.
- **Fix**: Add one achievable Phase 1 manual step — curl/Invoke-WebRequest with `Origin: https://localhost:5173` against a route, assert response carries `Access-Control-Allow-Origin` + `Access-Control-Allow-Credentials: true`, and login's `Set-Cookie` shows `SameSite=None; Secure`. Verifies the load-bearing config without an SPA.
  - Strength: Catches the exact class of bug that would otherwise surface only in S-01, at near-zero cost.
  - Tradeoff: Header assertion ≠ full browser round-trip (no real XHR cookie send), so necessary-not-sufficient.
  - Confidence: HIGH — preflight/header assertion is standard, no new infra.
  - Blind spot: Real browser `SameSite=None` send still unverified until a same-site https context exists.
- **Decision**: SKIPPED — carried into S-01 verification.

### F2 — Lesson-fix premise is stale; #5 and #7 are no-ops

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness (internal contradiction)
- **Location**: Current State line 15; Phase 1 #5 + #7
- **Detail**: DisplayName max-length lesson already satisfied. `ApplicationUserConfiguration.cs:11` already has `.IsRequired().HasMaxLength(256)`; `InitialCreate` migration line 33 already emits `nvarchar(256) maxLength:256`; model snapshot matches. No `nvarchar(max)` drift. Current State line 15 ("previously slipped to nvarchar(max) … Verify/fix") is stale; #7's conditional migration never fires.
- **Fix**: Reword #5 to verification-only ("confirm DisplayName already maps nvarchar(256) — no change expected") and drop #7's migration expectation.
- **Decision**: FIXED — reworded Current State + Phase 1 #5/#7 to verify-only.

### F3 — CORS registered inside Infrastructure auth extension

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 #4 (AddAuthenticationAndIdentity)
- **Detail**: Plan folds the CORS policy into Infrastructure's `AddAuthenticationAndIdentity`. CORS is a host/pipeline concern; `UseCors` already lives in `Program.cs` (Api). Bundling `AddCors` into the data/identity layer is a layering smell. Framework types resolve fine — Infra pulls `Microsoft.AspNetCore.App` transitively via `Identity.EntityFrameworkCore` — so this is layering, not feasibility.
- **Fix**: Register the CORS policy in `Program.cs` next to `UseCors`; keep Identity/Google/policy in the Infrastructure extension.
- **Decision**: FIXED — moved CORS registration to Phase 1 #6 (Program.cs); removed from #4.

### F4 — appsettings CORS origin + returnUrl validation imprecision

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 #8; Phase 3 #2
- **Detail**: (a) #8 hardcodes CORS origin `https://localhost:5173`, but Vite's dev default is `http://localhost:5173` (no https server config in `vite.config.ts`) — origin must match exactly or S-01's SPA gets CORS-blocked. (b) #2 says validate returnUrl against "Cors:AllowedOrigins or Url.IsLocalUrl" — but the SPA return target is a different origin, which `Url.IsLocalUrl` rejects by design; only the allowed-origins check applies.
- **Fix**: Align dev origin (note http+https or pick one) and drop the misleading "or Url.IsLocalUrl" — validate returnUrl solely against the configured SPA origin allowlist.
- **Decision**: SKIPPED.
