<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Scoring Engine & Standings (S-07)

- **Plan**: `context/changes/scoring-engine-standings/plan.md`
- **Scope**: Phases 1-5 of 5 (full plan review)
- **Date**: 2026-08-25
- **Verdict**: NEEDS ATTENTION → resolved during triage (the one CRITICAL is fixed)
- **Findings**: 1 critical, 3 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | FAIL → fixed (F1, F5, F6) |
| Architecture | PASS |
| Pattern Consistency | WARNING → fixed (F2, F4) |
| Success Criteria | WARNING (see F3) |

Plan adherence was clean: all 12 load-bearing contracts the plan calls out were verified present in
the code, no EXTRA files or endpoints, and every "What We're NOT Doing" guardrail respected (no
migration, no test project, no CSV import for events, replace-all only, lock/reveal untouched).

## Findings

### F1 — Fourth IsKeySet instance survives in ingest

- **Severity**: CRITICAL
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `FixtureIngestService.cs:225` (pre-fix)
- **Detail**: Commit `f65b221` fixed three call sites of the EF `IsKeySet` bug; a fourth survived in
  ingest's private `ReplaceEventsAsync`. On re-ingest of a known fixture (`isNew == false`) the match
  is tracked-not-Added, so each new `MatchEvent` is painted `Modified` → `UPDATE` against a row that
  was never inserted. Unlike the fixed sites, the `SaveChangesAsync` at line 155 has no try/catch, so
  `DbUpdateConcurrencyException` escapes `IngestTournamentAsync` and aborts the whole tournament run.
  Two implementations of "replace a match's events" existed and the fix landed in only one — which is
  precisely how it was missed.
- **Fix B (applied)**: Route ingest through the repository and delete the private copy.
  - A naive move would have broken the new-fixture path: the id-based `ReplaceEventsAsync` queries by
    id, and a fixture just `AddAsync`'d is not yet in the database. Added a
    `ReplaceEvents(Match, IReadOnlyList<MatchEvent>)` overload taking the already-tracked entity;
    the id-based overload loads and delegates to it. Ingest keeps its own event mapping (team/player
    cache resolution needs `await`) and calls the entity overload.
  - Blind spot: ingest cannot be exercised end-to-end right now (free tier cannot fetch current-season
    fixtures), so this rests on review plus a compile, not a live run.
- **Decision**: FIXED via Fix B

### F2 — ReplaceEventsAsync is the only write that doesn't own its save

- **Severity**: WARNING
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: `MatchRepository.cs:41-64`, `IMatchRepository.cs`, `plan.md:241`
- **Detail**: The comment claimed the arrangement was "consistent with the other repositories" — it is
  not. `JoinAsync`, `LeaveAsync`, `TransferOrganizerAsync`, `ReplaceScoringRulesAsync`,
  `UpsertManyAsync` and `SetAwardedPointsAsync` all own their save. It also contradicted the rationale
  written in this same slice at `IPredictionRepository.cs:45-51`. The flaw originated in the plan, not
  in a drift from it. F1 then made the caller-owned save genuinely correct: ingest replaces a fixture's
  events *and* writes the fixture, committing both in one save-per-match.
- **Fix (applied)**: Kept the save with the caller; corrected the false claim on the interface with the
  actual justification, and corrected `plan.md:241` at source so it stops propagating into future
  reviews.
- **Decision**: FIXED

### F3 — Manual criteria stamped without session evidence

- **Severity**: WARNING
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `plan.md:410-487`
- **Detail**: Automated 8/8 pass (re-run during this review). Manual was 0/27 (not 30 — the report's
  first count was wrong; the breakdown is 3 + 6 + 7 + 5 + 6). This slice has no test project by
  explicit decision, so manual checks are the only verification that exists.
- **Contrary evidence raised at triage**: checks 3.4-3.10 exercise
  `PUT /api/matches/{id}/events` → `MatchRepository.ReplaceEventsAsync`, which carried the `IsKeySet`
  bug until commit `f65b221` on 2026-08-25. That method loads the match by query, so the parent was
  always `Unchanged` and every save carrying at least one event returned 500. Those seven checks could
  not have passed before that commit. Checks 2.2-2.7 and 5.5 go through the match-result write, which
  the bug did not touch, and were reachable.
- **Decision**: ACCEPTED — user directed that all 27 be stamped, including 3.4-3.10, after the evidence
  above was presented. Recorded here as the user's call, not a verified result.

### F4 — StandingsCard did not special-case 404

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `StandingsCard.tsx:29-31`
- **Detail**: `StandingsPage.tsx:26` and `LeagueDetailPage.tsx:34` branch on `ApiError` with
  `status === 404`; the card fell through to a generic "Failed to load the standings." Unreachable
  today because the parent page gates visibility before the card mounts.
- **Fix (applied)**: Added the matching 404 branch with a not-available/not-a-member message.
- **Decision**: FIXED

### F5 — Cartesian Include in LeagueRepository

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `LeagueRepository.cs` — `GetWithDetailAsync`, `GetForUpdateAsync`, `GetByInviteCodeAsync`
- **Detail**: Each `Include`s two collection navigations in one query (rows = rules × members). EF
  emitted `MultipleCollectionIncludeWarning` live in the server log during this session's testing.
  Pre-existing, not introduced by this slice. The review named two sites; a third (`GetWithDetailAsync`)
  has the same shape and was included in the fix.
- **Fix (applied)**: `AsSplitQuery()` on all three, plus `OrderBy(l => l.Id)` — split queries need an
  ordering to stay deterministic under a row-limiting operator, and without it EF trades one warning
  for another. Moot in effect here since both filters are on unique keys.
- **Decision**: FIXED

### F6 — MatchScoringService reloaded dictionaries per fixture

- **Severity**: OBSERVATION
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `MatchScoringService.cs:71-78`, called from `FixtureIngestService.cs:165`
- **Detail**: Every fixture in an ingest run re-read the seeded event-type dictionary and the
  tournament's leagues-with-rules, though the ingest caller already caches both once per run and all
  fixtures share one tournament. N round-trips instead of 1.
- **Fix (applied)**: Per-instance memoisation inside the service. It is registered `Scoped`, so the
  cache lives exactly one request (one match, no benefit, no harm) or one ingest run (the case that
  pays). Safe to hold for a scope: event types are seeded reference data, and a league's rules lock at
  the tournament's first kickoff, which has necessarily passed for any match being scored. Idempotence
  is preserved — two runs in one scope read the same rules and produce the same rows. No interface
  change, so the caller keeps its cache-free contract.
- **Decision**: FIXED

## Triage summary

- **Fixed**: F1 (Fix B), F2, F4, F5, F6 — 5
- **Accepted**: F3 — 1 (user's call, against stated evidence)

Automated verification re-run after all fixes: `dotnet build` ✅, `npm run build` ✅, `npm run lint` ✅.
