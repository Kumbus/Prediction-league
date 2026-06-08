---
change_id: walking-skeleton-deploy
title: Walking-skeleton Azure deploy — layered API + Azure SQL, first prod migration
status: implementing
created: 2026-06-07
updated: 2026-06-08

# Resource audit trail (post-Phase 2)

| Resource              | Name                                         | SKU / Plan                         | Region        |
| --------------------- | -------------------------------------------- | ---------------------------------- | ------------- |
| Resource group        | rg-prediction-league                         | —                                  | polandcentral |
| App Service plan      | asp-prediction-league (existing)             | F1 Linux                           | polandcentral |
| API Web App           | prediction-league-api-0523444a (existing)    | .NET 10                            | polandcentral |
| SQL server            | sql-prediction-league                        | logical server, sqladmin           | polandcentral |
| SQL database          | appdb                                        | Basic (DTU, 5)                     | polandcentral |
| SQL firewall rule     | AllowAzureServices (0.0.0.0/0.0.0.0)         | "Allow Azure services"             | —             |
| Storage account       | stpredictionleague                           | Standard_LRS, StorageV2            | polandcentral |
| Function App          | func-prediction-league                       | **Flex Consumption** (Y1 unavail.) | polandcentral |
| Function App runtime  | dotnet-isolated 10.0                         | —                                  | —             |
| Application Insights  | func-prediction-league (auto-created by CLI) | —                                  | polandcentral |
| Static Web App (SPA)  | (existing from 2026-05-23 deploy)            | —                                  | —             |

**Phase 2 runtime deviation**: classic Y1 Linux Consumption was unavailable in `polandcentral` (`Linux dynamic workers are not available in resource group`) — fell back to **Flex Consumption** per Phase 1 decision. Billing model still consumption.

**Secrets** (NOT committed):
- SQL admin password: surfaced once in chat to operator at Phase 2 manual gate; stored in operator's password manager and GitHub repo secrets (`SQL_ADMIN_PASSWORD`, `AZURE_SQL_CONNECTION`).
- API-Football key: sourced from `dotnet user-secrets` (`src/server/PredictionLeague.Api`); set as App + Function app setting in Phase 3.
- Google OAuth client id/secret: **prod reuses the dev Google OAuth client** for the friend-MVP (consciously chosen). Replace before real-user GA. Sourced from `dotnet user-secrets`; set as API app settings.

# Phase 3 audit (2026-06-08)

**SPA origin**: `https://thankful-desert-02de6f703.7.azurestaticapps.net` (static web app `prediction-league-web`).

API app settings injected (`prediction-league-api-0523444a`):
- `ConnectionStrings__DefaultConnection` (Azure SQL)
- `Cors__AllowedOrigins__0` (SWA origin above)
- `ApiFootball__ApiKey`
- `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret`
- `httpsOnly=true`

Function App settings injected (`func-prediction-league`):
- `ConnectionStrings__DefaultConnection`
- `ApiFootball__ApiKey`, `ApiFootball__BaseUrl=https://v3.football.api-sports.io`
- `FixtureIngestSchedule=0 */30 * * * *` (every 30 min, match-window-frugal under API-Football free-tier 100 req/day cap)

**FUNCTIONS_WORKER_RUNTIME note**: On Flex Consumption the runtime is set via `functionAppConfig.runtime.name=dotnet-isolated` (verified in Phase 2) instead of as a classic app setting — the plan's "assert `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated` present" check is satisfied at the platform level.

**Google OAuth callback registration (Phase 3 follow-up, HUMAN action in Google Cloud Console)**: add `https://prediction-league-api-0523444a.azurewebsites.net/signin-google` as an authorized redirect URI on the (dev/prod-shared) OAuth client. Without it, Google login returns `redirect_uri_mismatch`.

# Phase 4 staging (2026-06-08)

Workflow file: `.github/workflows/deploy-backend.yml`. Jobs:

```
build  ──►  migrate  ──►  ┬── deploy-api  (azure/webapps-deploy + publish profile)
                          └── deploy-func (azure/login SP + Azure/functions-action)
```

Trigger: `push` to `main` touching `src/server/**` or the workflow itself; also `workflow_dispatch`.

Build step generates `migrate.sql` via `dotnet ef migrations script --idempotent` with a dummy `ConnectionStrings__DefaultConnection` env to defuse the `AddInfrastructure` connection-string-required throw (see `DependencyInjection.cs:25-28`). Migrate step uses the rg-scoped service principal (`AZURE_CREDENTIALS`) to open a transient `ci-<run-id>` SQL firewall rule for the runner IP, applies the script via `sqlcmd`, and deletes the rule in an `always()` step.

