<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Submit Locked Predictions (S-06)

- **Plan**: `context/changes/submit-locked-predictions/plan.md`
- **Scope**: Phases 1-4 of 4 (full plan)
- **Date**: 2026-08-15
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 6 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Automated verification (re-run during this review)

| Check | Result |
|---|---|
| `dotnet build` (to a scratch output dir — the running API locks `bin/`) | PASS, 0 errors |
| `dotnet ef migrations has-pending-model-changes` | PASS — "No changes have been made to the model since the last migration." |
| `GET /health/db` | PASS — 200 `Healthy` |
| `npm run build` | PASS |
| `npm run lint` | PASS, clean |

## Core guardrail verdict

The kickoff lock holds. One `IsLocked` definition (`PredictionsController.cs:300`), one clock captured per request (`:146`, `:178`, `:265`), both fed by the same `ListForPredictionsAsync` DB projection — nothing client-supplied reaches the comparison. No pre-kickoff reveal leak on any path, including a crafted `?round=` or a cross-tournament match id. Authorization masks non-members with 404 and never reads `LeagueMembership.Role` (lessons.md:32 honoured).

## Findings

### F1 — Retry save is unguarded; EF exception can reach the controller

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/PredictionRepository.cs:121`
- **Detail**: The first `SaveChangesAsync` is wrapped and absorbs a unique-index collision (`:95-108`). The retry save at `:121` is bare. A third concurrent first-time submit of the same round collides again and the raw `DbUpdateException` escapes into `Submit`, which has no catch — unhandled 500. Plan §Phase1.4 promised "No EF-shaped exception reaches the controller"; `lessons.md:25` makes it a standing rule. `LeagueRepository.cs:262-267` is the precedent that does wrap it.
- **Fix**: Wrap `:121` in the same `when (IsPredictionCollision(ex))` guard and throw a domain exception the controller maps to 409, mirroring `LeagueModifiedException` at `LeaguesController.cs:350`.
- **Decision**: FIXED — new `PredictionConflictException` (`Application/Abstractions/Predictions/`), thrown by the repository on a second collision; `Submit` answers 409.

### F2 — Eligible-scorer query runs per match, twice per call, on both paths

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/server/PredictionLeague.Api/Controllers/PredictionsController.cs:374-379` and `:443`; `PlayerRepository.cs:46-51`
- **Detail**: `ListEligibleScorersAsync` issues two queries (a `TournamentSquads` `AnyAsync` probe plus the player list) and is called once per match. A `GET` for a `CorrectGoalScorer` league costs 3+2N queries; a `POST` costs ~5+2M+2N, because validation does the lookup per item and the view rebuild repeats it — ~45 queries for one 10-match save. The squad probe is invariant across the request. Second-order effect: `now` is frozen at `:178` before the loop, so every extra round-trip widens the window in which a match can cross kickoff and still be written. The plan anticipated the read path ("noted, not pre-optimized") but not the write path's doubling.
- **Fix A ⭐ Recommended**: Resolve candidates once per request (batched over the round's distinct team set), share the map between `ValidateItemAsync` and `BuildRoundViewAsync`, and hoist the squad probe out of the per-match path.
  - Strength: Kills the N+1 on both paths at once and shrinks the lock window, which is the slice's load-bearing rule.
  - Tradeoff: New repository method plus a signature change on two private helpers; touches working code.
  - Confidence: HIGH — the plan already sketched this shape in Performance Considerations.
  - Blind spot: No latency measurement; the window may be small enough never to bite.
- **Fix B**: Leave as-is; revisit in S-07 when standings add read volume.
  - Strength: Plan-sanctioned deferral; round sizes are tens of rows.
  - Tradeoff: The write path keeps paying double and the TOCTOU window stays as wide as the request.
  - Confidence: MEDIUM — fine at MVP scale, untested beyond it.
  - Blind spot: No load data either way.
- **Decision**: FIXED via Fix A — `ListEligibleScorersByTeamAsync(tournamentId, teamIds)` replaces the per-match read; candidates resolved once per request and shared by validation and the round view. GET and POST are now a constant handful of queries instead of 2N / 2M+2N.

### F3 — Deleting a match silently destroys every member's forecast

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: `PredictionConfiguration.cs:27-30`; `TournamentsController.cs:284`
- **Detail**: The `Match` FK cascades. `DELETE /api/matches/{id}` has no guard, so an admin fixing a typo by delete-and-recreate wipes user-generated predictions across every league on that tournament — and, once S-07 lands, any `AwardedPoints` with them. No warning, no recovery. The plan chose this deliberately and recorded the consequence, so this is a plan-level decision to revisit rather than an implementation slip.
- **Fix A ⭐ Recommended**: Keep the cascade; add a pre-check in `DeleteMatch` that 409s when predictions reference the match ("12 predictions reference this match").
  - Strength: No migration, no FK change; makes the destructive case loud while the cascade stays a backstop for league deletion. Matches the existing 409 shape.
  - Tradeoff: The guard lives in the controller, so a future delete path could bypass it.
  - Confidence: HIGH — one route, one count query.
  - Blind spot: An admin may genuinely need to delete a bad match; needs a follow-up force flag or an edit-instead workflow.
- **Fix B**: Switch the Match FK to `Restrict` and handle the failure.
  - Strength: The database enforces it; no path can bypass.
  - Tradeoff: New migration, and every delete path must translate the constraint failure or it becomes the 500 the plan was avoiding.
  - Confidence: MEDIUM — more moving parts for the same user-visible outcome.
  - Blind spot: Haven't checked whether tournament deletion relies on the cascade transitively.
- **Decision**: ACCEPTED — user's call: fixing a typo should be an edit, not delete-and-recreate; if a match really is deleted, its predictions go with it. Notifying affected members is queued as a follow-up.

### F4 — A CorrectGoalScorer league with no linked players cannot save at all

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `PredictionsController.cs:437-446`
- **Detail**: When the league scores `CorrectGoalScorer` the scorer pair is mandatory on every item, and `eligible.All(...)` is vacuously true on an empty candidate list — so with no players linked to either team every row returns `Invalid`, including plain score forecasts. The client warns beside the empty picker (`MatchPredictionRow.tsx:187-191`) but still submits. `CorrectGoalScorer` is `defaultActive`, and player→team linkage only arrived in this change, so leagues created earlier are in this state with no in-app way out.
- **Fix A ⭐ Recommended**: When the eligible set for a match is empty, treat the scorer pair as not-required for that match (still reject a submitted id that isn't a candidate).
  - Strength: Unblocks the member without an admin round-trip; keeps the rule wherever the data exists.
  - Tradeoff: S-07 must handle a null scorer on a league that scores it — score it as a miss.
  - Confidence: MEDIUM — small change, but it softens a rule the plan stated symmetrically.
  - Blind spot: Unverified whether S-07's planned scoring shape tolerates a null forecast.
- **Fix B**: Refuse `CorrectGoalScorer` at league creation when the tournament has no linked players.
  - Strength: Fails at the point the organizer can fix it.
  - Tradeoff: Touches league creation (outside this slice) and doesn't help leagues created before the import.
  - Confidence: MEDIUM.
  - Blind spot: Squads can be populated after league creation, so the check is a snapshot.
- **Decision**: FIXED, wider than Fix A — user's call: the scorer pair is now optional whenever it is scored, not only when the candidate list is empty. No pick means no points for it. Half a pair is still `Invalid`.

### F5 — Unplanned global JsonStringEnumConverter

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Scope Discipline
- **Location**: `src/server/PredictionLeague.Api/Program.cs:11-15` (landed in c8b266d)
- **Detail**: `AddJsonOptions` registers `JsonStringEnumConverter` globally. The commit body explains it as a pre-existing bug — enums went over the wire as ordinals while every client type is a string union — and the client types confirm it, so it is a real fix. But it changes the wire format of every endpoint including `MatchWithEventsDto.Status`, `PlayerResponse.Position` and `TournamentStatus`, which the plan explicitly fenced off ("No changes to ingest or `MatchWithEventsDto`"). Keep it; the gap is that the plan no longer describes what shipped.
- **Fix**: Add it to the plan's "What We're NOT Doing" named-exceptions list as a third exception, with the one-line reason.
- **Decision**: FIXED — recorded as the third named exception in `plan.md`.

### F6 — Scorer pair does not clear together

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/client/src/components/leagues/MatchPredictionRow.tsx:152-186`
- **Detail**: Phase 3 §4's contract ends "Both clear together." The credited-team select and the player select write independent `set({...})` calls, so clearing one leaves the other populated. `PredictionsPage` then posts a half pair and the server correctly returns `Invalid` ("Pick both…"). Data is never corrupted — the member eats a round-trip and an error where the plan wanted the state to be unreachable.
- **Fix**: In each `onChange`, clear the sibling field when the value is set to `""`.
- **Decision**: FIXED — clearing either select now clears both.

### F7 — Client pre-flight only validates scores

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/client/src/routes/leagues/PredictionsPage.tsx:136-169`
- **Detail**: The incomplete-row check covers home/away scores only, but the server also requires each scored card parameter and the scorer pair. A member in a `CorrectCardCount` league who fills only scores gets a clean-looking submit and every row `Invalid`. `toNumber` is also applied unguarded to card fields (`:156-158`), so `NaN` serializes to `null` and reports as "required" rather than "not a number".
- **Fix**: Derive the required-field set from `view.scoredParameters` in the same pre-flight loop that builds `incomplete`.
- **Decision**: FIXED differently — user's call: card counts follow the scorer and are optional when scored, so no client pre-flight is needed. A blank card field forfeits those points; the row still saves.

### F8 — Shipped API behaviors the plan never describes

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `PredictionsController.cs:150`, `:172`, `:189`, `:241-244`, `:375`
- **Detail**: Five sensible rules exist in code but not in the plan: an unknown `?round=` 404s rather than falling back; an empty `items` array is 400; a `matchId` repeated within one batch is `Invalid`; the post-save refreshed view is the round of the first resolvable item (a two-round batch returns one round's view — unreachable from the current UI); and the eligible-scorer list is additionally gated on `canPredict`, not just on the league scoring it. All are good calls — the plan text is what is stale.
- **Fix**: Fold the five into the Phase 2 contract as an addendum so the next review reads the plan as ground truth.
- **Decision**: FIXED — `plan.md` Phase 2 §2a addendum, covering the five behaviors plus the F4/F7 optional-field rules, the batched candidate read and the 409 conflict.

### F9 — Departed members stay visible in the reveal list

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `PredictionsController.cs:277`; `PredictionRepository.ListForMatchesAsync`
- **Detail**: The reveal is scoped by `LeagueId` only. `Prediction.UserId` carries no FK and `LeaveAsync` removes only the membership row, so someone who leaves a league keeps their prediction rows and keeps appearing by name in `GET revealed`. Probably right — they were a member when they forecast, and S-07 standings will need the rows — but it was decided implicitly.
- **Fix**: Document the retention rule on `IPredictionRepository.ListForMatchesAsync` so S-07 inherits it deliberately.
- **Decision**: FIXED — retention rule documented on the contract; behaviour unchanged.

### F10 — All 42 manual checkboxes flipped in one closeout commit

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `plan.md` Progress; commit e43cae5
- **Detail**: Every phase carries an "Implementation Note: pause here for manual confirmation before proceeding", but all four phases' manual rows went from `[ ]` to `[x]` in a single epilogue commit after phase 4, each stamped retroactively with its phase SHA. The automated rows were re-run independently during this review and pass. The manual rows have no per-phase evidence trail, so a claim like 2.16 ("two back-to-back saves both return 200") reads as attested rather than observed — and F1 shows a third concurrent save does not.
- **Fix**: Check manual rows at each phase gate rather than at closeout, so the stamp records when it was observed.
- **Decision**: SKIPPED

## Noted, not findings

- `src/client/src/leagues/drafts.ts` is new and unplanned, but is only the shared draft-state type; Phase 3 §4's "holds no server state of its own" forces it out of the row component. Justified extraction.
- `SubmitPredictionsRequest.Items` has no size cap, but duplicate and unknown ids are rejected before any query, so cost stays bounded by tournament size.
- `IsPredictionCollision` re-implements `LeagueRepository`'s `IsUniqueViolationOf` — third copy of that predicate, worth lifting into a shared helper eventually.
- `Microsoft.OpenApi` 2.0.0 carries a known high-severity advisory (NU1903) — pre-existing, unrelated to this change.
