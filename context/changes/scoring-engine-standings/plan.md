# Scoring Engine & Standings Implementation Plan

## Overview

S-07, the roadmap's north star. Every prediction a member submitted in S-06 gets scored against the real match result using the league's own `ScoringRule` rows, the points are persisted on `Prediction.AwardedPoints`, and members see a standings table plus results and points on the round view they already use (FR-011, FR-012, US-01).

The slice also closes the input-data gap the wedge depends on: granular scoring parameters (`CorrectGoalScorer`, the three card parameters) currently have **no** data source, because `MatchEvent` rows are written by exactly one code path — `FixtureIngestService` — and API ingest is deferred. This plan adds an admin goal/card entry surface so all six parameters score against real data.

## Current State Analysis

**What exists:**

- `Prediction` is fully persisted (`AddPredictions` migration), unique on `(LeagueId, UserId, MatchId)`, and already carries `AwardedPoints` as a nullable int that nothing writes (`Prediction.cs:38`). **No migration is required by this slice** — the column is already in the database.
- Six scoring parameters, not four: `ExactScore`, `CorrectOutcome`, `CorrectGoalScorer`, `CorrectCardCount`, `CorrectYellowCards`, `CorrectRedCards` (`Enums.cs:26`). All six are selectable in S-04's editor.
- The scorer forecast is a **pair** — `PredictedFirstScorerPlayerId` + `PredictedFirstScorerTeamId` — deliberately shaped so a player credited to the opposing team expresses an own goal, matching `MatchEvent(PlayerId, TeamId)` (`Prediction.cs:20-25`).
- `MatchEventType` is a seeded dictionary: ids 1-6, `NormalGoal` / `OwnGoal` / `Penalty` / `MissedPenalty` (all `Category = Goal`) and `YellowCard` / `RedCard` (`Category = Card`) — `MatchEventTypeConfiguration.cs:20-26`.
- A result reaches the system through `PUT /api/matches/{matchId}` (admin sets `Status = Finished` plus scores) and, when ingest runs, through `FixtureIngestService.IngestTournamentAsync`.
- Scoring rules are **locked once any match in the tournament has kicked off** (`LeaguesController.cs:240`), so rules cannot change under already-awarded points.
- `IPlayerRepository.ListEligibleScorersByTeamAsync(tournamentId, teamIds)` already resolves both squads for a match and backs S-06's scorer picker.
- Conventions in force: clock-as-parameter (`AnyKickedOffAsync`, `IsLocked`), 404-masking per-league visibility, authorization always off `League.OrganizerUserId` never `LeagueMembership.Role` (lessons.md:32), and no ORM-specific exception types in controllers (lessons.md:25).

**What is missing:**

- No scoring logic anywhere in the solution. `AwardedPoints` is null on every row.
- No write path for `MatchEvent` other than API ingest. The admin match form (`MatchFormPage.tsx`) edits teams / kickoff / status / scores / round only — a league scoring `CorrectGoalScorer` has nothing to score against today.
- No standings read, no standings screen. The round view shows scores but no points.
- No server test project (`prediction-league.slnx` lists five projects, none of them tests). Verification for this slice is manual, by explicit decision (see Open Risks).

## Desired End State

An admin enters a finished match's score and its goals/cards. Immediately, without any further action, every member of every league on that tournament has points for that match, computed from that league's own rules. Members open their league, see a standings table with their position, open the predictions round view, and see each finished match's result next to what they predicted and what it earned them. Correcting the result later re-scores the match automatically and the table moves.

Verified by: entering a result for a match with two leagues on the same tournament configured with different rule sets, and confirming each league's standings reflect its own rules; then correcting the score and confirming both tables move.

### Key Discoveries:

- `Prediction.AwardedPoints` already exists in the schema (`20260815192037_AddPredictions.cs:30`) — no migration in this slice.
- **`MissedPenalty` carries `Category = Goal`** (`MatchEventTypeConfiguration.cs:24`). It is a shot, not a goal. First-scorer resolution must exclude it by `Code`, not trust the category.
- Ingest already delete-and-replaces a match's events (`FixtureIngestService.cs:172`, `match.Events.Clear()`), which is the semantics the admin editor mirrors.
- Rules are frozen after first kickoff, so recompute only ever has to answer to *result* corrections, never rule edits.
- `IPredictionRepository.ListForMatchesAsync` is deliberately league-scoped and not membership-scoped (`IPredictionRepository.cs:20-25`), with a comment asserting leavers keep their standings points. This plan's standings decision (current members only) contradicts that comment — the comment gets corrected in Phase 4.
- The Api layer never sees Identity types; display names are resolved by explicit join in Infrastructure (`PredictionRepository.cs:47`).

