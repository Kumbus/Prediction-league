<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: OAuth Sign-in Scaffold (F-02)

- **Plan**: context/changes/auth-oauth-scaffold/plan.md
- **Scope**: All 3 phases
- **Date**: 2026-06-07
- **Verdict**: NEEDS ATTENTION (all findings resolved during triage)
- **Findings**: 0 critical, 2 warnings, 1 observation

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

### F1 — Google login auto-links to existing local account by email

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: AuthController.cs:132-148
- **Detail**: ExternalCallback did FindByEmailAsync and, on a pre-existing local account, auto-linked Google and signed in. Local register has no email-confirmation flow → unverified local side. Takeover path: attacker pre-registers victim@gmail locally; victim signs in via Google and lands in attacker's account. Email-linking was also beyond the plan contract ("otherwise create an ApplicationUser").
- **Fix A ⭐ Recommended**: Don't auto-link — on email collision redirect with error, defer linking to an authenticated session.
  - Strength: Removes the takeover class; matches "no email infra yet" scope.
  - Tradeoff: Local-then-Google same-email user hits friction instead of a merge.
  - Confidence: HIGH — standard auto-link hardening.
  - Blind spot: S-01 UI may assume seamless merge.
- **Fix B**: Gate linking on EmailConfirmed == true (dead until an email flow exists).
- **Decision**: FIXED via Fix A — collision now returns `?error=account_exists`, create-only otherwise.

### F2 — CORS allowed-origins read raw in 2 places, no options class

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Program.cs:24, AuthController.cs:158
- **Detail**: "Cors:AllowedOrigins" read with GetSection().Get<string[]>() in both Program.cs (CORS policy) and ResolveReturnUrl (open-redirect guard). Repo convention is bind-once typed options (ApiFootballOptions, GoogleAuthOptions); the two reads already differed in trailing-slash handling.
- **Fix**: Add SpaCorsOptions (SectionName "Cors"), bind once, inject IOptions into AuthController; reuse the bound value in Program.cs.
- **Decision**: FIXED — added `PredictionLeague.Api/Configuration/SpaCorsOptions.cs`; both call sites now use it.

### F3 — Google registered conditionally vs plan's "lazy" wording

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: DependencyInjection.cs:68-79
- **Detail**: Impl gates AddGoogle behind a creds-present check (correct — OAuthOptions.Validate() throws on empty ClientId). Benign, well-commented deviation. Edge: with no creds, GET /api/auth/login/google challenged an unregistered scheme → 500 instead of a graceful response.
- **Fix**: Guard LoginGoogle via IAuthenticationSchemeProvider; return 501 when the Google scheme isn't registered.
- **Decision**: FIXED — LoginGoogle now returns 501 Not Implemented when Google sign-in is unconfigured.

## Notes

Final `dotnet build src/server/prediction-league.slnx` — succeeded, 0 warnings, 0 errors after all three fixes.
