# F-03 Football API Ingest Implementation Plan

## Overview

Build the football data ingest foundation on top of F-01's layered backend: an
API-Football (api-sports.io) typed HTTP client, a relational match-data model
(Team / Player / MatchEventType dictionary, richer Match / MatchEvent), and a
shared, idempotent ingest service driven by **two** hosts — an Azure Functions
timer (scheduled, production trigger) and a guarded Api manual-trigger endpoint
(on-demand verification before S-02 exists). Ingest writes through F-01 repos and
stays inside the free-tier budget (100 req/day) with Polly resilience + a
rate-limit quota guard.

## Current State Analysis

F-01 shipped the persistence **shell** this slice extends (verified against code,
not just research):

- **Layered solution** (`prediction-league.slnx`): `Domain` (pure POCOs, no package
  refs), `Application` (persistence abstractions only), `Infrastructure` (EF Core,
  repos, configs, migrations, DI), `Api` (host).
- **Persistence pattern is ready to copy**: `IRepository<T>`
  (`IRepository.cs:4-17`) → `BaseRepository<T>` (`BaseRepository.cs:8-34`) →
  per-aggregate marker interface + thin ctor class
  (`ILeagueRepository.cs:7-9`, `LeagueRepository.cs:8-13`). DbContext is the
  implicit unit-of-work (`SaveChangesAsync` on the repo; one scope = one
  transaction). Configs auto-load via `ApplyConfigurationsFromAssembly`
  (`AppDbContext.cs:28`). New repos register at `DependencyInjection.cs:24`.
- **DbSets already exist**: `Tournaments`, `Matches`, `MatchEvents`
  (`AppDbContext.cs:20-22`); physical tables created in
  `20260530155119_InitialCreate.cs`.
- **`Tournament.ExternalApiId`** (`Tournament.cs:11`, maxlen 100) already holds the
  API `league.id` — the external-key precedent to follow.
- **Scoring**: `ScoringParameter` enum (`Enums.cs:24-30`) persisted as int via
  `HasConversion<int>()` (`ScoringRuleConfiguration.cs:13`).

What is **missing** (this slice closes it):

- Relational model: no `Team`, no `Player`, no event-type dictionary. `Match` holds
  `HomeTeam`/`AwayTeam` as **strings** (`Match.cs:10-12`); `MatchEvent` has a
  coarse `Type` enum + `Player` **string**, no team attribution
  (`Match.cs:26-38`). No external fixture id, season, or round on `Match` →
  idempotent upsert impossible today.
- Infra: zero HttpClient / `IHttpClientFactory` (`Program.cs:9-17` has no
  `AddHttpClient`), no scheduler host of any kind (no Functions/Worker/HostedService
  anywhere), no api-key config slot, no `System.Text.Json` options.

## Desired End State

A maintainer can set `ApiFootball:ApiKey` in user-secrets, run the Api, and POST the
manual-trigger endpoint for a seeded tournament — the system calls API-Football,
maps the payload into the relational model, and upserts fixtures + results +
events **idempotently** (re-running produces no duplicates). The Azure Functions
project builds and its timer runs the same ingest service on a schedule. Verify by:
running the manual trigger twice and confirming row counts are stable on the second
run; inspecting `Matches`/`MatchEvents`/`Teams`/`Players` for correct attribution.

### Key Discoveries:

- Persistence shell pre-seeded by F-01 — only additive/restructure migration work,
  not from-scratch tables (`InitialCreate.cs:231-274`).
- `BaseRepository<T>` + marker-interface is the exact pattern to copy for every new
  repo (`BaseRepository.cs:8-34`, `LeagueRepository.cs:8-13`).
- Lessons rule: **every** EF-mapped string needs explicit `IsRequired()`/
  `HasMaxLength()` in a Fluent config (`lessons.md`) — applies to new
  `Team.Name`, `Player.Name`, `Match.Round`, dictionary `Code`.
