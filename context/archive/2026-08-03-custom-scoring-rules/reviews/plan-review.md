<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Custom Scoring Rules (S-04)

- **Plan**: `context/changes/custom-scoring-rules/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-03
- **Verdict**: REVISE → SOUND after triage (all 5 findings fixed)
- **Findings**: 0 critical, 3 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | WARNING |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

8/8 paths ✓, symbols ✓ (`BaseRepository.Set` is `DbSet<League>`, `LeagueConfiguration` cascade + no inverse nav, `SCORING_DEFAULTS`, `dotnet-ef` 10.0.8 with `Microsoft.EntityFrameworkCore.Design` on the Api project → criterion 1.2 is runnable), Progress↔Phase 16/16 items ✓, brief↔plan ✓, no `docs/reference/contract-surfaces.md` (check skipped).

## Findings

### F1 — Phase 1 breaks league creation until Phase 2 lands

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Phase 1 §3 + Implementation Note
- **Detail**: The shipped create form posts all six parameters (`LeagueFormPage.tsx:56-60`), three defaulting to `points: 0` (`types.ts:40-42`). Phase 1's floor rejects `points < 1`, so league creation from the live UI 400s the moment Phase 1 lands. The repo commits one phase per commit (S-03: 11a3454 p1, fd7ad89 p2) and Phase 1's Implementation Note pauses before Phase 2 — prescribing a state where main has a broken user-facing feature.
- **Fix A ⭐ Recommended**: Mark Phase 1 as not independently shippable; both phases land as one PR.
  - Strength: One-line plan edit; keeps the floor decision atomic.
  - Tradeoff: Loses the option of deploying server ahead of client.
  - Confidence: HIGH — breakage verified against the current form's payload.
  - Blind spot: None significant.
- **Fix B**: Ship the floor in two steps (Phase 1 keeps 0–1000; Phase 2 tightens).
  - Strength: Main is never broken.
  - Tradeoff: Splits one contract decision across two commits.
  - Confidence: MED.
  - Blind spot: Whether deploys are per-phase or per-slice.
- **Decision**: FIXED via Fix A — Implementation Approach now states the phases are not independently shippable; Migration Notes carries a "deploy the two phases together" paragraph.

### F2 — Legacy leagues hydrate the edit form into an invalid state

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §4
- **Detail**: S-03 leagues carry all six rows, typically three at `Points = 0`. Edit mode seeded from `league.scoringRules` would load those as active-with-0 — rejected by the new floor and flagged invalid by `min=1`. The organizer opens Edit on an untouched league and must repair rows they never set. Manual step 8 covered the save, not this state.
- **Fix**: Treat `points < 1` as inactive when seeding edit state.
- **Decision**: FIXED — Phase 2 §4 gains a **Hydration** paragraph; new criterion 2.9.

### F3 — Rule-removal mechanism is ambiguous where it matters most

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §2
- **Detail**: "Remove unmatched existing rules" names no mechanism, and the obvious reach doesn't compile — `BaseRepository<League>.Set` is `DbSet<League>` (`BaseRepository.cs:11-13`). The alternative (removing from `league.ScoringRules`) leans on EF orphan-delete for a required cascade relationship with no inverse navigation (`LeagueConfiguration.cs:20-24`). The plan also passed incoming rules as `ScoringRule` entities without saying they are values, not attachments — attaching them would reintroduce the unique-index collision the plan's Critical Implementation Details section exists to prevent.
- **Fix**: Name both — remove via `Context.Set<ScoringRule>().Remove(...)`; state that incoming instances are read for `Parameter`/`Points` only and never attached.
- **Decision**: FIXED — both mechanics spelled out in Phase 1 §2's Contract.

### F4 — Leagues created after kickoff are permanently unconfigurable, silently

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Implementation Approach
- **Detail**: The derivation is sound but the UX wasn't stated. Create still succeeds against a started tournament; the organizer sets rules once and can never change them. No warning at create time, and `LeagueSummaryResponse` deliberately lacks the lock flag — so they discover it by finding no Edit button.
- **Fix A ⭐ Recommended**: Accept the lock, warn at create time.
  - Strength: Keeps the rule intact; makes the consequence visible while the organizer can still act.
  - Tradeoff: Create form needs to know the tournament has started.
  - Confidence: MED.
  - Blind spot: `TournamentResponse`'s shape was unverified at review time — **resolved during triage**: `startDate` is already on the wire (`admin/types.ts:8`), so no server change is needed.
- **Fix B**: Reject creation against a started tournament (400).
  - Strength: No unconfigurable league can exist.
  - Tradeoff: Forecloses mid-tournament pools; widens S-04 into creation policy.
  - Confidence: MED.
  - Blind spot: Whether mid-tournament pools matter.
- **Decision**: FIXED via Fix A — Phase 2 §3 adds a `startDate`-based warning, worded as a heads-up rather than a claim about current lock state (the lock derives from `Match.KickoffUtc`, which can legitimately disagree with `StartDate`). Creation stays legal. New criterion 2.10.

### F5 — No `.http` sample for the endpoint the test steps exercise

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Testing Strategy step 5
- **Detail**: Manual steps 5–6 fire empty-array and wrong-user PUTs "via `PredictionLeague.http`", but no phase added the request to `src/server/PredictionLeague.Api/PredictionLeague.http`. With no test project, that file is the slice's only repeatable harness.
- **Fix**: Add the PUT sample to Phase 1's changes list.
- **Decision**: FIXED — new Phase 1 §6 covers the PUT samples and refreshes the stale all-six `POST` sample.
