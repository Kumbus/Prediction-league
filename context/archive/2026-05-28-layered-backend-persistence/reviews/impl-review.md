<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Layered Backend + EF Core Persistence (F-01)

- **Plan**: context/changes/layered-backend-persistence/plan.md
- **Scope**: Full plan — Phases 1–5 of 5
- **Date**: 2026-05-31
- **Verdict**: NEEDS ATTENTION → triaged 2026-05-31 (4 fixed, 1 accepted, 1 noted)
- **Findings**: 0 critical · 4 warnings · 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS (build env-blocked — see note) |

**Success-criteria note:** `dotnet build` reached the DLL-copy stage (compilation OK, 30 warnings) but failed MSB3021/MSB3027 — `PredictionLeague.Api` (PID 34468) was running and held the output DLLs. Environmental lock, not a code failure. Stop the running app + rebuild for a clean green. Grep checks pass (no `Models`/`LeaguesController`/`CreateLeagueRequest`). Migration `20260530155119_InitialCreate` present. Prior phase checks verified at their commits.

## Findings

### F1 — Startup auto-migrate has no error handling

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Reliability)
- **Location**: src/server/PredictionLeague.Api/Program.cs:27
- **Detail**: `db.Database.Migrate()` runs at boot inside `IsDevelopment()` with no try/catch. Container down or migration fault → host crashes with a raw stack trace. Dev-gated (local-only blast radius), but a logged message beats a crash dump on the common "forgot to start Docker" path.
- **Fix**: Wrap the migrate scope in try/catch; log a friendly "DB unreachable — is Docker up?" and rethrow (or fail fast).
- **Decision**: FIXED — try/catch + ILogger<Program> error log + rethrow (Program.cs:27)

### F2 — No connection-string guard in AddInfrastructure

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: src/server/PredictionLeague.Infrastructure/DependencyInjection.cs:17
- **Detail**: `config.GetConnectionString("DefaultConnection")` passed to `UseSqlServer` with no null/empty check. Missing user-secret → opaque deferred error at first DB use instead of a clear config failure at startup.
- **Fix**: Throw InvalidOperationException with a clear message if the connection string is null/empty before calling UseSqlServer.
- **Decision**: FIXED — null/empty guard throws InvalidOperationException (DependencyInjection.cs:16)

### F3 — ApplicationUser.DisplayName is nvarchar(max)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Data safety)
- **Location**: src/server/PredictionLeague.Infrastructure/Identity/ApplicationUser.cs:10 → migration 20260530155119_InitialCreate.cs:33
- **Detail**: `DisplayName` has no `HasMaxLength`, so it materializes as `nvarchar(max)`. Plan explicitly called for max-lengths on lookup strings to avoid `nvarchar(max)`; every other string column complies. This is the lone break — a user-facing, query-adjacent field that can't be indexed as max. Cheaper to fix pre-prod than re-migrate later.
- **Fix**: Add an ApplicationUser config (or builder.Entity in OnModelCreating) setting DisplayName HasMaxLength(256), then regenerate/amend the migration.
- **Decision**: FIXED + ACCEPTED-AS-RULE: "Custom string properties need explicit HasMaxLength" — new ApplicationUserConfiguration (HasMaxLength 256) + migration/Designer/snapshot amended to nvarchar(256). Dev DB must be dropped+re-migrated to apply (migration edited in place). Recurring rule recorded in context/foundation/lessons.md.

### F4 — Two unplanned files: .env.example, server AGENTS.md

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/server/.env.example, src/server/AGENTS.md
- **Detail**: Neither in plan. Both benign: `.env.example` is a justified companion to the Docker work (compose resolves `MSSQL_SA_PASSWORD` from it; real `.env` is gitignored). `AGENTS.md` is server-scoped onboarding docs, no build impact, reflects implemented state. Positive scope creep.
- **Fix**: Accept both; no action needed beyond acknowledging the addition.
- **Decision**: ACCEPTED — both benign; kept as-is.

### F5 — .env.example ships a realistic-looking password

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Security)
- **Location**: src/server/.env.example:3
- **Detail**: Example value `Your_strong_Pass123` looks usable; devs copy it verbatim → weak well-known SA password. Template only, dev-only, so low risk.
- **Fix**: Replace with an obvious placeholder like `<set-a-strong-password>`.
- **Decision**: FIXED — placeholder `<set-a-strong-password>` (.env.example:3)

### F6 — appsettings.Development.json omits the documented ConnectionStrings key

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/server/PredictionLeague.Api/appsettings.Development.json
- **Detail**: Plan said this file carries `DefaultConnection` (password via user-secrets). Actual: whole connection string deferred to user-secrets; file holds only Logging. Safety intent (no committed password) fully satisfied — pure doc-vs-reality gap, arguably cleaner than planned.
- **Fix**: None needed; optionally note the user-secrets-only approach in the plan/change epilogue.
- **Decision**: NOTED — user-secrets-only approach recorded in change.md epilogue.
