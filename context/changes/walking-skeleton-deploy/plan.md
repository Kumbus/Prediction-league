# Walking-skeleton Azure deploy (F-04) Implementation Plan

## Overview

Provision and wire the production backend pieces the first deploy (`context/changes/deployment/deployment-plan.md`, 2026-05-23) deliberately deferred — **Azure SQL Basic**, a **Storage account**, and a **Consumption Function App** — then stand up a **GitHub Actions pipeline that deploys the API + Functions to prod on merge to `main`** and applies the EF migration. A thin liveness path (`/health/db` reaching prod Azure SQL) proves the deployed shape before any user-facing slice (S-01) ships.

This closes the roadmap gap where S-01 assumed a "deployed shape" that nothing fully provisioned: the API + SPA shells exist, but no database, no background-job host, and no automated deploy.

## Current State Analysis

- **API** (`prediction-league-api-0523444a.azurewebsites.net`) and **SPA** (Static Web Apps, workflow committed) were deployed 2026-05-23 in `rg-prediction-league` / `asp-prediction-league` (F1 Linux, Poland Central) — but against **bare-scaffold code** (`static List<League>`, no EF, no auth). That deploy explicitly deferred Azure SQL + Functions.
- Since then **F-01** (layered solution + EF Core + Identity, two migrations: `20260530155119_InitialCreate`, `20260607113246_AddFootballIngestModel`), **F-02** (cookie Identity + Google login), and **F-03** (API-Football ingest + `PredictionLeague.Functions` timer host) all landed. The API now needs a real DB to boot meaningfully.
- The current API publish target is the layered **`PredictionLeague.Api`** project — the prior plan's `PredictionLeague.csproj` path is stale.
- `Program.cs:41` auto-migrates **only in Development**; prod is intentionally not migrated on startup. `Program.cs:74` already maps `/health/db` via `AddDbContextCheck<AppDbContext>`.
- EF reads `GetConnectionString("DefaultConnection")` (`Infrastructure/DependencyInjection.cs:25`) and throws if absent.
- `PredictionLeague.Functions/Program.cs` reuses `AddInfrastructure` + `AddFootballIngest`, so the Function App needs the **same** `ConnectionStrings:DefaultConnection`, plus `ApiFootball:ApiKey`, `FixtureIngestSchedule`, and a real `AzureWebJobsStorage` (Consumption requires a Storage account).
- CORS binds `SpaCorsOptions` from the `Cors` config section (`Program.cs:24-32`); prod must list the SWA origin.

### Key Discoveries:

- Connection string key is `DefaultConnection` — inject as App Service app setting `ConnectionStrings__DefaultConnection` (double underscore → `:`).
- `PredictionLeague.Api.csproj:13` already references `Microsoft.EntityFrameworkCore.Design`, so EF tooling can generate/apply migrations in CI with `dotnet-ef`.
- Functions project is `.NET 10` / `v4` / `dotnet-isolated` (`PredictionLeague.Functions.csproj`) — infra-v2 flags ".NET 10 isolated on Consumption" as a runtime availability *unknown* to verify in-region before committing.
- Existing resources (`rg-prediction-league`, `asp-prediction-league`, `prediction-league-api-0523444a`, the SWA) are **reused**, not recreated.

## Desired End State

Merging to `main` triggers a GitHub Actions run that builds + deploys the API and Functions to prod and applies the EF migration to Azure SQL. After it completes:

- `GET https://<api-host>/health/db` returns **Healthy** (200), proving the API reaches prod Azure SQL.
- API endpoints respond (e.g. `GET /api/leagues` → `200`); `[Authorize]` routes return 401 when anonymous.
- The Function App is provisioned, running .NET 10 isolated on Consumption, with the `FixtureIngestTimer` registered and reading its connection string + ApiFootball key from app settings.
- The SPA is still served over the SWA CDN.
- The auto-migrate deviation from the documented "never auto-migrate prod" guardrail is recorded in the foundation docs, with PITR named as the rollback path.

## What We're NOT Doing

