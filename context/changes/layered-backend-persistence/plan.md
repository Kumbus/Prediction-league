# Layered Backend + EF Core Persistence (F-01) Implementation Plan

## Overview

Rebuild `src/server` from a single ASP.NET Core webapi project into a four-project **layered solution** — `PredictionLeague.Domain`, `PredictionLeague.Application`, `PredictionLeague.Infrastructure`, `PredictionLeague.Api` — backed by **EF Core (SQL Server provider)** with persistence wired to a local **Docker SQL Server** for dev and **Azure SQL Basic** for prod. User identity is persisted via **ASP.NET Core Identity** (schema/tables only; no auth middleware — that is F-02). An **initial migration** covers only the entities the next slices (S-02, S-03) exercise plus the Identity tables. A `/health/db` endpoint proves the stack end-to-end. This is the foundation every downstream data slice writes through.

## Current State Analysis

- **Single project** at `src/server/PredictionLeague.csproj` (`Microsoft.NET.Sdk.Web`, .NET 10, nullable + implicit usings on). References only `Microsoft.AspNetCore.OpenApi`.
- **Solution** `src/server/prediction-league.slnx` references the one csproj (`.slnx` XML format).
- **Domain already modeled** in `src/server/Models/` (namespace `PredictionLeague.Models`): clean POCOs with `required` props, Guid keys, nav collections, FR-tagged comments — `League`/`LeagueMembership` (League.cs), `Tournament` (Tournament.cs), `Match`/`MatchEvent` (Match.cs), `Prediction` (Prediction.cs), `ScoringRule` (ScoringRule.cs), `User` (User.cs), `Enums.cs` (`MatchStatus`, `MatchEventType`, `MembershipRole`, `ScoringParameter`).
- **Key insight:** domain entities reference users by **Guid only** — `League.OrganizerUserId`, `LeagueMembership.UserId`, `Prediction.UserId` are bare Guids with **no navigation property to `User`**. This means the `User` type can move to Infrastructure as an Identity user without the Domain referencing it.
- **No persistence:** no EF packages, no `DbContext`, no migrations, no DB.
- **Throwaway store:** `src/server/Controllers/LeaguesController.cs` keeps a `static List<League>` + `CreateLeagueRequest` record. AGENTS.md flags it for removal when persistence lands.
- **`Program.cs`** minimal: `AddControllers`, `AddOpenApi`, `UseHttpsRedirection`, `UseAuthorization` (unconfigured), `MapControllers`.
- **Infra plan** (`context/foundation/infrastructure-v2.md`): Azure SQL **Basic DTU** (no auto-pause), connection string injected as an App Service app setting, EF Core reads it at startup. Migrations are **forward-only**; prod migrations are **human-gated**.
- Host: **Windows 11**, solo dev, speed goal, MVP deadline **2026-06-11**. No test suite exists.

## Desired End State

A four-project layered solution that builds clean, with EF Core + Identity wired, an initial migration that applies to a local Docker SQL Server, and a `/health/db` endpoint that returns healthy. Verify by: `dotnet build` green; `dotnet ef migrations list` shows the initial migration; with the Docker SQL container up, `dotnet run` auto-applies the migration in Development and `GET /health/db` returns `Healthy`; the database contains the domain tables + AspNet* Identity tables; the static-list `LeaguesController` is gone.

### Key Discoveries:

- Domain has no `User` navigation (`src/server/Models/League.cs:12,28`, `Prediction.cs:11`) → `ApplicationUser : IdentityUser<Guid>` can live in Infrastructure with Domain staying Identity-free.
- All FKs are `Guid` → Identity must be configured with **`Guid` keys** (`IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`) so existing `*UserId` columns line up.
- `.slnx` format (`src/server/prediction-league.slnx`) — add `<Project Path=...>` entries, not `.sln` sections.
- Infra rule: prod migrations forward-only + human-gated → auto-migrate must be **Development-guarded**.

## What We're NOT Doing