- No prod data exists yet (F-04 deploy not done; dev auto-migrates on startup at
  `Program.cs:23-37`) → the destructive `Match`/`MatchEvent` column changes are safe
  to drop-and-recreate in one migration.
- API gives **int** ids for fixture/team/player; all PKs are **Guid** → store API ints
  as separate external-key columns with unique indexes, never as PKs.

## What We're NOT Doing

- **No football-data.org fallback / role-split** — documented in `api-research.md`
  as fallback only; premature under `main_goal: speed`.
- **No live/15s polling** — out of scope v1 (budget constraint); timer fires in
  match windows only.
- **No CI changes** — F-04 (walking-skeleton-deploy) owns server + Functions CI and
  deploy. This slice verifies via local `dotnet build` + the manual endpoint.
- **No S-02 admin UI** — the manual-trigger endpoint is a dev/admin verification
  surface that S-02 later reuses; no tournament-seeding UI here.
- **No extra API calls to classify club vs national teams** — players are seeded
  upfront with both teams; ingest only references them (burning quota to classify
  fights the 100/day cap).
- **No logo/flag download pipeline** — out of scope; store URLs only if cheap.
- **No Prediction / scoring-engine work** — `ScoringParameter` members are added so
  S-04/S-07 can consume them, but no scoring logic ships here.

## Implementation Approach

Bottom-up so each phase is independently verifiable: model + migration first, then
the repos that query it, then the HTTP client that produces DTOs, then the ingest
service that maps DTOs → domain via the repos, then the two hosts that invoke the
service. The ingest service is the single seam shared by the Functions timer and the
Api endpoint — both resolve `IFixtureIngestService` from DI and call one method, so
there is exactly one mapping/upsert implementation.

Layer placement:

- **Domain**: new entities + enum members (no package refs).
- **Application**: `IFootballApiClient`, `IFixtureIngestService`, the new repository
  interfaces (abstractions only).
- **Infrastructure**: EF configs + migration, repository impls, `FootballApiClient`
  (typed client + Polly + DTOs), `FixtureIngestService`, and the
  `AddFootballIngest(config)` DI extension both hosts call.
- **Api**: manual-trigger controller.
- **Functions** (new project): timer trigger only — a thin shell over the service.

## Critical Implementation Details

- **Status mapping** (decided): API `status.short` → 3-bucket `MatchStatus` —
  `FT`/`AET`/`PEN` → `Finished`; `NS`/`TBD` → `Scheduled`; everything else (`1H`,
  `HT`, `2H`, `ET`, `LIVE`, `BT`, `P`, `SUSP`, `INT`) → `Live`. Keep the enum
  3-bucket; do not expand it.
- **Idempotency, fixtures**: upsert `Match` by `ExternalFixtureId` (unique index) —
  look up, update in place if found, insert if not. Without the unique index every
  poll duplicates fixtures.
- **Idempotency, events**: API events carry **no stable id**. On each events ingest
  for a match, **delete all existing `MatchEvent` rows for that match, then
  re-insert** the mapped set — within the same scoped transaction. This is safe
  because events are pulled once after FT (and re-pulled only on correction).
- **Envelope handling**: treat non-empty `errors[]` as failure even on HTTP 200;
  treat `204 No Content` as a valid empty result (events before kickoff), not an
  error. Guard null `type`/`player` on the trailing partial events array entry
  (`api-reference.md:77`).
- **Quota guard**: read `x-ratelimit-requests-remaining` (daily) and
  `X-RateLimit-Remaining` (per-min); when low, stop the run (skip remaining event
  calls) rather than tight-retry. Per match-day budget: 1× `/fixtures` + 1×
  `/fixtures/events` per finished/in-play fixture only.
- **Player resolution**: resolve `Player` by `ExternalPlayerId` (expected
  pre-seeded with club + national teams). If absent, create a **minimal** Player
  (name from the event payload, both team slots null) and log a warning — a safety
  net so a missing seed never drops an event; it does not attempt club/national
  classification.
