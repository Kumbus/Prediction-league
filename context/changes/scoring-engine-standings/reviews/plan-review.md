<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Scoring Engine & Standings (S-07)

- **Plan**: `context/changes/scoring-engine-standings/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-17
- **Verdict**: REVISE → SOUND after fixes
- **Findings**: 1 critical, 3 warnings, 3 observations (all fixed in plan)

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | FAIL |
| Plan Completeness | WARNING |

## Grounding

16/16 paths ✓, 7/7 symbols ✓, brief↔plan ✓, Progress format ✓ (one `## Progress`, 5 phases matched, every criterion has an item, no stray checkboxes).

Verified-true plan claims (no finding raised): `AwardedPoints` already in schema (`AddPredictions.cs:30`); `MatchEvent` written only by `FixtureIngestService`; `MissedPenalty` seeded `Category = Goal` (`MatchEventTypeConfiguration.cs:24`); predictions cascade on match delete (`PredictionConfiguration.cs:28`); CSV match import is insert-only, so it is not a missing scoring trigger; both hosts call `AddInfrastructure`, so one registration reaches Api + Functions; scoring rules lock per tournament kickoff (`LeaguesController.cs:238`).

## Findings

### F1 — First-scorer tie-break by `MatchEvent.Id` is not stable

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 §1 (MatchOutcome contract)
- **Detail**: `MatchEvent.Id` is a client-generated Guid (`FixtureIngestService.cs:207`), and Phase 3's replace-all editor mirrors that pattern — re-saving identical events mints new Guids and can flip which of two same-minute goals is "first", silently moving `CorrectGoalScorer` points. Null `MinuteExtra` ordering and SQL-vs-C# Guid collation were also unspecified.
- **Fix**: Order goal events by `(Minute, MinuteExtra ?? 0, MatchEventTypeId, PlayerId)`, in memory, after load; never by `Id`.
- **Decision**: FIXED — Phase 1 §1 rewritten with an explicit "Ordering key — load-bearing" paragraph; criteria 1.4 and 3.10 added.

### F2 — Scoring service mutates tracked entities outside the repository

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 §2 + §3
- **Detail**: Every existing write is an intent-named repository method that owns its save (`UpsertManyAsync`, `ReplaceScoringRulesAsync`, `JoinAsync`, `TransferOrganizerAsync`), and `IPredictionRepository.cs:12-13` documents the untracked stance outright. The plan had the service take a tracked graph and call the generic `SaveChangesAsync`, making "one save per match" unenforceable.
- **Fix A ⭐ Recommended**: Add `IPredictionRepository.SetAwardedPointsAsync(matchId, pointsByPredictionId, ct)`; keep the read untracked.
- **Fix B**: Keep the tracked read, document it as a deliberate exception.
- **Decision**: FIXED via Fix A — `ListForMatchAsync` is now untracked and a `SetAwardedPointsAsync` write method owns the single save.

### F3 — Post-commit scoring failure had no admin-facing contract

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Critical Implementation Details + Phase 2 §4
- **Detail**: "Surface the failure" left the admin with a 500 on a write that already committed — the form reads "save failed", the state is saved-but-stale, and only the unexposed rescore endpoint repairs it.
- **Fix**: Catch, log, return 200 with `ScoringFailed` + `ScoringMessage` naming the rescore endpoint; the match form renders a warning banner.
- **Decision**: FIXED — partial-success contract written into Critical Implementation Details, Phase 2 §4 and Phase 3 §1; criterion 2.7 added.

### F4 — `TournamentsController` took 5 more endpoints while Phase 4 split `StandingsController` out for the same reason

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 §6, Phase 3 §1 vs Phase 4 §2
- **Detail**: One plan, two opposite conclusions from the same "this controller already owns too much" rationale. `/api/match-event-types` is not even a tournament route.
- **Fix**: New `MatchesController` for rescore + events + eligible-players + event-types.
- **Decision**: FIXED — Phase 2 §6 creates `MatchesController`; Phase 3 §1 targets it. Moving the pre-existing `/api/matches/{id}` routes is explicitly out of scope.

### F5 — Events read DTO overlapped the existing `MatchEventDto`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3 §1
- **Detail**: `MatchEventDto` carries names but no ids and backs the tournament-detail projection; the plan did not say extend vs. add.
- **Fix**: New `MatchEventEditDto` with ids + names; leave `MatchEventDto` untouched.
- **Decision**: FIXED.

### F6 — Rescore 404 contradicted "unknown match is a no-op result"

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §1 vs §6
- **Detail**: A no-op result cannot distinguish "missing match" from "found, zero predictions".
- **Fix**: The controller does its own `GetByIdAsync` existence check.
- **Decision**: FIXED — stated in Phase 2 §6; criterion 2.6 extended.

### F7 — Organizer with no membership row is absent from their own standings

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 4 §1-§2, Phase 5 §1
- **Detail**: Visibility is organizer-OR-membership while the roster is memberships only; edge-only (creation inserts an organizer membership), but "highlight the caller's row" would highlight nothing.
- **Fix**: No matching row → no highlight, not an error.
- **Decision**: FIXED — noted in Phase 5 §1.