- **No OAuth / sign-in / authentication middleware / authorization policies / roles seeding** — that is F-02 (`auth-oauth-scaffold`). F-01 adds only the Identity *schema*.
- **No `Prediction` table in this migration** — S-06 owns it; added via its own migration later.
- **No tests / test project** — verify via build + migration + health endpoint (speed goal).
- **No data seeding** — empty DB after migration; S-02 seeds tournaments.
- **No scoring engine, no API ingest, no business endpoints** — later slices.
- **No DTO framework / AutoMapper / Mapperly** — manual DTOs when slices need them (none needed now).
- **No CI/Azure deployment changes** — local Docker dev loop only; prod connection injection already documented in infra-v2.

## Implementation Approach

Pragmatic Clean Architecture. Domain holds the existing POCOs with zero EF attributes. Application holds repository interfaces (`IRepository<T>`, `ILeagueRepository`) and is persistence-agnostic. Infrastructure owns EF Core: `AppDbContext`, `ApplicationUser`, per-entity `IEntityTypeConfiguration` (Fluent API), `BaseRepository<T>` + concrete repos, and an `AddInfrastructure` DI extension. Api is the thin host. Migrations live in Infrastructure; the Api project is the EF startup project and carries the design-time tooling. Local dev runs against a Dockerized SQL Server so the provider matches prod Azure SQL exactly.

## Critical Implementation Details

- **EF tooling project split:** `DbContext` + migrations live in `PredictionLeague.Infrastructure`, but the connection string + service registration live in `PredictionLeague.Api`. EF CLI commands must pass both: `dotnet ef migrations add <Name> --project ../PredictionLeague.Infrastructure --startup-project .` (run from the Api dir). A design-time `IDesignTimeDbContextFactory<AppDbContext>` in Infrastructure is the robust alternative if the startup-project path proves fragile.
- **Identity key type is load-bearing:** use `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. A wrong (string) key forces changing every `*UserId` FK — do not let the default string key slip in.
- **Dev-only auto-migrate:** `Database.Migrate()` must run only when `app.Environment.IsDevelopment()`. Prod stays forward-only + human-gated per infra-v2.

## Phase 1: Solution restructure & project skeleton

### Overview

Create the three new layer projects, rename the host to `.Api`, move the domain models into `PredictionLeague.Domain`, and wire references + namespaces. No behavior change; build stays green.

### Changes Required:

#### 1. New layer projects

**File**: `src/server/PredictionLeague.Domain/PredictionLeague.Domain.csproj`, `.Application/PredictionLeague.Application.csproj`, `.Infrastructure/PredictionLeague.Infrastructure.csproj`

**Intent**: Establish the layered project skeleton. Domain and Application are class libraries (`Microsoft.NET.Sdk`); Infrastructure is a class library. Match the host's `net10.0`, nullable + implicit usings on.

**Contract**: Three new `.csproj` files. References: Application → Domain; Infrastructure → Application. Domain references nothing.

#### 2. Rename host project to `.Api`

**File**: `src/server/PredictionLeague.csproj` → `src/server/PredictionLeague.Api/PredictionLeague.Api.csproj`

**Intent**: Make the webapi project an explicit host named `.Api`, holding `Program.cs`, `Properties/launchSettings.json`, `appsettings*.json`, `PredictionLeague.http`, and `Microsoft.AspNetCore.OpenApi`.

**Contract**: Host project renamed/moved under `PredictionLeague.Api/`. References Application + Infrastructure. Root namespace `PredictionLeague.Api`. Update `Properties/launchSettings.json` and `PredictionLeague.http` to keep dev URL `http://localhost:5185`.

#### 3. Move domain models into Domain

**File**: `src/server/PredictionLeague.Domain/Entities/*.cs` (from `src/server/Models/*.cs`)

**Intent**: Relocate `League`/`LeagueMembership`, `Tournament`, `Match`/`MatchEvent`, `Prediction`, `ScoringRule`, `Enums` into Domain. **Delete `User.cs`** — replaced by `ApplicationUser` in Infrastructure (Phase 2). Keep all `// FR-00x` comments and `required` props.

**Contract**: Namespace changes `PredictionLeague.Models` → `PredictionLeague.Domain` (or `PredictionLeague.Domain.Entities`). `User.cs` removed. No member/shape changes to the surviving entities.

#### 4. Update solution + remove the static-store controller's project home

**File**: `src/server/prediction-league.slnx`, `src/server/Controllers/LeaguesController.cs`