- **Team resolution**: upsert `Team` by `ExternalTeamId` from the `/fixtures`
  `teams.home`/`teams.away` payload (id + name always present there).

## Phase 1: Domain Model + Schema Migration

### Overview

Introduce the relational match-data model and the one migration that reshapes the
F-01 placeholder tables into it.

### Changes Required:

#### 1. Team entity

**File**: `src/server/PredictionLeague.Domain/Entities/Team.cs`

**Intent**: New aggregate for a football team (club or national), referenced by
`Match` (home/away) and `Player` (club/national).

**Contract**: `Guid Id`; `int ExternalTeamId` (API `team.id`); `required string
Name`; optional `string? LogoUrl`. Carry a `// FR-004/FR-005` comment per server
convention.

#### 2. Player entity

**File**: `src/server/PredictionLeague.Domain/Entities/Player.cs`

**Intent**: New aggregate for a player who can be credited with goals/cards;
pre-seeded with both teams, referenced by `MatchEvent`.

**Contract**: `Guid Id`; `int ExternalPlayerId` (API `player.id`); `required string
Name`; `Guid? ClubTeamId` + `Guid? NationalTeamId` (both FK → `Team`).

#### 3. MatchEventType dictionary entity

**File**: `src/server/PredictionLeague.Domain/Entities/MatchEventType.cs`

**Intent**: Replace the coarse `MatchEventType` **enum** with a dictionary table so
events carry the full API sub-type (Normal Goal / Own Goal / Penalty / Missed
Penalty / Yellow Card / Red Card) plus a category usable by scoring.

**Contract**: `int Id` (stable, seeded PK); `required string Code` (e.g.
`"NormalGoal"`); `required string DisplayName`; `MatchEventCategory Category`
(`Goal` | `Card` | `Other`). Remove the `MatchEventType` enum from `Enums.cs`; add a
new `MatchEventCategory` enum there.

#### 4. Match entity rework

**File**: `src/server/PredictionLeague.Domain/Entities/Match.cs`

**Intent**: Make `Match` ingest-ready and relational.

**Contract**: drop `string HomeTeam`/`AwayTeam`; add `Guid HomeTeamId` +
`Guid AwayTeamId` (FK → `Team`). Add `int ExternalFixtureId` (API `fixture.id`),
`int Season` (API `league.season`), `required string Round` (API `league.round`).
Keep `KickoffUtc`, `Status`, `HomeScore?`, `AwayScore?`, `Events`.

#### 5. MatchEvent entity rework

**File**: `src/server/PredictionLeague.Domain/Entities/Match.cs`

**Intent**: Attribute each event to a player, a team, and a dictionary type.

**Contract**: drop `MatchEventType Type` and `string Player`; add
`int MatchEventTypeId` (FK → `MatchEventType`), `Guid PlayerId` (FK → `Player`),
`Guid TeamId` (FK → `Team`). Keep `int Minute`; add `int? MinuteExtra` (API
`time.extra`).

#### 6. ScoringParameter members

**File**: `src/server/PredictionLeague.Domain/Entities/Enums.cs`

**Intent**: Enable per-color card scoring for S-04/S-07.

**Contract**: append `CorrectYellowCards`, `CorrectRedCards` to `ScoringParameter`
**after** the existing four members (preserve int ordinals — append only, never
reorder, since values persist as int).

#### 6b. Tournament.Season

**File**: `src/server/PredictionLeague.Domain/Entities/Tournament.cs`

**Intent**: Give the production Functions timer the `season` it must pass to
`IngestTournamentAsync` without a query param (the manual endpoint supplies it; the
timer cannot). `Tournament` today has only `StartDate`/`EndDate`/`ExternalApiId`.

**Contract**: add `int Season` (API `league.season`). Folds into the Phase-1
migration (one extra column on `Tournaments`). No Fluent config needed (non-string,
non-relational).

