# F-03 Football API Ingest — Plan Brief

> Full plan: `context/changes/football-api-ingest/plan.md`
> Research: `context/changes/football-api-ingest/research.md`
> Source decision: `context/changes/football-api-ingest/api-research.md` · Contracts: `api-reference.md`

## What & Why

Build the football-data ingest foundation (F-03): an API-Football client + scheduled
ingest that populates fixtures, results, and per-match detail (scorers, cards) into
the F-01 data layer. This unlocks S-02 (admin seeding) and feeds S-07 (auto-scoring)
— without it there is no real match data for the prediction loop to score against.

## Starting Point

F-01 shipped the persistence **shell**: layered solution (Domain/Application/
Infrastructure/Api), EF Core, the `BaseRepository<T>` + marker-interface pattern, and
placeholder `Tournaments`/`Matches`/`MatchEvents` tables. But `Match` stores teams as
**strings**, `MatchEvent` can't attribute a goal/card to a team, there is no external
fixture id (so no idempotent upsert), and there is **zero** HTTP client or scheduler
host anywhere.

## Desired End State

Set `ApiFootball:ApiKey` in user-secrets, run the Api, POST the manual-trigger
endpoint for a seeded tournament → the system calls API-Football, maps the payload
into a relational model (Team / Player / event-type dictionary), and upserts
fixtures + results + events **idempotently**. An Azure Functions timer runs the same
ingest service on a schedule, within the free-tier 100 req/day budget.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Source API | API-Football (api-sports.io) free tier | `Events` gives scorers+cards at €0; hard budget constraint | Research |
| Scheduler host | Azure Functions timer (new project) | Matches api-reference + infra-v2 deploy plan | Plan |
| Team/Player model | Team + Player tables; Player has club + national team; Match → two Teams | Richer relational integrity over MVP strings | Plan |
| Event type | `MatchEventType` becomes a **dictionary table** (event → type + player + team) | Lossless sub-types (Own Goal / Missed Penalty) without enum churn | Plan |
| Card scoring | Add `CorrectYellowCards` / `CorrectRedCards` to `ScoringParameter` | Per-color scoring wanted now | Plan |
| Player population | Players pre-seeded with both teams; ingest references by external id (minimal-create fallback) | API never labels a match team club vs national | Plan |
| Resilience | Polly typed-client policies + rate-limit quota guard | Standard .NET pattern; enforce budget in one place | Plan |
| Verification | Guarded Api manual-trigger endpoint + the timer (shared service) | Verifiable end-to-end before S-02 UI exists | Plan |
| Status mapping | Keep 3-bucket `MatchStatus` (FT→Finished, NS→Scheduled, else→Live) | Sufficient; orthogonal to event model | Plan |
| CI | None this slice | F-04 owns server + Functions CI/deploy | Plan |

## Scope

**In scope:** relational match-data model + one migration; new repos; Polly typed
HTTP client + DTOs + config/secrets; shared idempotent ingest service; Azure
Functions timer project; guarded manual-trigger endpoint.

**Out of scope:** football-data.org fallback; live/15s polling; CI/deploy (F-04);
S-02 admin UI; club/national classification via extra API calls; logo download
pipeline; prediction/scoring logic.

## Architecture / Approach

Bottom-up, each phase independently verifiable. One `IFixtureIngestService` is the
single seam shared by the Functions timer and the Api endpoint — both resolve it from
DI and call one method, so there is exactly one mapping/upsert path. Abstractions
(`IFootballApiClient`, `IFixtureIngestService`, repo interfaces) live in Application;
impls (typed client + Polly + DTOs, ingest service, EF) in Infrastructure; thin hosts
in Api + the new Functions project.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain model + migration | Team/Player/MatchEventType + reshaped Match/MatchEvent + 1 migration | Destructive column rework (safe: no prod data) |
| 2. Repositories + DI | External-id lookup repos copying F-01 pattern | Low |
| 3. HTTP client + DTOs + config | Polly typed client, envelope DTOs, api-key slot | Envelope `errors` array-vs-`{}` quirk; quota guard |
| 4. Ingest service | Mapping + idempotent upsert (fixtures upsert, events delete-replace) | Event idempotency (no stable API event id) |
| 5. Hosts | Functions timer project + manual-trigger endpoint | New Functions project + local Functions tooling |

**Prerequisites:** F-01 (done); a valid API-Football key in user-secrets; a seeded
Tournament row with `ExternalApiId`.
**Estimated effort:** ~3–4 sessions across 5 phases.

## Open Risks & Assumptions

- **Players seeded upfront** with club+national teams is assumed; the ingest
  minimal-create fallback prevents dropped events if a seed is missing, but the
  club/national split won't be populated for unseeded players.
- Free-tier **100 req/day** is the binding constraint; the quota guard must actually
  stop runs, not just log.
- Azure Functions isolated-worker on **.NET 10** + local Core Tools must be available
  for local timer verification.
- The destructive Match/MatchEvent migration is only safe while no prod data exists
  (pre-F-04).

## Success Criteria (Summary)

- Manual trigger ingests a seeded tournament end-to-end: correct scores, status, and
  scorer/card attribution to the right player + team.
- Re-running ingest is idempotent — stable row counts, no duplicates.
- The Functions timer runs the same ingest locally without error, and the whole
  solution (incl. Functions) builds.