## What We're NOT Doing

- No automated tests, no test project. Verification is manual (user decision — recorded under Open Risks).
- No database migration — nothing in this slice changes the schema.
- No standings history, snapshots, or rank-change indicators; no audit trail of point changes.
- No notification when a correction changes someone's points.
- No re-scoring triggered by scoring-rule edits (impossible: rules lock at first kickoff).
- No leaderboard across leagues, no per-round standings, no charts.
- No changes to prediction submission, the lock, or the reveal contract beyond adding points to the payloads.
- No CSV import for match events, no per-event add/delete endpoints — the editor is replace-all.
- No changes to the API-Football client, its rate-limit handling, or the ingest timer schedule.

## Implementation Approach

A **pure scoring function in Domain** takes a prediction, a match result, that match's events, and a league's rules, and returns points. It has no I/O, no EF, and no clock — it is the one place scoring correctness lives and the only place a rule's meaning is decided.

An **Application-layer scoring service** wraps it: given a match id, load the match with its events, load every league bound to that tournament with its rules, load every prediction for that match, compute, and write `AwardedPoints` in one save. The service is idempotent — running it twice produces the same rows — which makes it safe to call from every path that can change a result: the admin match save, the admin event save, `FixtureIngestService`, and an explicit admin rescore endpoint. A match that is not `Finished` (or lacks scores) has its `AwardedPoints` set back to null, so reverting a result cleanly un-scores it.

**Standings** are a grouped sum over `AwardedPoints`, joined to current memberships, computed per request. There is no second store to drift.

On the client, the league page gains a standings card linking to a full standings route, and the existing round view grows a result-and-points column on finished matches.

## Critical Implementation Details

**Event-type gotcha.** `MissedPenalty` is seeded with `Category = Goal`. First-scorer resolution must filter to `Category == Goal` **and** exclude the `MissedPenalty` code; every other goal type (including `OwnGoal` and `Penalty`) counts. Card counting filters on `Category == Card`; yellows and reds are separated by `Code`.

**Ordering within a request.** Scoring reads the match through a repository, so it must run *after* the match's `SaveChangesAsync`, never before — otherwise it scores the pre-edit result. The admin write endpoints therefore save, then score, then return.

**Scoring failure after a committed save.** The match write commits before scoring runs, so a scoring failure leaves a saved result with stale points. Do not attempt to roll the match write back, and do not let the failure surface as a 500 either — an admin who sees "save failed" on a write that *did* land will re-save, and the only thing that actually repairs the state is the rescore endpoint.

The contract instead is **partial success, reported**: every admin write that scores (`CreateMatch`, `UpdateMatch`, `PUT .../events`) catches a scoring failure, logs it at error level, and returns `200` with the normal body plus `ScoringFailed = true` and a message naming `POST /api/matches/{id}/rescore` as the remedy. The client renders that as a warning banner on the match form — "the result saved, its points did not" — rather than a save error. This mirrors `PredictionsController.Submit`, where a well-formed request that could not be fully applied still answers 200 and carries per-item verdicts.

## Phase 1: Scoring engine (Domain, pure)

### Overview

The rules of the game as a pure function: no database, no clock, no EF. Everything downstream calls this and nothing else decides what a point is worth.

### Changes Required:

#### 1. Match result inputs

**File**: `src/server/PredictionLeague.Domain/Scoring/MatchOutcome.cs`

**Intent**: A value type describing what actually happened in a match, in the shape scoring needs: final scores plus the goal and card facts derived from the event list. Keeping this separate from `Match`/`MatchEvent` is what lets the engine stay free of persistence concerns and lets a reader see the whole scoring input on one screen.

