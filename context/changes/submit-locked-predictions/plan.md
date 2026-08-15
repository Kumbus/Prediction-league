# Submit Locked Predictions Implementation Plan

## Overview

A league member forecasts matches one round at a time — score always, first scorer and card counts only when the league's scoring rules award them — and the server refuses every write from the kickoff instant onward (FR-009, FR-010, FR-002). Other members' forecasts stay invisible until a match kicks off, then become readable.

This is S-06 on the roadmap: the last slice before the north star (S-07, scoring engine + standings). It produces the `Prediction` rows that S-07 consumes.

## Current State Analysis

The `Prediction` entity already exists at `src/server/PredictionLeague.Domain/Entities/Prediction.cs`, written during F-01 as a forward declaration and deliberately excluded from the EF model — `AppDbContext.cs:11` states it outright: *"Prediction is owned by S-06 and stays out of the model (no nav prop pulls it in)."* There is no `DbSet`, no `IEntityTypeConfiguration`, no repository, no migration, and no API or client surface. Nothing reads or writes a prediction today.

Everything the slice depends on is in place:

- **Matches** carry `KickoffUtc` as `DateTimeOffset` (`Match.cs`), with the comment *"Stored UTC; predictions lock at this instant (FR-010)"* — the lock contract was declared at F-01 and is being cashed here.
- **The kickoff-lock pattern already exists**: `IMatchRepository.AnyKickedOffAsync(tournamentId, asOf, ct)` takes the clock as a parameter rather than reading `UtcNow` internally, so the caller owns "now" (S-04 scoring lock, `MatchRepository.cs:48`).
- **Per-league authorization is inline in the controller**, not a policy: 404 masks a league the caller cannot see, 403 is for a league they *can* see but may not act on (`LeaguesController.cs:117, 230`). `lessons.md:32` names S-06 directly: authorization derives from `League.OrganizerUserId`, never from `LeagueMembership.Role`.
- **A member-readable match list exists**: `GET api/tournaments/{id}/matches` (`TournamentsController.cs:172`) returns `MatchWithEventsDto` — kickoff, status, team names, scores, events — and 404s a draft tournament for non-admins.
- **The client has no match-facing screen at all.** `LeagueDetailPage` composes cards (`ScoringCard`, `MembersCard`); `apiFetch` + `ApiError` (`lib/api.ts`) is the entire data layer — no react-query, no cache.

Three mismatches this plan has to resolve rather than inherit:

1. `Prediction.PredictedFirstScorer` is `string?`, while `MatchEvent.PlayerId` is `Guid`. Left as-is, S-07 would have to match scorers by name.
2. `Prediction` has no fields for card counts, but `ScoringParameter` carries `CorrectCardCount`, `CorrectYellowCards`, and `CorrectRedCards` — a league can already select rules nothing could ever satisfy.
3. `Match.Round` is free text and defaults to `"Manual"` for admin-entered matches (`TournamentsController.cs:233`), so round grouping cannot trust the string for ordering.
4. **No bulk path links a player to a team.** The player CSV importer has no team column (`CsvHelperPlayerImporter.cs:212-217`: `Name, NationalityCode, Position, DateOfBirth, HeightCm, ExternalPlayerId`) and the admin form takes a pasted raw Guid one player at a time (`PlayerFormPage.tsx:142,146`). API-Football ingest, which would populate `ClubTeamId`/`NationalTeamId`, is deferred. So `Player.ClubTeamId` and `Player.NationalTeamId` are null in practice — and the eligible-scorer set this slice derives from them would be empty for every match, making a `CorrectGoalScorer` league unsubmittable. That parameter is `defaultActive: true` in `SCORING_DEFAULTS` (`types.ts:65`), so it is the default league, not an edge case. This slice fixes the data path rather than working around it.

## Desired End State

A signed-in member opens a league they belong to, lands on its predictions page scrolled to the round in play, and fills in forecasts for that round's matches. Fields shown match what the league scores. One save writes the whole round; a match that kicked off while the form was open is reported as rejected by name, and the rest still save. Past rounds render read-only with the member's own forecast, plus everyone else's for matches that have kicked off.

Verify by: joining a league from two accounts, typing a round from both, confirming neither sees the other's forecast until kickoff, and confirming a write against a kicked-off match is refused by the API — not just hidden by the UI.

### Key Discoveries:

- `src/server/PredictionLeague.Infrastructure/Persistence/AppDbContext.cs:11` — `Prediction` is intentionally out of the model and this slice owns bringing it in.
- `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/MatchRepository.cs:48` — `AnyKickedOffAsync` shows the house style for time-dependent rules: clock as a parameter.
- `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:117` — the 404-masks-invisible / 403-for-visible-but-forbidden convention every per-league route follows.
- `context/foundation/lessons.md:32` — authorize off `League.OrganizerUserId`; `LeagueMembership.Role` is display metadata that can drift. For this slice, *membership existence* is the check, and it reads the membership rows — but no permission may hang off `Role`.
- `context/foundation/lessons.md:5` — every mapped string needs explicit `HasMaxLength` in a Fluent config.
- `context/foundation/lessons.md:25` — controllers must not catch EF-specific exceptions; a persistence conflict is translated by the repository into a domain exception (`InviteCodeCollisionException`, `LeagueModifiedException` are the precedents).
- `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/LeagueMembershipConfiguration.cs` — the unique-index + `HasDefaultValueSql` pattern for a per-(user, X) row.
- `src/server/PredictionLeague.Domain/Entities/TournamentSquad.cs` — squads are *optional* ("CSV import may write here; ingest does not depend on it"), so scorer validation cannot rely on a squad existing.
- `src/client/src/leagues/types.ts` — `SCORING_DEFAULTS` is the catalogue driving rule UI; the predictions form keys off the same `ScoringParameter` union.

