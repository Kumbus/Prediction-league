# Submit Locked Predictions — Plan Brief

> Full plan: `context/changes/submit-locked-predictions/plan.md`

## What & Why

A league member forecasts matches one round at a time, and the server refuses every write from the kickoff instant onward (FR-009, FR-010, FR-002). This is S-06, the last slice before the north star: it produces the `Prediction` rows S-07's scoring engine consumes. Without it there is nothing to score.

## Starting Point

`Prediction` exists as a domain entity but is deliberately outside the EF model — `AppDbContext.cs:11` says so in writing, reserving it for this slice. No table, no repository, no API, no screen. Everything it depends on is already there: `Match.KickoffUtc` was declared at F-01 as the lock instant, `AnyKickedOffAsync` established the clock-as-parameter pattern in S-04, per-league 404-masking authorization is settled in `LeaguesController`, and `GET api/tournaments/{id}/matches` already serves matches to non-admins.

## Desired End State

A member opens a league they belong to, lands on its predictions page scrolled to the round in play, fills that round, and saves it in one click. Fields shown are the ones the league actually scores. A match that kicked off mid-form is reported as rejected by name while the rest still save. Other members' forecasts are invisible until a match kicks off, then readable by everyone in the league.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Predictable parameters | Driven by the league's `ScoringRules` | A league that scores `CorrectGoalScorer` must have scorers to score — otherwise the rule is dead and S-07 inherits the gap. |
| Scorer representation | A pair: `Guid?` FK to `Player` **plus** `Guid?` FK to the credited `Team` | S-07 compares against `MatchEvent`'s `PlayerId` *and* `TeamId`; a player id alone cannot express an own goal, and a string would force name matching where a typo costs a point silently. |
| Own goals | Credited team A + player from team B | The candidate list already spans both squads, so the mismatch between the two picks *is* the own-goal signal — no special case, no extra enum. |
| Player→team data | This slice adds `ClubTeam` / `NationalTeam` columns to the player CSV import and team selects on the admin player form | No bulk path populated them and ingest is deferred, so the scorer picker would otherwise be empty for every match and a default league (which scores `CorrectGoalScorer`) unsubmittable. |
| `Match.Round` | Becomes a required field on both match write paths | It defaulted to the literal `"Manual"`, which with manual entry as the primary data source would put every match in one round — the switcher, the auto-scroll, and "save one round" all collapse. |
| Match delete vs predictions | Cascade | The delete endpoint has no guard, so restrict would surface as a 500; safe because `League.TournamentId` carries no FK, leaving no common cascading ancestor. Cost: an admin deleting a match destroys forecasts silently. |
| Card predictions | Three nullable ints (total / yellow / red) | Mirrors the three card `ScoringParameter` members that already exist and can already be selected. |
| Lock boundary | `KickoffUtc <= UtcNow`, server-side, no buffer | One rule, same shape as S-04's lock; the client only hides what the server would refuse anyway. |
| Write shape | Batch upsert with a per-item outcome (`Saved` / `Locked` / `Invalid`) | A round is filled as a unit; a match that kicks off mid-form must be reported, never silently dropped. |
| Screen location | New route `/app/leagues/:id/predictions` | The round list is a large, frequently-refreshed canvas that would make the league detail page expensive; leaves room for S-07 standings beside it. |
| Round navigation | Switcher over rounds, chronological within, auto-scroll to live/nearest | Matches how people think about predicting — by round; `Round` is free text, so rounds are ordered by earliest kickoff, never by the string. |
| Visibility of others | Hidden before kickoff, revealed after | Anti-cheat enforced in the API contract, not the UI, so devtools cannot bypass it; the reveal is the social payoff. |
| Verification | Manual only | Consistent with S-03/S-04/S-05; no test project exists and this slice does not add one. |

## Scope

**In scope:** `Prediction` persistence (entity revision, config, migration, repository); a round-view match projection; predictions API (round view, batch upsert, revealed view) with server-side lock and rules-driven validation; predictions screen with round switcher and per-row outcomes; post-kickoff reveal of other members' forecasts. Two narrow admin-surface fixes the slice is unusable without: player→team linkage (CSV column + form selects) and a required `Round`.

**Out of scope:** all scoring (`AwardedPoints` stays null) and standings — that is S-07; automated tests (the existing client Playwright harness is not extended); any post-kickoff edit or admin override; prediction deletion; reminders/notifications; round pagination; cross-league copy of a forecast; changes to ingest or `MatchWithEventsDto`; any admin change beyond the two named above.

## Architecture / Approach

`PredictionsController` under `api/leagues/{leagueId}/predictions` — kept out of the already-large `LeaguesController` — reads matches via `IMatchRepository`, the caller's league via `ILeagueRepository`, and forecasts via a new `IPredictionRepository`. The lock lives in one private expression (`match.KickoffUtc <= now`) with `now` captured once per request, so every item in a batch is judged against the same instant. Which optional fields are accepted is derived from the league's `ScoringRules` on every write, keeping per-league custom scoring authoritative over the input surface. The client holds no lock logic: it renders the server's `canPredict` flag and replaces local state with the server's refreshed view after each save.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Server — data layer | `Prediction` in the EF model with S-07-compatible fields, unique `(LeagueId, UserId, MatchId)`, repository, round-view match projection, migration, plus the player-linkage and required-`Round` data fixes | Entity field shape is a contract for S-07 — cheap now, expensive after deploy |
| 2. Server — predictions API | Round view, batch upsert, revealed view, server-side lock | The lock is the anti-cheat guardrail; a timezone slip fails silently rather than loudly |
| 3. Client — predictions screen | Route, round switcher, rules-driven inputs, batch save with per-row outcomes | Partial-batch feedback must never let a locked row look saved |
| 4. Client — post-kickoff reveal | Other members' forecasts on kicked-off matches | Reveal must key off the server's response, not a local clock |

**Prerequisites:** S-05 (membership — done, archived), S-02 (seeded matches with kickoff times — done). Local Docker SQL running; two accounts for two-member verification.
**Estimated effort:** ~4 sessions, one per phase; phases 1-2 are the heavier half.

## Open Risks & Assumptions

- **No automated coverage on the guardrail.** The lock is verified by hand, including one direct-HTTP attempt per phase 2. A timezone or boundary regression would not be caught by a build.
- **`TournamentSquad` is optional and often empty**, so scorer candidates fall back to players attached to either team; a tournament with sparse player data will still show a thin picker even after the CSV/form linkage fix.
- **`Match.Round` stays free text** once required, so a typo yields its own round section. Ordering by earliest kickoff contains the damage but does not prevent it. Legacy rows already saved as `"Manual"` are not backfilled — an admin retitles them.
- **Deleting a match destroys its forecasts** with no confirmation, a consequence of cascading rather than blocking the unguarded admin delete endpoint.
- **`Prediction.AwardedPoints` stays null** through this slice; anything reading it before S-07 sees nothing.

## Success Criteria (Summary)

- A member fills and saves a whole round in one action, and the values survive a reload.
- No forecast can be written at or after its match's kickoff — including by a request sent directly to the API.
- No member can see another's forecast before kickoff, and every member can see all of them after.
