<!-- PLAN-REVIEW-REPORT -->
# Plan Review: User Sign-in (S-01) Implementation Plan

- **Plan**: context/changes/user-sign-in/plan.md
- **Mode**: Deep
- **Date**: 2026-06-08
- **Verdict**: REVISE
- **Findings**: 2 critical, 2 warnings, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | FAIL |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

7/7 paths ✓ (AuthController.cs:18/125/172, appsettings.json Cors:AllowedOrigins, launchSettings :7182, vite.config.ts, package.json, ui/ primitives), 5/5 symbols ✓ (SameSite=None+Secure, ExternalCallback error codes, ResolveReturnUrl, AppendError, AuthUser shape), brief↔plan ✓.

## Findings

### F1 — Google `?error=` lands on /app, never /sign-in

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: End-State Alignment
- **Location**: Phase 3 §1 (GoogleSignInButton) + Critical Implementation Details + Phase 3 §2 (SignInPage `?error=` reader)
- **Detail**: Plan passes `returnUrl=${origin}/app` to `/api/auth/login/google`. On failure, `ExternalCallback` appends `?error=<code>` to that target (AuthController.cs:131,142,150,160,164), so the browser lands on `/app?error=...` while still anonymous. `RequireAuth` then `<Navigate to="/sign-in" state={{from}} replace />` — which does NOT carry the query string. User arrives at `/sign-in` with no `?error=` to render. Desired End State, Phase 3 §2 `useSearchParams` consumer, and Manual SC bullets 3.6/3.7 all silently fail.
- **Fix ⭐**: Pass `${origin}/sign-in` as returnUrl (not `/app`). Success → cookie set → SignInPage's authenticated-redirect (Phase 2 §4) bounces to /app. Failure → /sign-in?error=<code> renders alert directly. Update Critical Implementation Details bullet to reflect /sign-in target.
  - Strength: Single endpoint owns both branches; no query-string forwarding required.
  - Tradeoff: Tiny double-hop on success; one render with no flash (redirect at mount).
  - Confidence: HIGH — Cors:AllowedOrigins already allows the SPA origin (appsettings.json:20); ResolveReturnUrl matches by origin not path (AuthController.cs:185-187).
  - Blind spot: SignInPage authenticated-redirect runs after the /me probe resolves — see F4.
- **Decision**: FIXED

### F2 — Playwright config requires `@playwright/test`, not installed

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3 §3 (playwright.config.ts contract)
- **Detail**: devDeps has `playwright@^1.60.0` (the library), not `@playwright/test` (the runner). `defineConfig` and the `playwright test` runner both live in `@playwright/test`. SC bullet 3.3 will fail.
- **Fix**: Add `@playwright/test@^1` to devDependencies in Phase 3 §3 contract. Optionally drop the redundant bare `playwright` dep (`@playwright/test` brings it transitively).
- **Decision**: FIXED

### F3 — e2e fetch resolves to SPA origin, not API origin

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 3 §3 (auth.spec.ts contract)
- **Detail**: Contract: "asserts that an `apiFetch`-equivalent `fetch('/api/auth/me', { credentials: 'include' })` returns 401". With Playwright `baseURL=https://localhost:5173`, a relative `fetch` from `page.evaluate` resolves to the SPA origin (no /api/auth/me there) — test sees a Vite 404, not the intended 401.
- **Fix**: Use the API origin explicitly. Either Playwright's `request` fixture (`await request.get(`${API}/api/auth/me`)`) or `page.evaluate(() => fetch('https://localhost:7182/api/auth/me', {credentials:'include'}).then(r=>r.status))`. Wire `API` via env or a constant in the spec.
  - Strength: Test actually exercises the API surface it claims to.
  - Tradeoff: One more env wiring step (or a hardcoded URL).
  - Confidence: HIGH — Playwright `request` is the documented path for API assertions alongside browser steps.
  - Blind spot: Cookies vs request-context isolation — page.evaluate path is simplest.
- **Decision**: FIXED

### F4 — SignInPage authenticated-redirect runs before /me probe resolves

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §4 (SignInPage contract)
- **Detail**: "On mount inspect `useAuth().status === 'authenticated'` → `<Navigate to='/app' replace />`". On first mount the status is 'loading'; the form renders briefly until the probe resolves. If F1 lands, the same SignInPage also handles Google success via this branch — the loading-window flash becomes more user-visible.
- **Fix**: Treat 'loading' like RequireAuth does — render null (or neutral placeholder) until status ≠ 'loading'. Only render tabs when status === 'anonymous'. Add a 'loading' arm to the contract.
  - Strength: Removes flash; aligns with the "no flash" rule from Critical Implementation Details.
  - Tradeoff: None significant.
  - Confidence: HIGH — mirrors the RequireAuth pattern from Phase 1 §6.
  - Blind spot: None significant.
- **Decision**: FIXED