## What We're NOT Doing

- **No scoring.** `Prediction.AwardedPoints` stays null; nothing computes points, and no standings table appears. That is S-07.
- **No automated tests.** The user chose manual verification (see Key Decisions). The existing client Playwright harness is not extended; the server still has no test project after this slice.
- **No editing after kickoff, ever** — not by the organizer, not by an admin. There is no override path in this slice.
- **No prediction deletion.** A member can overwrite a forecast before kickoff; they cannot withdraw one.
- **No notifications or reminders** about unfilled rounds.
- **No pagination of rounds.** The round switcher lists every round in the tournament.
- **No changes to ingest or `MatchWithEventsDto`.** Two narrow, named exceptions on the admin surface, both because the slice is unusable without them, and nothing beyond them:
  - *Player team linkage* (Phase 1 §8, Phase 3 §6) — otherwise the scorer picker is empty for every match.
  - *`Round` becomes required* on the match write paths (Phase 1 §9, Phase 3 §6) — otherwise every match lands in one round called "Manual" and the round switcher is meaningless.
  No other admin field, endpoint, or flow changes.
- **No cross-league copy** ("apply my forecast to my other leagues") — FR-002 keys predictions per (user, league, match) and each league is filled separately.

## Implementation Approach

Four phases in dependency order, mirroring the S-05 shape: server data layer, server API, client entry screen, client reveal surface. Phases 1-2 ship a testable API without any UI; phases 3-4 build on it.

The lock is enforced in exactly one place — a private helper in the predictions controller that compares `match.KickoffUtc <= now` against a single `now` captured once per request, so every item in a batch is judged against the same instant. The client never decides the lock; it only hides affordances the server would refuse anyway.

Validation is driven by the league's `ScoringRules`: a field is *accepted* only if the league scores the corresponding parameter, and *required* on the same condition. This keeps the wedge (per-league custom scoring) authoritative over the input surface rather than letting the form define what a prediction is.

## Critical Implementation Details

**Round ordering.** `Match.Round` is free text, so rounds are ordered by the earliest `KickoffUtc` among their matches, never by the string. A typo produces its own section rather than reordering the tournament. Within a round, matches are ordered by `KickoffUtc`, then by match id for stability when two kick off together.

**Round stops being optional.** The `"Manual"` default (`TournamentsController.cs:233`, `CsvHelperMatchImporter.cs:121`) is not a harmless placeholder for this slice: manual entry is the current primary data source, so it would put every match in a tournament into one round called "Manual". The round switcher would show one entry, "scroll to the round in play" would mean scrolling the whole tournament, and one "Save round" would write every match including ones weeks out — the Desired End State, inverted. So this slice makes `Round` a required field on both match write paths and drops the default (Phase 1 §9).

**One clock per request.** Capture `DateTimeOffset.UtcNow` once at the top of each request handler and pass it down. A batch that reads the clock per item can accept match A and reject match B on a boundary that moved between two lines of the same loop.

**Scorer candidates when the squad is empty.** `TournamentSquad` is optional and frequently empty. The candidate set for a match is: players whose `ClubTeamId` or `NationalTeamId` equals the home or away team, intersected with the tournament squad **only when that squad has at least one row**. An empty squad widens to the team-derived set rather than rejecting every scorer.

**Own goals are expressed as a team/player mismatch.** A first-scorer forecast is a *pair*: which team the goal is credited to, and which player scored it. The candidate list spans both teams' players, so predicting an own goal means picking team A as the scoring side and a player from team B. This is exactly the shape `MatchEvent` already records — `PlayerId` alongside `TeamId`, "the team the event is attributed to" (`Match.cs`) — so S-07 compares the pair directly, with no special case. The seeded event dictionary already distinguishes the two (`MatchEventTypeConfiguration.cs:21-22`: `NormalGoal`, `OwnGoal`), but the plan does not depend on that: the credited team carries the information on its own. Storing only `PlayerId` would have made "scored for their own side" and "scored for the opposition" indistinguishable, and S-07 could not have scored either correctly.

## Phase 1: Server — data layer

### Overview

Bring `Prediction` into the EF model with the field shape S-07 can actually score, expose it through a repository, add the round-view match read the API needs, and close the two data-quality gaps (§8, §9) without which the screens in phases 3-4 would render an empty scorer picker and a single all-encompassing round.

### Changes Required:

#### 1. Prediction entity

**File**: `src/server/PredictionLeague.Domain/Entities/Prediction.cs`