**Contract**: `MatchOutcome` exposes home/away score, the first scorer as a `(PlayerId, TeamId)` pair or none, and total / yellow / red card counts. A factory builds it from a `Match` plus its `MatchEvent` list and the event-type dictionary, applying the two filters that matter: first scorer is the lowest-ordering event whose type is `Category == Goal` and whose `Code != "MissedPenalty"`; card counts come from `Category == Card`, split by `Code` into `YellowCard` / `RedCard`, with the total being every card event.

**Ordering key — load-bearing.** Goal events are ordered by `(Minute, MinuteExtra ?? 0, MatchEventTypeId, PlayerId)`, **in memory**, after the events have loaded. Every component is admin-entered data, so the same recorded facts always name the same first scorer. `MatchEvent.Id` must **never** appear in the ordering: it is a fresh `Guid.NewGuid()` on every replace-all save (`FixtureIngestService.cs:207`), so ordering by it would let a no-op re-save of identical events move `CorrectGoalScorer` points between members. Sorting in memory rather than in SQL also keeps the comparison off SQL Server's Guid/`uniqueidentifier` collation, which does not match `Guid.CompareTo`. A null `MinuteExtra` means "no stoppage time" and sorts as `0`, ahead of `90+1`.

#### 2. The scoring function

**File**: `src/server/PredictionLeague.Domain/Scoring/PredictionScorer.cs`

**Intent**: Given one prediction, one `MatchOutcome`, and a league's rules, return the points that prediction earned. Rules stack cumulatively — a member who nails the exact score in a league that scores both `ExactScore` and `CorrectOutcome` collects both, because each configured rule means exactly what the organizer's editor said it means.

**Contract**: A static entry point taking the predicted values, the outcome, and the league's `(ScoringParameter, Points)` pairs, returning an `int`. Parameter semantics, each awarded only when the league configures it:

- `ExactScore` — predicted home and away both equal the actual.
- `CorrectOutcome` — `Sign(predictedHome - predictedAway) == Sign(actualHome - actualAway)`; a predicted draw matches an actual draw.
- `CorrectGoalScorer` — the prediction's player **and** credited team both equal the outcome's first scorer pair. A prediction missing either half, or a match with no qualifying goal, awards nothing.
- `CorrectCardCount` / `CorrectYellowCards` / `CorrectRedCards` — the predicted int equals the corresponding count. A null prediction (the member left it blank) awards nothing; a match with no card events counts as zero, so a member who predicted 0 is correct.

A parameter the league does not configure contributes nothing, and an unconfigured parameter's `Points` value is never read. Points come only from `ScoringRule.Points` — no literal point values in this file.

### Success Criteria:

#### Automated Verification:

- Server builds: `cd src/server && dotnet build`

#### Manual Verification:

- Reading the file, each of the six parameters maps to exactly one documented rule and no point value is hardcoded.
- `MissedPenalty` cannot be selected as the first scorer.
- The goal ordering key contains no `MatchEvent.Id` and runs in memory; null `MinuteExtra` sorts as 0.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human before proceeding.

---

## Phase 2: Scoring service and triggers

### Overview

Wire the engine to data and to every path that can change a result. After this phase, an admin saving a finished match's score produces points, and standings data exists even though nothing renders it yet.

### Changes Required:

#### 1. Scoring service contract

**File**: `src/server/PredictionLeague.Application/Abstractions/Scoring/IMatchScoringService.cs`

**Intent**: The one entry point everything else calls to (re)score a match. Lives in Application so both `PredictionLeague.Api` and `PredictionLeague.Infrastructure`'s ingest service can depend on it without either depending on the other.