**Func deploy deviation (Phase 2 fallback consequence)**: `azure/webapps-deploy` does not apply to Function Apps on Flex Consumption (no publish profile). Replaced with `azure/login` + `Azure/functions-action@v1` using the same SP creds.

**Phase 4 status**: workflow file committed; **first run still pending** — operator will push to `main` manually. Automated checks 4.1–4.3 + manual 4.5 stay unchecked until the first green run.

# Phase 4 first-run record (2026-06-08)

Three commits to land a green workflow on `main`:

| Run | Commit | Outcome |
| --- | --- | --- |
| #27125534998 | 4771fe6 (PR #27 merge) | ❌ migrate: `CREATE INDEX failed because SET options have incorrect settings: 'QUOTED_IDENTIFIER'`. Azure SQL requires `QUOTED_IDENTIFIER ON` for Identity's filtered/computed-column indexes; sqlcmd defaults to OFF. |
| #27125657881 | bde0841 (fix: `sqlcmd -I`) | ✅ migrate. ❌ deploy-func: `InvalidPackageContentException: Cannot find required .azurefunctions directory at root level in the .zip package`. `actions/upload-artifact@v4` excludes hidden files by default — `.azurefunctions/` was dropped. ❌ deploy-api: `Publish profile is invalid for app-name and slot-name provided` because SCM Basic Auth is disabled on the API app (`basicPublishingCredentialsPolicies/scm.properties.allow=false`). |
| #27125886319 | ee5dbd3 (fix: `include-hidden-files: true` on func artifact; SP auth for API instead of publish-profile) | ✅ all jobs green. Idempotent EF script applied (both `20260530155119_InitialCreate` and `20260607113246_AddFootballIngestModel` baked in); `FixtureIngestTimer` registered on func app; `ci-<run>` firewall rule deleted by `always()` cleanup. |

**Post-deploy startup trap (resolved out-of-band, then baked into workflow)**: after the green deploy, the API responded 404 on every route and the docker log showed `Content root path: /defaulthome/hostingstart/`. wwwroot inspection via Kudu (mgmt-bearer AAD token; SCM Basic Auth stays disabled by design — request to enable was blocked by Claude Code auto-mode classifier and that was the right call) revealed both the **new** `PredictionLeague.Api.dll` and the **stale** `PredictionLeague.dll`/`PredictionLeague.exe` from the 2026-05-23 pre-F-01 deploy living side by side. App Service auto-detect picked the wrong entry → fallback page. Fixed by `az webapp config set --startup-file "dotnet PredictionLeague.Api.dll"` and then committed as a defensive `Pin startup command` step in `deploy-backend.yml` (idempotent on subsequent runs).

# Phase 5 final audit (2026-06-08)

## End-to-end smoke verification (live against prod)

| Check                                                                | Result                                                                                                                                                       |
| -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `GET /health/db`                                                     | `200` / body `Healthy` — proves API → Azure SQL Basic connectivity end-to-end                                                                                |
| `GET /api/auth/me` anonymous (adapted from removed `/api/leagues`)   | `401` — proves ASP.NET pipeline (CORS → Authentication → Authorization) is wired and the API is serving from `PredictionLeague.Api.dll`, not the default page |
| `GET /api/auth/login/google`                                         | `302` → `https://accounts.google.com/o/oauth2/v2/auth?client_id=820162924702-…&redirect_uri=https%3A%2F%2F<api-host>%2Fsignin-google&…` — Google scheme registered, redirect URI uses the prod API host |
| Function App functions list                                          | `func-prediction-league/FixtureIngestTimer` (dotnet-isolated) registered                                                                                     |
| SPA over CDN                                                         | `https://thankful-desert-02de6f703.7.azurestaticapps.net/` → `200`                                                                                          |

## Final resource audit trail

