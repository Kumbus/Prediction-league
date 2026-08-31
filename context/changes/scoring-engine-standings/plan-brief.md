# Scoring Engine & Standings — Plan Brief

> Full plan: `context/changes/scoring-engine-standings/plan.md`

## What & Why

S-07, the roadmap's north star and the only slice that exercises the product wedge end-to-end: predictions scored automatically per each league's own custom rules, with points and standings appearing without anyone entering a result by hand (FR-011, FR-012, US-01). Everything shipped so far — rules, invites, predictions — only matters if this works.

## Starting Point

`Prediction.AwardedPoints` exists in the schema and is null on every row; nothing in the solution scores anything. Six scoring parameters are selectable, and the scorer forecast is already stored as a player + credited-team pair so own goals are expressible. Results reach the system through the admin match form (score/status only) and, when it returns, through `FixtureIngestService`. The gap that matters: `MatchEvent` rows are written by exactly one code path — API ingest — which is deferred, so the granular parameters have no data source at all today.

## Desired End State

An admin enters a finished match's score and its goals and cards. Without any further action, every member of every league on that tournament has points for that match computed from that league's own rules. Members open their league and see a standings table with their position, and the round view they already use shows each finished match's result beside what they predicted and what it earned. Correcting a result later re-scores the match and the table moves.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Granular-event data gap | Add an admin goal/card entry surface in this slice | Without it `CorrectGoalScorer` and the card rules score zero for everyone forever, which is indistinguishable from a scoring bug and leaves the wedge unproven. |
| Scoring trigger | One service, called from admin match save, admin event save, ingest, and an explicit rescore endpoint | Matches the only real data path today (manual entry) while staying compatible with the football-API job that will later check results and recompute the same way. |
| Rule stacking | Cumulative | Each configured rule means what the organizer's editor said it means; no invisible precedence to explain. |
| Standings storage | Persisted `AwardedPoints`, aggregated on read | One source of truth that cannot drift — wrong standings is the guardrail that kills the product; per-match breakdown comes free. |
| First scorer | Earliest goal event, player **and** credited team must both match | Uses the exact pair S-06 designed, so own goals score with no special case. |
| Same-minute goals | Ordered by `(Minute, MinuteExtra ?? 0, MatchEventTypeId, PlayerId)`, never by `MatchEvent.Id` | Replace-all mints new Guids on every save, so an Id-based tie-break would move scorer points on a no-op re-save (plan review F1). |
| Scoring failure after commit | Write endpoints return 200 with `ScoringFailed` + a rescore hint, never a 500 | The result *did* save; telling the admin "save failed" invites a re-save that fixes nothing (plan review F3). |
| New match/event routes | New `MatchesController`, not more routes on `TournamentsController` | Same split rationale the plan applies to `StandingsController`; the `/api/matches/...` routes are already absolute (plan review F4). |
| Missing event data | Score it — no events means zero | No half-scored state; scoring stays a pure function of what is recorded, and one recompute fixes a late detail entry. |
| Result corrections | Auto re-score the match on every save | Standings always agree with recorded results; a forgotten manual click would leave permanently wrong standings. |
| Tiebreak | Shared rank (1, 2, 2, 4) | A tie is a tie; no invented second key. |
| Standings roster | Current members only, zero-prediction members included | The table reads as the league roster; contradicts a stale S-06 comment, which the plan corrects. |
| Standings UI | Card on the league page + a full standings route | Makes the payoff the league page's headline without a 20-row table dominating it. |
| Past/upcoming matches | Extend the existing round view | Points appear where the forecast that earned them lives; no third league screen. |
| Event entry shape | Replace-all list on the match edit page | Same semantics ingest already uses (`Clear()`-then-add), one write path, natural for corrections. |
| Verification | Manual only | User decision, consistent with S-03 through S-06; no test project exists and this slice does not add one. |

## Scope

**In scope:** a pure scoring engine in Domain covering all six parameters; a scoring service that writes `AwardedPoints` and is called from every result-changing path; an admin rescore endpoint; admin goal/card entry (server + match-form editor) with eligible-player and event-type lookups; a standings read and endpoint with shared ranks; awarded points on the round view and reveal payloads; standings card, standings route, and results/points in the round view on the client.

**Out of scope:** any database migration (none needed); automated tests; standings history, snapshots, or audit trail; notifications on point changes; re-scoring on rule edits (impossible — rules lock at first kickoff); CSV import of events; per-event endpoints; cross-league leaderboards; any change to the prediction lock or the reveal rule.

## Architecture / Approach

A pure function in `Domain/Scoring` takes (prediction, match outcome, league rules) and returns points — no EF, no clock, one place where a rule's meaning is decided. An Application-level `IMatchScoringService` wraps it: load the match with events, every league on its tournament with rules, and every prediction for that match; compute; write in one save. It is idempotent, so every path that can change a result calls the same method — admin match save, admin event save, ingest, rescore endpoint. A match that is not Finished has its points set back to null. Standings are a grouped sum over those points joined to current memberships, computed per request, with rank assigned server-side.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Scoring engine (Domain) | Pure six-parameter scoring function, cumulative stacking, first-scorer and card resolution | The rules of the game; a wrong parameter here is invisible until standings are wrong |
| 2. Scoring service + triggers | `AwardedPoints` written from admin save, ingest, and a rescore endpoint | Must run *after* the match save, or it scores the pre-edit result |
| 3. Admin match-event entry | Goals/cards editor + replace-all endpoint; granular rules get real data | Validation must reject ineligible players and wrong teams, or scoring silently mis-awards |
| 4. Standings + points read APIs | Standings endpoint with shared ranks; points on prediction payloads | Visibility must mask non-members as 404, matching the predictions surface |
| 5. Client — standings and results | Standings card, standings route, results and points on the round view | Unscored must read as "not scored yet", never as zero |

**Prerequisites:** S-04, S-05, S-06 (all shipped). Local Docker SQL running; two accounts and two leagues on one tournament with different rule sets for verification; a tournament with squads linked so the scorer picker is populated.
**Estimated effort:** ~5 sessions, one per phase; phases 2 and 3 are the heavier half.

## Open Risks & Assumptions

- **No automated coverage on the correctness guardrail.** Scoring is the surface the PRD says kills the product if wrong, and it ships verified by hand only. A regression in a rule's semantics would not be caught by any build.
- **Scoring runs after the match write commits.** A scoring failure leaves a saved result with stale points; the rescore endpoint is the remedy, and nothing alerts an admin that it is needed.
- **Points can change under a member with no notice** when an admin corrects a result — the accepted cost of auto re-scoring.
- **Granular scoring depends on admin diligence.** Points are published as soon as the score is saved; if goals and cards are entered afterwards, members briefly see zero for those rules.
- **Leavers lose their points** in the league they left, which contradicts a comment written during S-06; the comment is corrected in Phase 4 so the code stops asserting the opposite.
- **`MissedPenalty` is seeded as `Category = Goal`** — the engine must exclude it by code, and any future goal-like event type inherits that trap.

## Success Criteria (Summary)

- After an admin enters a finished match's result and its goals/cards, every member's points and league position update with no further action.
- Two leagues on the same tournament with different rules award different points for identical forecasts.
- Correcting a result moves the standings; reverting it removes the points.