- **No staging App Service** — deferred; deploy straight to the single prod app (revisit with CI promotion later).
- **No OIDC / federated CI auth** — using publish-profile secrets (revisit for hardening).
- **No Key Vault** — connection string lives as a plain App Service app setting (Key Vault reference is a documented follow-up).
- **No Managed Identity DB auth** — SQL auth connection string per infra-v2.
- **No Azure Front Door / multi-region** — single region (Poland Central) per infra-v2; global deferred.
- **No observability investment** (App Insights) — framework defaults only, per `main_goal: speed`.
- **No new app features** — this is pure deploy/infra wiring.

## Implementation Approach

Provision the missing Azure resources first (Phase 2) so the pipeline has targets, configure their settings/secrets (Phase 3), then automate build+deploy+migrate via GitHub Actions on merge to `main` (Phase 4), and verify the whole shape end-to-end (Phase 5). Preflight (Phase 1) gates the two runtime-availability unknowns (web .NET 10, Functions .NET 10 isolated on Consumption) before any resource is created.

## Critical Implementation Details

- **Auto-migrate runs only after a green build** and applies an **idempotent** EF script (`dotnet ef migrations script --idempotent`), never `database update` against a half-built artifact. The runner needs transient DB network access: open a SQL firewall rule for the runner's egress IP at the start of the migrate job and delete it at the end. Migrations are forward-only; the rollback path is **Azure SQL Basic Point-in-Time-Restore (~7-day window)** — a code revert cannot undo a schema change.
- **Functions on Consumption require a Storage account** and `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated`. If Phase 1 finds .NET 10 isolated is not offered on Consumption in Poland Central, fall back to a **Flex Consumption** plan in-region (still consumption-billed) before considering deferral.
- **App Service connection-string injection**: app setting named `ConnectionStrings__DefaultConnection` maps to `GetConnectionString("DefaultConnection")`. Do not rely on the SQLAzure-typed "Connection strings" blade prefix (`SQLAZURECONNSTR_`) — use the explicit app setting for predictability.

## Phase 1: Preflight & verification

### Overview

Confirm account, region, and runtime availability before creating anything. Read-only except the human `az login` and one-time provider registration.

### Changes Required:

#### 1. Account & subscription (HUMAN)

**Intent**: Establish an authenticated Azure session on the correct subscription. The agent cannot perform interactive login.

**Contract**: Human runs `! az login` (device/browser); then `az account set --subscription "<id>"` and `az account show` confirm the `dd204810-…` subscription from the prior deploy.

#### 2. Runtime & region availability checks

**Intent**: Gate the two infra-v2 unknowns before provisioning.

**Contract**:
- `az webapp list-runtimes --os-type linux` lists `DOTNETCORE:10.0`.
- `az functionapp list-runtimes` (or `az functionapp list-flexconsumption-runtimes -l polandcentral`) shows **.NET 10 isolated** available on Consumption/Flex Consumption in `polandcentral`. If absent → adopt the Flex Consumption fallback noted in Critical Details.
- Azure SQL Basic is offerable in `polandcentral`.

#### 3. Resource-provider registration (one-time)

**Intent**: Ensure the subscription can create the new resource types.

**Contract**: `az provider register --namespace Microsoft.Sql` and `--namespace Microsoft.Storage` (no-op if already registered; `Microsoft.Web` already registered).

### Success Criteria:

#### Automated Verification:

- `az account show` returns the expected subscription id.
- `az webapp list-runtimes --os-type linux` includes `DOTNETCORE:10.0`.
- Functions .NET 10 isolated availability confirmed via the runtimes query (or fallback decision recorded).
- `Microsoft.Sql` and `Microsoft.Storage` show `Registered`.

#### Manual Verification:

- Human has completed `az login` and confirmed the correct tenant/subscription.
- Region/runtime fallback decision (if any) is noted in `change.md` Notes.

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation before provisioning paid resources.

---

## Phase 2: Provision infrastructure

### Overview

Create the Azure SQL Basic database, a Storage account, and a Consumption Function App. Reuse the existing resource group and API app.

### Changes Required:

#### 1. Azure SQL Basic (HUMAN-gated creation)

**Intent**: Stand up the predictable-cost, no-cold-start production database EF will target.

**Contract**: `az sql server create` (admin login + strong password, captured as a secret — never committed) + `az sql db create --service-objective Basic` in `rg-prediction-league` / `polandcentral`. Capture the ADO.NET connection string (with `Encrypt=True`).

