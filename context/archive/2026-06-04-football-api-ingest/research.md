---
date: 2026-06-04T00:00:00Z
researcher: Kumbus
git_commit: c4f1ef8a7d1cfef341d192a5146db2ef396a3449
branch: features/footbal-api
repository: Kumbus/Prediction-league
topic: "Is api-reference.md compatible with the codebase for implementing F-03 (football API ingest)?"
tags: [research, codebase, football-api-ingest, F-03, persistence, ingest, httpclient]
status: complete
last_updated: 2026-06-04
last_updated_by: Kumbus
---

# Research: F-03 ingest — api-reference.md vs codebase compatibility

**Date**: 2026-06-04
**Researcher**: Kumbus
**Git Commit**: c4f1ef8a7d1cfef341d192a5146db2ef396a3449
**Branch**: features/footbal-api
**Repository**: Kumbus/Prediction-league

## Research Question

Review whether `context/changes/football-api-ingest/api-reference.md` (API-Football endpoint/payload contracts) is compatible with the current codebase, to implement **F-03** (football data API client + scheduled ingest) from `context/foundation/roadmap.md`. Scope: full implementation-readiness (domain + persistence + infra wiring). Scoring: verify enum mapping only.

## Summary

**Verdict: compatible, no architectural rework. F-03 buildable on the F-01 layered backend with one additive migration + new HTTP/scheduler wiring.**

Good news, bigger than expected: F-01 already shipped the persistence **shell** for ingest. `Tournaments`, `Matches`, `MatchEvents` entities, DbSets, Fluent configs, and physical tables all exist in the initial migration. Scores live inline on `Match` (no separate Result entity needed). `Tournament.ExternalApiId` already exists for the API league id. The repo/DbContext/migration pattern is ready to extend by copying the `LeagueRepository` / `BaseRepository` pattern.

Two classes of gap remain:

1. **Schema/model gaps (additive migration):** `Match` has no external fixture id (blocks idempotent upsert), no `Season`/`Round`; `MatchEvent` has no `Team` field (can't attribute goal/card to home vs away) and no goal `Detail` (can't exclude Missed Penalty / handle Own Goal per api-reference.md:75-76). `MatchEventType` and `MatchStatus` enums are coarser than the API payload.
2. **Infra wiring gaps (net-new):** zero HTTP client, no `IHttpClientFactory`, no Functions/Worker/BackgroundService project (baseline confirmed), no JSON config, no api-key config slot, server not in CI.

Scoring mapping verified: `ScoringParameter` enum already carries the four members ingest must feed. One soft gap — `CorrectCardCount` can't distinguish yellow vs red (no `CorrectYellowCards`/`CorrectRedCards`), but the API supplies the distinction if a future rule needs it.

## Detailed Findings

### Solution layout — clean 4-project layered (`src/server/prediction-league.slnx:1-7`)

- **PredictionLeague.Domain** — `Entities/` pure POCOs (League, Match, Prediction, ScoringRule, Tournament, Enums). No package refs. `net10.0`.
- **PredictionLeague.Application** — `Abstractions/Persistence/` only (`IRepository.cs`, `ILeagueRepository.cs`). No package refs.
- **PredictionLeague.Infrastructure** — `Persistence/` (AppDbContext, Repositories, Configurations, Migrations), `Identity/`, `DependencyInjection.cs`. Refs: `Microsoft.EntityFrameworkCore.SqlServer` 10.0.8, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.8.
- **PredictionLeague.Api** — `Program.cs` host (auto-migrate-in-dev, `/health/db`). Refs: OpenApi, EFCore.Design, HealthChecks.EFCore (all 10.0.8). `UserSecretsId` present (`.Api.csproj:8`).

### Persistence layer — ready to extend

