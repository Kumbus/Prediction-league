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

## Persistence exception types must not reach the Api layer

- **Context**: src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:4,159-174 — S-03 invite-code collision retry.
- **Problem**: The controller imports `Microsoft.EntityFrameworkCore` and catches `DbUpdateException` to retry an invite-code collision. No other controller references EF Core, and `IRepository.cs` states the intent outright: "Application depends on this, not on EF Core." The plan prescribed the catch, so it passed review as plan-sanctioned — but it leaks the persistence technology through the Api→Application boundary. `Program.cs` also imports EF Core; that is composition-root/migration bootstrapping, a different and accepted use.
- **Rule**: Controllers must not catch provider- or ORM-specific exception types. When a slice needs to react to a persistence failure (unique-violation retry, concurrency conflict), the repository or an Application-layer abstraction translates it into a domain-level exception or result the controller can handle without an EF Core reference. Plan-prescribed shortcuts are still a boundary debt — record them rather than let them set precedent.
- **Applies to**: All controllers in `src/server/PredictionLeague.Api/Controllers/` and the repository contracts in `PredictionLeague.Application/Abstractions/Persistence/`. Revisit the S-03 retry when S-05 (invite-and-join-league) touches the same path.

## League organizer identity is single-sourced on OrganizerUserId, not on membership Role

- **Context**: src/server/PredictionLeague.Infrastructure/Persistence/Repositories/LeagueRepository.cs:161-176 — S-05 TransferOrganizerAsync.
- **Problem**: The organizer is represented twice — `League.OrganizerUserId` and a `LeagueMembership` row with `Role = Organizer`. `TransferOrganizerAsync` moves both in one save, which holds within a request but not across concurrent ones: neither entity carries a concurrency token, so two transfers that read before either commits can leave two `Role = Organizer` rows while `OrganizerUserId` names only one of them. Harmless today only because every authorization check reads `OrganizerUserId`; `Role` reaches nothing but the roster badge.
- **Rule**: Per-league authorization must always derive from `League.OrganizerUserId`. Never authorize off `LeagueMembership.Role` — it is display metadata that can legitimately drift under concurrency. If a slice ever needs `Role` to be authoritative, add a concurrency token to `League` first so a stale transfer fails loudly instead of writing.
- **Applies to**: `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs` and every future per-league permission check (S-06 predictions, S-07 standings).

## New children of a tracked parent need an explicit Add when the key is client-generated

- **Context**: S-05/S-07 manual verification. `LeagueRepository.JoinAsync` (join a league), `LeagueRepository.ReplaceScoringRulesAsync` (add a scoring parameter that wasn't there), `MatchRepository.ReplaceEventsAsync` (admin enters match events).
- **Problem**: All three built a child with `Id = Guid.NewGuid()` and added it only to the tracked parent's navigation collection (`league.Memberships.Add(...)`). EF Core paints a graph-discovered child by the **`IsKeySet` heuristic**: the key already has a non-default value, so EF reads it as an existing row and tracks it as `Modified` — emitting `UPDATE ... WHERE Id = @p` against a row that was never inserted. SaveChanges then throws `DbUpdateConcurrencyException` ("expected to affect 1 row(s), but actually affected 0"), surfacing as a 500. The create paths get away with the identical shape only because their parent is `Added`, which paints the whole graph `Added` regardless of key state — which is exactly why this survived S-04, S-05, and archival: only the *second* user joining, or the *first* new rule on an existing league, ever hits it.
- **Rule**: When adding a new entity to an already-tracked parent's collection, always call `Context.Set<TChild>().Add(child)` explicitly. Never rely on navigation fixup to infer `Added` — with client-generated keys it infers `Modified` instead, and the failure is a runtime 500 that no build or lint catches. Mirror the codebase's existing explicit `Context.Set<T>().Remove(...)` for orphan deletion.
- **Applies to**: every repository in `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/` that mutates a collection navigation on a tracked aggregate. `PredictionRepository.UpsertManyAsync` already does it right (`Set.AddAsync`) — use it as the reference shape.
