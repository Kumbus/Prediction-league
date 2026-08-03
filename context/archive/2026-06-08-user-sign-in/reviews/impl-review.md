<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: User Sign-in (S-01)

- **Plan**: context/changes/user-sign-in/plan.md
- **Scope**: Full plan (Phases 1-3)
- **Date**: 2026-06-08
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Open-redirect surface on post-login `from` state

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/client/src/auth/LoginForm.tsx:39-40
- **Detail**: `location.state.from.pathname` flows into `navigate(from ?? "/app")` without validating shape. A crafted state like `"//evil.com/x"` would pass to react-router. Same-origin SPA limits real impact, but hardening is one line.
- **Fix**: Replace `navigate(from ?? "/app", ...)` with `navigate(from && from.startsWith("/") && !from.startsWith("//") ? from : "/app", { replace: true })`.
- **Decision**: FIXED

### F2 — AuthContext maps network/5xx failures to "anonymous"

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Reliability)
- **Location**: src/client/src/auth/AuthContext.tsx:17-23
- **Detail**: `/api/auth/me` probe treats any non-401 ApiError as `anonymous`. API down, CORS misconfig, or transient 500 silently signs the user out and bounces to /sign-in.
- **Fix A ⭐ Recommended**: Add an `"error"` status; render a neutral retry surface in RequireAuth instead of redirecting.
  - Strength: Preserves last-known intent; no spurious sign-out flash.
  - Tradeoff: Touches AuthContext + RequireAuth + SignInPage branch.
  - Confidence: HIGH — pattern is conventional in TanStack-style probes.
  - Blind spot: SSR/Strict-mode double-mount paths unchecked.
- **Fix B**: Keep `anonymous` mapping but show a top-bar toast on non-401.
  - Strength: Smaller diff; preserves current redirect flow.
  - Tradeoff: Still logs the user out on transient API blips.
  - Confidence: MEDIUM — depends on a toast primitive (not vendored yet).
  - Blind spot: shadcn `sonner`/`toast` would need to be added.
- **Decision**: FIXED via Fix A

### F3 — Unguarded JSON.parse in apiFetch breaks ApiError contract

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Correctness)
- **Location**: src/client/src/lib/api.ts:69-71
- **Detail**: Successful 2xx response with non-JSON or empty-but-whitespace body throws raw `SyntaxError`. Consumers (LoginForm, RegisterForm, SignOutButton) only catch `ApiError`, so error surface skips friendly mapping.
- **Fix**: Wrap `JSON.parse(text)` in try/catch; on failure throw `new ApiError(status, "Unexpected non-JSON response")`.
- **Decision**: FIXED

### F4 — GoogleSignInButton has no VITE_API_BASE_URL guard

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality (Correctness)
- **Location**: src/client/src/auth/GoogleSignInButton.tsx:33-36
- **Detail**: Missing env var → redirect to `"undefined/api/auth/..."`.
- **Fix**: Centralize base-URL access in @/lib/api with a load-time assert; reuse from the button.
- **Decision**: FIXED

### F5 — form.tsx / tabs.tsx mix component + non-component exports

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Pattern Consistency
- **Location**: src/client/src/components/ui/form.tsx, tabs.tsx
- **Detail**: `useFormField` + `tabsListVariants` CVA share a file with components. `react-refresh/only-export-components` rule territory. AuthContext was already split for this reason.
- **Fix**: Disable rule per-file (shadcn vendored convention) or split hooks/variants into siblings.
- **Decision**: SKIPPED

### F6 — Inconsistent Radix import style

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Pattern Consistency
- **Location**: src/client/src/components/ui/form.tsx:2, label.tsx, tabs.tsx vs button.tsx:2
- **Detail**: Umbrella `radix-ui` vs scoped `@radix-ui/react-slot`. Two coexisting flavors.
- **Fix**: Align primitives to the umbrella `radix-ui` import (current shadcn default).
- **Decision**: FIXED

### F7 — SignOutButton has no in-flight guard

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality (Reliability)
- **Location**: src/client/src/auth/SignOutButton.tsx:9-12
- **Detail**: Async onClick without disabled state; repeated clicks fire concurrent POST /logout + navigate races.
- **Fix**: Track local `pending` state; disable button while in flight.
- **Decision**: FIXED

### F8 — AuthContext probe has no AbortController + flash-of-blank loading

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality (Reliability) + UX
- **Location**: src/client/src/auth/AuthContext.tsx:38-41, src/client/src/routes/RequireAuth.tsx:8, src/client/src/routes/SignInPage.tsx:15
- **Detail**: Probe lacks AbortController (stale setState under StrictMode/unmount). Both gates render `null` during loading → blank flash on cold load.
- **Fix**: Pass AbortController.signal through apiFetch; render lightweight skeleton/spinner instead of `null`.
- **Decision**: FIXED