**Intent**: Replace the string scorer with a player reference so S-07 can compare against `MatchEvent.PlayerId` by equality, and add the card-count fields the existing `ScoringParameter` members already imply. Keep the `// FR-00x` comments per AGENTS.md.

**Contract**: `PredictedFirstScorer: string?` → `PredictedFirstScorerPlayerId: Guid?`, plus `PredictedFirstScorerTeamId: Guid?` — the team the goal is *credited to*, which is what makes an own goal expressible (see Critical Implementation Details). Add `PredictedTotalCards: int?`, `PredictedYellowCards: int?`, `PredictedRedCards: int?` — all nullable, because a league that does not score a parameter stores nothing for it. `AwardedPoints` stays as-is and stays null this slice.

#### 2. EF configuration

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/PredictionConfiguration.cs` (new)

**Intent**: Map the aggregate with the uniqueness that FR-002 requires, following the `LeagueMembershipConfiguration` pattern.

**Contract**: Unique index on `(LeagueId, UserId, MatchId)` — the keying FR-002 calls for and the guard against a double-submit racing itself. FK to `League` (cascade, a deleted league takes its predictions) and to `Match` (**cascade** — a deleted match takes its predictions with it). `PredictedFirstScorerPlayerId` FK to `Player` and `PredictedFirstScorerTeamId` FK to `Team`, both restrict and nullable. `UserId` stays a bare Guid with no FK, consistent with `LeagueMembership`. `SubmittedUtc` is set explicitly on every insert and gets **no** `HasDefaultValueSql` — the default on `LeagueMembership.JoinedUtc` exists solely to backfill rows that predated the column (`LeagueMembershipConfiguration.cs:17-20`), and `Predictions` is a new table with no such rows, so the default could never fire. Take the unique-index and bare-Guid halves of that pattern, not the default.

**Why cascade on both, and why it is safe here**: `MatchConfiguration` uses restrict on the *team* FKs specifically because a match reaches `Team` twice (home and away) from one row. `Predictions` has no such doubling: `League.TournamentId` is a bare Guid with no FK constraint (`LeagueConfiguration.cs:36`), so there is no cascading path from `Tournament` to `League`, and the only cascading route into `Predictions` from any shared ancestor is `Tournament → Match → Prediction`. Two cascades, no common cascading ancestor, no multiple-cascade-path error. The alternative — restrict — would turn `DELETE /api/matches/{matchId}` (`TournamentsController.cs:284-296`, which has no guard and calls `Remove` + `SaveChangesAsync` directly) into an unhandled `DbUpdateException` → 500, and `lessons.md:25` bars catching that in the controller. **Consequence to accept**: an admin deleting a match silently destroys every member's forecast for it. There is no confirmation step in this slice.

#### 3. Model registration

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/AppDbContext.cs`

**Intent**: Add the `DbSet` and correct the class comment, which currently states Prediction is out of the model.

**Contract**: `public DbSet<Prediction> Predictions => Set<Prediction>();`

#### 4. Repository contract and implementation

**Files**: `src/server/PredictionLeague.Application/Abstractions/Persistence/IPredictionRepository.cs` (new), `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/PredictionRepository.cs` (new)

