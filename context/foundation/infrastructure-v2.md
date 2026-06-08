---
project: Football Match Prediction App
researched_at: 2026-05-23
recommended_platform: Azure App Service
runner_up: Fly.io
context_type: mvp
tech_stack:
  language: C#
  framework: ASP.NET Core Web API (.NET 10) + React 19 / Vite SPA
  runtime: .NET 10 (LTS)
---

## Recommendation

**Deploy on Azure App Service.** It scored 5/5 against the agent-friendly criteria, the developer already knows Azure, and the stack was shaped for `azure-app-service` from the start. .NET 10 is GA on App Service Linux (no Dockerfile required, checked 2026-05-23). The developer's explicit MVP shape resolves the two weak axes — cost and the persistent-worker requirement — by **decoupling**: the API runs on **F1 Free** single-region, the persistent ingest + post-match recompute run on **Azure Functions (Consumption)** timer triggers (so no "Always On" / Basic tier is needed on the web tier), the Vite SPA goes on **Static Web Apps Free** (global CDN — this delivers most of the perceived "global" feel for read-heavy standings), and data lives on **Azure SQL Basic** (~$5/mo flat, no cold-start). Global multi-region for the API is consciously deferred — single region first, revisit if overseas latency proves unacceptable. This lands near $0–5/mo to start.

## Platform Comparison

Hard filter applied before scoring: the interview confirmed the app needs **persistent always-on background work** (data ingest + post-match recompute), which drops JS-only serverless/edge platforms (Cloudflare Workers, Vercel, Netlify) from the backend candidate pool. The four survivors all run persistent .NET processes (the recompute can be a co-located worker or, on Azure, a decoupled Function — both satisfy the constraint).

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Global reach |
|---|---|---|---|---|---|---|
| **Azure App Service** | Pass | Pass | Pass | Pass | Pass | Partial (single-region/plan) |
| **Fly.io** | Pass | Partial | Pass | Pass | Pass (experimental) | **Pass (native multi-region)** |
| **Railway** | Pass | Pass | Pass | Pass | Partial (WIP) | Partial (4 regions; CDN disabled) |
| **Render** | Pass | Pass | Pass | Pass | Pass (read-mostly) | Fail (5 single regions, no edge) |

- **Azure App Service** — `az webapp` covers deploy / restart / log tail / slot-swap rollback, fully scriptable and non-interactive. PaaS-managed. Docs on learn.microsoft.com are markdown-backed (GitHub-sourced). **Azure MCP Server 2.0 is GA (2026-04-10, checked 2026-05-23)**. Native .NET 10 runtime on Linux is GA (`DOTNETCORE:10.0`). Weak axes: (a) global reach — App Service is single-region per plan; true geo-distribution needs multiple plans + Azure Front Door; (b) cost of in-process always-on work (Basic+, ~$13/mo) — both sidestepped by the decoupled Free-tier + Functions shape.
- **Fly.io** — Strongest global story by far: anycast routing, `fly regions add`, 12+ regions, and the best persistent-worker model (background workers run as a process group with no services, so autostop never touches them). flyctl is fully scriptable. Knocks: container-only (Dockerfile + autostop tuning you own — Managed/Serverless scored Partial), **flyctl MCP is experimental** ("sharp edges", checked 2026-05-23), **Managed Postgres starts at $38/mo** (Shared-2x), and global writes still hit a single primary Postgres region. No prior developer experience.
- **Railway** — Cheap always-on managed option (~$6–20/mo incl. DB), one-click Postgres, no spin-down, clean agent docs (`/agents`, `/ai/mcp-server`). Knocks: no native .NET (Dockerfile required), **MCP labelled work-in-progress** (checked 2026-05-23), only 4 regions, **CDN reportedly disabled May 2026 with no return date** (third-party review, verify), and no prior developer experience.
- **Render** — Clean GA CLI, official MCP server (read-mostly), solid docs. Knocks: persistent workers need a **separate paid Background Worker (+$7/mo each)**, free web service spins down after 15 min, **free Postgres expires 30 days after creation** (checked 2026-05-23), and only 5 isolated single regions with no edge. Most cost + lifecycle friction of the four; dropped to 4th.

### Shortlisted Platforms

#### 1. Azure App Service (Recommended)