**Intent**: Register all four projects in the `.slnx`. The `LeaguesController` (static-list store) is removed in Phase 5; in Phase 1 it must keep compiling after the namespace move (its `using PredictionLeague.Models;` becomes the new Domain namespace) OR be moved with the Api project — keep it temporarily so build stays green, delete in Phase 5.

**Contract**: `.slnx` lists `PredictionLeague.Api`, `.Application`, `.Domain`, `.Infrastructure`. `LeaguesController` still present, namespace import updated, compiles against the moved `League` type.

### Success Criteria:

#### Automated Verification:

- [ ] Solution builds: `dotnet build src/server/prediction-league.slnx`
- [ ] All four projects resolve in the solution: `dotnet sln src/server/prediction-league.slnx list` (or `.slnx` equivalent) shows 4 projects
- [ ] No stray references to `PredictionLeague.Models` remain: grep returns nothing

#### Manual Verification:

- [ ] Project dependency directions are correct (Domain depends on nothing; Application→Domain; Infrastructure→Application; Api→Application+Infrastructure)
- [ ] Dev URL still `http://localhost:5185` and `PredictionLeague.http` requests resolve

**Implementation Note**: After this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: EF Core + Identity DbContext

### Overview

Add EF Core SQL Server + Identity packages to Infrastructure, define `ApplicationUser`, the `AppDbContext` as an `IdentityDbContext`, and one `IEntityTypeConfiguration` per domain entity. Compiles; no DB yet.

### Changes Required:

#### 1. Infrastructure packages

**File**: `src/server/PredictionLeague.Infrastructure/PredictionLeague.Infrastructure.csproj`

**Intent**: Add the persistence + Identity dependencies.

**Contract**: PackageReferences: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (matched to the .NET 10 / EF Core 10 line). `Microsoft.EntityFrameworkCore.Design` is added to the Api startup project (Phase 4) for tooling.

#### 2. ApplicationUser

**File**: `src/server/PredictionLeague.Infrastructure/Identity/ApplicationUser.cs`

**Intent**: The persisted user, Identity-backed, Guid-keyed, carrying the custom profile fields the domain needs.

**Contract**: `public class ApplicationUser : IdentityUser<Guid>` with `required string DisplayName` and `bool IsGlobalAdmin`. `ExternalAuthId` is **dropped** — the OAuth subject lives in `AspNetUserLogins` (F-02). Existing Guid `*UserId` columns reference `ApplicationUser.Id`.

#### 3. AppDbContext

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/AppDbContext.cs`

**Intent**: The EF Core context, Identity-aware with Guid keys, exposing the domain aggregates and auto-applying entity configurations.

**Contract**: `public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. `DbSet`s for `League`, `LeagueMembership`, `Tournament`, `Match`, `MatchEvent`, `ScoringRule` (NOT `Prediction`). `OnModelCreating` calls `base.OnModelCreating(...)` then `ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)`.