- Abstractions: `IRepository<T>` (`IRepository.cs:4-17`) = GetByIdAsync(Guid)/GetAllAsync/AddAsync/Update/Remove/SaveChangesAsync. `ILeagueRepository : IRepository<League>` (`ILeagueRepository.cs:7-9`) empty marker = per-aggregate query slot.
- Impl: `BaseRepository<T>` (`BaseRepository.cs:8-34`) holds `AppDbContext` + `DbSet<T>`, implements all CRUD. `LeagueRepository` (`LeagueRepository.cs:8-13`) just a ctor.
- `AppDbContext` (`AppDbContext.cs:12-30`) = `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. **DbSets already present: `Tournaments`, `Matches`, `MatchEvents`** (lines 20-22) + Leagues, LeagueMemberships, ScoringRules. `ApplyConfigurationsFromAssembly` auto-loads configs (line 28).
- **UoW**: no separate interface. `SaveChangesAsync` on `IRepository<T>` (`IRepository.cs:16`); shared scoped DbContext = implicit unit-of-work. One scope = one transaction.
- DI: `DependencyInjection.cs:21-26` registers DbContext + `AddScoped<ILeagueRepository, LeagueRepository>()`. New repos register on line 24.

### Domain model — what EXISTS (all PKs are `Guid`)

| Entity | File:line | Notes vs API contract |
| --- | --- | --- |
| **Tournament** | `Tournament.cs:4-18` | has **`ExternalApiId` string?** (line 11, maxLen 100) → API `league.id`. ✓ |
| **Match** (=Fixture+Result) | `Match.cs:4-24` | `HomeTeam`/`AwayTeam` **string** (10-12), `KickoffUtc` DateTimeOffset (15), `Status` MatchStatus, `HomeScore`/`AwayScore` **int?** (19-21), `Events` collection. Scores inline → no Result entity needed. ✓ |
| **MatchEvent** | `Match.cs:26-38` | `Type` MatchEventType, `Player` **string** (35), `Minute` int. **No Team field, no Detail.** |
| ScoringRule | `ScoringRule.cs:4-13` | `Parameter` ScoringParameter, `Points` int |
| Prediction | `Prediction.cs` | deliberately NOT in DbContext (`AppDbContext.cs:10-11`, owned by S-06) |

Enums (`Enums.cs`): `MatchStatus { Scheduled, Live, Finished }` (3-8); `MatchEventType { Goal, YellowCard, RedCard }` (10-15); `ScoringParameter { ExactScore, CorrectOutcome, CorrectGoalScorer, CorrectCardCount }` (24-30).

**Missing entities:** no Team, no Player, no standalone Result/Score.

### Migration — tables already physically exist

Single migration `20260530155119_InitialCreate.cs` already creates: `Tournaments` (lines 70-83, incl. `ExternalApiId` col 76), `Matches` (231-253: Id, TournamentId, HomeTeam, AwayTeam, KickoffUtc `datetimeoffset`, Status int, HomeScore/AwayScore nullable int; FK→Tournaments cascade; `IX_Matches_TournamentId`), `MatchEvents` (255-274: Id, MatchId, Type int, Player, Minute; FK→Matches cascade; `IX_MatchEvents_MatchId`). So F-03 needs **additive** `AddColumn`/`CreateTable`, not a from-scratch create.

### Infra wiring — net-new for F-03

- **No Functions/Worker/BackgroundService/IHostedService anywhere** (baseline confirmed via `prediction-league.slnx` + grep).
- `Program.cs` (Api): `AddControllers` (9), `AddOpenApi` (11), `AddInfrastructure(config)` (14), `AddHealthChecks().AddDbContextCheck` (17). **No `AddHttpClient`/IHttpClientFactory.**
- Zero existing HttpClient/external-HTTP usage. No `JsonSerializerOptions` config (ASP.NET defaults only).
- Config/secrets: `appsettings.json`/`.Development.json` carry logging only — no connection strings in source. DB conn read via `config.GetConnectionString("DefaultConnection")` (`DependencyInjection.cs:16`) from user-secrets/env. `UserSecretsId` = `cd81226a-...` (`.Api.csproj:8`). → api-key slots in the same way (e.g. `ApiFootball:ApiKey` via user-secrets, mirrored empty in appsettings).
- NuGet missing for F-03: `Microsoft.Extensions.Http` (HttpClientFactory), optional Polly (resilience), any `Microsoft.Azure.Functions.Worker.*` if a Functions project is chosen.
- CI: only `.github/workflows/azure-static-web-apps-*.yml` — deploys **client only**, `api_location` empty (line 32). Server not built in CI.

### Scoring mapping — VERIFIED

`ScoringParameter` (`Enums.cs:24-30`) is a C# `enum`, persisted as **int** via `.HasConversion<int>()` (`ScoringRuleConfiguration.cs:13`, migration col `int` at InitialCreate.cs:217). `ScoringRule.Parameter`→`ScoringRule.Points int` (`ScoringRule.cs:10,12`).

| Parameter | API data source | Status |
| --- | --- | --- |
| ExactScore | `HomeScore`+`AwayScore` (from `score.fulltime`) | ✓ |
| CorrectOutcome | derived W/D/L from scores | ✓ |
| CorrectGoalScorer | event `type==Goal` + `Player` | ✓ |
| CorrectCardCount | count `type==Card` (Yellow+Red) | ✓ total; ✗ can't split yellow/red — no `CorrectYellowCards`/`CorrectRedCards` member |

All four needed members exist. F-03 ingest does not require enum changes for scoring; the yellow/red split is a future S-04/S-07 concern only if a league rule needs per-type card scoring (API supplies it via `detail`).

## Compatibility verdict (api-reference.md ↔ code)

**REUSE as-is:** `fixture.date`→`KickoffUtc` (both tz-aware DateTimeOffset), `score.fulltime`/`goals`→`HomeScore`/`AwayScore int?`, `league.id`→`Tournament.ExternalApiId`. Repo/DbContext/migration/DI patterns all extend cleanly.

**MUST ADD (additive migration + model):**
1. `Match.ExternalFixtureId` (`int?`) **+ unique index** — without it every poll re-inserts duplicate fixtures; blocks idempotent upsert. **Highest priority.**
2. `Match.Season` (int) + `Match.Round` (string) — API `league.season`/`round`; needed to scope queries and dedupe.
3. `MatchEvent.Team` (which side) — currently impossible to attribute a goal/card to home vs away.
4. `MatchEvent.Detail` (string) or enum expansion — API `detail` carries Own Goal / Penalty / Missed Penalty; api-reference.md:75-76 needs it to exclude Missed Penalty and decide Own Goal handling for `CorrectGoalScorer`.
5. New `GetByExternalIdAsync` query on a new `IMatchRepository` (generic `IRepository` only has `GetByIdAsync(Guid)`) — needed for upsert.

**MISMATCHES to handle in ingest mapping (not blockers):**
- Status: API `status.short` (NS/1H/HT/2H/LIVE/FT/…) → 3-bucket `MatchStatus` (FT→Finished, NS→Scheduled, else→Live).
- Event type: API `type`+`detail` flattened into `MatchEventType {Goal, YellowCard, RedCard}` — pair with a `Detail` column to avoid losing goal sub-types.
- Minute: API `time.elapsed`+`time.extra`; domain `Minute` single int — stoppage `extra` lost unless captured.
- Keys: API ids are `int`, all PKs are `Guid` → store API ints as external-key columns, never as PKs.

**INFRA to add (net-new):** HttpClient via `IHttpClientFactory` typed client + `System.Text.Json`; api-key in user-secrets following `DefaultConnection`; a scheduler host. api-reference.md assumes **Azure Functions timer** — that project does not exist. Decision point: separate Functions/Worker project vs a simpler in-Api `IHostedService`/`BackgroundService` timer (lower ceremony, writes through the same scoped repos). Either writes through F-01 repos exactly as api-reference.md §.NET describes.

## Code References

- `src/server/prediction-league.slnx:1-7` — 4 projects, no Functions/Worker
- `src/server/PredictionLeague.Domain/Entities/Match.cs:4-38` — Match + MatchEvent (gaps: external id, Season/Round, Team, Detail)
- `src/server/PredictionLeague.Domain/Entities/Tournament.cs:11` — `ExternalApiId` (the one external-key that exists)
- `src/server/PredictionLeague.Domain/Entities/Enums.cs:3-30` — MatchStatus / MatchEventType / ScoringParameter
- `src/server/PredictionLeague.Domain/Entities/ScoringRule.cs:10,12` — Parameter→Points
- `src/server/PredictionLeague.Application/Abstractions/Persistence/IRepository.cs:4-17` — generic repo (no GetByExternalId)
- `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/BaseRepository.cs:8-34` — repo base to copy
- `src/server/PredictionLeague.Infrastructure/Persistence/AppDbContext.cs:20-22` — Tournaments/Matches/MatchEvents DbSets exist
- `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/MatchConfiguration.cs`, `MatchEventConfiguration.cs`, `ScoringRuleConfiguration.cs:13` — Fluent configs (HasConversion<int>)
- `src/server/PredictionLeague.Infrastructure/Persistence/Migrations/20260530155119_InitialCreate.cs:70-83,231-274` — tables already created
- `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs:16,21-24` — register new repos + future AddFootballApiClient here
- `src/server/PredictionLeague.Api/Program.cs:9-17` — no AddHttpClient
- `src/server/PredictionLeague.Api/PredictionLeague.Api.csproj:8` — UserSecretsId
- `.github/workflows/azure-static-web-apps-*.yml:32` — client-only CI (api_location empty)

## Architecture Insights

- F-01 deliberately pre-seeded the ingest target tables (Tournaments/Matches/MatchEvents) even though no slice writes them yet — F-03 inherits a ready persistence shell, only column additions needed.
- DbContext-as-UoW + per-aggregate marker repos is the established pattern; F-03 adds `IMatchRepository`/`ITournamentRepository` the same way.
- All external provider ids must be **separate external-key columns** (int) distinct from Guid PKs — the existing `Tournament.ExternalApiId` is the precedent to follow on `Match`/`MatchEvent`.
- Lessons: every EF-mapped string needs explicit `IsRequired()`/`HasMaxLength()` in a Fluent config (`lessons.md`) — applies to the new `MatchEvent.Team`/`Detail`, `Match.Round` columns.

## Historical Context (from prior changes)

- `context/changes/football-api-ingest/api-research.md` — source decision: API-Football free tier, `x-apisports-key`, 100 req/day cap, poll-frugal + cache-hard, role-split fallback documented but not built. .NET shape = HttpClient + System.Text.Json, no SDK.
- `context/changes/football-api-ingest/api-reference.md` — endpoint/payload contracts (`/fixtures`, `/fixtures/events`), envelope `errors[]` handling, status codes, scoring map. Assumes Azure Functions timer + writes through F-01 repos.
- `context/changes/layered-backend-persistence/` — F-01 (done 2026-05-31) built the layered solution + persistence shell this ingest extends.

## Open Questions

1. **Scheduler host:** Azure Functions timer (matches api-reference + infra-v2) vs in-Api `BackgroundService`? Functions defers to F-04 deploy work; BackgroundService ships F-03 self-contained. (Decide in `/10x-plan`.)
2. **Team/Player: strings vs lookup entities?** MVP scoring (`CorrectGoalScorer`) compares names, so strings suffice — but the API gives stable `{id,name}`; adding `MatchEvent.Team` is required regardless. Full Team/Player tables are optional.
3. **Goal `detail` modeling:** add `MatchEvent.Detail string` vs expand `MatchEventType` to carry Own Goal / Penalty / Missed Penalty. api-reference.md:75-76 requires excluding Missed Penalty and an Own-Goal decision.
4. **Card granularity:** keep `CorrectCardCount` total-only, or is a future `CorrectYellowCards`/`CorrectRedCards` rule wanted? API supports the split.
5. **Status mapping table:** confirm the short-code→3-bucket mapping (esp. extra-time/penalty short codes) during planning.

## Related Research

- `context/changes/football-api-ingest/api-research.md`
- `context/changes/football-api-ingest/api-reference.md`
