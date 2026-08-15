<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Submit Locked Predictions (S-06)

- **Plan**: `context/changes/submit-locked-predictions/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-15
- **Verdict**: REVISE → **SOUND** after triage (all 9 findings fixed)
- **Findings**: 3 critical, 3 warnings, 3 observations

## Verdicts

| Dimension | Verdict (as reviewed) | After fixes |
|-----------|----------------------|-------------|
| End-State Alignment | FAIL | PASS |
| Lean Execution | WARNING | PASS |
| Architectural Fitness | PASS | PASS |
| Blind Spots | WARNING | PASS |
| Plan Completeness | FAIL | PASS |

## Grounding

10/11 paths ✓ (`PredictionLeague.http` path wrong — F6), 7/7 symbols ✓, brief↔plan ✓.
Codebase verification run inline rather than via sub-agent (session rule: no AgentTool unless requested).
Progress↔Phase contract re-verified after all edits: 4 phases, counts and numbering contiguous
(1.1–1.13, 2.1–2.16, 3.1–3.16, 4.1–4.7), one `## Progress` block, no stray checkboxes in phase bodies.

## Findings

### F1 — Required scorer field is unsatisfiable on real player data

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: End-State Alignment
- **Location**: Phase 2 §2 (validation rules), plan.md:182,184
- **Detail**: Plan made an optional field REQUIRED when the league scores it, and required
  `firstScorerPlayerId` ∈ an eligible set derived from `Player.ClubTeamId` / `NationalTeamId`.
  But no bulk path populates those: the player CSV importer has no team column
  (`CsvHelperPlayerImporter.cs:212-217`) and the admin form takes a pasted raw Guid
  (`PlayerFormPage.tsx:142,146`); API-Football ingest is deferred. `CorrectGoalScorer` is
  `defaultActive: true` (`types.ts:65`), so the default league scores it. Net effect: empty
  candidate set ⇒ every item `Invalid` ⇒ no round is ever savable. The slice's happy path was
  unreachable on the repo's actual data path.
- **Fix A**: Accepted-if-scored, never required.
- **Fix B**: Required only when the candidate set is non-empty.
- **Fix C ⭐ (chosen)**: Fix the data at source — `ClubTeam` / `NationalTeam` columns on the player
  CSV import resolved via the existing `ITeamRepository.FindByNameAsync`, plus team selects
  replacing the raw-Guid inputs on the admin player form. Unknown team name is a row conflict,
  never an auto-created team (unlike the match importer, where auto-create is justified).
  The "required when scored" rule stays — it is now satisfiable.
- **Decision**: FIXED via Fix C (plan.md — Current State Analysis mismatch 4, Phase 1 §8,
  Phase 3 §6, criteria 1.8–1.10 / 3.14–3.15, Manual Testing step 0)

### F2 — Round view has no source for `Match.Round`

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 1 (Changes Required) vs Phase 2 §1
- **Detail**: Phase 2's round view, switcher, and `IsLocked` all need `Match` rows with `Round`,
  `KickoffUtc`, and team names. The only member-readable match read is
  `IMatchRepository.ListByTournamentAsync` → `MatchWithEventsDto`, which carries no `Round`
  (`MatchWithEventsDto.cs:7-13`) — and plan.md:53 forbade changing it. `IMatchRepository` has no
  entity-returning list-by-tournament. Phase 1 provisioned no match read at all, so the
  implementer would open Phase 2 with nothing to query.
- **Fix ⭐ (chosen)**: Add `IMatchRepository.ListForPredictionsAsync(tournamentId, ct)` returning a
  new `MatchRoundDto` beside `MatchWithEventsDto`, reusing `TeamRefDto` and the existing team
  joins. New projection; the old DTO is untouched, so the "not doing" boundary holds. One
  projection backs both the read and the write path, so `IsLocked` compares the same `KickoffUtc`
  in both.
- **Decision**: FIXED (plan.md — Phase 1 §5, Phase 1 overview, Phase 2 §2 `IsLocked` signature)