#### 7. Fluent configurations

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/`
(`TeamConfiguration.cs`, `PlayerConfiguration.cs`,
`MatchEventTypeConfiguration.cs`, edits to `MatchConfiguration.cs` +
`MatchEventConfiguration.cs`)

**Intent**: Map the new entities/relationships and satisfy the lessons rule on every
string column.

**Contract**: per the existing config style (`MatchConfiguration.cs:9-21`) —
`HasKey`; `IsRequired().HasMaxLength(...)` on `Team.Name`, `Player.Name`,
`MatchEventType.Code`/`DisplayName`, `Match.Round`, `Team.LogoUrl` (nullable,
maxlen); `HasConversion<int>()` on `MatchEventType.Category`; **unique indexes** on
`Team.ExternalTeamId`, `Player.ExternalPlayerId`, `Match.ExternalFixtureId`;
configure the `Match→Team` (home/away, `OnDelete.Restrict` to avoid multiple-cascade
paths), `Player→Team` (club/national, `Restrict`), `MatchEvent→Player`/`→Team`
(`Restrict`), `MatchEvent→MatchEventType` (`Restrict`) relationships. Seed the
`MatchEventType` rows via `HasData` (stable ids).

#### 8. Migration

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Migrations/` (new
`dotnet ef migrations add AddFootballIngestModel`)

**Intent**: One migration that creates `Teams`, `Players`, `MatchEventTypes`
(seeded), drops the old `Match.HomeTeam`/`AwayTeam` + `MatchEvent.Type`/`Player`
columns, and adds the new FK/external-key columns + indexes.

**Contract**: generated migration; verify it drops string team columns and adds the
FK columns (safe — no prod data). Dictionary seed rows present in the `Up`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build src/server/prediction-league.slnx`
- Migration is generated without model-snapshot errors:
  `dotnet ef migrations add AddFootballIngestModel` (Infrastructure project)
- Migration applies cleanly on dev startup (`dotnet run` in `Api` →
  `db.Database.Migrate()` succeeds; check startup log)
- `GET /health/db` returns healthy after migration

#### Manual Verification:

- Inspect the DB: `Teams`, `Players`, `MatchEventTypes` tables exist;
  `MatchEventTypes` is seeded; `Matches` has `ExternalFixtureId` (unique),
  `Season`, `Round`, `HomeTeamId`, `AwayTeamId`; `MatchEvents` has
  `MatchEventTypeId`, `PlayerId`, `TeamId`.
- No string `HomeTeam`/`AwayTeam`/`Player` columns remain.

**Implementation Note**: After automated verification passes, pause for human
confirmation of the DB shape before Phase 2.

---

## Phase 2: Repositories + DI

### Overview

Add per-aggregate repositories with the external-id lookups ingest needs, following
the F-01 pattern, and register them.

### Changes Required:

#### 1. Repository interfaces

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/`
(`ITournamentRepository.cs`, `IMatchRepository.cs`, `ITeamRepository.cs`,
`IPlayerRepository.cs`, `IMatchEventTypeRepository.cs`)

**Intent**: Per-aggregate query slots for ingest upsert, mirroring
`ILeagueRepository`.

**Contract**: each `: IRepository<T>` plus the external-id lookup it needs —
`ITournamentRepository.GetByExternalApiIdAsync(string)` +
`GetActiveAsync(DateOnly onDate)` (tournaments whose `StartDate <= onDate <=
EndDate` with a non-null `ExternalApiId` — the set the timer iterates);
`IMatchRepository.GetByExternalFixtureIdAsync(int)` (include `Events`);
`ITeamRepository.GetByExternalTeamIdAsync(int)`;
`IPlayerRepository.GetByExternalPlayerIdAsync(int)`;
`IMatchEventTypeRepository.GetAllAsync()` is inherited — add
`GetByCodeAsync(string)` for mapping.

