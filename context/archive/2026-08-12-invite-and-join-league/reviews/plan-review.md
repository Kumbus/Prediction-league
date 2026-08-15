<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Invite and join a league (S-05)

- **Plan**: `context/changes/invite-and-join-league/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-12
- **Verdict**: REVISE → SOUND after triage
- **Findings**: 1 critical, 4 warnings, 1 observation

## Verdicts

| Dimension | Verdict | After triage |
|-----------|---------|--------------|
| End-State Alignment | WARNING | PASS |
| Lean Execution | PASS | PASS |
| Architectural Fitness | FAIL | PASS |
| Blind Spots | WARNING | WARNING (F4 accepted) |
| Plan Completeness | WARNING | PASS |

## Grounding

16/16 existing paths ✓ (2 new files correctly absent), 8/8 symbols ✓, brief↔plan ✓.
Nit: the plan cites `RequireAuth.tsx:20` without a directory — the file is
`src/client/src/routes/RequireAuth.tsx`; content matches.

## Findings

### F1 — Injecting IInviteCodeGenerator into LeagueRepository closes a DI cycle

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 §5 — boundary-debt payoff
- **Detail**: `RandomInviteCodeGenerator` already depends on `ILeagueRepository` for its existence probe (`RandomInviteCodeGenerator.cs:18-23`), and both are `AddScoped` (`DependencyInjection.cs:39`, `:49`). Constructor-injecting the generator into `LeagueRepository` closes the cycle `ILeagueRepository → IInviteCodeGenerator → ILeagueRepository`; .NET DI throws at resolve time, so every league route 500s while the build stays green and Phase 1's automated criteria all pass.
- **Fix A ⭐ Recommended**: `CreateAsync(League, Func<CancellationToken, Task<string>> nextCode, ct)` — the controller passes `_inviteCodes.GenerateAsync`.
  - Strength: Settles `lessons.md:25-30` exactly as intended, with no constructor edge and no new type.
  - Tradeoff: Callback-shaped repository method, unusual for this codebase.
  - Confidence: HIGH — cycle verified in code; the delegate removes the only edge.
  - Blind spot: None significant.
- **Fix B**: Application-layer `LeagueCreationService` depending on both.
  - Strength: Textbook layering; repository stays a pure persistence type.
  - Tradeoff: First Application service in a controller→repository codebase.
  - Confidence: MEDIUM — correct but larger than the debt it pays off.
  - Blind spot: Whether S-06/S-07 would follow the pattern is unexamined.
- **Decision**: FIXED — via Fix A. Plan now specifies the delegate parameter, states the cycle
  explicitly as a "do not do this", and adds manual criterion 1.8 (`GET /api/leagues` returns
  200/401, not 500) because the build cannot prove DI resolves.

### F2 — Repointing the Google returnUrl silently swallows external-login errors

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 3 §3 — deep-link survival through sign-in
- **Detail**: `ExternalCallback` reports failures by appending `?error=` to `returnUrl` (`AuthController.cs:139`, `155`, `163`, `173`, `177`), and only `SignInPage` renders those codes (`SignInPage.tsx:13`). Pointing `returnUrl` at `/app/leagues/join/:code` drops an unauthenticated user on a `RequireAuth` route, bounces them to `/sign-in`, and strips the error — a silent failure on every Google path, notably `account_exists`. Separately, `RequireAuth.tsx:20` passes `from` as a Location object, so the plan's literal `${window.location.origin}${from}` yields `[object Object]`.
- **Fix**: `returnUrl` stays `/sign-in?returnTo=<encoded path>`; `SignInPage` prefers `location.state?.from`, falls back to a `returnTo` validated client-side as a same-origin relative path (starts with `/`, not `//` or `/\`).
  - Strength: Errors keep landing where they render; one mechanism serves both sign-in paths; still no open redirect.
  - Tradeoff: Contradicts the plan's "never a value from the query string" rule — unimplementable for Google anyway, since router state cannot survive a full-page round trip.
  - Confidence: HIGH — error redirect targets read directly from `AuthController`.
  - Blind spot: Whether the join page should also render `messageForExternalError` is left open.