**Intent**: Give the API the three reads and one write it needs, without leaking EF into the Application layer (`IRepository<T>`'s stated intent).

**Contract**: Extends `IRepository<Prediction>` with:
- `ListForUserAsync(Guid leagueId, Guid userId, IReadOnlyCollection<Guid> matchIds, ct)` — the caller's own forecasts for a round.
- `ListForMatchesAsync(Guid leagueId, IReadOnlyCollection<Guid> matchIds, ct)` — every member's forecasts for kicked-off matches; returns a DTO carrying `UserId` + `DisplayName` (joined from `AspNetUsers`, the way `LeagueRepository.ListMembersAsync` resolves names).
- `UpsertManyAsync(Guid leagueId, Guid userId, IReadOnlyList<Prediction> predictions, ct)` — insert-or-update the batch in one `SaveChangesAsync`, so a round saves atomically. Read-then-write is not enough on its own: two concurrent first-time submits of the same round (a double-clicked Save, two tabs) both read "no row" and both insert, and the unique index rejects the loser. The repository absorbs that — on a unique-index rejection it re-reads the affected rows and applies the update once, then returns normally, mirroring `ILeagueRepository.JoinAsync`'s idempotent contract. Last write wins, which is the right semantic for a member overwriting their own forecast. No EF-shaped exception reaches the controller (`lessons.md:25`).

New DTO `MemberPredictionDto` lives beside `LeagueMemberDto` in `Application/Abstractions/Persistence/`.

#### 5. Round-view match read

**Files**: `src/server/PredictionLeague.Application/Abstractions/Persistence/IMatchRepository.cs` + `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/MatchRepository.cs`, new DTO beside `MatchWithEventsDto.cs`

**Intent**: Give Phase 2 something to query. The existing member-readable read (`ListByTournamentAsync` → `MatchWithEventsDto`) carries no `Round` (`MatchWithEventsDto.cs:7-13`) and hauls every match event along, and this plan does not change it — so the round view needs its own projection.

**Contract**: `ListForPredictionsAsync(Guid tournamentId, CancellationToken ct)` returning `IReadOnlyList<MatchRoundDto>` — a new record `(Guid MatchId, string Round, DateTimeOffset KickoffUtc, MatchStatus Status, TeamRefDto HomeTeam, TeamRefDto AwayTeam)`, reusing the existing `TeamRefDto` and the same team joins `ListByTournamentAsync` already uses (`MatchRepository.cs:25-30`). Ordered by `KickoffUtc`, then `MatchId`. No events. This is the single read backing both the round view and the batch write's lock check, so `IsLocked` compares against a `KickoffUtc` from the same projection in both paths.

#### 6. Scorer-candidate read

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/IPlayerRepository.cs` + `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/PlayerRepository.cs`

**Intent**: Supply the eligible-scorer set for a match so the API can validate a submitted `PlayerId` and the client can render a picker.

**Contract**: `ListEligibleScorersAsync(Guid tournamentId, Guid homeTeamId, Guid awayTeamId, ct)` returning `IReadOnlyList<EligibleScorerDto>` — `(Guid PlayerId, string Name, Guid TeamId)` — players attached to either team via `ClubTeamId` or `NationalTeamId`, narrowed to `TournamentSquads` for that tournament only when that squad is non-empty (see Critical Implementation Details). `TeamId` is the team the player belongs to, which the client groups the picker by; it is *not* the credited team, which the member chooses separately (own goals).

#### 7. DI registration and migration

**Files**: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs`, `src/server/PredictionLeague.Infrastructure/Persistence/Migrations/` (new migration)

**Intent**: Register the repository alongside its siblings and scaffold the schema change.

**Contract**: `services.AddScoped<IPredictionRepository, PredictionRepository>();` next to the existing repository registrations (`DependencyInjection.cs:39-48`). Migration name: `AddPredictions`. Dev auto-migrates on startup; prod stays forward-only and human-gated per `infrastructure-v2.md`.

#### 8. Player→team linkage on the CSV import

**File**: `src/server/PredictionLeague.Infrastructure/Players/CsvHelperPlayerImporter.cs`, `src/server/PredictionLeague.Application/Abstractions/Players/IPlayerCsvImporter.cs`

**Intent**: Make the eligible-scorer set non-empty in practice. Without this the scorer picker is empty for every match and a `CorrectGoalScorer` league — the default — can never be submitted (see Current State Analysis, mismatch 4). No API change is needed: `PlayersController` already accepts `ClubTeamId`/`NationalTeamId`.

**Contract**: Two new optional CSV columns, `ClubTeam` and `NationalTeam`, resolved case-insensitively via the existing `ITeamRepository.FindByNameAsync`. A name that matches no team is reported as a row conflict in the preview, **not** auto-created — the match importer auto-creates teams by name (`CsvHelperMatchImporter.cs:111`) because a fixture is meaningless without both sides, but a player typo would silently mint a junk team that then shows up in every admin team picker. Blank cells leave the existing value untouched, matching the importer's other optional columns. The client-side header hint is updated in Phase 3.

#### 9. Round becomes a required match field

**Files**: `src/server/PredictionLeague.Api/Controllers/TournamentsController.cs`, `src/server/PredictionLeague.Infrastructure/Matches/CsvHelperMatchImporter.cs`

**Intent**: Remove the `"Manual"` default that would collapse every round into one (see Critical Implementation Details). The round is the unit this whole slice reads, writes, and navigates by — it cannot be a field admins skip.

**Contract**: In `ValidateMatchAsync`, reject a blank `Round` with a 400 alongside the existing team and score checks (`TournamentsController.cs:330-350`); create and edit both route through it. Drop the `IsNullOrWhiteSpace(...) ? "Manual"` fallbacks at `TournamentsController.cs:233` and `CsvHelperMatchImporter.cs:121` — a blank `Round` in the CSV becomes a row conflict reported in the dry-run preview, the same shape as a missing team name (`CsvHelperMatchImporter.cs:81`). `MatchConfiguration`'s `IsRequired().HasMaxLength(100)` already holds at the database level; nothing schema-side changes.

**Existing rows**: no backfill migration. Rows already carrying `"Manual"` stay valid and keep loading; an admin retitles them through the edit form. The data is dev-only at this point, and a migration guessing round names from kickoff dates would be worse than an admin typing them.

### Success Criteria:

#### Automated Verification:

- Solution builds: `cd src/server && dotnet build`
- Migration scaffolds without a pending-model diff: `cd src/server/PredictionLeague.Api && dotnet ef migrations add AddPredictions --project ../PredictionLeague.Infrastructure`
- Migration applies against local Docker SQL on startup: `cd src/server/PredictionLeague.Api && dotnet run` boots without a migration error
- `GET /health/db` returns healthy against the migrated database

#### Manual Verification:

- The `Predictions` table exists with the unique index on `(LeagueId, UserId, MatchId)`
- Deleting a league removes its predictions; deleting a match removes its predictions and returns 204, not a 500
- No existing screen regressed — leagues list, league detail, admin tournament detail all still load
- A player CSV carrying `ClubTeam` / `NationalTeam` links those players to the named teams; the imported rows show the team on the admin players list
- A player CSV row naming a team that does not exist is reported as a conflict in the dry-run preview and creates no team
- A player CSV without the new columns still imports exactly as before (blank leaves the existing link untouched)
- Creating or editing a match with a blank `Round` is refused with a 400 on both the create and the edit route
- A match CSV row with a blank `Round` is a dry-run conflict; no row is written with `"Manual"`
- Existing `"Manual"` rows still load and are editable to a real round name

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Server — predictions API

### Overview

One controller: read a round's matches with the caller's forecasts, write a batch of forecasts, and refuse anything at or past kickoff.

### Changes Required:

#### 1. Predictions controller

**File**: `src/server/PredictionLeague.Api/Controllers/PredictionsController.cs` (new)

**Intent**: Own the member-facing prediction surface for one league. Kept out of `LeaguesController`, which is already ~425 lines and owns league identity, scoring config, and membership.

**Contract**: `[ApiController] [Route("api/leagues/{leagueId:guid}/predictions")] [Authorize]`. Three routes:

- `GET ` (optional `?round=<string>`) — the round view: its matches in kickoff order, the caller's own forecast per match, a `canPredict` flag per match, and the eligible-scorer list per match when the league scores `CorrectGoalScorer`. Response also carries the full ordered round list (name + earliest kickoff + whether it is the current one) so the client can render the switcher from one call. With no `round` parameter the server picks the round containing the earliest match that has not finished, falling back to the last round when everything has.
- `POST ` — batch upsert. Request: `{ items: [{ matchId, homeScore, awayScore, firstScorerPlayerId?, firstScorerTeamId?, totalCards?, yellowCards?, redCards? }] }`. Response: `200` with a per-item outcome list (`matchId`, `status` ∈ `Saved | Locked | Invalid`, `detail?`) plus the refreshed round view. A wholly rejected batch is still `200` with every item marked — the caller's request was well-formed; the individual matches were not writable.
- `GET revealed` (optional `?round=`) — every member's forecasts for the round's matches that have kicked off. Matches before kickoff are absent from the response entirely, not present-but-empty.

Authorization on all three: league must exist and the caller must be organizer or member, else `404` — the same masking as `LeaguesController.cs:117`. Membership is read from the membership rows; no permission derives from `Role` (`lessons.md:32`).

#### 2. Lock and validation helpers

**File**: same controller (private methods)

**Intent**: Concentrate the guardrail so it cannot drift between the read and the write path.

**Contract**:
- `IsLocked(MatchRoundDto match, DateTimeOffset now) => match.KickoffUtc <= now` — the single expression the whole slice depends on, fed by the single Phase 1 §5 projection on both the read and the write path. `now` is captured once per request.
- Rules-driven field validation: the league's `ScoringRules` decide which optional fields are accepted. A field submitted for a parameter the league does not score is `Invalid` (not silently dropped — silently dropping would let a member believe they had forecast a scorer). A field missing for a parameter the league *does* score is also `Invalid`.
- Score bounds: `0..99` for each side; card counts `0..99`. Out of range is `Invalid`.
- `firstScorerPlayerId` must be in the match's eligible-scorer set, else `Invalid`. `firstScorerTeamId` must be the match's home or away team, else `Invalid`. The two are submitted and validated together — one without the other is `Invalid`, since a player with no credited team cannot be scored and a credited team with no player is not a forecast. The pair is *not* required to agree: a player from team B credited to team A is an own-goal prediction and is accepted.
- Unknown `matchId`, or a match belonging to a different tournament than the league, is `Invalid` — never a 500.

#### 3. HTTP samples

**File**: `src/server/PredictionLeague.Api/PredictionLeague.http`

**Intent**: Keep the manual-verification file current, as every prior slice did.

**Contract**: One request per route, including a batch that mixes a writable and a locked match.

### Success Criteria:

#### Automated Verification:

- Solution builds: `cd src/server && dotnet build`
- API boots and serves the new routes: `cd src/server/PredictionLeague.Api && dotnet run`

#### Manual Verification:

- `GET` on a league the caller does not belong to returns 404, not 403 — no membership leak
- `GET` with no round parameter lands on the round holding the nearest unfinished match
- A batch of forecasts for upcoming matches returns every item `Saved` and persists
- Re-submitting the same round overwrites rather than duplicating (unique index holds)
- A batch mixing an upcoming and a kicked-off match returns `Saved` + `Locked`, and the upcoming one is written
- A forecast for a match whose kickoff has passed is refused even when sent directly via `PredictionLeague.http` — the lock is server-side
- A scorer field sent to a league that does not score `CorrectGoalScorer` returns `Invalid`
- A scorer id for a player on neither team returns `Invalid`
- A scorer sent without a credited team (or a team without a scorer) returns `Invalid`
- A credited team that is neither the home nor the away team returns `Invalid`
- An own-goal forecast — team A credited, player from team B — is accepted and persists both ids
- `GET revealed` omits matches before kickoff entirely and lists every member's forecast after
- Two members in the same league store independent forecasts for the same match
- Two saves of the same round fired back-to-back both return 200 — no 500 from the unique index

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Client — the predictions screen

### Overview

The member-facing screen: pick a round, fill the round, save it once.

### Changes Required:

#### 1. Types

**File**: `src/client/src/leagues/types.ts`

**Intent**: Mirror the new controller records, beside the existing league shapes.

**Contract**: `RoundRef`, `EligibleScorer` (`playerId`, `name`, `teamId`), `MatchPredictionRow` (match + team ids and names + kickoff + status + own forecast + `canPredict` + optional scorer candidates), `RoundViewResponse`, `PredictionSubmissionItem` (score pair, plus the `firstScorerPlayerId` / `firstScorerTeamId` pair and the three card counts, all optional), `PredictionItemOutcome` (`"Saved" | "Locked" | "Invalid"`), `BatchSubmitResponse`, `RevealedPrediction`. Field names match the C# records so `apiFetch` needs no mapping layer.

#### 2. Route

**File**: `src/client/src/routes/index.tsx`

**Intent**: Add the screen under the authenticated tree.

**Contract**: `{ path: "/app/leagues/:id/predictions", element: <PredictionsPage /> }` inside the `RequireAuth` children, after `/app/leagues/:id`. The static `join` segments already rank ahead of `:id`, so nothing collides.

#### 3. Predictions page

**File**: `src/client/src/routes/leagues/PredictionsPage.tsx` (new)

**Intent**: Load the round view, render the round switcher and the match rows, and own the batch save.

**Contract**: Fetches `GET /api/leagues/{id}/predictions` on mount and on round change. Renders a round switcher driven by the server's ordered round list. On mount, scrolls to the first match that is live or nearest-upcoming (`scrollIntoView`, `block: "center"`) — chosen off the server's `canPredict`/status data, not a local clock. One "Save round" button posts every dirty row; the per-item outcomes then paint each row (saved / locked / rejected with reason) and the refreshed round view replaces local state, the same server-is-truth rule `ScoringCard` follows. 404 renders the same "not found or not a member" copy as `LeagueDetailPage`.

#### 4. Match row component

**File**: `src/client/src/components/leagues/MatchPredictionRow.tsx` (new)

**Intent**: One match: teams, kickoff, and the inputs the league's rules call for.

**Contract**: Score inputs always. When the league scores `CorrectGoalScorer`, two controls: a credited-team select (home or away) and a player select listing both teams' candidates grouped by team, so picking team A with a team-B player — an own goal — is a natural two-click action rather than a hidden capability. Both clear together. Card-count inputs only for the card parameters the league scores, keyed off the same `ScoringParameter` union `SCORING_DEFAULTS` uses. When `canPredict` is false the row renders read-only with a "Locked at kickoff" note. Emits draft changes upward; holds no server state of its own.

#### 5. Entry point from the league page

**File**: `src/client/src/routes/leagues/LeagueDetailPage.tsx`

**Intent**: Make the screen reachable.

**Contract**: A "Predictions" button linking to `/app/leagues/{id}/predictions`, alongside the existing header actions.

#### 6. Admin surface — team pickers and a required Round

**Files**: `src/client/src/routes/admin/players/PlayerFormPage.tsx`, `src/client/src/routes/admin/players/PlayerImportPage.tsx`, `src/client/src/routes/admin/matches/MatchFormPage.tsx`, `src/client/src/routes/admin/matches/MatchImportPage.tsx`

**Intent**: Close the client half of the two data-quality fixes from Phase 1 §8 and §9. A raw pasted Guid is not a usable way to attach a player to a team, so in practice nobody does it and the scorer picker stays empty; and a Round field labelled "optional" invites exactly the blank the server now rejects.

**Contract**:
- `PlayerFormPage` — replace the two free-text Guid inputs for club and national team (`:142,146`) with selects populated from the existing admin team list (`ITeamRepository.ListAsync` already backs the admin team pickers — the same source `MatchFormPage` uses for home/away). Both stay optional and clearable.
- `PlayerImportPage` — extend the header hint to `Name,NationalityCode,Position,DateOfBirth,HeightCm,ExternalPlayerId,ClubTeam,NationalTeam`.
- `MatchFormPage` — relabel `Round (optional)` → `Round`, drop the `"Manual"` placeholder (`:195-196`), stop sending `null` for a blank round (`:97`), and block submit on an empty value so the admin sees it inline rather than as a 400.
- `MatchImportPage` — the header hint already lists `Round` (`:64`); note in the copy that it is required.

### Success Criteria:

#### Automated Verification:

- Client builds: `cd src/client && npm run build`
- Lint passes: `cd src/client && npm run lint`

#### Manual Verification:

- The page opens from the league detail page and lists the current round's matches in kickoff order
- On entry the view is scrolled to the live or nearest-upcoming match
- Switching rounds loads that round and keeps the round list stable
- A league scoring only `ExactScore`/`CorrectOutcome` shows score inputs and nothing else
- A league scoring `CorrectGoalScorer` shows a credited-team select plus a player picker listing only the two teams' players, grouped by team
- Picking team A and a team-B player saves as an own-goal forecast and reloads showing both choices
- A league scoring card parameters shows exactly those card inputs
- Filling a round and saving once persists everything; reloading shows the saved values
- A row for a kicked-off match renders read-only with the locked note and no inputs
- Saving a round where one match kicked off mid-form marks that row locked and still saves the rest
- A rejected row shows the server's reason rather than a generic failure
- The admin player form picks club and national team from a dropdown, and clearing a selection removes the link
- A player linked to a team through the form or the CSV appears in that team's matches' scorer picker
- The admin match form requires a Round and blocks submit on a blank one rather than surfacing a 400

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Client — post-kickoff reveal

### Overview

Once a match kicks off, everyone's forecast for it becomes readable — the social payoff, and the visible proof that nothing leaked earlier.

### Changes Required:

#### 1. Revealed predictions fetch

**File**: `src/client/src/routes/leagues/PredictionsPage.tsx`

**Intent**: Pull the reveal data for the displayed round alongside the round view.

**Contract**: `GET /api/leagues/{id}/predictions/revealed?round=` on round change, keyed by round so switching does not show a stale round's forecasts. Absence of a match in the response is what "not yet revealed" means — the UI must not infer it from a local clock.

#### 2. Reveal surface on the row

**File**: `src/client/src/components/leagues/MatchPredictionRow.tsx`

**Intent**: Show the other members' forecasts under a kicked-off match.

**Contract**: For a match present in the revealed set, render a compact list of `displayName` + forecast (score, and cards / scorer-with-credited-team when the league scores them — an own-goal pick reads as the player's name against the other team), the caller's own row marked as theirs. For a match absent from the set, render nothing — no placeholder, no "hidden until kickoff" teaser that could be mistaken for a value.

### Success Criteria:

#### Automated Verification:

- Client builds: `cd src/client && npm run build`
- Lint passes: `cd src/client && npm run lint`

#### Manual Verification:

- Before kickoff, a second account's forecast is invisible in the UI **and** absent from the network response
- After kickoff, both accounts' forecasts appear on that match for both members
- A past round shows every member's forecasts and no editable inputs
- Switching rounds does not carry the previous round's revealed forecasts over
- A member who forecast nothing simply does not appear in the revealed list

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

There is no server-side test project, and this slice does not add one (user decision — see `plan-brief.md` Key Decisions). The client does carry a Playwright harness — `npm run e2e` over `src/client/tests/e2e/auth.spec.ts` (`package.json:11`, documented in `src/client/AGENTS.md`) — which covers the sign-in round-trip only. It is not extended here, but it is the runway if the kickoff lock ever warrants automated coverage: the harness exists, so adding that spec is a spec, not a project.

Verification is the phase checklists above plus the scenarios below, run against local Docker SQL with two browser profiles signed in as different accounts.

### Manual Testing Steps:

0. As admin, ensure the tournament's teams have players linked — via the player CSV's `ClubTeam` / `NationalTeam` columns or the form's team pickers (Phase 1 §7, Phase 3 §6). Without this the scorer picker is empty and step 1's `CorrectGoalScorer` league cannot be submitted at all.
1. Sign in as account A, create a league on a published tournament with matches both before and after now, select scoring rules including `CorrectGoalScorer` and `CorrectYellowCards`.
2. Join from account B with the invite code.
3. As A, fill the current round and save; reload and confirm the values persisted.
4. As B, open the same round and confirm A's forecasts are not visible — check the network response, not just the screen.
5. As B, fill and save the same matches; confirm both accounts' rows coexist (independent per-(user, league, match) keying).
6. Admin-edit a match's `KickoffUtc` to a past instant, then attempt a save for it from the UI and again directly via `PredictionLeague.http` — both must be refused.
7. Confirm that match now shows both members' forecasts to both members.
8. Create a second league on the same tournament, join with both accounts, and confirm forecasts there are independent of the first league's.
9. Switch across every round, including one containing only finished matches, and confirm ordering and read-only rendering.

### Edge cases to exercise manually:

- A tournament with a single round, and one where every match has finished
- A league whose rules include no optional parameters at all
- A tournament with an empty `TournamentSquad` (scorer picker falls back to team-derived players)
- Two matches with identical kickoff instants (stable ordering)
- A legacy match still carrying `Round = "Manual"` mixed among named rounds
- An own-goal forecast: credited team A, player from team B

## Performance Considerations

The round view is one round of matches — tens of rows at most — so no pagination is warranted. Both list reads are keyed by `(LeagueId, MatchId)` and covered by the unique index from Phase 1. The batch upsert is a single `SaveChangesAsync` per round. `ListEligibleScorersAsync` is called once per match in a round view; if a round view ever feels slow, fetch candidates for the round's distinct team set in one query rather than per match — noted, not pre-optimized.

## Migration Notes

One additive migration (`AddPredictions`) creating a new table. No existing table or column changes; the `Prediction` entity edits in Phase 1 happen before the entity has ever been mapped, so there is no data to migrate and no backfill. Prod migrations stay forward-only and human-gated (`infrastructure-v2.md`); dev auto-migrates on startup.

## References

- Roadmap slice: `context/foundation/roadmap.md` → S-06
- Lessons applied: `context/foundation/lessons.md:5` (HasMaxLength), `:25` (no ORM exceptions in controllers), `:32` (authorize off `OrganizerUserId`)
- Prior slice for membership + per-league auth patterns: `context/archive/2026-08-12-invite-and-join-league/plan.md`
- Kickoff-lock precedent: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/MatchRepository.cs:48`
- Controller conventions: `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:110-130`
- Client card/state conventions: `src/client/src/components/leagues/ScoringCard.tsx`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Server — data layer

#### Automated

- [x] 1.1 Solution builds: `cd src/server && dotnet build` — c1fab8f
- [x] 1.2 Migration scaffolds without a pending-model diff (`AddPredictions`) — c1fab8f
- [x] 1.3 Migration applies against local Docker SQL on startup — c1fab8f
- [x] 1.4 `GET /health/db` returns healthy against the migrated database — c1fab8f

#### Manual

- [ ] 1.5 `Predictions` table exists with the unique index on `(LeagueId, UserId, MatchId)`
- [ ] 1.6 League delete cascades to predictions; match delete cascades too and returns 204, not 500
- [ ] 1.7 No existing screen regressed
- [ ] 1.8 Player CSV with `ClubTeam` / `NationalTeam` links players to the named teams
- [ ] 1.9 A CSV row naming an unknown team is a dry-run conflict and creates no team
- [ ] 1.10 A player CSV without the new columns imports exactly as before
- [ ] 1.11 A blank `Round` is refused with a 400 on both the match create and edit routes
- [ ] 1.12 A match CSV row with a blank `Round` is a dry-run conflict; nothing is written as `"Manual"`
- [ ] 1.13 Existing `"Manual"` rows still load and are editable to a real round name

### Phase 2: Server — predictions API

#### Automated

- [x] 2.1 Solution builds: `cd src/server && dotnet build` — c8b266d
- [x] 2.2 API boots and serves the new routes — c8b266d

#### Manual

- [ ] 2.3 Non-member `GET` returns 404, not 403
- [ ] 2.4 `GET` with no round lands on the round holding the nearest unfinished match
- [ ] 2.5 Batch of upcoming matches returns every item `Saved` and persists
- [ ] 2.6 Re-submitting the same round overwrites rather than duplicating
- [ ] 2.7 Mixed batch returns `Saved` + `Locked`, and the upcoming match is written
- [ ] 2.8 A kicked-off match is refused even when sent directly via `PredictionLeague.http`
- [ ] 2.9 Scorer field sent to a league that does not score it returns `Invalid`
- [ ] 2.10 Scorer id for a player on neither team returns `Invalid`
- [ ] 2.11 Scorer without a credited team (or team without a scorer) returns `Invalid`
- [ ] 2.12 A credited team that is neither home nor away returns `Invalid`
- [ ] 2.13 Own-goal forecast — team A credited, team B player — is accepted and persists both ids
- [ ] 2.14 `GET revealed` omits pre-kickoff matches entirely
- [ ] 2.15 Two members store independent forecasts for the same match
- [ ] 2.16 Two back-to-back saves of the same round both return 200 — no 500 from the unique index

### Phase 3: Client — the predictions screen

#### Automated

- [x] 3.1 Client builds: `cd src/client && npm run build` — b4edfe5
- [x] 3.2 Lint passes: `cd src/client && npm run lint` — b4edfe5

#### Manual

- [ ] 3.3 Page opens from the league detail page, matches in kickoff order
- [ ] 3.4 On entry the view scrolls to the live or nearest-upcoming match
- [ ] 3.5 Round switching loads that round and keeps the round list stable
- [ ] 3.6 Score-only league shows score inputs and nothing else
- [ ] 3.7 `CorrectGoalScorer` league shows a credited-team select plus a player picker grouped by team
- [ ] 3.8 Team A + team-B player saves as an own-goal forecast and reloads showing both choices
- [ ] 3.9 Card-scoring league shows exactly those card inputs
- [ ] 3.10 Filling a round and saving once persists everything
- [ ] 3.11 Kicked-off row renders read-only with the locked note
- [ ] 3.12 Round where one match kicked off mid-form marks that row locked and saves the rest
- [ ] 3.13 A rejected row shows the server's reason
- [ ] 3.14 Admin player form picks club/national team from a dropdown; clearing removes the link
- [ ] 3.15 A player linked via form or CSV appears in that team's matches' scorer picker
- [ ] 3.16 The admin match form requires a Round and blocks submit on a blank one

### Phase 4: Client — post-kickoff reveal

#### Automated

- [x] 4.1 Client builds: `cd src/client && npm run build` — 006e2b6
- [x] 4.2 Lint passes: `cd src/client && npm run lint` — 006e2b6

#### Manual

- [ ] 4.3 Pre-kickoff, another account's forecast is absent from the network response
- [ ] 4.4 Post-kickoff, both accounts' forecasts appear for both members
- [ ] 4.5 A past round shows every member's forecasts and no editable inputs
- [ ] 4.6 Round switching does not carry the previous round's revealed forecasts over
- [ ] 4.7 A member who forecast nothing does not appear in the revealed list