#### 4. Per-entity Fluent configurations

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/*Configuration.cs`

**Intent**: One `IEntityTypeConfiguration<T>` per entity to keep Domain attribute-free and configuration organized — keys, required/max-length on string props (`Name`, `InviteCode`, team names, `Player`), enum-to-int (default), and the League→ScoringRules / League→Memberships / Tournament→Matches / Match→Events relationships with FK + delete behavior.

**Contract**: Config classes for `League`, `LeagueMembership`, `Tournament`, `Match`, `MatchEvent`, `ScoringRule`. Relationships configured via Fluent API; cascade-delete chosen explicitly per relationship (e.g. League→ScoringRules cascade; user references are bare Guids, no FK constraint to AspNetUsers in this slice unless trivially addable). String max-lengths set to avoid `nvarchar(max)` on lookup columns.

### Success Criteria:

#### Automated Verification:

- [ ] Solution builds: `dotnet build src/server/prediction-league.slnx`
- [ ] `AppDbContext` model validates at design time: `dotnet ef dbcontext info --project src/server/PredictionLeague.Infrastructure --startup-project src/server/PredictionLeague.Api` — **deferred to Phase 4** (requires the EF.Design tooling added in Phase 4)

#### Manual Verification:

- [ ] `AppDbContext` uses `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` (Guid keys confirmed)
- [ ] No `Prediction` DbSet present
- [ ] Domain entities carry no EF attributes (config is all Fluent)

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 3: Repository abstraction + DI

### Overview

Define the persistence-agnostic repository contracts in Application and their EF implementations in Infrastructure, plus the `AddInfrastructure` DI extension. Compiles; not yet called by the host.

### Changes Required:

#### 1. Repository interfaces (Application)

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/IRepository.cs`, `ILeagueRepository.cs`

**Intent**: Generic CRUD contract plus a per-aggregate interface, so Application stays persistence-agnostic and slices depend on intention-revealing repos.

**Contract**: `IRepository<T>` with async basic CRUD — `GetByIdAsync(Guid)`, `GetAllAsync()`, `AddAsync(T)`, `Update(T)`, `Remove(T)`, `SaveChangesAsync()`. `ILeagueRepository : IRepository<League>` (add league-specific queries, e.g. `GetByInviteCodeAsync`, only as needed — none required this slice). Interfaces reference Domain types only.

#### 2. BaseRepository + concrete repos (Infrastructure)

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/BaseRepository.cs`, `LeagueRepository.cs`

**Intent**: A reusable EF base implementing the generic CRUD against `AppDbContext`, with per-aggregate repos inheriting it.

**Contract**: `public abstract class BaseRepository<T> : IRepository<T> where T : class` backed by `AppDbContext` + `DbSet<T>`. `LeagueRepository : BaseRepository<League>, ILeagueRepository`. `SaveChangesAsync` delegates to the context.

#### 3. DI extension

**File**: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs`

**Intent**: One call the host uses to register the context + repositories, keeping `Program.cs` thin.

**Contract**: `public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)` registering `AddDbContext<AppDbContext>(o => o.UseSqlServer(config.GetConnectionString("DefaultConnection")))` and scoped repository registrations (`ILeagueRepository → LeagueRepository`). Connection string is read here but consumed at host startup (Phase 4).

### Success Criteria:

#### Automated Verification:

- [ ] Solution builds: `dotnet build src/server/prediction-league.slnx`

#### Manual Verification:

- [ ] `IRepository<T>` exposes only generic CRUD; `ILeagueRepository` extends it
- [ ] `BaseRepository<T>` is the shared CRUD base; `LeagueRepository` inherits it
- [ ] Repository interfaces live in Application; implementations in Infrastructure (layer boundary intact)

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 4: Host wiring, connection config & Docker SQL

### Overview

Wire the Api host to Infrastructure, add the local Docker SQL Server, configure the connection string for dev (no secrets committed), and add EF design-time tooling — without yet creating the migration.

### Changes Required:

#### 1. Local SQL Server via Docker

**File**: `src/server/docker-compose.yml` (or repo-root `docker-compose.yml`)

**Intent**: A reproducible local SQL Server matching the prod provider, so migrations behave identically dev→prod.

**Contract**: One `mcr.microsoft.com/mssql/server:2022-latest` service, `ACCEPT_EULA=Y`, `SA_PASSWORD` (dev-only), port `1433:1433`, a named volume for data. A short README/`change.md` note documents `docker compose up -d` and the dev connection string.

#### 2. Connection string config

**File**: `src/server/PredictionLeague.Api/appsettings.Development.json`, dev user-secrets

**Intent**: Provide the dev connection string to the Docker container without committing secrets; prod is injected as an App Service app setting per infra-v2.

**Contract**: `ConnectionStrings:DefaultConnection` pointing at `Server=localhost,1433;Database=PredictionLeague;User Id=sa;Password=...;TrustServerCertificate=True`. The password is supplied via `dotnet user-secrets` (preferred) or `appsettings.Development.json`. **Note:** `appsettings.Development.json` is currently TRACKED in git — before using it for any secret, add it to `.gitignore` AND run `git rm --cached src/server/appsettings.Development.json` so the password is never committed. `appsettings.json` carries NO real connection string. Verification 4.4 must confirm the file is untracked if it holds a password.

#### 3. EF design-time tooling on Api

**File**: `src/server/PredictionLeague.Api/PredictionLeague.Api.csproj`

**Intent**: Enable `dotnet ef` with Api as the startup project.

**Contract**: Add `Microsoft.EntityFrameworkCore.Design` PackageReference to the Api project.

#### 4. Register Infrastructure in Program.cs

**File**: `src/server/PredictionLeague.Api/Program.cs`

**Intent**: Call `AddInfrastructure(builder.Configuration)` so the context + repositories are available; leave `UseAuthorization` as-is (auth is F-02).

**Contract**: `builder.Services.AddInfrastructure(builder.Configuration);` added. No auth middleware changes. Build still green.

### Success Criteria:

#### Automated Verification:

- [ ] Solution builds: `dotnet build src/server/prediction-league.slnx`
- [ ] EF tooling resolves the context: `dotnet ef dbcontext info --project src/server/PredictionLeague.Infrastructure --startup-project src/server/PredictionLeague.Api`
- [ ] Docker SQL starts: `docker compose up -d` then container reports healthy

#### Manual Verification:

- [ ] No connection string / SA password committed to git (`appsettings.json` clean; secrets via user-secrets)
- [ ] App starts (`dotnet run` from Api) and connects configuration without throwing on missing connection string

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 5: Initial migration, DB proof & cleanup

### Overview

Generate and apply the initial migration (domain + Identity tables), guard auto-migrate to Development, remove the throwaway `LeaguesController`, and add a `/health/db` endpoint as the end-to-end proof.

### Changes Required:

#### 1. Initial migration

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Migrations/*_InitialCreate.cs` (generated)

**Intent**: Create the schema for the S-02/S-03 entities + Identity tables.

**Contract**: `dotnet ef migrations add InitialCreate --project src/server/PredictionLeague.Infrastructure --startup-project src/server/PredictionLeague.Api`. Resulting migration includes tables for `Leagues`, `LeagueMemberships`, `Tournaments`, `Matches`, `MatchEvents`, `ScoringRules`, and the `AspNet*` Identity tables. **No `Predictions` table.**

**Scope note:** Tournament/Match/MatchEvent = S-02, League = S-03. `ScoringRules` (S-04) and `LeagueMemberships` (S-05) ride along **not** as schema pre-build but because they are owned children of `League` via its nav collections (`League.ScoringRules`, `League.Memberships` — `League.cs:18,20`); EF pulls them into the model unless explicitly `Ignore<>()`-d, which would leave `League` half-mapped. Including them keeps the aggregate intact. `Predictions` has no nav prop into any mapped entity, so it stays out cleanly.

#### 2. Dev-only auto-migrate

**File**: `src/server/PredictionLeague.Api/Program.cs`

**Intent**: Apply pending migrations automatically in local dev only; prod stays forward-only + human-gated.

**Contract**: After `var app = builder.Build();`, inside `if (app.Environment.IsDevelopment())`, create a scope, resolve `AppDbContext`, call `Database.Migrate()`. Not executed outside Development.

#### 3. Remove static-store controller

**File**: `src/server/Controllers/LeaguesController.cs` (delete), `CreateLeagueRequest` record

**Intent**: Delete the throwaway `static List<League>` store + endpoints; league CRUD returns in S-03 against the repository.

**Contract**: `LeaguesController.cs` deleted. No remaining references to the static `Store` or `CreateLeagueRequest`.

#### 4. DB health endpoint

**File**: `src/server/PredictionLeague.Api/Program.cs` (+ `PredictionLeague.http`)

**Intent**: A minimal endpoint that proves DB connectivity end-to-end.

**Contract**: Add the `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` PackageReference to the Api project (provides `AddDbContextCheck`). Register `AddHealthChecks().AddDbContextCheck<AppDbContext>()` and `MapHealthChecks("/health/db")` — returns `Healthy` when the context can reach the DB. Add a sample request to `PredictionLeague.http`.

### Success Criteria:

#### Automated Verification:

- [ ] Solution builds: `dotnet build src/server/prediction-league.slnx`
- [ ] Migration exists: `dotnet ef migrations list --project src/server/PredictionLeague.Infrastructure --startup-project src/server/PredictionLeague.Api` shows `InitialCreate`
- [ ] Migration applies cleanly to Docker SQL: `dotnet ef database update --project src/server/PredictionLeague.Infrastructure --startup-project src/server/PredictionLeague.Api`
- [ ] No references to `LeaguesController` / `CreateLeagueRequest` remain (grep returns nothing)

#### Manual Verification:

- [ ] With Docker SQL up, `dotnet run` auto-applies the migration and `GET http://localhost:5185/health/db` returns `Healthy`
- [ ] DB contains the domain tables + `AspNet*` Identity tables, and **no** `Predictions` table
- [ ] Re-running `dotnet run` is idempotent (no duplicate migration, no error)

**Implementation Note**: Pause for manual confirmation; this is the end-to-end gate for F-01.

---

## Testing Strategy

No automated test suite this slice (speed goal; no existing harness). Verification is build + migration + health endpoint.

### Manual Testing Steps:

1. `docker compose up -d` (local SQL Server).
2. `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."` for the Api project.
3. `dotnet run` from `PredictionLeague.Api` → migration auto-applies in Development.
4. `GET http://localhost:5185/health/db` → expect `Healthy`.
5. Inspect the DB (e.g. via a SQL client): confirm domain + Identity tables, no `Predictions`.
6. Stop and re-run → confirm idempotent startup.

## Performance Considerations

None material for a foundation slice at low/small target scale. Prod uses Azure SQL Basic DTU (no auto-pause) so post-match recompute (later) avoids cold starts. String columns get explicit max-lengths to avoid `nvarchar(max)` on lookup fields.

## Migration Notes

Migrations are **forward-only** (infra-v2). Local dev auto-applies in Development only. Prod migration runs explicitly and is **human-gated** — never auto-migrate prod, never roll back by reverting code (write a reversing migration). EF tooling uses Api as startup project, Infrastructure as the migrations project.

## References

- Change identity: `context/changes/layered-backend-persistence/change.md`
- Roadmap item: `context/foundation/roadmap.md` F-01 (lines 67–78)
- Infra/DB plan: `context/foundation/infrastructure-v2.md` (Azure SQL Basic, forward-only migrations, secrets)
- Tech stack: `context/foundation/tech-stack.md`
- Existing domain models: `src/server/Models/*.cs`
- Throwaway store to remove: `src/server/Controllers/LeaguesController.cs:12`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Solution restructure & project skeleton

#### Automated

- [ ] 1.1 Solution builds: `dotnet build src/server/prediction-league.slnx`
- [ ] 1.2 All four projects resolve in the solution
- [ ] 1.3 No stray references to `PredictionLeague.Models` remain

#### Manual

- [ ] 1.4 Project dependency directions are correct
- [ ] 1.5 Dev URL still `http://localhost:5185` and `PredictionLeague.http` resolves

### Phase 2: EF Core + Identity DbContext

#### Automated

- [ ] 2.1 Solution builds
- [ ] 2.2 `AppDbContext` model validates at design time (deferred to Phase 4 — needs EF.Design tooling)

#### Manual

- [ ] 2.3 `AppDbContext` uses `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`
- [ ] 2.4 No `Prediction` DbSet present
- [ ] 2.5 Domain entities carry no EF attributes

### Phase 3: Repository abstraction + DI

#### Automated

- [ ] 3.1 Solution builds

#### Manual

- [ ] 3.2 `IRepository<T>` exposes only generic CRUD; `ILeagueRepository` extends it
- [ ] 3.3 `BaseRepository<T>` is the shared CRUD base; `LeagueRepository` inherits it
- [ ] 3.4 Repository interfaces in Application; implementations in Infrastructure

### Phase 4: Host wiring, connection config & Docker SQL

#### Automated

- [ ] 4.1 Solution builds
- [ ] 4.2 EF tooling resolves the context (`dotnet ef dbcontext info`)
- [ ] 4.3 Docker SQL starts and reports healthy

#### Manual

- [ ] 4.4 No connection string / SA password committed to git
- [ ] 4.5 App starts and connects configuration without throwing

### Phase 5: Initial migration, DB proof & cleanup

#### Automated

- [ ] 5.1 Solution builds
- [ ] 5.2 Migration exists (`dotnet ef migrations list` shows `InitialCreate`)
- [ ] 5.3 Migration applies cleanly to Docker SQL (`dotnet ef database update`)
- [ ] 5.4 No references to `LeaguesController` / `CreateLeagueRequest` remain

#### Manual

- [ ] 5.5 `dotnet run` auto-applies migration; `GET /health/db` returns `Healthy`
- [ ] 5.6 DB has domain + `AspNet*` tables, no `Predictions` table
- [ ] 5.7 Re-running `dotnet run` is idempotent