5/5 on the criteria, native .NET 10 (no Dockerfile), developer familiarity (a strong tie-breaker for a solo after-hours build), GA MCP, and a documented Free-tier path. The user's chosen shape — F1 Free API single-region, Static Web Apps Free SPA, Functions Consumption for ingest + recompute, Azure SQL Basic — meets the persistent-worker need without paying for Always On, and accepts single-region for now. The global requirement is partially satisfied today by the SPA CDN and consciously deferred for the API.

#### 2. Fly.io (Runner-up)

The direct architectural answer to the global-reach requirement: native multi-region anycast at ~$2/region and the cleanest always-on worker model. Promoted over Railway this run specifically because the developer flagged global users. The gap vs. Azure: zero prior experience, Dockerfile required, Managed Postgres $38/mo floor (or break co-location with external Neon/Supabase), MCP still experimental, and global writes are single-primary-region. The fallback if Azure's single-region latency proves unacceptable for overseas members.

#### 3. Railway

Would win on pure cost + DX — fully managed, no spin-down, ~$6–20/mo all-in, one-click Postgres. The gap: only 4 regions (weak on the global constraint), CDN reportedly disabled, Dockerfile required for .NET, MCP in preview, and no prior experience.

## Anti-Bias Cross-Check: Azure App Service

### Devil's Advocate — Weaknesses

1. **Global reach is Azure's structural weak spot.** App Service is single-region per plan; real geo-distribution needs multiple plans + Azure Front Door (~$35/mo base + traffic), directly contradicting the global requirement the developer raised. The MVP consciously defers this — but the deferral is the risk.
2. **No free always-on path for in-process work.** Persistent in-process `IHostedService` workers need "Always On" = Basic+ (~$13/mo Linux). Resolved only by decoupling jobs to Functions (the chosen shape); if a worker ever creeps back into the web tier, the cost reappears.
3. **Azure SQL predictable-cost trap.** Serverless auto-pause means cold-start latency on the post-match recompute (the freshness guardrail); non-pausing General Purpose is >$200/mo. Only **Basic DTU (~$4.90/mo flat)** is both cheap and cold-start-free.
4. **F1 Free has a 60 CPU-min/day/region cap** — exceeding it returns HTTP 403 ("quota exceeded"). Fine at friend-group scale, but a hard ceiling to watch on match days.
5. **Blue-green slots require Standard tier (~$70/mo).** F1/B1 have none; safe rollback means a separate `-staging` Free app + GitHub Actions promotion, not slots.

### Pre-Mortem — How This Could Fail

The dev picked Azure for familiarity, but the friend group spread across continents and standings felt sluggish for overseas members — the single region meant 300ms+ for distant users, and the "global" promise went undelivered because Azure Front Door looked too expensive to justify mid-MVP. The API stayed on F1 Free to hold costs, then hit the 60 CPU-min/day cap on a World Cup match day and started returning 403s during peak submissions — the one window users actually cared about. The Functions recompute fired fine, but the API quota ceiling, not the worker, became the failure. Azure SQL serverless auto-paused overnight so morning-after recomputes ate cold starts. Each fix (Front Door, B1 upgrade, Basic DTU, staging app) added cost and after-hours config time until the familiarity advantage was consumed by Azure knob-tuning — while Fly.io, which would have given multi-region for ~$2/region, sat unused.

### Unknown Unknowns

- **"Global" on App Service = App Service + Front Door + multi-plan** — there is no single toggle; budget and complexity jump the moment the global requirement is acted on. Deferring it is the right MVP call, but it is a deferral, not a solve.
- **The SPA on Static Web Apps Free delivers most of the "global" feel** for read-heavy standings at $0 (global CDN, per-PR previews) — decoupling the global question from the API region choice.
- **Azure SQL serverless still bills when "paused"** (storage + compute floor); the cheap estimate assumes genuinely low active hours. Basic DTU ~$4.90 flat is the no-surprise alternative with no cold start.
- **.NET 10 isolated-worker Functions on Consumption** — verify the .NET 10 isolated runtime is available on Consumption in your region before committing the recompute there; the in-process Functions model is retired for new runtimes, so isolated worker (Core Tools v4) is the only path.
- **.NET 10 Linux runtime** — confirm `az webapp list-runtimes --os-type linux` lists `DOTNETCORE:10.0` in your chosen region before first deploy; Windows updates on a different cadence and costs ~4× for identical specs (always pick Linux).

## Operational Story