#### 2. Repository implementations

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/`
(one class per interface)

**Intent**: EF impls copying `LeagueRepository` (inherit `BaseRepository<T>`, add the
lookup via `Set`).

**Contract**: e.g. `MatchRepository.GetByExternalFixtureIdAsync` →
`Set.Include(m => m.Events).FirstOrDefaultAsync(m => m.ExternalFixtureId == id, ct)`.

#### 3. DI registration

**File**: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs`

**Intent**: Register the new repos alongside `LeagueRepository`.

**Contract**: `AddScoped<I..., ...>()` lines after `DependencyInjection.cs:24`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build src/server/prediction-league.slnx`

#### Manual Verification:

- Repos resolve from DI without missing-registration errors at startup.

---

## Phase 3: Typed HTTP Client + DTOs + Config

### Overview

A Polly-resilient typed `HttpClient` for API-Football that returns deserialized
envelope DTOs, plus the config/secrets slot for the key.

### Changes Required:

#### 1. Client abstraction

**File**:
`src/server/PredictionLeague.Application/Abstractions/Football/IFootballApiClient.cs`

**Intent**: Persistence-style abstraction the ingest service depends on (not the
concrete HttpClient).

**Contract**: `Task<FixturesResponse> GetFixturesAsync(string leagueId, int season,
DateOnly date, CancellationToken)`; `Task<EventsResponse>
GetFixtureEventsAsync(int fixtureId, CancellationToken)`. DTO types live with the
impl (Infrastructure) and are referenced via the Application abstraction namespace,
or define lightweight result records in Application — keep DTOs out of Domain.

#### 2. Envelope + payload DTOs

**File**: `src/server/PredictionLeague.Infrastructure/Football/Dtos/` (envelope,
fixture, events)

**Intent**: `System.Text.Json` records matching `api-reference.md` shapes.

**Contract**: generic envelope `{ get, errors[], results, paging, response[] }`;
fixture item (`fixture.id`/`date`/`status.short`/`status.extra`, `league.season`/
`round`, `teams.home`/`away` {id,name}, `goals`, `score.fulltime`); event item
(`time.elapsed`/`extra`, `team`{id,name}, `player`{id,name}, `type`, `detail`).
Nullable everywhere the API can omit; `[JsonPropertyName]` for snake/lower keys.

#### 3. Typed client implementation

**File**:
`src/server/PredictionLeague.Infrastructure/Football/FootballApiClient.cs`

**Intent**: Issue the two calls, attach auth, enforce envelope/quota rules.

**Contract**: typed client with base address `https://v3.football.api-sports.io`
and default header `x-apisports-key`; deserialize the envelope; throw a typed
`FootballApiException` when `errors[]` non-empty; return empty result on `204`; read
rate-limit headers and surface remaining-quota so the service can stop. Tricky bit
(snippet-worthy): the `errors` field is an **array when present but an empty
object `{}` when absent** in some API-Sports responses — deserialize defensively
(e.g. `JsonElement` check) rather than assuming `string[]`.

#### 4. DI + config wiring

**File**: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs`
(new `AddFootballIngest`), `appsettings.json`, user-secrets

**Intent**: Register the typed client with Polly and bind options.

**Contract**: `AddFootballIngest(this IServiceCollection, IConfiguration)` →
`services.AddHttpClient<IFootballApiClient, FootballApiClient>(...)` +
`.AddPolicyHandler(...)` (timeout + retry on transient 5xx/timeout via
`Microsoft.Extensions.Http.Polly`); bind `ApiFootball` options
(`BaseUrl`, `ApiKey`). Add empty `ApiFootball:{ BaseUrl, ApiKey }` to
`appsettings.json`; real key via user-secrets `ApiFootball:ApiKey` (mirror the
`DefaultConnection` precedent). NuGet: `Microsoft.Extensions.Http.Polly` into
Infrastructure.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build src/server/prediction-league.slnx`
- `Microsoft.Extensions.Http.Polly` restores (`dotnet restore`)