#### 2. SQL firewall rules

**Intent**: Allow the App Service and (transiently) the migration runner to reach the DB without standing public exposure.

**Contract**: Enable "Allow Azure services and resources" (`0.0.0.0` rule) so App Service connects. Per-run runner-IP rules are created/deleted inside the CI migrate job (Phase 4), not left standing.

#### 3. Storage account (for Functions)

**Intent**: Satisfy the Consumption Function App's required `AzureWebJobsStorage` backing.

**Contract**: `az storage account create` (Standard LRS) in `rg-prediction-league` / `polandcentral`. Capture its connection string.

#### 4. Consumption Function App

**Intent**: Host the `FixtureIngestTimer` decoupled from the web tier (keeps the API off Always On).

**Contract**: `az functionapp create --consumption-plan-location polandcentral --runtime dotnet-isolated --runtime-version 10 --functions-version 4 --os-type Linux --storage-account <acct>` (or the Flex Consumption variant from the fallback). Globally-unique app name.

### Success Criteria:

#### Automated Verification:

- `az sql db show ... --query "currentServiceObjectiveName"` → `Basic`.
- `az storage account show` returns the account.
- `az functionapp show ... --query "state"` → `Running`; runtime query confirms dotnet-isolated / .NET 10.
- `az sql server firewall-rule list` shows the Allow-Azure-services rule.

#### Manual Verification:

- Human confirms the SQL admin password and connection strings are stored as secrets (GitHub secrets / App Service settings), not in the repo.
- Resource names + region recorded in the plan/`change.md` as the deploy audit trail.

**Implementation Note**: Pause for human confirmation of provisioned resources and secret handling before wiring settings.

---

## Phase 3: Configure app settings & secrets

### Overview

Inject prod configuration into the API app and the Function App so both boot against the real DB and external services.

### Changes Required:

#### 1. API app settings

**Intent**: Give the deployed API its DB connection, allowed SPA origin, and external API key.

**Contract** (`az webapp config appsettings set` on `prediction-league-api-0523444a`):
- `ConnectionStrings__DefaultConnection` = Azure SQL connection string
- `Cors__AllowedOrigins__0` = the SWA default hostname (`https://<spa>.azurestaticapps.net`)
- `ApiFootball__ApiKey` = the API-Football key
- `Authentication__Google__ClientId` / `Authentication__Google__ClientSecret` = the prod Google OAuth client (the scheme registers only when both are present — `DependencyInjection.cs:68-79`; absent → Google login silently off, though local email/password still works)
- `az webapp update --set httpsOnly=true`

**Also (HUMAN, Google Cloud console):** add `https://<api-host>/signin-google` as an authorized redirect URI on the prod OAuth client, mirroring the dev `https://localhost:7182/signin-google`. Without it Google login returns `redirect_uri_mismatch`.

#### 2. Function App settings

**Intent**: Give the ingest host the same DB plus its ingest-specific config.

**Contract** (`az functionapp config appsettings set`):
- `ConnectionStrings__DefaultConnection` = same Azure SQL connection string
- `ApiFootball__ApiKey` = the API-Football key
- `ApiFootball__BaseUrl` = `https://v3.football.api-sports.io`
- `FixtureIngestSchedule` = the production CRON (e.g. match-window-frugal `0 */30 * * * *`)
- `FUNCTIONS_WORKER_RUNTIME` = `dotnet-isolated` (set by create; assert present)

### Success Criteria:

#### Automated Verification:

- `az webapp config appsettings list` shows `ConnectionStrings__DefaultConnection`, `Cors__AllowedOrigins__0`, `ApiFootball__ApiKey`, `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` present (values masked).
- `az webapp show --query "httpsOnly"` → `true`.
- `az functionapp config appsettings list` shows the connection string, ApiFootball key, and `FixtureIngestSchedule`.

#### Manual Verification:

- No secret values appear in the repo or workflow files (only secret *references*).
- Human confirms the SWA origin value matches the live SPA hostname.

**Implementation Note**: Pause for human confirmation that secrets are set on Azure (not committed) before building the pipeline.

---

## Phase 4: CI/CD pipeline + auto-migrate

### Overview

