# Layered Backend + EF Core Persistence (F-01) — Plan Brief

> Full plan: `context/changes/layered-backend-persistence/plan.md`

## What & Why

Rebuild the throwaway single-project backend into a four-project **layered solution** (Domain / Application / Infrastructure / Api) with **EF Core** persistence and an initial migration. This is roadmap foundation **F-01** — every downstream data slice (S-02 tournaments, S-03 leagues, and beyond) writes through this layer, so the shape must be right before anything builds on it.

## Starting Point

`src/server` is a single .NET 10 webapi project with clean domain POCOs in `Models/`, a `LeaguesController` over a `static List<League>` placeholder, and no EF Core, no DbContext, no DB. The domain references users by bare Guid (no `User` navigation), which lets the user type move to Identity without coupling the Domain.

## Desired End State

A layered solution that builds clean, EF Core + ASP.NET Core Identity wired (schema only), an initial migration (S-02/S-03 entities + Identity tables, **no Predictions**) that applies to a local Docker SQL Server, and a `/health/db` endpoint returning `Healthy`. The static-list controller is gone.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Layering strictness | Pragmatic — EF maps domain POCOs via Fluent API | Keeps clean models, no dual-model overhead for a solo MVP | Plan |
| Data access | Repository interfaces (App) + EF impl (Infra), `BaseRepository<T>` + per-aggregate | Persistence-agnostic Application, shared CRUD base | Plan |
| Projects | 4 projects; host renamed to `.Api` | Conventional .NET layered names | Plan |
| DTOs | Manual records, hand-mapping | Zero dependency, trivial at this scale | Plan |
| Local DB | SQL Server in Docker | Provider parity with prod Azure SQL | Plan |
| Migration scope | S-02 + S-03 entities + Identity; no Predictions | Matches F-01 scope guard | Plan / Roadmap |
| User | `ApplicationUser : IdentityUser<Guid>` in Infrastructure (Identity schema only) | Identity-ready persistence now; OAuth wiring deferred to F-02 | Plan |
| Migrate exec | Auto in Dev only; prod manual/human-gated | Honors infra-v2 forward-only rule | Plan / Infra |
| EF config | Fluent, one `IEntityTypeConfiguration` per entity | Domain stays attribute-free | Plan |
| Controller | Remove `LeaguesController`; add `/health/db` | DB smoke test; CRUD returns in S-03 | Plan |
| Testing / Seed | None | Speed goal; foundation slice | Plan |

## Scope

**In scope:** 4-project restructure; EF Core SqlServer + Identity packages; `AppDbContext` (Guid-keyed IdentityDbContext); per-entity Fluent configs; `IRepository<T>`/`BaseRepository<T>` + `ILeagueRepository`; DI extension; Docker SQL + connection config (no committed secrets); initial migration; dev auto-migrate; `/health/db`; remove static store.

**Out of scope:** OAuth / sign-in / auth middleware / roles (F-02); `Prediction` table (S-06); scoring engine, API ingest, business endpoints (later slices); tests; seed data; CI/Azure deploy changes.

## Architecture / Approach

Pragmatic Clean Architecture: Domain (POCOs, no EF) ← Application (repository interfaces, DTOs) ← Infrastructure (AppDbContext, ApplicationUser, Fluent configs, repos, DI extension) ← Api (thin host). Migrations live in Infrastructure with Api as the EF startup project. Local dev runs Dockerized SQL Server matching prod Azure SQL.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Restructure | 4 projects, host→`.Api`, domain moved, build green | Namespace churn / reference cycles |
| 2. EF + Identity | `AppDbContext` (Guid Identity) + Fluent configs | Wrong Identity key type breaks Guid FKs |
| 3. Repositories + DI | `IRepository`/`BaseRepository` + `ILeagueRepository` + DI ext | Over-broad generic repo |
| 4. Host + Docker + config | Infra registered, Docker SQL, conn string (no secrets) | EF startup/migrations project split; secret leakage |
| 5. Migration + proof | Initial migration applied, `/health/db`, controller removed | Migration includes/excludes wrong tables |

**Prerequisites:** Docker Desktop running; .NET 10 SDK + `dotnet-ef` tool.
**Estimated effort:** ~1–2 sessions across 5 phases.

## Open Risks & Assumptions

- EF tooling needs the `--project Infrastructure --startup-project Api` split; an `IDesignTimeDbContextFactory` is the fallback if path resolution is fragile.
- Identity must use `Guid` keys (`IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`) or every `*UserId` FK breaks.
- Identity *schema* lands in F-01; actual auth wiring is F-02 — `UseAuthorization()` stays unconfigured until then.
- Prod migrations are forward-only + human-gated; auto-migrate is Development-only.

## Success Criteria (Summary)

- `dotnet build` green; `InitialCreate` migration applies cleanly to Docker SQL.
- `GET /health/db` returns `Healthy`; DB has domain + `AspNet*` tables, no `Predictions`.
- Static-list `LeaguesController` removed; layer boundaries intact.