#### Manual Verification:

- With a real key in user-secrets, a throwaway call to `GetFixturesAsync`
  returns deserialized fixtures (verified via the Phase 5 endpoint, or a scratch
  test) and a bad/empty key surfaces the envelope error, not a crash.

---

## Phase 4: Ingest Service

### Overview

The single shared orchestration: pull fixtures + events for a tournament, map DTOs
into the relational model, and upsert idempotently through the repos.

### Changes Required:

#### 1. Service abstraction

**File**:
`src/server/PredictionLeague.Application/Abstractions/Football/IFixtureIngestService.cs`

**Intent**: One method both hosts call.

**Contract**: `Task<IngestResult> IngestTournamentAsync(Guid tournamentId, int
season, DateOnly? date, CancellationToken)`; `IngestResult` carries counts
(fixtures upserted, events upserted, calls used, quota remaining) for endpoint
output and logging.

#### 2. Service implementation

**File**:
`src/server/PredictionLeague.Infrastructure/Football/FixtureIngestService.cs`

**Intent**: The mapping + upsert orchestration described in Critical Implementation
Details.

**Contract**: resolve tournament → `ExternalApiId`; `GetFixturesAsync`; for each
fixture: upsert `Team` (home/away) by external id, upsert `Match` by
`ExternalFixtureId` (map status via the decided table, set season/round). **Score
source by status** (`api-reference.md:51`): when mapped `Status==Finished` read
`score.fulltime.home`/`away`; otherwise (`Live`) read the running `goals.home`/
`away`. Never store running `goals` as the final result.
for fixtures that are `Finished`/`Live`, `GetFixtureEventsAsync`, then
**delete-and-replace** that match's events. **Filter to `type=="Goal"` and
`type=="Card"` only — skip `Subst`/`Var`** (the dictionary seeds no row for them
and `MatchEventTypeId` is a non-null FK, so storing them would null-FK-crash). Also
**skip any event with a null `type` or null `player.id`** (the trailing partial
array entry, `api-reference.md:77`) — `MatchEvent.PlayerId` is a non-null Guid FK,
so an event with no player cannot be stored; the minimal-create fallback only covers
a present-but-unseeded id, not a null id. For
the kept set, resolve `MatchEventType` (by code/detail), `Player` (by external id,
minimal-create fallback), `Team` per event.
Honor the quota guard (stop issuing event calls when remaining is low). One
`SaveChangesAsync` per match (scoped transaction) so a partial run leaves consistent
matches. Map goal/card filtering per `api-reference.md:74-77` (exclude
`Missed Penalty` from goal-scorer credit; keep it as a stored event row of that
type).