A GitHub Actions workflow that, on push/merge to `main`, builds and publishes the API + Functions, deploys both via publish-profile secrets, and applies the idempotent EF migration to prod **after a green build**.

> **DEVIATION (conscious):** Auto-applying migrations to prod contradicts the documented "never auto-migrate prod / forward-only + human-gated" guardrail (infra-v2 Risk Register, roadmap F-04, `lessons.md`). Chosen by the user for this MVP. Mitigations baked in below; deviation recorded in Phase 5.

### Changes Required:

#### 1. Publish-profile secrets

**Intent**: Let `azure/webapps-deploy` authenticate to the API app and Function App without a stored service principal.

**Contract**: Download each app's publish profile (`az webapp deployment list-publishing-profiles` / Function App equivalent) and store as GitHub repo secrets (e.g. `AZURE_API_PUBLISH_PROFILE`, `AZURE_FUNC_PUBLISH_PROFILE`). Also store the SQL connection string as `AZURE_SQL_CONNECTION` and SQL server name/rg for firewall steps. **The migrate job's firewall bracketing needs an authenticated `az` session** — publish profiles auth only `azure/webapps-deploy`, not `az`, and the "Allow Azure services" rule does not cover GitHub-hosted runners (external IPs). Create a **resource-group-scoped service principal** (`az ad sp create-for-rbac --role Contributor --scopes /subscriptions/<sub>/resourceGroups/rg-prediction-league`) and store its JSON as `AZURE_CREDENTIALS` for `azure/login`. (This reintroduces a single scoped AAD app the original sketch hoped to avoid; OIDC remains the deferred hardening.)

#### 2. Deploy workflow

**File**: `.github/workflows/deploy-backend.yml`

**Intent**: Build, publish, deploy, and migrate on merge to `main`.

**Contract**: Triggered on `push` to `main`. Jobs:
- **build**: `dotnet publish src/server/PredictionLeague.Api -c Release -o api-publish`; `dotnet publish src/server/PredictionLeague.Functions -c Release -o func-publish`; generate `dotnet ef migrations script --idempotent --project src/server/PredictionLeague.Infrastructure --startup-project src/server/PredictionLeague.Api -o migrate.sql` (after `dotnet tool install --global dotnet-ef`). **Set a dummy `ConnectionStrings__DefaultConnection=Server=_;Database=_;` env on this step** — there is no `IDesignTimeDbContextFactory`, so `dotnet ef` builds the host via `Program.cs`, and `AddInfrastructure` throws on an absent connection string (`DependencyInjection.cs:25-28`) *before* EF reads the model. Script generation only parses the string; it never connects. Upload artifacts.
- **deploy-api** (needs: [build, migrate]): `azure/webapps-deploy@v3` with `AZURE_API_PUBLISH_PROFILE` → `prediction-league-api-0523444a`. Gate on `migrate` so the schema leads the code (expand/contract ordering); harmless here on the empty first DB but keeps the convention correct for riskier future migrations.
- **deploy-func**: `Azure/functions-action@v1` with `AZURE_FUNC_PUBLISH_PROFILE` → the Function App.
- **migrate** (needs: build): `azure/login@v2` with `AZURE_CREDENTIALS` (the scoped SP — `az sql server firewall-rule` cannot run on a publish profile), fetch runner egress IP, `az sql server firewall-rule create` for it, apply `migrate.sql` via `sqlcmd` (SQL auth), then delete the rule in an `always()` step. Guarded so it runs only if build succeeded.

> Snippet — the firewall-bracketed migrate step is the non-obvious, load-bearing part:
> ```yaml
> - uses: azure/login@v2
>   with:
>     creds: ${{ secrets.AZURE_CREDENTIALS }}   # rg-scoped SP; az sql needs a real principal
> - name: Apply migration (forward-only, idempotent)
>   run: |
>     ip=$(curl -s https://api.ipify.org)
>     az sql server firewall-rule create -g $RG -s $SQL_SERVER -n ci-$GITHUB_RUN_ID \
>       --start-ip-address $ip --end-ip-address $ip
>     sqlcmd -S $SQL_HOST -d $SQL_DB -U $SQL_USER -P "$SQL_PASS" -i migrate.sql -b
> - name: Remove CI firewall rule
>   if: always()
>   run: az sql server firewall-rule delete -g $RG -s $SQL_SERVER -n ci-$GITHUB_RUN_ID
> ```