### F3 — "Manual" round default collapses the switcher to one section

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: End-State Alignment
- **Location**: Critical Implementation Details (Round ordering); Phase 2 §1, Phase 3 §3
- **Detail**: The plan treated free-text `Round` as an ordering hazard ("a typo produces its own
  section"). The real hazard is the inverse: `Round` is optional and defaults to the single
  literal `"Manual"` on both write paths (`TournamentsController.cs:233`,
  `CsvHelperMatchImporter.cs:121`), and the admin form labels it "Round (optional)" with a
  `"Manual"` placeholder (`MatchFormPage.tsx:195-196`). Manual entry is the current primary data
  source. So a realistic tournament has one round named "Manual" holding every match: the
  switcher shows one entry, "scroll to the round in play" means scrolling the whole tournament,
  and one "Save round" writes every match including ones weeks out.
- **Fix A**: Derive sections from kickoff date when the round name is non-discriminating.
- **Fix B ⭐ (chosen)**: Make `Round` required — reject blank in `ValidateMatchAsync`, drop both
  `"Manual"` fallbacks, blank CSV round becomes a row conflict, and the client form requires it.
  No backfill migration: existing `"Manual"` rows stay valid and an admin retitles them.
  Accepted cost: this breaches "no changes to the admin match surface", now carved out
  explicitly as one of two named exceptions.
- **Decision**: FIXED via Fix B (plan.md — Critical Implementation Details, Phase 1 §9,
  Phase 3 §6, What We're NOT Doing, criteria 1.11–1.13 / 3.16)

### F4 — Match delete would 500, not "refuse", on a match with predictions

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 §2 (FK restrict); Success Criterion 1.6
- **Detail**: Criterion 1.6 said "match delete with predictions is refused", but
  `DELETE /api/matches/{matchId}` (`TournamentsController.cs:284-296`) has no guard — it calls
  `Remove` + `SaveChangesAsync` directly. With a restrict FK that surfaces as an unhandled
  `DbUpdateException` → 500, not a refusal, and `lessons.md:25` bars catching it in the
  controller. The plan also forbade touching that surface, so no phase could have fixed it.
- **Fix A**: Pre-check via `AnyForMatchAsync` + 409, mirroring the tournament-delete guard.
- **Fix B ⭐ (chosen)**: Cascade the `Match` → `Prediction` FK. Verified safe: `League.TournamentId`
  is a bare Guid with no FK (`LeagueConfiguration.cs:36`), so there is no cascading path
  `Tournament → League`, and the only shared-ancestor route into `Predictions` is
  `Tournament → Match → Prediction`. Two cascades, no common cascading ancestor, no
  multiple-cascade-path error. `MatchConfiguration`'s restrict on team FKs is a different case
  (one row reaches `Team` twice). Accepted consequence, documented in the plan: an admin
  deleting a match silently destroys every member's forecast for it; no confirmation step.
- **Decision**: FIXED via Fix B (plan.md — Phase 1 §2 contract + rationale, criterion 1.6)

### F5 — Double-submit unique-index violation had no translation path

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 §4 (`UpsertManyAsync`)
- **Detail**: Phase 1 §2 named the unique index "the guard against a double-submit racing itself",
  but `UpsertManyAsync` was specified as read-then-insert-or-update in one save. Two concurrent
  first-time submits of the same round both read "no row" and both insert; the index rejects the
  loser and the `DbUpdateException` reaches the controller. `lessons.md:25` was cited in Key
  Discoveries but no phase implemented the translation.
- **Fix ⭐ (chosen)**: The repository absorbs the rejection — re-read the affected rows, apply the
  update once, return normally, mirroring `ILeagueRepository.JoinAsync`'s idempotent contract.
  Last write wins, which is correct for a member overwriting their own forecast.
- **Decision**: FIXED (plan.md — Phase 1 §4 contract, criterion 2.16)

### F6 — Wrong path for the HTTP samples file

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §3
- **Detail**: Plan said `src/server/PredictionLeague.http`; the file is at
  `src/server/PredictionLeague.Api/PredictionLeague.http`.
- **Fix**: Corrected the path.
- **Decision**: FIXED

### F7 — `SubmittedUtc` `HasDefaultValueSql` was cargo-culted

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Lean Execution
- **Location**: Phase 1 §2
- **Detail**: Copied from `LeagueMembershipConfiguration`, whose own comment
  (`:17-20`) says the default exists solely to backfill rows predating the column. `Predictions`
  is a new table with no such rows and the plan sets the value explicitly on insert, so the
  default could never fire.
- **Fix**: Dropped `HasDefaultValueSql`; kept the unique-index and bare-Guid-`UserId` halves of
  the pattern.
- **Decision**: FIXED

### F8 — "No automated test project exists" was inaccurate

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Testing Strategy; What We're NOT Doing
- **Detail**: A Playwright harness already exists — `package.json:11` (`"e2e": "playwright test"`),
  `src/client/tests/e2e/auth.spec.ts`, documented in `src/client/AGENTS.md`. It covers sign-in
  only, but it is the runway for the one risk the plan flags as uncovered (the kickoff lock).
- **Fix**: Corrected the claim and named the existing harness as available. No test added this
  slice, per the standing manual-only decision.
- **Decision**: FIXED (statement corrected only)

### F9 — Own goals were not modelled (raised by the user during triage)

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 §1 (entity contract), Phase 2 §2, Phase 3 §4
- **Detail**: `Prediction.PredictedFirstScorerPlayerId` stored a player id alone. The candidate set
  spans both teams, so an own-goal scorer was technically pickable, but nothing distinguished
  "scored for their own side" from "scored for the opposition" — and `MatchEvent` records
  `TeamId` alongside `PlayerId` (`Match.cs`), so S-07 could not have scored either case
  correctly. The entity field shape is a contract for S-07: cheap now, expensive after deploy.
- **Fix ⭐ (chosen, user's own formulation)**: A first-scorer forecast is a pair — the credited team
  plus the player. Added `PredictedFirstScorerTeamId: Guid?` (FK to `Team`, restrict, nullable).
  An own goal is team A credited with a team-B player. Validated together (one without the other
  is `Invalid`), but deliberately not required to agree. The seeded dictionary already carries
  `NormalGoal` / `OwnGoal` (`MatchEventTypeConfiguration.cs:21-22`), though the plan does not
  depend on it — the credited team carries the information on its own.
- **Decision**: FIXED (plan.md — Phase 1 §1 + §2 + §6, Critical Implementation Details,
  Phase 2 §1 request shape + §2 validation, Phase 3 §1 types + §4 row, Phase 4 §2 reveal,
  criteria 2.11–2.13 / 3.7–3.8, edge cases)

## Notes

- The two admin-surface exceptions (player team linkage, required `Round`) are now named
  explicitly in "What We're NOT Doing" rather than left as silent contradictions.
- `plan-brief.md` was updated alongside the plan so the two do not disagree.