**`detail` → dictionary `Code` mapping**: API `detail` carries spaces
(`"Normal Goal"`, `"Own Goal"`, `"Missed Penalty"`, `"Yellow Card"`, `"Red Card"`,
`"Penalty"`) while seed `Code` is space-free (`"NormalGoal"`). Do **not** call
`GetByCodeAsync(detail)` directly — define an explicit `detail → Code` switch (or
strip whitespace) in the service. `Own Goal` decision: store it as its own
`OwnGoal` dictionary row (category `Goal`) but **do not** credit it as
`CorrectGoalScorer` for the scoring player — record the event, exclude from scorer
credit (same treatment as `Missed Penalty`).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build src/server/prediction-league.slnx`

#### Manual Verification:

- Run ingest (via Phase 5 endpoint) against a seeded tournament: fixtures,
  teams, and events appear correctly attributed (goal/card → right player + team).
- Run ingest **a second time**: row counts are stable (no duplicate matches or
  events) — confirms idempotency.
- A fixture with no events yet (pre-kickoff, `204`) ingests without error.

---

## Phase 5: Hosts — Functions Timer + Api Manual Trigger

### Overview

Two thin hosts over `IFixtureIngestService`: the production Azure Functions timer and
the on-demand Api endpoint used to verify F-03 before S-02 exists.

### Changes Required:

#### 1. Azure Functions project

**File**: `src/server/PredictionLeague.Functions/` (new isolated-worker .NET 10
Functions project) added to `prediction-league.slnx`

**Intent**: Scheduled trigger that runs ingest in match windows.

**Pre-step (tooling gate)**: before scaffolding, confirm isolated-worker on **.NET
10** + the Timer extension + local Core Tools are GA in the installed toolchain
(Context7 / Azure Functions docs). If unsupported, fall back to a
`BackgroundService`/`IHostedService` with a `PeriodicTimer` hosted **in the Api
project** (same `IFixtureIngestService` seam, no new project) — or target a `net9`
worker. Pick the fallback before writing Phase-5 code so criterion 5.1 (whole
solution builds) isn't blocked mid-phase.

**Contract**: isolated-worker Functions project referencing `Application` +
`Infrastructure`; `Program.cs` builds the host, calls `AddInfrastructure(config)` +
`AddFootballIngest(config)`; one `TimerTrigger` function resolving
`IFixtureIngestService` + `ITournamentRepository`, calling
`GetActiveAsync(today)` and, for each, `IngestTournamentAsync(t.Id, t.Season, today)`
— `Season` comes from the `Tournament` row, the date is today. CRON fires in match
windows only (e.g. every 30–60 min during the tournament window — schedule via app
setting, not live-15s). `local.settings.json` carries `ApiFootball:ApiKey` +
`DefaultConnection` for local runs (gitignored). NuGet: `Microsoft.Azure.Functions.
Worker.*` + `Microsoft.Azure.Functions.Worker.Extensions.Timer`.

#### 2. Manual-trigger endpoint

**File**: `src/server/PredictionLeague.Api/Controllers/IngestController.cs`

**Intent**: Guarded on-demand ingest for verification; S-02 later reuses the
service.

**Contract**: `[ApiController]` `[Route("api/[controller]")]`; `POST
api/ingest/{tournamentId}?season={season}&date={date}` → calls
`IngestTournamentAsync`, returns `IngestResult` counts. Guard it (dev-environment
gate and/or a simple admin check — real auth is F-02; keep the gate minimal but not
publicly open). Register `AddFootballIngest(builder.Configuration)` in
`Program.cs`.

### Success Criteria:

#### Automated Verification:

- Whole solution builds incl. the Functions project:
  `dotnet build src/server/prediction-league.slnx`
- `prediction-league.slnx` lists `PredictionLeague.Functions`

#### Manual Verification:

- `POST api/ingest/{tournamentId}` against a seeded tournament returns counts and
  populates the DB (end-to-end through the real API).
- Functions host runs locally (`func start` / Core Tools) and the timer
  invocation runs the same ingest without error.
- Endpoint is not reachable as an anonymous public route in a non-dev config.

**Implementation Note**: After automated verification, pause for human confirmation
of the live end-to-end run before considering F-03 complete.

---

## Testing Strategy

No test suite exists in the server yet, and the chosen verification path is the
manual-trigger endpoint, not a test project. Testing is therefore manual +
build-gated:

### Unit Tests:

- Optional (not required this slice): a mapper test over a recorded `/fixtures` +
  `/fixtures/events` JSON payload asserting status mapping, goal/card filtering, and
  team/player attribution. Recommended if a test project is later added — keeps the
  envelope/quota edge cases regression-safe.

### Integration Tests:

- Deferred. The manual endpoint exercises the real HTTP + envelope + upsert path
  end-to-end for this slice.

### Manual Testing Steps:

1. Set `ApiFootball:ApiKey` + `DefaultConnection` in user-secrets; seed a Tournament
   row with a valid `ExternalApiId` (API league id).
2. `dotnet run` (Api), then `POST api/ingest/{tournamentId}?season=YYYY` for a date
   with finished fixtures.
3. Inspect `Matches`/`MatchEvents`/`Teams`/`Players` — correct scores, status,
   scorer/card attribution.
4. Re-POST the same request → confirm stable row counts (idempotency).
5. POST for a future date (no events) → confirms `204`/empty handled.
6. Run the Functions host locally → timer fires and ingests without error.

## Performance Considerations

The binding constraint is the **100 req/day free-tier cap**, not latency. Budget: 1×
`/fixtures` per tournament-day + 1× `/fixtures/events` per finished/in-play fixture;
World-Cup worst case ~7 calls/day. The quota guard (reading rate-limit headers and
stopping when low) is the protection. Cache-hard: only fetch events for fixtures
whose status is Finished/Live and not already cached. Polly retry is limited and
transient-only — never tight-retry against the per-minute limit.

## Migration Notes

One migration (`AddFootballIngestModel`) reshapes the F-01 placeholder `Matches`/
`MatchEvents` tables (drops string team/player columns, adds FK + external-key
columns) and creates `Teams`/`Players`/`MatchEventTypes` (seeded). Safe to
drop-and-recreate columns because no prod data exists (F-04 deploy is not done; dev
auto-migrates on startup). Prod stays forward-only + human-gated when F-04 lands.

## References

- Internal research: `context/changes/football-api-ingest/research.md`
- API source decision: `context/changes/football-api-ingest/api-research.md`
- Endpoint/payload contracts: `context/changes/football-api-ingest/api-reference.md`
- Pattern to copy: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/LeagueRepository.cs:8-13`
- Config precedent: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs:16`
- Lessons: `context/foundation/lessons.md` (string `HasMaxLength` rule)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain Model + Schema Migration

#### Automated

- [x] 1.1 Solution builds (`dotnet build prediction-league.slnx`) — 07600ce
- [x] 1.2 Migration generated without snapshot errors (`dotnet ef migrations add AddFootballIngestModel`) — 07600ce
- [x] 1.3 Migration applies cleanly on dev startup — 07600ce
- [x] 1.4 `GET /health/db` healthy after migration — 07600ce

#### Manual

- [x] 1.5 New tables exist + `MatchEventTypes` seeded; Match/MatchEvent columns reshaped — 07600ce
- [x] 1.6 No string `HomeTeam`/`AwayTeam`/`Player` columns remain — 07600ce

### Phase 2: Repositories + DI

#### Automated

- [x] 2.1 Solution builds — 0efbdbd

#### Manual

- [x] 2.2 Repos resolve from DI without missing-registration errors — 0efbdbd

### Phase 3: Typed HTTP Client + DTOs + Config

#### Automated

- [x] 3.1 Solution builds
- [x] 3.2 `Microsoft.Extensions.Http.Polly` restores (adapted → `Microsoft.Extensions.Http.Resilience` v10.6.0 / Polly v8)

#### Manual

- [ ] 3.3 Real-key call returns deserialized fixtures; bad/empty key surfaces envelope error, not a crash

### Phase 4: Ingest Service

#### Automated

- [ ] 4.1 Solution builds

#### Manual

- [ ] 4.2 Ingest populates correctly-attributed fixtures/teams/events
- [ ] 4.3 Second run is idempotent (stable row counts)
- [ ] 4.4 Pre-kickoff fixture (`204`) ingests without error

### Phase 5: Hosts — Functions Timer + Api Manual Trigger

#### Automated

- [ ] 5.1 Whole solution builds incl. Functions project
- [ ] 5.2 `prediction-league.slnx` lists `PredictionLeague.Functions`

#### Manual

- [ ] 5.3 `POST api/ingest/{tournamentId}` returns counts + populates DB end-to-end
- [ ] 5.4 Functions host runs locally and timer ingests without error
- [ ] 5.5 Endpoint not reachable anonymously in non-dev config