- **Decision**: FIXED — Phase 3 §3 rewritten (path is `from.pathname + from.search`, `returnTo`
  carrier, client-side validation) and manual criterion 3.9 added for the failing-Google-sign-in
  message.

### F3 — The join/transfer detail response reads data the plan's queries don't fetch

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: End-State Alignment
- **Location**: Phase 1 §4 / Phase 2 §1, §4
- **Detail**: `GetByInviteCodeAsync` is specified as "tracked, includes `Memberships`" with no `ScoringRules`, but `POST /join` returns the full `LeagueDetailResponse` and `ToDetailResponse` enumerates `league.ScoringRules` (`LeaguesController.cs:281`). Lazy loading is off (`DependencyInjection.cs:36`), so join would ship `scoringRules: []` silently. Same class of gap for `Members` on the `POST` and `PUT /organizer` responses — the plan never says where that list comes from.
- **Fix**: Add `.Include(l => l.ScoringRules)` to the contract, and state that all five routes fill `Members` from `ListMembersAsync` called after their own save.
- **Decision**: FIXED.

### F4 — "No test suite exists" is false for the client, and the existing e2e covers what Phase 3 edits

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: "What We're NOT Doing" / "Testing Strategy" / Phase 3 verification
- **Detail**: `src/client/tests/e2e/auth.spec.ts` exists and runs via `npm run e2e` (documented in `src/client/AGENTS.md`). It asserts the post-register redirect lands on `/app` (`auth.spec.ts:18`) — the exact `SignInPage` navigation Phase 3 rewrites. The plan states no suite exists and never runs it.
- **Fix**: Correct the claim (server: none; client: Playwright smoke) and add `cd src/client && npm run e2e` to Phase 3's automated verification and Progress.
- **Decision**: ACCEPTED — handled during implementation.

### F5 — JoinedUtc's migration default is asserted but never specified

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §1-§2, Progress 1.6
- **Detail**: "Existing rows take a migration default (`SYSUTCDATETIME()`)" names an outcome, not a mechanism. `dotnet ef migrations add` on a plain required `DateTimeOffset` scaffolds `defaultValue: 0001-01-01`, backdating every current membership to year 1 — and criterion 1.6 ("non-null `JoinedUtc`") passes anyway, so the check cannot catch the failure it exists for.
- **Fix**: Specify `HasDefaultValueSql("SYSUTCDATETIME()")` in `LeagueMembershipConfiguration`, and restate 1.6 as "a plausible recent timestamp, not `0001-01-01`".
- **Decision**: FIXED.

### F6 — A solo organizer has no exit from their own league

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: End-State Alignment
- **Location**: Phase 2 §2 / Phase 4 §2-§3, "What We're NOT Doing"
- **Detail**: Leave 409s the organizer, transfer needs another member, and deletion was out of scope — so creating a league was permanently undoable, and the "transfer first" copy pointed at a control a solo league never renders.
- **Fix (as directed)**: Leaving as the last member deletes the league.
- **Decision**: FIXED — scope changed on the user's call. `LeaveAsync` removes the league instead of
  the row when it is the only membership (one save; `ScoringRules` and the membership go out on the
  cascade already configured at `LeagueConfiguration.cs:19-27`). The endpoint 409s an organizer only
  while other members exist, so a league other people are in can never be destroyed. Phase 4 shows a
  destructive "Leave and delete league" for the solo case. "What We're NOT Doing" now excludes a
  standalone delete endpoint rather than deletion outright; `plan-brief.md` decisions and scope
  updated to match.

## Triage summary

- **Fixed**: F1 (Fix A), F2, F3, F5, F6 (5)
- **Accepted**: F4 (1)