### Success Criteria:

#### Automated Verification:

- Pushing to `main` triggers the workflow; `gh run view` shows all jobs **green**.
- `sqlcmd`/migrate step reports the idempotent script applied without error.
- The transient `ci-<run-id>` firewall rule is absent after the run (`az sql server firewall-rule list`).

#### Manual Verification:

- Human reviews the workflow file once before first run (no secrets inlined; migrate step gated on build).
- Human confirms the first auto-migrate ran against the intended prod DB and the schema matches the two expected migrations.

**Implementation Note**: After the workflow lands and the first run is green, pause for human confirmation that the prod schema is correct before relying on auto-migrate for subsequent merges.

---

## Phase 5: End-to-end verification + deviation record

### Overview

Prove the deployed shape works end-to-end and record the guardrail deviation + deferred hardening so docs and reality stay aligned.

### Changes Required:

#### 1. End-to-end smoke verification

**Intent**: Confirm the walking skeleton is alive against prod Azure SQL.

**Contract**:
- `GET https://<api-host>/health/db` → `200` / Healthy.
- `GET https://<api-host>/api/leagues` → `200`.
- An anonymous call to an `[Authorize]` route → `401`.
- `GET https://<api-host>/api/auth/login/google` (or the configured Google challenge route) → `302` to `accounts.google.com` (proves the Google scheme registered in prod), **not** a 404/500.
- Function App logs (`az webapp log tail` / portal) show `FixtureIngestTimer` registered.
- SWA default hostname still serves the SPA.

#### 2. Record deviation + follow-ups

**Files**: `context/foundation/infrastructure-v2.md`, `context/foundation/roadmap.md` (F-04 line), `context/foundation/lessons.md`

**Intent**: Document that prod auto-migrate was consciously adopted for the MVP, against the prior guardrail, with PITR as rollback — and list deferred hardening.

**Contract**: Append a short, dated note to each: the deviation, its rationale (MVP/friend-scale, speed), the mitigation (green-build gate, idempotent script, PITR), and a revisit trigger (before real users / S-01 GA). List deferred items: staging app, OIDC CI auth, Key Vault connection string, Managed Identity DB auth.

#### 3. Update deploy audit trail

**File**: `context/changes/walking-skeleton-deploy/change.md`

**Intent**: Capture final resource names, URLs, and region.

**Contract**: Record SQL server/db names, Function App name, Storage account, API/SPA URLs, region, subscription id.

### Success Criteria:

#### Automated Verification:

- `curl https://<api-host>/health/db` → `200`.
- `curl https://<api-host>/api/leagues` → `200`.
- Anonymous `[Authorize]` route → `401`.
- Google challenge route → `302` to `accounts.google.com` (Google scheme live in prod).

#### Manual Verification:

- Prod Google OAuth redirect URI (`https://<api-host>/signin-google`) registered; a real Google login completes end-to-end.
- Function App timer registered and reading prod settings (logs reviewed).
- SPA loads over the CDN.
- Deviation note added to infra-v2 / roadmap / lessons; deferred-hardening list captured.
- Final resource names + URLs recorded in `change.md`.

**Implementation Note**: This is the final phase — confirm the full shape with the human before marking the change complete.

---

## Testing Strategy

No automated test suite exists in either unit (per AGENTS.md) — verification is deploy-time smoke checks, not unit/integration tests.

### Manual Testing Steps:

1. Merge a trivial change to `main`; watch the workflow go green via `gh run view`.
2. `curl https://<api-host>/health/db` → Healthy.
3. `curl https://<api-host>/api/leagues` → `200`.
4. Hit an `[Authorize]` route anonymously → expect `401`.
5. Tail Function App logs; confirm `FixtureIngestTimer` registered (and fires on its CRON).
6. Open the SWA hostname; confirm the SPA loads.

## Performance Considerations

- **F1 Free 60 CPU-min/day/region cap** — fine at friend-scale; watch on match days (upgrade API to B1 before a major tournament, per infra-v2).
- **Azure SQL Basic** chosen specifically to avoid serverless cold-start on the freshness-sensitive recompute path.
- **Functions Consumption** keeps the API off Always On; the timer is match-window-frugal (free-tier API-Football 100 req/day cap).

