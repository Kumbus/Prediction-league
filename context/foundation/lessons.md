# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Custom string properties need explicit HasMaxLength

- **Context**: src/server/PredictionLeague.Infrastructure/Identity/ApplicationUser.cs:10 → migration nvarchar(max)
- **Problem**: DisplayName had no HasMaxLength, so EF materialized it as nvarchar(max). Plan called for max-lengths on lookup strings; every other string column complied — this one slipped because it lives on the Identity-derived ApplicationUser, outside the per-entity Fluent configs where the team set lengths.
- **Rule**: Every string property that EF maps — including custom props added to Identity base types — must get an explicit IsRequired()/HasMaxLength() in a Fluent IEntityTypeConfiguration. Never let a queryable/user-facing string default to nvarchar(max).
- **Applies to**: All EF Core entity + Identity configurations (src/server/PredictionLeague.Infrastructure/Persistence/Configurations/).

## App Service auto-detect breaks when wwwroot has more than one entry-point dll

- **Context**: F-04 walking-skeleton deploy. After the F-01 layered refactor renamed the API assembly (`PredictionLeague.dll` → `PredictionLeague.Api.dll`), the next deploy left both `*.dll`s in `/home/site/wwwroot/`. App Service Linux auto-detect picked the wrong one (or neither) and fell back to `/defaulthome/hostingstart/`, returning the default welcome page on every route. `/health/db` and every controller returned 404 even though the container started, the DB schema was correctly applied, and the deploy job reported success.
- **Problem**: `azure/webapps-deploy@v3` (and OneDeploy generally) writes the package contents on top of existing wwwroot — it does **not** clean stale binaries first. Any renamed assembly leaves a ghost behind. App Service's "find the entry .dll" heuristic is brittle with multiple candidates.
- **Rule**: When deploying a .NET app to Azure App Service, **always pin the entry assembly explicitly** via `az webapp config set --startup-file "dotnet <YourAssembly>.dll"`. Bake it as a defensive step in the deploy workflow — it is idempotent and survives wwwroot drift. Do not rely on auto-detect, especially across renames.
- **Applies to**: any Azure App Service deploy of .NET on Linux. Mirror in the workflow as a post-deploy step so a fresh app (recreated from scratch) is also pinned automatically.

## SCM Basic Auth is disabled by default on new App Service apps in this tenant

- **Context**: F-04 walking-skeleton deploy. The plan called for `azure/webapps-deploy@v3` + `publish-profile` secret. First run failed with `Publish profile is invalid for app-name and slot-name provided`. Inspection: `basicPublishingCredentialsPolicies/scm.properties.allow=false`. Re-enabling SCM Basic Auth to make publish profiles work weakens auth posture (Claude Code's auto-mode classifier rightly blocked the attempt).
- **Rule**: Don't plan for publish-profile auth on App Service in this tenant. Use a resource-group-scoped service principal via `azure/login@v2` + `azure/webapps-deploy@v3` (no `publish-profile` input) — the action falls back to the bearer token from the previous `azure/login`. Same auth path works for `Azure/functions-action@v1` on Flex Consumption, which doesn't expose publish profiles at all.
- **Applies to**: any GitHub Actions workflow deploying to App Service / Function Apps under this subscription.
