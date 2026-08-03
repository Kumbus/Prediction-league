<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Organizer creates a league (S-03)

- **Plan**: context/changes/organizer-create-league/plan.md
- **Scope**: Full plan (Phases 1–2)
- **Date**: 2026-08-03
- **Verdict**: NEEDS ATTENTION (both warnings resolved during triage)
- **Findings**: 0 critical, 2 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Changed-file set matched the plan's file list exactly — no unplanned files, no scope-guardrail
violations (no join-by-code, no rule editing, no league delete, no standings, no policy, no tests).

## Findings

### F1 — Draft-tournament existence leaks through create validation

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:114-119
- **Detail**: Create returned two distinguishable 400s — "Tournament not found." vs "Tournament is
  not published." — letting a non-admin confirm a draft tournament exists, while
  `TournamentsController.cs:79,177` hides drafts behind 404 explicitly to prevent that.
- **Fix**: Collapse both branches into one message, "Tournament not found or not published."
- **Decision**: FIXED

### F2 — Api layer catches an EF Core exception type

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:4,159-174
- **Detail**: The controller imported `Microsoft.EntityFrameworkCore` and caught
  `DbUpdateException` for the invite-code retry. No other controller references EF Core, and
  `IRepository.cs` states the intent: "Application depends on this, not on EF Core." The approved
  plan prescribed the catch, so it was plan-sanctioned rather than unilateral drift.
- **Fix A ⭐ Recommended**: Leave as-is; record the boundary as a lesson, extract when S-05 revisits
  invite codes.
- **Fix B**: Push collision-retry behind the repository/generator abstraction.
- **Decision**: ACCEPTED-AS-RULE ("Persistence exception types must not reach the Api layer") +
  FIXED — the user chose to record the lesson *and* apply the fix. Implemented as
  `InviteCodeCollisionException` (Application) thrown by a new `ILeagueRepository.CreateAsync`,
  with SQL Server error 2601/2627 + `IX_Leagues_InviteCode` detection confined to
  `LeagueRepository`. This also closes F4: the retry now triggers only on a genuine invite-code
  collision, never on an unrelated write failure.

### F3 — GET /api/leagues loads the whole Tournament table

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality
- **Location**: src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:71-73
- **Detail**: `ListAsync(includeUnpublished: true)` materializes every tournament to resolve a
  handful of names, scaling with total tournaments rather than the caller's league count.
  Acceptable at MVP scale (admin-seeded, few rows) and consistent with the plan's stated
  friend-group performance posture.
- **Fix**: If it ever matters, add `ITournamentRepository.GetNamesByIdsAsync`.
- **Decision**: SKIPPED — acceptable at current scale.

### F4 — Retry path is sound, but the catch was not root-caused

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality
- **Location**: src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:155-175
- **Detail**: The load-bearing technical question came back clean: EF Core does not reset entity
  state on a failed `SaveChangesAsync` (`AcceptAllChangesAfterSave` runs only on success), the
  League + rules + membership stay tracked as `Added`, the failed batch rolls back atomically, and
  no `EnableRetryOnFailure` execution strategy is configured — so reassigning `InviteCode` and
  saving again genuinely recovers. The context is not poisoned. The gap was that *any*
  `DbUpdateException` was assumed to be a collision.
- **Decision**: FIXED as a side effect of F2 — the repository now inspects the SQL error number and
  index name before translating to `InviteCodeCollisionException`.

### F5 — Two small unplanned client additions

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Scope Discipline
- **Location**: src/client/src/leagues/types.ts:36-47, routes/leagues/LeagueFormPage.tsx:37
- **Detail**: `SCORING_LABELS` plus a `label` field on `SCORING_DEFAULTS` (plan specified defaults
  only), and a client-side `isPublished` re-filter on the tournament dropdown. Both benign; the
  re-filter is defensive for admin callers, who would otherwise see their own drafts in a list the
  server then rejects.
- **Decision**: SKIPPED — recorded, no action.

### F6 — Bounded cartesian Include; unguarded async effects

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Pattern Consistency
- **Location**: src/server/.../LeagueRepository.cs:28-33, src/client/src/routes/leagues/*.tsx
- **Detail**: `GetWithDetailAsync` includes two collections in one query — correct via identity
  resolution, fan-out capped at 6 × memberCount. `ListForUserAsync` translates `.Any()` to
  `EXISTS`, no double-counting. The three new pages fire unguarded async IIFEs in `useEffect`
  exactly as every existing admin page does — pre-existing convention, not a regression.
- **Decision**: SKIPPED — not a regression; systemic and out of scope for this slice.

## Success criteria — re-verified at review time

| Check | Result |
|---|---|
| `dotnet build` | PASS (0 errors) |
| `dotnet ef migrations has-pending-model-changes` | PASS (no drift) |
| `npm run build` | PASS (tsc -b + vite) |
| `npm run lint` | PASS (0 errors, 0 warnings) |
| Manual 1.3–1.9, 2.3–2.8 | Confirmed by the user this session |
