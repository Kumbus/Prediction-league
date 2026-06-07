<!-- PLAN-REVIEW-REPORT -->
# Plan Review: F-03 Football API Ingest

- **Plan**: context/changes/football-api-ingest/plan.md
- **Mode**: Deep
- **Date**: 2026-06-07
- **Verdict**: REVISE → SOUND (after triage; all findings fixed)
- **Findings**: 1 critical · 4 warnings · 2 observations

## Verdicts

| Dimension | Verdict (pre-fix) |
|-----------|-------------------|
| End-State Alignment | WARNING |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | FAIL |
| Plan Completeness | FAIL |

## Grounding

6/6 paths ✓, symbols ✓ (Match strings, MatchEventType enum, DI:24, Program migrate,
slnx has no Functions, Tournament has no Season), blast radius clean (only Domain +
Infra persistence touch these — no controller/service callers), brief↔plan ✓.

## Findings

### F1 — Phase Success Criteria use checkboxes (Progress contract break)

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: All phases — `#### Automated/Manual Verification:` blocks
- **Detail**: Phase bodies must hold plain `- ` bullets; `- [ ]`/`- [x]` live only under `## Progress`. Plan duplicated checkboxes in both (lines 250-263, 319-323, 396-405, 452-462, 507-519). /10x-implement parses Progress for state — stray phase-body checkboxes risk double-tracking / mis-ticks.
- **Fix**: Convert the `- [ ]` bullets inside every `#### *Verification:` section to plain `- ` bullets; keep `## Progress` checkboxes as the single source of completion state.
- **Decision**: FIXED (Fix in plan)

### F2 — Unhandled event types crash required FK

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 #3 (seed) + Phase 4 #2 (delete-replace)
- **Detail**: MatchEventTypeId is a non-null FK (Restrict); dictionary seeds only 6 Goal/Card subtypes. API events also carry type `Subst` and `Var` (api-reference.md:60,69). Phase 4 mapped "the mapped set" with no filter → a Subst/Var event → null FK → crash or silent drop.
- **Fix**: State explicitly in Phase 4 — store only `type=="Goal"` and `type=="Card"` events (Subst/Var skipped). Keep Missed Penalty as a stored Goal-type row.
- **Decision**: FIXED (Fix in plan)

### F3 — Production timer can't enumerate tournaments or derive season

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Phase 5 #1 (TimerTrigger) + Phase 2 #1 (ITournamentRepository)
- **Detail**: End state promises the timer runs ingest on schedule, iterating active tournaments + supplying `season` to IngestTournamentAsync. But ITournamentRepository defined only GetByExternalApiIdAsync (no list/active query), and Tournament has no Season field (only StartDate/EndDate DateOnly, ExternalApiId nullable). Manual endpoint hid this via query params; the production host had no data path.
- **Fix A ⭐ Recommended**: Add `Season` to Tournament + repo `GetActiveAsync(DateOnly)` window query.
  - Strength: Timer self-sufficient; Season genuinely needed for the /fixtures call and survives past this slice.
  - Tradeoff: Touches Phase-1 migration (one more column) + a repo method.
  - Confidence: HIGH — Season is already required by IFixtureIngestService.
  - Blind spot: How "active window" maps to poll cadence unverified.
- **Fix B**: Drive timer from app-setting list of {tournamentId, season}.
  - Strength: No schema change; config-only.
  - Tradeoff: Config drift; ExternalApiId/season split across DB+config.
  - Confidence: MED — works but pushes domain data into settings.
  - Blind spot: Multi-tournament ops ergonomics.
- **Decision**: FIXED (Fix A)

### F4 — Functions on .NET 10 isolated-worker tooling assumed available

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 5 #1 + criterion 5.1/5.2
- **Detail**: Phase 5 gates "whole solution builds incl. Functions" on a new .NET 10 isolated-worker project + local Core Tools. Flagged in brief risks but it's a hard phase-blocker. If .NET 10 worker + Timer extension aren't GA in the installed tooling, Phase 5 stalls with no fallback host named.
- **Fix**: Verify isolated-worker .NET 10 + Core Tools support (Context7 / docs) before Phase 5; name a fallback (IHostedService/PeriodicTimer in Api, or net9 worker) if unsupported.
- **Decision**: FIXED (Fix in plan)

### F5 — detail→Code lookup mismatch (spaces)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 4 #2 (resolve MatchEventType "by code/detail")
- **Detail**: Dictionary Code = "NormalGoal" (no spaces); API detail = "Normal Goal" (spaces, api-reference.md:70). GetByCodeAsync(detail) never matches. Own Goal handling also TBD (api-reference.md:75).
- **Fix**: Define the detail→Code map in Phase 4 (strip spaces / explicit switch); decide Own Goal handling (store as OwnGoal row, exclude from scorer credit).
- **Decision**: FIXED (Fix in plan)

### F6 — Null player payload vs required PlayerId FK

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 #5 (PlayerId Guid) + Phase 4
- **Detail**: PlayerId is non-null Guid. api-reference.md:77 warns trailing partial entries can lack player. Minimal-create fallback covers a missing seed (id present), not a null id in the payload. Such events must be skipped.
- **Fix**: State in Phase 4 — skip events with null player/type (folds into the F2 filter).
- **Decision**: FIXED (Fix in plan)

### F7 — Score source (fulltime vs running goals) unspecified

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 4 #2 ("set season/round/scores")
- **Detail**: api-reference.md:51 — final result = score.fulltime at FT; goals is live/running. Plan said "set scores" without naming the field per status. Risk: storing running goals as final.
- **Fix**: Specify — HomeScore/AwayScore from score.fulltime when status==Finished, else from goals.
- **Decision**: FIXED (Fix in plan)

## Triage Summary

- **Fixed**: F1, F2, F3 (Fix A), F4, F5, F6, F7 (7)
- **Skipped / Accepted / Dismissed**: none
- **Verdict after fixes**: SOUND