## Migration Notes

- Two migrations apply on first prod deploy: `20260530155119_InitialCreate`, `20260607113246_AddFootballIngestModel`. The idempotent script applies both.
- Migrations are **forward-only**. A bad migration is reversed by a new reversing migration *or* by **Azure SQL Basic Point-in-Time-Restore (~7-day window)** — never by reverting code.

## References

- Prior (partial) deploy: `context/changes/deployment/deployment-plan.md`
- Infra decision + risk register: `context/foundation/infrastructure-v2.md`
- Tech stack: `context/foundation/tech-stack.md`
- Roadmap F-04: `context/foundation/roadmap.md`
- Guardrail lesson context: `context/foundation/lessons.md`
- Connection string read: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs:25`
- Health check + CORS + dev-only migrate: `src/server/PredictionLeague.Api/Program.cs:24,41,74`
- Ingest timer host: `src/server/PredictionLeague.Functions/Program.cs`, `FixtureIngestTimer.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Preflight & verification

#### Automated

- [x] 1.1 `az account show` returns expected subscription id — e17fe8e
- [x] 1.2 `az webapp list-runtimes --os-type linux` includes `DOTNETCORE:10.0` — e17fe8e
- [x] 1.3 Functions .NET 10 isolated availability confirmed (or fallback recorded) — e17fe8e
- [x] 1.4 `Microsoft.Sql` and `Microsoft.Storage` show `Registered` — e17fe8e

#### Manual

- [x] 1.5 Human completed `az login`, confirmed tenant/subscription — e17fe8e
- [x] 1.6 Region/runtime fallback decision (if any) noted in `change.md` — e17fe8e

### Phase 2: Provision infrastructure

#### Automated

- [x] 2.1 `az sql db show` service objective → `Basic` — fbcc22e
- [x] 2.2 `az storage account show` returns the account — fbcc22e
- [x] 2.3 `az functionapp show` state → `Running`; runtime is dotnet-isolated / .NET 10 — fbcc22e
- [x] 2.4 `az sql server firewall-rule list` shows Allow-Azure-services rule — fbcc22e

#### Manual

- [x] 2.5 SQL admin password + connection strings stored as secrets, not in repo — fbcc22e
- [x] 2.6 Resource names + region recorded as deploy audit trail — fbcc22e

### Phase 3: Configure app settings & secrets

#### Automated

- [x] 3.1 API app settings show connection string, CORS origin, ApiFootball key — 71a7c7b
- [x] 3.2 `az webapp show` httpsOnly → `true` — 71a7c7b
- [x] 3.3 Function App settings show connection string, ApiFootball key, `FixtureIngestSchedule` — 71a7c7b

#### Manual

- [x] 3.4 No secret values in repo/workflow (references only) — 71a7c7b
- [x] 3.5 SWA origin value matches live SPA hostname — 71a7c7b

### Phase 4: CI/CD pipeline + auto-migrate

#### Automated

- [ ] 4.1 Push to `main` triggers workflow; all jobs green (`gh run view`)
- [ ] 4.2 Idempotent migration script applied without error
- [ ] 4.3 Transient `ci-<run-id>` firewall rule absent after run

#### Manual

- [x] 4.4 Human reviewed workflow file (no inlined secrets; migrate gated on build)
- [ ] 4.5 Human confirmed first auto-migrate hit intended prod DB; schema matches expected migrations

### Phase 5: End-to-end verification + deviation record

#### Automated

- [ ] 5.1 `curl /health/db` → `200` / Healthy
- [ ] 5.2 `curl /api/leagues` → `200`
- [ ] 5.3 Anonymous `[Authorize]` route → `401`
- [ ] 5.4 Google challenge route → `302` to `accounts.google.com`

#### Manual

- [ ] 5.5 Prod Google redirect URI registered; real Google login completes end-to-end
- [ ] 5.6 Function App timer registered, reading prod settings (logs reviewed)
- [ ] 5.7 SPA loads over CDN
- [ ] 5.8 Deviation note added to infra-v2 / roadmap / lessons; deferred-hardening listed
- [ ] 5.9 Final resource names + URLs recorded in `change.md`