- **Preview deploys**: App Service deployment slots require Standard tier — none on Free/Basic. MVP approach: deploy to a separate `-staging` App Service (also Free) via GitHub Actions on PR, promote by deploying to prod on merge. The SPA gets automatic per-PR preview URLs from Static Web Apps Free (GitHub Actions–driven).
- **Secrets**: App settings / connection strings live in **App Service Configuration** (or Key Vault references) and **GitHub Actions Secrets** for CI — never committed. Function secrets live in the Function App's Configuration. The DB connection string is injected as an app setting, read by EF Core at startup. Rotate via `az webapp config appsettings set` + restart.
- **Rollback**: On Free/Basic (no slots), redeploy the previous artifact: `az webapp deploy` with the prior build, or re-run the previous successful GitHub Actions run. Time-to-revert ≈ one deploy (~1–2 min). Caveat: EF Core migrations do **not** auto-roll-back — a forward-only migration must be reversed by a new migration, never by reverting code alone.
- **Approval**: Agent may deploy to staging and tail logs unattended. **Human-only**: publishing to production, rotating the DB connection string / primary secrets, dropping or migrating the database, deleting the App Service or Function App. Manual click = 30s; cleanup after an automated mistake = hours.
- **Logs**: `az webapp log tail --name <app> --resource-group <rg>` for live API logs; Functions logs via `az webapp log tail` on the Function App or Application Insights live metrics. GitHub Actions run logs via `gh run view`. Azure MCP Server (GA 2.0) exposes structured resource/log queries when the agent needs many of them.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Single-region API → high latency for overseas members (global requirement deferred) | Devil's advocate / Pre-mortem | M | M | Put SPA on Static Web Apps Free (global CDN) now; pick a central region; revisit Front Door or a Fly.io migration only if overseas latency proves unacceptable |
| F1 Free 60 CPU-min/day cap → HTTP 403 during match-day spike | Pre-mortem / Unknown unknowns | M | H | Monitor CPU quota; upgrade API to B1 (~$13, still modest) before a major tournament |
| Persistent worker creeps back in-process → forces Always On (Basic+) | Devil's advocate | M | M | Keep ingest + recompute on Azure Functions Consumption (timer trigger); never co-locate them in the web app |
| Azure SQL serverless cold-start on post-match recompute (freshness guardrail) | Devil's advocate / Unknown unknowns | M | M | Use Azure SQL **Basic DTU** (~$5 flat, no auto-pause) for predictable latency |
| .NET 10 isolated-worker not available on Consumption in target region | Unknown unknowns | L | M | Verify .NET 10 isolated runtime + Core Tools v4 on Consumption in-region before deploying the recompute Function |
| .NET 10 Linux runtime not yet in target region | Unknown unknowns | L | M | Verify `az webapp list-runtimes --os-type linux` before first deploy |
| Accidentally provisioning Windows hosting (~4× cost) | Devil's advocate | L | M | Always specify `--os-type linux`; assert in deploy plan |
| Deployment slots need Standard tier — no cheap blue-green | Devil's advocate | M | L | Use a separate `-staging` Free App Service + GitHub Actions promotion instead of slots |
| EF Core migration cannot auto-roll-back with code revert | Research finding | M | H | Treat migrations as forward-only; write a reversing migration; gate prod migrations behind human approval |
| Cost creep past budget once Front Door / B1 / staging added | Pre-mortem | M | M | Keep the decoupled Free-tier shape; review monthly billing; each upgrade is a deliberate decision, not a default |

## Getting Started

Versions assume the .NET 10 SDK installed locally and the Azure CLI. Validate the runtime is live in your region before deploying.

1. **Install/confirm tooling**: `az --version` (Azure CLI) and `func --version` (Azure Functions Core Tools v4, .NET 10 isolated worker). Login: `az login`.
2. **Verify .NET 10 availability** in your target region: `az webapp list-runtimes --os-type linux | findstr -i dotnet`.
3. **Create the API (Linux, Free tier)**: `az appservice plan create -g <rg> -n <plan> --sku F1 --is-linux` then `az webapp create -g <rg> -p <plan> -n <api-app> --runtime "DOTNETCORE:10.0"`.
4. **Deploy the API**: from `server/`, `dotnet publish -c Release -o ./publish`, zip it, then `az webapp deploy -g <rg> -n <api-app> --src-path publish.zip --type zip`.
5. **Background jobs as a Function** (decoupled, keeps API off Always On): scaffold a .NET 10 **isolated** Timer-triggered Function for data ingest + post-match recompute; deploy on the **Consumption** plan (`az functionapp create ... --consumption-plan-location <region>`).
6. **SPA on Static Web Apps Free**: `az staticwebapp create -g <rg> -n <spa> --source <repo> --branch main --app-location client --output-location dist` — wires GitHub Actions CI + per-PR previews automatically.
7. **Database**: create Azure SQL **Basic** (predictable ~$5/mo, no cold start) — `az sql server create` + `az sql db create ... --service-objective Basic`. Add the connection string as an App Service app setting; point EF Core (SQL Server provider) at it and run migrations.

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration / Dockerfiles
- CI/CD pipeline setup (GitHub Actions workflow authoring)
- Production-scale architecture (multi-region, HA, DR)