| Item                       | Value                                                                                                                                                                                                                  |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Subscription               | `dd204810-4214-41a4-880b-05fe99e649e4` (Visual Studio Enterprise – MPN, tenant `onedynamics.pl`)                                                                                                                       |
| Resource group             | `rg-prediction-league`                                                                                                                                                                                                 |
| Region                     | `polandcentral`                                                                                                                                                                                                        |
| API Web App                | `prediction-league-api-0523444a` — `https://prediction-league-api-0523444a.azurewebsites.net` (Linux, .NET 10, startup pinned to `dotnet PredictionLeague.Api.dll`, `httpsOnly=true`)                                  |
| App Service plan           | `asp-prediction-league` (F1 Linux, reused from 2026-05-23 deploy)                                                                                                                                                       |
| Function App               | `func-prediction-league` — `https://func-prediction-league.azurewebsites.net` (Flex Consumption / `dotnet-isolated 10`, `FixtureIngestSchedule=0 */30 * * * *`)                                                         |
| Application Insights       | `func-prediction-league` (auto-created by CLI on Function App provision; not yet instrumented)                                                                                                                          |
| Storage account            | `stpredictionleague` (Standard_LRS, StorageV2) — backs Flex Consumption deployment + AzureWebJobsStorage                                                                                                                |
| Azure SQL server           | `sql-prediction-league.database.windows.net` (admin `sqladmin`, password in `SQL_ADMIN_PASSWORD` GH secret + operator password manager)                                                                                  |
| Azure SQL database         | `appdb` (Basic, 5 DTU, `Encrypt=True`)                                                                                                                                                                                  |
| SQL firewall rules         | `AllowAzureServices` (0.0.0.0/0.0.0.0); transient `ci-<run-id>` rules created/destroyed by the migrate job                                                                                                              |
| Static Web App (SPA)       | `prediction-league-web` — `https://thankful-desert-02de6f703.7.azurestaticapps.net` (existing from 2026-05-23 deploy)                                                                                                  |
| Service principal (CI)     | `gh-actions-walking-skeleton` (rg-scoped Contributor), JSON creds in `AZURE_CREDENTIALS` GH secret                                                                                                                       |
| GH Actions secrets         | `AZURE_CREDENTIALS`, `AZURE_SQL_CONNECTION`, `SQL_ADMIN_PASSWORD`, `AZURE_API_PUBLISH_PROFILE` (kept though unused after switching to SP auth), `AZURE_STATIC_WEB_APPS_API_TOKEN_THANKFUL_DESERT_02DE6F703` (pre-existing) |
| Deploy workflow            | `.github/workflows/deploy-backend.yml` — triggers on `push` to `main` with `src/server/**` paths, and `workflow_dispatch`                                                                                                |

## Deviation note links

Foundation docs updated in Phase 5:
- `context/foundation/infrastructure-v2.md` — 2026-06-08 deviations + deferred hardening section
- `context/foundation/roadmap.md` — F-04 row flipped `proposed` → `done` with a pointer to the deviation note
- `context/foundation/lessons.md` — two recurring rules appended (App Service entry-assembly pinning; SCM Basic Auth disabled by default → use SP auth)

## Manual follow-ups still owned by the operator (NOT blockers)

- Save the SQL admin password to the password manager (already surfaced once at the Phase 2 gate).
- Add `https://prediction-league-api-0523444a.azurewebsites.net/signin-google` as an authorized redirect URI in the Google Cloud Console on the shared dev/prod OAuth client. Without this, real Google sign-in completes with a `redirect_uri_mismatch`. The `/api/auth/login/google` → `302` automated check verifies the *server-side* challenge issuance only.
archived_at: null
---

## Notes

from F-04 at @context/foundation/roadmap.md

### Phase 1 preflight (2026-06-07)

- Subscription: `dd204810-4214-41a4-880b-05fe99e649e4` (Visual Studio Enterprise – MPN), tenant `onedynamics.pl` — matches prior deploy. Confirmed via `az account show`.
- Region: `polandcentral` (reused from prior deploy).
- Resource group: `rg-prediction-league` (reused).
- Web runtime: `DOTNETCORE:10.0` listed by `az webapp list-runtimes --os-type linux`.
- Functions runtime: `dotnet-isolated` v10 confirmed available on **Flex Consumption** in `polandcentral` via `az functionapp list-flexconsumption-runtimes`. Listed in the global `list-runtimes` table for Linux; per-region SKU availability on classic Consumption (Y1) is not directly queryable.
  - **Decision**: attempt classic Consumption (Y1) create first per the plan's primary path; if creation in `polandcentral` rejects .NET 10 isolated, fall back to Flex Consumption (already verified). Either way the billing model stays consumption.
- Azure SQL Basic offered in `polandcentral` (`az sql db list-editions … --edition Basic` → `Available: True`).
- Providers `Microsoft.Sql` and `Microsoft.Storage` were `NotRegistered`; `az provider register` issued — registration in progress / completed before Phase 2.