**Contract**: `Task<MatchScoringResult> ScoreMatchAsync(Guid matchId, CancellationToken)`, where the result reports predictions scored and leagues touched (for logging and the rescore endpoint's response). Idempotent: calling it twice in a row leaves identical rows. A match id that does not exist is a no-op result, not an exception.

#### 2. Scoring service implementation

**File**: `src/server/PredictionLeague.Infrastructure/Scoring/MatchScoringService.cs`

**Intent**: Load the match with its events, every league bound to that tournament with its rules, and every prediction for that match; compute points per (league, prediction) via the Phase 1 engine; write them in one save.

**Contract**: Depends on `IMatchRepository`, `ILeagueRepository`, `IPredictionRepository`, `IMatchEventTypeRepository`, and `ILogger`. Behaviour:

- A match whose `Status != Finished`, or whose `HomeScore`/`AwayScore` is null, has every prediction's `AwardedPoints` set back to **null** — reverting a result un-scores it rather than freezing stale points.
- Otherwise every prediction gets a non-null value, including `0`. Null means "not scored"; `0` means "scored, earned nothing" — standings and the UI depend on that distinction.
- A league whose tournament matches but which has no rules configured scores every prediction as `0`.
- The service computes values only — it never mutates a tracked entity. It hands the whole map to `IPredictionRepository.SetAwardedPointsAsync`, which owns the single save, so a match is never half-scored.

Registered in `DependencyInjection.AddInfrastructure` alongside the repositories.

#### 3. Repository reads the service needs

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/IMatchRepository.cs`, `ILeagueRepository.cs`, `IPredictionRepository.cs` (+ their EF implementations)

**Intent**: Three additions, each the narrowest read that serves the service.

**Contract**:

- `IMatchRepository.GetWithEventsAsync(Guid matchId, …)` — a match plus its `Events`, tracked-or-not per the existing convention in `GetByExternalFixtureIdAsync`.
- `ILeagueRepository.ListByTournamentWithRulesAsync(Guid tournamentId, …)` — every league on the tournament with `ScoringRules` included. Follow `GetWithDetailAsync`'s include style.
- `IPredictionRepository.ListForMatchAsync(Guid matchId, …)` — every prediction for one match across **all** leagues, **untracked**, matching `ListForUserAsync`'s documented stance (`IPredictionRepository.cs:12-13`: the read path renders, the write path re-reads).
- `IPredictionRepository.SetAwardedPointsAsync(Guid matchId, IReadOnlyDictionary<Guid, int?> pointsByPredictionId, …)` — the write half, and the reason the read stays untracked. Every other write in this layer is an intent-named repository method that owns its save (`UpsertManyAsync`, `ReplaceScoringRulesAsync`, `JoinAsync`, `TransferOrganizerAsync`); handing a tracked graph out to a service that mutates it and calls the generic `SaveChangesAsync` would break that convention and make "one save per match" an unenforceable promise — anything else tracked in the same scoped context would flush with it. A `null` value un-scores that prediction. One `SaveChangesAsync` inside the method covers the whole match.

#### 4. Trigger: admin match writes

**File**: `src/server/PredictionLeague.Api/Controllers/TournamentsController.cs`

**Intent**: Scoring follows any admin write that can change a result. `CreateMatch` and `UpdateMatch` call the service after their existing `SaveChangesAsync`; `DeleteMatch` does not (predictions cascade away with the match, `PredictionConfiguration.cs:28`). The CSV import path needs no trigger: it only ever inserts new matches (duplicates are rejected as conflicts), and a match that did not exist has no predictions to score.

**Contract**: `IMatchScoringService` injected into the controller; both write endpoints score the saved match before returning. Ordering is load-bearing — score after save, never before. Both responses gain `bool ScoringFailed` and an optional `ScoringMessage`, per the partial-success contract in Critical Implementation Details: a scoring exception is caught, logged, and reported on a `200`, never rethrown into a 500 on a write that already committed.

#### 5. Trigger: ingest

**File**: `src/server/PredictionLeague.Infrastructure/Football/FixtureIngestService.cs`

**Intent**: The timer-driven path that will eventually replace manual entry must produce points the same way the admin path does, without a second implementation of scoring.

**Contract**: `IMatchScoringService` injected; called once per fixture immediately after that fixture's `SaveChangesAsync` (the existing per-match save at `FixtureIngestService.cs:151`). Scoring failure for one fixture is logged and does not abort the run — a partial ingest already leaves each processed match consistent, and the rescore endpoint recovers the rest.

#### 6. Explicit rescore endpoint

**File**: `src/server/PredictionLeague.Api/Controllers/MatchesController.cs` (new)

**Intent**: An admin-only escape hatch for the one failure this design can produce — a result that committed while scoring failed.

A **new controller**, not a sixth match route bolted onto `TournamentsController` (365 lines, already owning tournaments, matches and CSV import). This is the same reasoning Phase 4 applies to `StandingsController`, and the routes are already absolute — `TournamentsController.cs:249` serves `/api/matches/{matchId}` from a `api/[controller]`-routed class, which is precisely the seam this split removes. Phase 3's four event/lookup endpoints land here too. Moving the existing `GET`/`PUT`/`DELETE /api/matches/{matchId}` off `TournamentsController` is **out of scope** — this slice only stops adding to the pile.

**Contract**: `POST /api/matches/{matchId}/rescore`, `AdminOnly`, no request body, returns the `MatchScoringResult` counts. The controller resolves the match itself (`IMatchRepository.GetByIdAsync`) and answers 404 when it is missing — the service's "unknown id is a no-op result" rule keeps it exception-free but cannot distinguish a missing match from one with no predictions, so existence is the controller's check to make.

### Success Criteria:

#### Automated Verification:

- Server builds: `cd src/server && dotnet build`

#### Manual Verification:

- Admin sets a match to Finished with a score; querying the database shows `AwardedPoints` non-null for every prediction on that match, with values matching each league's rules.
- Two leagues on the same tournament with different rule sets receive different points for the same prediction values.
- Editing the score on that match changes the awarded points without any further action.
- Reverting the match to Scheduled sets `AwardedPoints` back to null for its predictions.
- `POST /api/matches/{id}/rescore` as admin returns counts and leaves the same values; as a non-admin returns 403; an unknown match id returns 404.
- Reading `CreateMatch` / `UpdateMatch`, a scoring exception cannot escape as a 500 — it is caught, logged, and reported as `ScoringFailed` on a 200.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human before proceeding.

---

## Phase 3: Admin match-event entry

### Overview

Give the granular scoring parameters real data. Without this phase, `CorrectGoalScorer` and the card rules score zero for everyone forever, which is indistinguishable from a scoring bug.

### Changes Required:

#### 1. Events read + replace endpoints

**File**: `src/server/PredictionLeague.Api/Controllers/MatchesController.cs` (created in Phase 2 §6)

**Intent**: Let an admin see and set a match's goals and cards. Replace-all rather than per-row, mirroring how ingest already rebuilds a match's event set, so both writers share one semantic.

**Contract**:

- `GET /api/matches/{matchId}/events` (`AdminOnly`) — the match's events as `(id, matchEventTypeId, playerId, teamId, minute, minuteExtra)` plus resolved player/team/type display names for rendering. Backed by a **new** `MatchEventEditDto` in `Application/Abstractions/Persistence`. The existing `MatchEventDto` (`MatchWithEventsDto.cs`) is deliberately left alone: it carries names but no ids, it backs the admin tournament-detail projection (`MatchRepository.ListByTournamentAsync`), and widening it would drag that read into this slice for no gain.
- `PUT /api/matches/{matchId}/events` (`AdminOnly`) — body is the full event list; the stored set is replaced with it. Validation, each rejected as 400 with a specific message: `MatchEventTypeId` must exist in the dictionary; `TeamId` must be one of the match's two teams; `PlayerId` must be a player eligible for that match (same source as the prediction picker); `Minute` in 0-130; `MinuteExtra` null or 0-30. After saving, the match is re-scored through `IMatchScoringService`, and the response carries the same `ScoringFailed` / `ScoringMessage` pair as the match writes.
- `GET /api/matches/{matchId}/eligible-players` (`AdminOnly`) — both squads for the match, reusing `IPlayerRepository.ListEligibleScorersByTeamAsync` so the admin picker and the member's scorer picker can never disagree about who is eligible.
- `GET /api/match-event-types` — the seeded dictionary for the type dropdown. Authenticated; it is reference data with nothing sensitive in it.

#### 2. Repository support

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/IMatchRepository.cs` (+ EF implementation)

**Intent**: Replace a match's events in one tracked graph.

**Contract**: `ReplaceEventsAsync(Guid matchId, IReadOnlyList<MatchEvent> events, …)` — clears the tracked `Events` collection and adds the new set, matching `FixtureIngestService.cs:172`'s `Clear()`-then-add pattern so orphan deletion behaves identically. Saving is the caller's call, consistent with the other repositories.

#### 3. Events editor on the match form

**File**: `src/client/src/components/admin/MatchEventsFieldset.tsx` (new), `src/client/src/routes/admin/matches/MatchFormPage.tsx`

**Intent**: An events section on the existing match edit page — add a row (type, player, credited team, minute, optional extra), remove a row, save the whole list. Edit mode only: a match being created has no id to hang events on yet.

**Contract**: The fieldset owns the event rows and calls `PUT /api/matches/{matchId}/events`; the player select is populated from `/api/matches/{matchId}/eligible-players`, the team select is the match's two teams, and the type select comes from `/api/match-event-types`. Follows the client's existing conventions: `apiFetch` from `@/lib/api`, shadcn primitives, one component per file under `components/<feature>/`. Types land in `src/client/src/admin/types.ts` next to the existing admin response types.

### Success Criteria:

#### Automated Verification:

- Server builds: `cd src/server && dotnet build`
- Client builds (type errors fail it): `cd src/client && npm run build`
- Client lints: `cd src/client && npm run lint`

#### Manual Verification:

- An admin adds two goals and a yellow card to a finished match, saves, reloads the form, and sees exactly those events.
- Saving events immediately changes standings points for a league scoring `CorrectGoalScorer` — no separate rescore needed.
- A member who predicted the actual first scorer with the correct credited team earns the scorer points; one who named the right player but the wrong team does not.
- An own goal entered as (player from team A, credited to team B) awards the member who predicted that same pair.
- A `MissedPenalty` event entered before the first real goal does **not** become the first scorer.
- Submitting an event whose player is not in either squad is rejected with a specific message.
- Two goals entered in the same minute: saving the unchanged list again leaves the scorer points exactly where they were.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human before proceeding.

---

## Phase 4: Standings and points read APIs

### Overview

Expose what Phase 2 computed: a league's table, and per-prediction points on the surfaces that already show forecasts.

### Changes Required:

#### 1. Standings read

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/StandingRowDto.cs` (new), `IPredictionRepository.cs` (+ EF implementation)

**Intent**: One query returning a league's table: every current member with their total points and how many matches they have been scored on.

**Contract**: `ListStandingsAsync(Guid leagueId, …)` returns `StandingRowDto(Guid UserId, string DisplayName, int Points, int ScoredMatches, int PredictionsMade)`. Built from `LeagueMembership` **left-joined** to predictions so a member who never predicted appears with zero — the table is the roster, not just the players. Members who have left the league do not appear. Display name resolved by explicit join to `Context.Users`, as in `ListForMatchesAsync`. Ordered points descending, then display name.

**Also**: correct the stale comment at `IPredictionRepository.cs:20-25`, which asserts that a leaver's points survive in standings. That is no longer the chosen behaviour and the comment must not outlive it.

#### 2. Standings endpoint

**File**: `src/server/PredictionLeague.Api/Controllers/StandingsController.cs` (new)

**Intent**: A member-facing read of their league's table. A separate controller rather than growing `LeaguesController` (425 lines, already owning identity, rules and membership), mirroring how `PredictionsController` was split out in S-06.

**Contract**: `GET /api/leagues/{leagueId:guid}/standings`, `[Authorize]`, route `api/leagues/{leagueId:guid}/standings`. Visibility identical to `PredictionsController.LoadVisibleLeagueAsync` — organizer or membership row, 404 for anything else so membership never leaks. Response carries league id/name, the caller's own user id (so the client can highlight their row), and the rows. **Rank is shared on ties**: equal point totals get the same rank, and the next distinct total skips accordingly (1, 2, 2, 4). Rank is computed server-side so every surface agrees.

#### 3. Points on the prediction surfaces

**File**: `src/server/PredictionLeague.Api/Controllers/PredictionsController.cs`, `src/server/PredictionLeague.Application/Abstractions/Persistence/MemberPredictionDto.cs`, `PredictionRepository.cs`

**Intent**: The round view should show what a forecast earned, next to the forecast itself.

**Contract**: `OwnPredictionResponse` and `RevealedPredictionResponse` each gain `int? AwardedPoints`, sourced from `Prediction.AwardedPoints` (null = not scored yet). `MemberPredictionDto` gains the same field and the projection carries it. No change to the lock, the reveal rule, or which matches appear — points ride along on payloads that already exist.

### Success Criteria:

#### Automated Verification:

- Server builds: `cd src/server && dotnet build`

#### Manual Verification:

- `GET /api/leagues/{id}/standings` as a member returns every current member, including one who never predicted (0 points).
- The same call for a league the caller is not in returns 404, not 403.
- Two members on equal points share a rank, and the next member's rank skips.
- A member who leaves the league disappears from the table on the next read.
- The round view's own prediction and revealed predictions carry `awardedPoints` for finished matches and null for unfinished ones.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human before proceeding.

---

## Phase 5: Client — standings and results

### Overview

The payoff screen. Points and position become visible without anyone reading the database.

### Changes Required:

#### 1. Standings types and card

**File**: `src/client/src/leagues/types.ts`, `src/client/src/components/leagues/StandingsCard.tsx` (new), `src/client/src/routes/leagues/LeagueDetailPage.tsx`

**Intent**: Make the league page lead with the table. The card shows the top rows and links to the full standings; the page keeps its existing card composition (invite / scoring / members).

**Contract**: `StandingsCard` fetches `/api/leagues/{id}/standings`, renders rank, name, points for the leading rows, highlights the caller's row, links to `/app/leagues/:id/standings`, and degrades to an explanatory empty state before any match is scored. When no row matches the caller's user id, nothing is highlighted and the table renders normally — visibility is organizer-**or**-membership while the roster is memberships only, so an organizer who left without transferring the league can legitimately see a table they are not in. Same rule on the full standings route. Added to `LeagueDetailPage`'s card grid alongside `ScoringCard` and `MembersCard`, following their prop shape.

#### 2. Standings route

**File**: `src/client/src/routes/leagues/StandingsPage.tsx` (new), `src/client/src/routes/index.tsx`

**Intent**: The full table on its own screen, with room to breathe.

**Contract**: Route `/app/leagues/:id/standings` under `RequireAuth`, alongside the existing `/app/leagues/:id/predictions`. Renders rank, member, points, and matches scored for every row; the caller's row is marked. 404 from the API renders the same "not found or not a member" copy `LeagueDetailPage` uses.

#### 3. Results and points on the round view

**File**: `src/client/src/components/leagues/MatchPredictionRow.tsx`, `src/client/src/routes/leagues/PredictionsPage.tsx`, `src/client/src/leagues/types.ts`

**Intent**: Satisfy FR-012's "past matches" where the member already looks: a finished match shows the actual result, the member's forecast, and what it earned — and the reveal becomes a scoreboard, showing every member's points for that match.

**Contract**: `MatchPredictionRow` renders `awardedPoints` on the caller's own prediction and on each revealed prediction, shown only when non-null so an unscored finished match reads as "not scored yet" rather than zero. No new fetch and no client-side scoring: the values come from payloads Phase 4 already extended. The round switcher, the lock rendering, and the reveal rule are untouched.

### Success Criteria:

#### Automated Verification:

- Client builds: `cd src/client && npm run build`
- Client lints: `cd src/client && npm run lint`

#### Manual Verification:

- A member opens their league and sees the standings card with their own row highlighted; the full standings route shows the same order and ranks.
- A league with nothing scored yet shows an empty state, not a broken table.
- After the admin scores a match, reloading standings shows the new totals.
- The round view shows the result and points on finished matches and nothing extra on upcoming ones.
- The reveal on a kicked-off match shows each member's forecast with their points once the match is scored.
- Two leagues with different rules, same tournament, same member: the two standings pages show different totals.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human.

---

## Testing Strategy

There is no test project in either unit and this slice does not add one (user decision). Verification is manual and is specified per phase above. The end-to-end scenario that exercises the whole slice:

### Manual Testing Steps:

1. As admin, seed a tournament with at least two matches in one round, with squads linked so the scorer picker is populated.
2. As two different users, create two leagues on that tournament with **different** rule sets — one scoring `ExactScore` + `CorrectOutcome`, the other adding `CorrectGoalScorer` and `CorrectYellowCards`.
3. Have both users join both leagues and submit differing forecasts before kickoff.
4. As admin, set match 1 to Finished with a score, then add its goals and cards.
5. Confirm each league's standings reflect its own rules and that the same forecast earns different totals in the two leagues.
6. Correct match 1's score; confirm both tables move with no further action.
7. Enter a `MissedPenalty` before the first goal; confirm the first-scorer award is unaffected.
8. Enter an own goal and confirm the member who predicted (player from A, credited to B) is awarded.
9. Revert match 1 to Scheduled; confirm points disappear from both tables.
10. Have one member leave a league; confirm they drop out of that league's table and the other league is unaffected.

## Performance Considerations

Scoring loads one match, its events, the tournament's leagues with rules, and that match's predictions — bounded by (leagues on a tournament × members per league), which is friend-group scale. Standings are one grouped query per view. Neither is worth caching at `target_scale: low/small`, and caching would reintroduce the drift the persisted-points-plus-aggregate design exists to avoid.

## Migration Notes

None. `Prediction.AwardedPoints` and the `MatchEvents` table already exist; this slice adds no schema change and therefore no migration to gate on prod deploy. Existing predictions on already-finished matches stay null until someone re-saves the match or calls the rescore endpoint — which is the documented way to backfill them.

## References

- Roadmap slice S-07: `context/foundation/roadmap.md:199`
- PRD FR-011 / FR-012 and the scoring-correctness guardrail: `context/foundation/prd.md:82`
- Upstream slice (predictions, entity shape, reveal contract): `context/changes/submit-locked-predictions/plan.md`
- Lessons in force: `context/foundation/lessons.md:25` (no ORM exceptions in controllers), `:32` (authorize off `OrganizerUserId`)
- Event dictionary seed (the `MissedPenalty` trap): `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/MatchEventTypeConfiguration.cs:20`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Scoring engine (Domain, pure)

#### Automated

- [x] 1.1 Server builds: `cd src/server && dotnet build` — 15a26d2

#### Manual

- [ ] 1.2 Each of the six parameters maps to one documented rule; no point value hardcoded
- [ ] 1.3 `MissedPenalty` cannot be selected as the first scorer
- [ ] 1.4 Goal ordering key excludes `MatchEvent.Id`, runs in memory, null `MinuteExtra` sorts as 0

### Phase 2: Scoring service and triggers

#### Automated

- [x] 2.1 Server builds: `cd src/server && dotnet build` — 2b72209

#### Manual

- [ ] 2.2 Finishing a match writes non-null `AwardedPoints` matching each league's rules
- [ ] 2.3 Two leagues with different rules on one tournament get different points for identical forecasts
- [ ] 2.4 Editing the score re-scores automatically
- [ ] 2.5 Reverting a match to Scheduled sets `AwardedPoints` back to null
- [ ] 2.6 `POST /api/matches/{id}/rescore` works for admin, 403 for non-admin, 404 for unknown match
- [ ] 2.7 A scoring exception cannot escape as a 500; it returns 200 with `ScoringFailed`

### Phase 3: Admin match-event entry

#### Automated

- [x] 3.1 Server builds: `cd src/server && dotnet build`
- [x] 3.2 Client builds: `cd src/client && npm run build`
- [x] 3.3 Client lints: `cd src/client && npm run lint`

#### Manual

- [ ] 3.4 Events entered on a match survive a reload of the form
- [ ] 3.5 Saving events immediately changes scorer/card points
- [ ] 3.6 Correct player + correct credited team earns the scorer points; wrong team does not
- [ ] 3.7 An own-goal pair is awarded correctly
- [ ] 3.8 A `MissedPenalty` before the first goal does not become the first scorer
- [ ] 3.9 An ineligible player is rejected with a specific message
- [ ] 3.10 Re-saving an unchanged same-minute goal pair leaves scorer points unchanged

### Phase 4: Standings and points read APIs

#### Automated

- [ ] 4.1 Server builds: `cd src/server && dotnet build`

#### Manual

- [ ] 4.2 Standings list every current member, including one with no predictions (0 points)
- [ ] 4.3 A non-member gets 404, not 403
- [ ] 4.4 Equal totals share a rank and the next rank skips
- [ ] 4.5 A member who leaves disappears from the table
- [ ] 4.6 Round view and reveal carry `awardedPoints`, null before scoring

### Phase 5: Client — standings and results

#### Automated

- [ ] 5.1 Client builds: `cd src/client && npm run build`
- [ ] 5.2 Client lints: `cd src/client && npm run lint`

#### Manual

- [ ] 5.3 Standings card on the league page highlights the caller's row and links to the full table
- [ ] 5.4 An unscored league shows an empty state, not a broken table
- [ ] 5.5 Standings update after the admin scores a match
- [ ] 5.6 Finished matches in the round view show result and points; upcoming ones show neither
- [ ] 5.7 The reveal shows each member's forecast with their points
- [ ] 5.8 Two leagues on one tournament show different totals for the same member