## 2026-06-08 — Walking-skeleton deploy (F-04) deviations + deferred hardening

F-04 closed; the walking skeleton (API + Functions + Azure SQL Basic) is live on `polandcentral` with auto-deploy from `main`. The deploy contradicted **two** items on this doc's prior posture and pulled in a few unexpected fallbacks worth recording so future work can plan against reality rather than the original guess.

### Conscious deviations

1. **Auto-migrate prod from CI** — contradicts the "never auto-migrate prod / forward-only + human-gated" guardrail (Risk Register above). Adopted for the MVP/friend-scale by explicit operator choice. Mitigations: migrate runs only after a green build, applies an `--idempotent` script generated by `dotnet ef migrations script`, is bracketed by a transient per-runner-IP SQL firewall rule (deleted in `always()`), and is forward-only. Rollback path: **Azure SQL Basic Point-in-Time-Restore (~7-day window)**. Revisit trigger: before real users / S-01 GA.

2. **Flex Consumption Functions instead of classic Y1 Consumption** — `az functionapp create --consumption-plan-location polandcentral …` rejected with `Linux dynamic workers are not available in resource group`. Flex Consumption was the fallback noted at planning time in `walking-skeleton-deploy/plan.md` Phase 1 and was exercised. Two practical consequences:
   - Function App publish profiles are **not exposed** on Flex Consumption (`az functionapp deployment list-publishing-profiles` returns *"not currently supported for Flex Consumption"*). Func deploy uses `azure/login` + `Azure/functions-action@v1` (SP bearer) instead of `--publish-profile`.
   - `FUNCTIONS_WORKER_RUNTIME` is **not a plain app setting** on Flex; the runtime lives at `functionAppConfig.runtime.name=dotnet-isolated`. Plan assertions for `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated` translate to checking the platform-level field.

### Unplanned traps (worth knowing)

- **SCM Basic Auth is OFF by default** on newly-created App Service apps in this tenant (`basicPublishingCredentialsPolicies/scm.properties.allow=false`). The plan's original `azure/webapps-deploy@v3` + publish-profile path failed with `Publish profile is invalid for app-name and slot-name provided`. Switched the API deploy to the same SP bearer used by the migrate job. Don't re-enable SCM Basic Auth as a workaround — SP auth is the more robust path.
- **`sqlcmd` defaults to `QUOTED_IDENTIFIER OFF`** which Azure SQL refuses for Identity's filtered/computed-column indexes (`Msg 1934`). The migrate step needs the `-I` flag.
- **`actions/upload-artifact@v4` excludes hidden files by default**. The Flex Consumption package validator requires the `.azurefunctions/` directory at the zip root; without `include-hidden-files: true` the deploy fails with `InvalidPackageContentException`.
- **App Service auto-detect picks the wrong entry assembly when wwwroot has multiple `*.dll` candidates** (e.g., the F-01 rename from `PredictionLeague.dll` → `PredictionLeague.Api.dll` left both side by side after redeploy → fallback to `/defaulthome/hostingstart/`, 404s on every route). Pin the entry assembly explicitly via `az webapp config set --startup-file "dotnet PredictionLeague.Api.dll"`. The workflow now bakes this as a defensive idempotent step.

### Deferred hardening (revisit before real-user GA)

- **Staging slot / CI promotion** — single prod deploy today; no staging.
- **OIDC / federated CI auth** — currently `AZURE_CREDENTIALS` carries an SP client secret. Migrate to `azure/login@v2` with `id-token: write` workload identity federation.
- **Key Vault for connection strings** — `ConnectionStrings__DefaultConnection` is a plain App Service app setting. Move behind a Key Vault reference.
- **Managed Identity DB auth** — SQL auth + password today; switch to MI + AAD-only auth on the SQL server.
- **Separate prod Google OAuth client** — prod currently reuses the dev OAuth client; carve a dedicated prod client before real users.
- **App Insights as an actual observability investment** — autocreated on Function App provision, untouched; instrument the API + alerts before GA.
