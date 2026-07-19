# S-02 Admin Seed Tournament Implementation Plan

## Overview

Build the admin surface around the already-shipped F-03 ingest: tournament + player + nationality admin APIs (full CRUD on tournaments, list/create/edit on players, read-only nationalities), CSV bulk player import with dry-run preview, a publish/draft gate that hides in-prep tournaments from non-admins, config-driven admin-email bootstrap, an admin verification page exposing per-match detail (scorers + cards from F-03), and the React admin section that ties it together. Outcome: an admin can sign in, add a tournament, optionally upload a player roster from CSV, click "Ingest now" to pull fixtures + results + events through F-03, verify the data, then publish the tournament — making it visible to S-03 (organizer-creates-league) and downstream slices.

> **Addendum (2026-07-19): manual match entry.** A follow-on slice was folded into this change
> instead of a separate `admin-manual-matches` change: an admin adds matches by hand (form + CSV
> import) as the interim data source while paid API-Football ingest is deferred. It makes
> `Match.ExternalFixtureId` / `Team.ExternalTeamId` nullable (filtered unique indexes), adds a
> `TeamsController`, match write endpoints + `IMatchCsvImporter` on the server, and match
> form/import pages on the client. Full detail in `change.md` → Notes → "Manual match entry".

## Current State Analysis

F-03 (`football-api-ingest`) and F-02 (`auth-oauth-scaffold`) are both in the tree (verified by file inspection, not just roadmap):

- **Tournament entity** (`Tournament.cs:4-21`) already carries `Name`, `ExternalApiId` (string?, maxlen 100), `Season`, `StartDate`, `EndDate`, `Matches` collection.
- **Player entity** has `Guid Id`, `int ExternalPlayerId`, `string Name`, `Guid? ClubTeamId`, `Guid? NationalTeamId`. **No personal stats, no Nationality FK yet.**
- **TournamentRepository** has `GetByExternalApiIdAsync` + `GetActiveAsync(onDate)` — the second is what the Functions timer iterates; ingest is fully wired through `IFixtureIngestService`.
- **IngestController** (`IngestController.cs`) already `[Authorize(Policy=AdminOnly)]`-gated with a Development-only 404 fallback as belt-and-suspenders. Endpoint: `POST api/ingest/{tournamentId}?season={season}&date={date}` — returns `IngestResult` counts.
- **Auth** (F-02): `ApplicationUser.IsGlobalAdmin` bool exists; `AuthorizationPolicies.AdminOnly` policy registered; `AppUserClaimsPrincipalFactory` emits the `prediction:admin` claim from the bool. **No path to flip `IsGlobalAdmin` exists yet** — it must be true in the DB before any admin endpoint works.
- **AuthController** has `Login`, `Register`, `ExternalCallback` (Google), `Me`, `Logout`. All three sign-in paths call `SignInManager.SignInAsync` / `PasswordSignInAsync` / `ExternalLoginSignInAsync`. Each is the natural promotion seam.
- **Client routes** (`routes/index.tsx`): `/`, `/sign-in`, `/app` (guarded by `RequireAuth`). **No admin route, no admin-aware shell.** `AuthUser` type already exposes `isGlobalAdmin: boolean` and `AppShell.tsx` reads `user.displayName` — but the shell is a stub that says "League creation arrives in S-03".
- **F-04 deploy is done** — prod App Service + Azure SQL Basic exist. Any migration this slice ships must be **additive and forward-only** (no destructive changes to F-03's just-deployed schema).

What is **missing** (this slice closes it):

- No `Nationality` entity / seed; no per-Player personal stats; no per-tournament squad concept; no `IsPublished` on Tournament.
- No tournament admin controller (`POST/GET/PUT/DELETE /api/tournaments`).
- No player admin controller (`GET/POST/PUT /api/players`).
- No nationalities read controller.
- No CSV bulk-import endpoint.
- No tournament-detail read endpoint that exposes matches + events for admin verification (the existing `Matches` DbSet is reachable only through F-03 ingest internals).
- No path to grant `IsGlobalAdmin` — the F-02 cookie carries the claim correctly, but the underlying bool is unreachable from any code path.
- No admin section in the SPA.

## Desired End State

A signed-in admin can:

1. Land on `/admin/tournaments`, see all tournaments (Draft + Published), and create a new one (Name / ExternalApiId / Season / StartDate / EndDate, IsPublished defaults false).
2. Open `/admin/players`, see the global player table, add or edit players individually, **or** upload a CSV — see a dry-run preview (`X to create / Y to update / Z conflicts`) — confirm — commit. Optionally include a `tournamentId` in the upload to bind the imported players to that tournament's squad.
3. From the tournament detail page click **Ingest now** → `POST api/ingest/{id}` runs through F-03 → counts return to the UI → the same page lists ingested Matches (kickoff / teams / status / score) with each match expandable into its events (scorer + team + type) — direct visual proof of FR-005.
4. Toggle the tournament from Draft to Published. Non-admin signed-in users now see it via `GET /api/tournaments`; while Draft, only admins do.
5. The first admin gets bootstrapped via `Admin:Emails` in configuration: any matching email is auto-promoted on next sign-in (local or Google). The promotion is idempotent and refreshes the auth cookie so the `prediction:admin` claim takes effect in the same session.

Verify by: creating a Tournament with a known `ExternalApiId` + Season, uploading a sample CSV, clicking Ingest now against a date with played matches, confirming the matches list + event drill-down shows correct scorer/card attribution, then publishing and checking visibility through a non-admin signed-in account.

### Key Discoveries

- Tournament's `ExternalApiId` is `string?` (`Tournament.cs:11`, maxlen 100) — must stay nullable (an admin can create a tournament before deciding the API id) but **must be unique when non-null** to keep F-03's ingest lookup (`GetByExternalApiIdAsync`) deterministic. There is no unique index today; this slice adds a filtered unique index.
- F-03's ingest is callable for a Draft tournament without changes (the `IsPublished` filter is a read-side concern only). This is intentional: admin pre-loads data, verifies, then publishes.
- The `AppUserClaimsPrincipalFactory` reads `IsGlobalAdmin` at cookie-creation time. After flipping the bool, the cookie must be regenerated via `SignInManager.RefreshSignInAsync(user)` for the new claim to appear in the same session.
- F-04 is deployed, so all migrations are additive + forward-only. The lessons.md rule about explicit max-lengths applies to every new string column (`Player.Position` enum-as-int, `Nationality.Code`/`Name`, `Player.HeightCm` int — string ones get `IsRequired().HasMaxLength()` in Fluent configs).
- The `[Authorize(Policy=AdminOnly)]` already on `IngestController` means the dev-env 404 guard is now belt-and-suspenders on top of a real policy — drop it so prod admins can use the verification button.
- `League.TournamentId` will become a FK in S-03; this slice anticipates the delete-cascade rule (block tournament delete if any League references it). Currently there is no `League` table yet — `League` exists as a placeholder; the delete-block check is over the `Leagues` DbSet existing or not. **Defensive**: query for any `League` with a matching `TournamentId` via the existing `LeagueRepository`; if zero, allow delete.
- `CsvHelper` (NuGet) is the established .NET CSV reader; no parser exists in the solution yet — Infrastructure project gets it.

## What We're NOT Doing

- **No player career stats** (goals / appearances / minutes). PRD scope: identity + nationality + position is enough for scoring + display; career stats are admin reporting and a real maintenance burden (not in API-Football free tier).
- **No Excel / JSON import**. CSV only. Spreadsheets save to CSV; JSON can be added later additively.
- **No fuzzy player matching**. Exact `Name + NationalityCode` only. Fuzzy matching silently mis-credits scoring (FR-005 break).
- **No player delete**. `MatchEvent.PlayerId` is a non-null FK; delete needs orphan handling we don't want now.
- **No tournament soft-delete / Archived state**. Publish is the only visibility gate; Draft ↔ Published only. Archived deferred.
- **No squad-from-API ingest** (`/players/squads?team=`). Burns a third of the daily free-tier budget; ingest's minimal-create Player fallback (already in F-03) plus admin CSV is enough.
- **No tournament-level role permissions** beyond AdminOnly. Organizer/member roles stay per-league (S-03+).
- **No background recompute of standings** on result corrections. S-07's territory.
- **No prod migration automation**. Migrations stay forward-only + human-gated per infra-v2.
- **No S-03 league flow**. Tournament list endpoint is shaped so S-03 can consume it, but no league code ships here.
- **No tests**. No test project exists in the solution; this slice is verified by `dotnet build` + manual end-to-end run, like F-01/F-02/F-03.

## Implementation Approach

Bottom-up, like F-03: model + migration + admin bootstrap first, then the tournament admin API + ingest-controller hardening (both verifiable through HTTP without UI), then the player + nationality + CSV-import surface, then the matches read endpoint that fuels the verification UI, then the React admin section that ties them together. Each phase ends with a verifiable build + endpoint or screen.

Layer placement:

- **Domain**: `Nationality` entity, `PlayerPosition` enum, `TournamentSquad` join entity, `Tournament.IsPublished`, additional `Player` fields. No package refs.
- **Application**: new `INationalityRepository`, `IPlayerRepository` already exists from F-03 (extended), `ITournamentSquadRepository`; new abstractions `IAdminEmailAllowlist`, `IPlayerCsvImporter` (port + DTOs).
- **Infrastructure**: EF configs + one additive migration; repository impls; `AdminEmailAllowlist` reading `AdminOptions`; `CsvHelperPlayerImporter` (CsvHelper-backed); seed `Nationality` rows via `HasData`.
- **Api**: new `TournamentsController`, `PlayersController`, `NationalitiesController`; AuthController gains a single `EnsureAdminClaimAsync` call in three sign-in success paths.
- **Client**: new `routes/admin/` subtree, `RequireAdmin` guard, admin nav in `AppShell`, pages for tournaments / players / CSV import / tournament detail.

The ingest service stays untouched. We add **read** endpoints that consume what ingest writes; we do not change the write path.

## Critical Implementation Details

- **Admin claim refresh timing**: `AppUserClaimsPrincipalFactory` reads `ApplicationUser.IsGlobalAdmin` at cookie-creation time. The promotion path must (1) `await _userManager.UpdateAsync(user)` to persist the flag, then (2) `await _signInManager.RefreshSignInAsync(user)` to re-issue the cookie with the new claim, in this order. Skipping (2) leaves the session unaware of admin status until next sign-in.
- **`ExternalApiId` uniqueness**: a filtered unique index `WHERE ExternalApiId IS NOT NULL` is required so the F-03 `GetByExternalApiIdAsync` lookup is sound. Without it two tournaments can share an api id and ingest writes the wrong target.
- **CSV idempotency**: each row is matched on `(Name, NationalityCode)`. If found, update non-null fields (don't blank existing data with empty CSV cells); if not, create. Dry-run runs the entire match logic but skips `SaveChangesAsync`. Commit runs in a single transaction so a partial fail rolls back. Optional `tournamentId` query param adds (or no-ops if present) a `TournamentSquad` row per resolved player.
- **CSV conflict definition**: a row is a "conflict" if the parsed `NationalityCode` does not resolve to a seeded `Nationality.Code`, or if the row's `(Name, NationalityCode)` matches a player but the row's `ExternalPlayerId` collides with a different existing player. Conflicts are returned in the dry-run response and the row is skipped on commit (admin must fix the CSV).
- **Delete cascade for Tournament**: cascade `Tournament → Matches → MatchEvents` (configure `OnDelete.Cascade` for these specific FKs only). `Teams` and `Players` are global — never cascade through them. `TournamentSquad` cascade-deletes with the tournament. Delete is **blocked** (return 409) if any `League` row references the tournament (S-03 will populate this).
- **Publish gate**: `GET /api/tournaments` and `GET /api/tournaments/{id}` filter to `IsPublished=true` for non-admin callers; admins see all. Implement via a single `IsAdmin(User)` check at the controller, not via a query filter — query filters at the DbContext level affect ingest reads too, which must always see Drafts.
- **Position enum, stored as int**: `PlayerPosition` (`GK`, `DEF`, `MID`, `FWD`, `Unknown=0`) with `HasConversion<int>()`. Append-only ordering — never reorder.
- **F-04 is deployed**: this migration is additive only (new tables, new nullable columns, default `false` for `IsPublished`). No column drops, no type changes on the F-03 tables. Prod migration is human-gated per infra-v2 (do not auto-apply outside Development).

## Phase 1: Domain Model + Migration + Admin Bootstrap

### Overview

Add the new entities + columns, the one additive migration, and the config-driven admin promotion path that makes every later phase reachable.

### Changes Required

#### 1. Nationality entity (new)

**File**: `src/server/PredictionLeague.Domain/Entities/Nationality.cs`

**Intent**: Lookup table for player nationality, pre-seeded with ISO 3166-1 alpha-3 codes. Separate from `Team` — a nationality is a citizenship; a national team is who plays for that nation. They usually align but the model honors the distinction.

**Contract**: `int Id` (stable seeded PK); `required string Code` (3 chars, ISO 3166-1 alpha-3, e.g. `"POL"`, unique); `required string Name` (e.g. `"Poland"`). Carry `// FR-005` comment per server convention.

#### 2. Player entity extensions

**File**: `src/server/PredictionLeague.Domain/Entities/Player.cs`

**Intent**: Add the identity fields admin manages (DOB, position, height, nationality FK). Drop nothing.

**Contract**: append `DateOnly? DateOfBirth`, `PlayerPosition Position` (default `Unknown`), `int? HeightCm`, `int? NationalityId` (FK → `Nationality`). Keep all existing fields untouched.

#### 3. PlayerPosition enum

**File**: `src/server/PredictionLeague.Domain/Entities/Enums.cs`

**Intent**: Constrain player position to a small set; persisted as int.

**Contract**: append `public enum PlayerPosition { Unknown = 0, GK = 1, DEF = 2, MID = 3, FWD = 4 }`. Append-only.

#### 4. Tournament.IsPublished

**File**: `src/server/PredictionLeague.Domain/Entities/Tournament.cs`

**Intent**: Visibility gate. Default false.

**Contract**: `public bool IsPublished { get; set; }` — defaults to `false` (CLR default; migration emits SQL default `0`).

#### 5. TournamentSquad join entity

**File**: `src/server/PredictionLeague.Domain/Entities/TournamentSquad.cs`

**Intent**: Explicit per-tournament roster. CSV import optionally writes rows here; UI later can show a tournament's roster. Ingest still works without rows here (events auto-create Players).

**Contract**: `Guid TournamentId` + `Guid PlayerId` (composite PK); FK navs to `Tournament` and `Player`. No payload fields v1.

#### 6. Fluent configurations

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/` — new `NationalityConfiguration.cs`, `TournamentSquadConfiguration.cs`; edits to `PlayerConfiguration.cs`, `TournamentConfiguration.cs`.

**Intent**: Map the new entities; satisfy the lessons rule on every string column; seed Nationality rows; configure cascade rules.

**Contract**:
- `Nationality`: `HasKey(Id)`; `Property(Code).IsRequired().HasMaxLength(3)`; `Property(Name).IsRequired().HasMaxLength(100)`; unique index on `Code`. Seed via `HasData` (250 ISO 3166-1 alpha-3 entries) with stable ids (1..n in alphabetical order).
- `Player`: add `Property(Position).HasConversion<int>()`; `HasOne<Nationality>().WithMany().HasForeignKey(p => p.NationalityId).OnDelete(DeleteBehavior.Restrict)`; no max-length on int columns.
- `Tournament`: add a **filtered unique index** on `ExternalApiId` (`HasIndex(t => t.ExternalApiId).IsUnique().HasFilter("[ExternalApiId] IS NOT NULL")`); no default needed for `IsPublished` (CLR default works).
- `TournamentSquad`: `HasKey(ts => new { ts.TournamentId, ts.PlayerId })`; both FKs `OnDelete.Cascade` from Tournament side, `Restrict` from Player side.
- `TournamentConfiguration` Match relationship — already `OnDelete.Cascade` (`TournamentConfiguration.cs:16-19`); no edit needed. `MatchConfiguration` already restricts Match → Team; keep that.

#### 7. Migration

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Migrations/` (new `dotnet ef migrations add AddAdminSeedSurface`)

**Intent**: One additive migration. Creates `Nationalities` (seeded), `TournamentSquads`; adds `IsPublished` to `Tournaments`; adds `DateOfBirth`, `Position`, `HeightCm`, `NationalityId` to `Players`; filtered unique index on `Tournaments.ExternalApiId`.

**Contract**: generated migration; verify the `Up` is additive (no column drops/type changes) and seed data is in place. Tournament → Matches cascade is already configured (`TournamentConfiguration.cs:16-19`), so no FK alteration is expected on that edge. **Forward-only**: do not auto-run in prod; F-04 runbook applies.

#### 8. AdminOptions + IAdminEmailAllowlist

**File**: `src/server/PredictionLeague.Infrastructure/Identity/AdminOptions.cs`, `IAdminEmailAllowlist.cs`, `AdminEmailAllowlist.cs`.

**Intent**: Bind `Admin:Emails` (string[]) and expose case-insensitive contains.

**Contract**: `AdminOptions { string[] Emails = []; }` (section `"Admin"`). `IAdminEmailAllowlist.IsAdmin(string? email): bool`. Implementation hashes emails into a `HashSet<string>(StringComparer.OrdinalIgnoreCase)` at construction; constructor takes `IOptions<AdminOptions>`. Registered as singleton.

#### 9. EnsureAdminClaim seam in AuthController

**File**: `src/server/PredictionLeague.Api/Controllers/AuthController.cs`

**Intent**: After every successful sign-in, promote the user's `IsGlobalAdmin` based on the allowlist and refresh the cookie so the claim takes effect immediately.

**Contract**: inject `IAdminEmailAllowlist`. Private helper:
`private async Task EnsureAdminClaimAsync(ApplicationUser user)` →
if `_allowlist.IsAdmin(user.Email)` and `!user.IsGlobalAdmin`, set `user.IsGlobalAdmin = true`, `await _userManager.UpdateAsync(user)`, then `await _signInManager.RefreshSignInAsync(user)`. Call sites:
- `Register` — already has `user`; call after `SignInAsync`.
- `Login` — `PasswordSignInAsync` does not return the user; after success do `var user = await _userManager.FindByEmailAsync(request.Email)`; if non-null, call helper.
- `ExternalCallback` — newly-provisioned branch already has `user`; call after `SignInAsync`. Existing-link branch: `ExternalLoginSignInAsync` does not return the user; after success do `var user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey)`; if non-null, call helper.

#### 10. DI registration

**File**: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs`

**Intent**: Register `AdminOptions` + `IAdminEmailAllowlist`; register `INationalityRepository` (Phase 2 introduces interface, but the binding hook lands now alongside others).

**Contract**: `services.Configure<AdminOptions>(config.GetSection("Admin"))` + `services.AddSingleton<IAdminEmailAllowlist, AdminEmailAllowlist>()` in the existing `AddInfrastructure` extension. Add empty `"Admin": { "Emails": [] }` to `appsettings.json`.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build src/server/prediction-league.slnx`
- Migration generates without snapshot errors: `dotnet ef migrations add AddAdminSeedSurface`
- Migration applies cleanly on dev startup: `dotnet run` (Api) → `db.Database.Migrate()` succeeds
- `GET /health/db` returns healthy after migration

#### Manual Verification

- Inspect DB: `Nationalities` table exists and is seeded (~250 rows); `TournamentSquads` exists; `Tournaments.IsPublished` column exists (default 0); `Players` has new columns; filtered unique index on `Tournaments.ExternalApiId` exists.
- Add own email to `Admin:Emails` in user-secrets; sign in fresh → `GET /api/auth/me` returns `isGlobalAdmin: true`; existing IngestController returns non-404 (no 401/403). Without the email in the allowlist, fresh sign-in returns `isGlobalAdmin: false`.

**Implementation Note**: After automated verification passes, pause for human confirmation of the DB shape + admin promotion before Phase 2.

---

## Phase 2: Tournament Admin API + IngestController Hardening

### Overview

CRUD-by-policy controller for tournaments. Read endpoints visible to all authenticated users (with non-admins seeing only `IsPublished=true`); write endpoints AdminOnly. Drop the now-redundant dev-only 404 guard on the existing IngestController.

### Changes Required

#### 1. Tournament repository extension

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/ITournamentRepository.cs` + `Infrastructure/Persistence/Repositories/TournamentRepository.cs`

**Intent**: Add the queries the controller needs without altering F-03's contract.

**Contract**: add `Task<IReadOnlyList<Tournament>> ListAsync(bool includeUnpublished, CancellationToken)` (admin = true, non-admin = false). Existing methods untouched.

#### 2. League references check

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/ILeagueRepository.cs` + impl

**Intent**: Block delete when leagues reference the tournament.

**Contract**: add `Task<bool> AnyForTournamentAsync(Guid tournamentId, CancellationToken)`. Implementation: `await Set.AnyAsync(l => l.TournamentId == id, ct)`. (Per F-01, `League.TournamentId` is already a Guid on the entity.)

#### 3. TournamentsController

**File**: `src/server/PredictionLeague.Api/Controllers/TournamentsController.cs`

**Intent**: CRUD over Tournament with role-aware visibility.

**Contract**:
- `[ApiController]` `[Route("api/[controller]")]` `[Authorize]` (all routes need a signed-in user; admin gate per action).
- `GET /` → list. Internally checks `User.HasClaim(AuthorizationPolicies.AdminClaimType, ...)` to decide `includeUnpublished`.
- `GET /{id:guid}` → detail. 404 if missing; for non-admins, 404 if `IsPublished=false` (no information leak).
- `POST /` `[Authorize(Policy=AdminOnly)]` → `CreateTournamentRequest { Name, ExternalApiId?, Season, StartDate, EndDate }`. Validate `EndDate >= StartDate`; map; persist via `AddAsync` + `SaveChangesAsync`. Returns 201 + body.
- `PUT /{id:guid}` `[Authorize(Policy=AdminOnly)]` → `UpdateTournamentRequest { Name, Season, StartDate, EndDate }`. **`ExternalApiId` is immutable post-create** (silently ignore if sent; document in `PredictionLeague.http`). Returns 200 + body.
- `PATCH /{id:guid}/publish` `[Authorize(Policy=AdminOnly)]` → body `{ isPublished: bool }`. Toggle. Returns 204.
- `DELETE /{id:guid}` `[Authorize(Policy=AdminOnly)]` → call `_leagueRepository.AnyForTournamentAsync(id)`; if true, return 409 `ProblemDetails` `"Cannot delete a tournament that has leagues"`. Otherwise delete; EF cascades to Matches/MatchEvents/TournamentSquads. Returns 204.
- DTOs: small records in the same controller file (`TournamentResponse`, `CreateTournamentRequest`, `UpdateTournamentRequest`). Manual mapping (no AutoMapper); match the existing AuthController style.

#### 4. IngestController guard cleanup

**File**: `src/server/PredictionLeague.Api/Controllers/IngestController.cs`

**Intent**: Drop the `if (!_environment.IsDevelopment()) return NotFound();` line. `AdminOnly` policy is sufficient now.

**Contract**: remove the env check and the `IWebHostEnvironment` field+constructor parameter. Comment block above the class — update to reflect that this is no longer dev-only.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build src/server/prediction-league.slnx`
- Existing F-03 flow still works: `POST api/ingest/{tournamentId}` with a Draft tournament + admin auth returns 200 (verified manually in next bullet).

#### Manual Verification

- As admin: `POST /api/tournaments` creates a Draft; `GET /api/tournaments` returns it; `PATCH /api/tournaments/{id}/publish` sets `isPublished: true`; `PUT` updates Name/Season/Dates but ignores `ExternalApiId` if sent.
- As non-admin: `GET /api/tournaments` returns only Published ones; `GET /api/tournaments/{draft-id}` returns 404; `POST/PUT/DELETE/PATCH` return 403.
- `DELETE` on a tournament with no leagues succeeds; with a (manually inserted) League row referencing it, returns 409.
- `POST /api/ingest/{tournamentId}` works in `Production` env config (set `ASPNETCORE_ENVIRONMENT=Production`) with an admin cookie — confirms the dev-guard removal.

**Implementation Note**: pause for human confirmation of the full CRUD round-trip before Phase 3.

---

## Phase 3: Player + Nationality API + CSV Bulk Import

### Overview

Read-only nationality list (consumed by client dropdowns), Player CRUD (no delete), and the CSV bulk-import endpoint with a dry-run preview. Optional `tournamentId` on the import binds imported players to a TournamentSquad.

### Changes Required

#### 1. Nationality repository

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/INationalityRepository.cs` + impl

**Intent**: Look up nationalities by code (for CSV import) and list all (for client dropdown).

**Contract**: `Task<IReadOnlyList<Nationality>> ListAsync(CancellationToken)`; `Task<Nationality?> GetByCodeAsync(string code, CancellationToken)` (case-insensitive). Impl inherits `BaseRepository<Nationality>`.

#### 2. Player repository extension

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/IPlayerRepository.cs` + impl

**Intent**: Need a paged list, an exact-match lookup by `(Name + NationalityCode)`, and an `ExternalPlayerId` collision check for CSV conflict detection. F-03's `GetByExternalPlayerIdAsync` already exists.

**Contract**: add `Task<PagedResult<Player>> ListAsync(int page, int pageSize, CancellationToken)` where `PagedResult<T> { IReadOnlyList<T> Items; int Total; int Page; int PageSize; }` — `Total` is a single `CountAsync` over the same `Where` filter (no filter today, so over whole `Players` set; cheap at MVP scale). Page-size capped server-side at 100. Also `Task<Player?> FindByNameAndNationalityAsync(string name, int nationalityId, CancellationToken)`. Existing `GetByExternalPlayerIdAsync` unchanged.

#### 3. TournamentSquad repository

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/ITournamentSquadRepository.cs` + impl

**Intent**: Idempotent upsert of squad rows during CSV import; list by tournament for later UI.

**Contract**: `Task<bool> ExistsAsync(Guid tournamentId, Guid playerId, CancellationToken)`; `Task AddAsync(TournamentSquad entry, CancellationToken)`; `Task<IReadOnlyList<TournamentSquad>> ListByTournamentAsync(Guid tournamentId, CancellationToken)`. Saves via the unit-of-work pattern (no `SaveChangesAsync` on this repo — the import service owns the transaction).

#### 4. NationalitiesController (read-only)

**File**: `src/server/PredictionLeague.Api/Controllers/NationalitiesController.cs`

**Intent**: Feed the client dropdown.

**Contract**: `[ApiController] [Route("api/[controller]")] [Authorize]`. `GET /` → `NationalityResponse[]` (id, code, name). No write endpoints.

#### 5. PlayersController (list/create/edit)

**File**: `src/server/PredictionLeague.Api/Controllers/PlayersController.cs`

**Intent**: Admin maintenance for Player rows. No delete.

**Contract**:
- `[ApiController] [Route("api/[controller]")] [Authorize(Policy=AdminOnly)]`.
- `GET /?page={n}&pageSize={n}` → `PagedPlayersResponse { items: PlayerResponse[]; total: int; page: int; pageSize: int }` (default page=1, pageSize=50, cap 100). Client uses `total + pageSize` to render pagination controls.
- `GET /{id:guid}` → detail (404 if missing).
- `POST /` → `CreatePlayerRequest { Name, ExternalPlayerId?, NationalityId?, DateOfBirth?, Position?, HeightCm?, ClubTeamId?, NationalTeamId? }`. Validate FKs (Nationality/Team) resolve; ExternalPlayerId unique if non-null.
- `PATCH /{id:guid}` → same body shape; partial-update semantics (null fields leave the row's value unchanged — explicit to keep CSV import semantics consistent). PUT not used; the partial semantics violate REST replace.
- DTOs in-file as records.

#### 6. CSV import abstraction

**File**: `src/server/PredictionLeague.Application/Abstractions/Players/IPlayerCsvImporter.cs` + DTOs

**Intent**: Port the controller calls into. Owns both dry-run and commit paths.

**Contract**:
- `Task<PlayerImportPreview> PreviewAsync(Stream csv, Guid? tournamentId, CancellationToken)` → `{ int ToCreate, int ToUpdate, int Skipped, List<PlayerImportRow> Rows, List<PlayerImportConflict> Conflicts }`. Each row carries the parsed input + the resolved action (`Create | Update | Skip`).
- `Task<PlayerImportResult> CommitAsync(Stream csv, Guid? tournamentId, CancellationToken)` → counts only.
- CSV schema (header row required): `Name,NationalityCode,Position,DateOfBirth,HeightCm,ExternalPlayerId`. `Name` + `NationalityCode` are required per row; the rest are optional and treated as "do not overwrite" when blank.
- `Position` parsed via case-insensitive `Enum.TryParse<PlayerPosition>`; unknown → `Unknown`.
- `DateOfBirth` parsed as ISO-8601 date.
- Errors that prevent a row from being processed (unknown NationalityCode, ExternalPlayerId collides with a different existing player) land in `Conflicts` with a 1-based line number and message; the row is skipped on commit.

#### 7. CSV importer implementation

**File**: `src/server/PredictionLeague.Infrastructure/Players/CsvHelperPlayerImporter.cs`

**Intent**: CsvHelper-backed parser + matching + upsert orchestration.

**Contract**: depends on `INationalityRepository`, `IPlayerRepository`, `ITournamentSquadRepository`, `AppDbContext`. Pseudocode flow:
1. Parse CSV into `PlayerCsvRow` records (CsvHelper, headers required).
2. Load all nationalities into a `Dictionary<string, Nationality>` once (~250 rows).
3. For each row: resolve nationality → if missing, conflict; resolve existing player by `(Name, NationalityId)` → action = update if found else create; if `ExternalPlayerId` set and a *different* player owns it, conflict.
4. Dry-run returns the preview without `SaveChangesAsync`. Commit applies all non-conflict rows in a single `await SaveChangesAsync(ct)`; on `tournamentId` set, idempotently upserts `TournamentSquad` rows for each resolved player.

NuGet: add `CsvHelper` to the Infrastructure project.

#### 8. Import endpoint on PlayersController

**File**: `src/server/PredictionLeague.Api/Controllers/PlayersController.cs` (extend)

**Intent**: HTTP surface for the importer.

**Contract**: `POST /api/players/import?dryRun={bool}&tournamentId={guid?}` — multipart upload, field name `file`. `[Authorize(Policy=AdminOnly)]` (inherited from controller). `dryRun=true` → returns `PlayerImportPreview`; `dryRun=false` → returns `PlayerImportResult`. If `tournamentId` provided, 404 when the tournament does not exist. File size cap: 2 MB (decorate the action with `[RequestSizeLimit(2 * 1024 * 1024)]` and `[RequestFormLimits(MultipartBodyLengthLimit = 2 * 1024 * 1024)]`).

#### 9. DI registration

**File**: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs`

**Intent**: Wire repositories + importer.

**Contract**: `AddScoped<INationalityRepository, NationalityRepository>()`, `AddScoped<ITournamentSquadRepository, TournamentSquadRepository>()`, `AddScoped<IPlayerCsvImporter, CsvHelperPlayerImporter>()`. (`IPlayerRepository` already registered by F-03.)

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build src/server/prediction-league.slnx`
- `CsvHelper` restores: `dotnet restore`

#### Manual Verification

- `GET /api/nationalities` returns the seeded list (~250 rows).
- `POST /api/players` creates a player; `PATCH /api/players/{id}` updates only non-null fields.
- Upload sample CSV with `dryRun=true` → response shows correct create/update/conflict counts.
- Same CSV with `dryRun=false` → DB matches the preview; re-uploading is idempotent (no duplicates, no conflicts).
- Upload with `tournamentId` → `TournamentSquads` rows present for resolved players.
- CSV with an unknown `NationalityCode` row → reported as conflict, the row is not written.
- CSV row reusing an `ExternalPlayerId` already owned by a different player → conflict, not written.

**Implementation Note**: pause for human confirmation of the CSV round-trip (dry-run vs commit parity, idempotency) before Phase 4.

---

## Phase 4: Tournament Matches Read Endpoint

### Overview

Read-only endpoint that returns a tournament's matches with their events resolved (scorer + team + type) — the data backing the admin verification page that proves FR-005 visually.

### Changes Required

#### 1. Match repository extension

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/IMatchRepository.cs` + impl

**Intent**: Tournament-scoped list with eager-loaded events and named references.

**Contract**: add `Task<IReadOnlyList<MatchWithEventsDto>> ListByTournamentAsync(Guid tournamentId, CancellationToken)`. `Match` / `MatchEvent` carry FK ids only — no nav properties exist (`Match.cs`, `MatchEvent.cs`) and the existing Fluent configs use `HasOne<T>()` without a nav selector (`MatchConfiguration.cs:21-29`, `MatchEventConfiguration.cs:14-29`). Implementation projects with explicit joins instead of `Include`:

```csharp
from m in db.Matches.Where(m => m.TournamentId == tournamentId)
join home in db.Teams on m.HomeTeamId equals home.Id
join away in db.Teams on m.AwayTeamId equals away.Id
orderby m.KickoffUtc
select new MatchWithEventsDto(
    m.Id, m.ExternalFixtureId, m.KickoffUtc, m.Status,
    new TeamRefDto(home.Id, home.Name, m.HomeScore),
    new TeamRefDto(away.Id, away.Name, m.AwayScore),
    (from e in db.MatchEvents.Where(e => e.MatchId == m.Id)
     join p in db.Players on e.PlayerId equals p.Id
     join t in db.Teams on e.TeamId equals t.Id
     join et in db.MatchEventTypes on e.MatchEventTypeId equals et.Id
     orderby e.Minute, e.MinuteExtra
     select new MatchEventDto(e.Minute, e.MinuteExtra, et.Code, et.Category, p.Name, t.Name)
    ).ToList()
);
```

DTOs live next to the controller (Phase 4 #2). This stays under Phase 4 scope — no Domain change, no ingest path edits.

#### 2. Matches endpoint on TournamentsController

**File**: `src/server/PredictionLeague.Api/Controllers/TournamentsController.cs` (extend)

**Intent**: One endpoint for the admin detail page.

**Contract**: `GET /api/tournaments/{id:guid}/matches` `[Authorize]` — admin sees regardless of publish; non-admin gets 404 if tournament is Draft. Maps `Match` + `Events` into a flat response shape:
```jsonc
{
  "matchId": "...", "externalFixtureId": 0, "kickoffUtc": "...",
  "status": "Scheduled|Live|Finished",
  "homeTeam": { "id": "...", "name": "...", "score": 0 },
  "awayTeam": { "id": "...", "name": "...", "score": 0 },
  "events": [ { "minute": 0, "code": "NormalGoal", "category": "Goal", "playerName": "...", "teamName": "..." } ]
}
```
Map status from the existing 3-bucket enum; nulls when unresolved.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build src/server/prediction-league.slnx`

#### Manual Verification

- Seed a tournament with a valid `ExternalApiId` + `Season`, run `POST /api/ingest/{id}` for a date with finished fixtures, then `GET /api/tournaments/{id}/matches` returns the ingested matches with scorers and cards attributed to the right player + team.
- Draft tournament: admin gets data; non-admin gets 404.
- Empty tournament (no ingest yet): returns `[]`, not an error.

**Implementation Note**: pause for human confirmation of the data shape before Phase 5 starts on the client.

---

## Phase 5: Client Admin Section

### Overview

React admin subtree gated by `RequireAdmin`. Pages for Tournaments (list/create/edit/detail-with-ingest-and-verification), Players (list/create/edit + CSV upload with preview), updated AppShell nav. Reuses the existing `apiFetch` + `useAuth` plumbing.

### Changes Required

#### 1. RequireAdmin guard

**File**: `src/client/src/routes/RequireAdmin.tsx`

**Intent**: Mirror `RequireAuth` but additionally check `user.isGlobalAdmin`.

**Contract**: functional component returning `<Outlet />` when `status==="authenticated" && user.isGlobalAdmin`; redirect to `/app` when authenticated but not admin (with a flash message via search-param); delegate to `RequireAuth` semantics for unauthenticated.

#### 2. Admin routes

**File**: `src/client/src/routes/index.tsx`

**Intent**: Add the admin subtree.

**Contract**: under a new `{ element: <RequireAdmin /> }` entry, children:
- `/admin/tournaments` → `<TournamentsListPage />`
- `/admin/tournaments/new` → `<TournamentFormPage />`
- `/admin/tournaments/:id` → `<TournamentDetailPage />` (ingest button + matches/events view)
- `/admin/tournaments/:id/edit` → `<TournamentFormPage />` (edit mode; `ExternalApiId` field disabled)
- `/admin/players` → `<PlayersListPage />`
- `/admin/players/new` → `<PlayerFormPage />`
- `/admin/players/:id/edit` → `<PlayerFormPage />`
- `/admin/players/import` → `<PlayerImportPage />`

#### 3. Tournament pages

**File**: `src/client/src/routes/admin/tournaments/` (new directory: `TournamentsListPage.tsx`, `TournamentFormPage.tsx`, `TournamentDetailPage.tsx`)

**Intent**: CRUD + ingest trigger + matches view.

**Contract**:
- List: table of tournaments (Name / Season / Dates / Published badge), row actions Edit / Publish toggle / Delete (delete confirms via a `Dialog` shadcn primitive; 409 surfaces the "has leagues" message).
- Form: shadcn `Form` + `Input` + `Label`; fields per the API. On edit, `ExternalApiId` is `disabled`. Submit → POST or PUT; redirect to detail.
- Detail: header with Name + Publish toggle + **Ingest now** button → `POST /api/ingest/{id}?season=${tournament.season}` → display returned `IngestResult` counts in a toast/card; below, the matches list from `GET /api/tournaments/{id}/matches`. Each match row expands to show events (collapsible via a shadcn primitive — already-vendored `Card` is enough; no need to add Accordion if not present).

#### 4. Player pages

**File**: `src/client/src/routes/admin/players/` (new: `PlayersListPage.tsx`, `PlayerFormPage.tsx`, `PlayerImportPage.tsx`)

**Intent**: CRUD + CSV.

**Contract**:
- List: paged table (Name / Nationality / Position / Club / National Team), search-by-name (client-side filter on the page is fine for v1).
- Form: same shadcn shape; `NationalityId` is a `<select>` populated from `GET /api/nationalities`; `Position` is a `<select>` with the enum values; `ClubTeamId`/`NationalTeamId` are free `Guid` inputs for v1 (team admin lives in a later slice).
- Import: file input (`<input type="file" accept=".csv" />`); optional Tournament `<select>` (populated from `GET /api/tournaments` with `includeUnpublished` because admin sees all); buttons: **Preview** → `POST /api/players/import?dryRun=true` → render preview table (rows + conflicts); **Commit** (enabled only after a successful preview) → same endpoint with `dryRun=false`. Show counts on each step.

#### 5. AppShell nav

**File**: `src/client/src/routes/AppShell.tsx`

**Intent**: Show an Admin link when `user.isGlobalAdmin`.

**Contract**: in the header, conditionally render a `<Link to="/admin/tournaments">Admin</Link>` next to the displayName. Keep the existing "League creation arrives in S-03" placeholder body untouched.

#### 6. API client helpers

**File**: `src/client/src/lib/api.ts` (no new file — existing `apiFetch` is sufficient)

**Intent**: No structural change; new endpoints called directly from pages via `apiFetch`. For multipart upload, extend `apiFetch` if needed to skip the `Content-Type: application/json` default when a `FormData` body is passed.

**Contract**: confirm `apiFetch` does not force a JSON `Content-Type` on `FormData` bodies. If it does, add a small branch: when `body instanceof FormData`, omit `Content-Type` (the browser sets it with boundary). Touches only the existing helper.

### Success Criteria

#### Automated Verification

- Type-check + build pass: `npm run build` (runs `tsc -b` then `vite build`)
- Lint passes: `npm run lint`

#### Manual Verification

- Sign in as a non-admin user → no "Admin" link in the shell; `/admin/tournaments` redirects to `/app`.
- Sign in as an allowlisted admin → "Admin" link appears.
- Create a tournament; toggle Publish; verify a non-admin sees it only when Published.
- Edit a tournament — `ExternalApiId` is disabled.
- Delete a tournament with no leagues — succeeds; with a manually inserted League → 409 message shown.
- On tournament detail, click **Ingest now** for a tournament with a valid `ExternalApiId` and a past date → counts appear, matches list populates, each match expands to show events with scorer + team.
- Players list paginates; create + edit both work; nationality dropdown shows the seeded list.
- CSV upload: Preview shows accurate counts + conflicts; Commit applies; re-uploading shows zero new creates (idempotent); supplying a `tournamentId` writes `TournamentSquads` rows visible in DB.

**Implementation Note**: this is the last phase — pause for human end-to-end confirmation against the actual API-Football data before marking S-02 complete.

---

## Testing Strategy

No test project exists. Verification is build-gated + manual end-to-end, like F-01/F-02/F-03.

### Unit Tests

- Optional, not required this slice. If a test project is later added, the CSV importer's matching logic (`PreviewAsync`) is the high-value target — record a sample CSV + a `Nationality` set + an existing `Player` set and assert the action breakdown.

### Integration Tests

- Deferred. The admin UI + manual API calls exercise the full HTTP + persistence path end-to-end.

### Manual Testing Steps

1. Set `Admin:Emails` in user-secrets (include the dev sign-in email); sign in fresh; `GET /api/auth/me` shows `isGlobalAdmin: true`.
2. Create a tournament (`Name="Euro 2024 test"`, `ExternalApiId="4"`, `Season=2024`, start/end dates).
3. Upload a sample CSV with `dryRun=true` → preview → `dryRun=false` → commit. Verify idempotency by re-uploading.
4. Click **Ingest now** for a past date → counts return; matches list shows fixtures with scores; expand a finished match → events show scorers + cards attributed correctly.
5. Toggle the tournament Published; sign out and sign in as a non-admin → tournament appears in `GET /api/tournaments` but the Admin nav doesn't show.
6. Toggle back to Draft → non-admin no longer sees it.
7. Attempt `DELETE /api/tournaments/{id}` — succeeds. Manually insert a League row pointing at a fresh tournament; DELETE returns 409 with the explanatory message.

## Performance Considerations

- Player list paging caps at 100 per page server-side; v1 admin will not see thousands of players.
- CSV import is bounded by the 2 MB request cap and a single transaction commit; bulk for ~700-row World Cup squads stays under a second on dev SQL Server.
- Matches read endpoint loads events with three `Include`/`ThenInclude` chains — fine for ~64 matches × ~5 events on a typical tournament; revisit if a tournament's event count crosses 10k.

## Migration Notes

- Migration is purely additive; safe to run against the F-04 prod environment under the existing forward-only + human-gated process (per infra-v2). No data backfill needed: existing F-03 tournaments default `IsPublished=false` (Draft) — admin must explicitly publish them. This is the desired post-migration state.

## References

- F-03 plan + brief: `context/changes/football-api-ingest/plan.md`, `plan-brief.md`
- F-02 / F-04 lessons: `context/foundation/lessons.md`
- Roadmap S-02: `context/foundation/roadmap.md`
- PRD: `context/foundation/prd.md` (FR-003, FR-004, FR-005)
- Existing IngestController: `src/server/PredictionLeague.Api/Controllers/IngestController.cs`
- Existing AuthController (admin-promote seams): `src/server/PredictionLeague.Api/Controllers/AuthController.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain Model + Migration + Admin Bootstrap

#### Automated

- [x] 1.1 Solution builds: `dotnet build src/server/prediction-league.slnx` — 3a90cae
- [x] 1.2 Migration generates without snapshot errors: `dotnet ef migrations add AddAdminSeedSurface` — 3a90cae
- [x] 1.3 Migration applies cleanly on dev startup — 3a90cae
- [x] 1.4 `GET /health/db` returns healthy after migration — 3a90cae

#### Manual

- [x] 1.5 `Nationalities`/`TournamentSquads` tables exist; `Tournaments.IsPublished` + Player columns added; filtered unique index on `Tournaments.ExternalApiId` exists — 3a90cae
- [x] 1.6 Admin allowlist promotes IsGlobalAdmin on fresh sign-in and refreshes the cookie — 3a90cae

### Phase 2: Tournament Admin API + IngestController Hardening

#### Automated

- [x] 2.1 Solution builds: `dotnet build src/server/prediction-league.slnx` — 1c151e7
- [x] 2.2 Existing `POST api/ingest/{tournamentId}` still works against a Draft tournament with admin auth — 1c151e7

#### Manual

- [x] 2.3 Admin CRUD round-trip: POST/GET/PUT/PATCH/DELETE works as specified — 1c151e7
- [x] 2.4 Non-admin sees only Published tournaments and gets 403 on writes — 1c151e7
- [x] 2.5 DELETE on a tournament with leagues returns 409 — 1c151e7
- [x] 2.6 `POST /api/ingest/{id}` works in non-Development environment with an admin cookie — 1c151e7

### Phase 3: Player + Nationality API + CSV Bulk Import

#### Automated

- [x] 3.1 Solution builds: `dotnet build src/server/prediction-league.slnx` — 4c7e2bc
- [x] 3.2 `CsvHelper` restores: `dotnet restore` — 4c7e2bc

#### Manual

- [x] 3.3 `GET /api/nationalities` returns the seeded list (~250 rows) — 4c7e2bc
- [x] 3.4 `POST /api/players` creates a player; `PATCH /api/players/{id}` updates only non-null fields — 4c7e2bc
- [x] 3.5 CSV `dryRun=true` preview shows correct create/update/conflict counts — 4c7e2bc
- [x] 3.6 CSV `dryRun=false` commit: DB matches the preview — 4c7e2bc
- [x] 3.7 CSV re-upload is idempotent (no duplicates, no conflicts) — 4c7e2bc
- [x] 3.8 Upload with `tournamentId` populates `TournamentSquads` for resolved players — 4c7e2bc
- [x] 3.9 Unknown NationalityCode row reported as conflict, not written — 4c7e2bc
- [x] 3.10 ExternalPlayerId reused by a different player reported as conflict, not written — 4c7e2bc

### Phase 4: Tournament Matches Read Endpoint

#### Automated

- [x] 4.1 Solution builds: `dotnet build src/server/prediction-league.slnx` — 5f76694

#### Manual

- [x] 4.2 `GET /api/tournaments/{id}/matches` returns ingested matches with correct scorer + team attribution — 5f76694
- [x] 4.3 Draft tournament: admin sees data, non-admin gets 404 — 5f76694
- [x] 4.4 Empty tournament returns `[]` without error — 5f76694

### Phase 5: Client Admin Section

#### Automated

- [x] 5.1 Type-check + build pass: `npm run build` — bb16d68
- [x] 5.2 Lint passes: `npm run lint` — bb16d68

#### Manual

- [x] 5.3 Non-admin: no Admin link; `/admin/*` redirects to `/app` — bb16d68
- [x] 5.4 Allowlisted admin: Admin link appears in shell — bb16d68
- [x] 5.5 Admin Tournament CRUD + Publish toggle: non-admin sees only Published — bb16d68
- [x] 5.6 Edit form disables `ExternalApiId` — bb16d68
- [x] 5.7 Delete on tournament with no leagues succeeds; with leagues → 409 message — bb16d68
- [x] 5.8 Tournament detail Ingest button populates counts + matches/events list (scorer + team) — bb16d68
- [x] 5.9 Players list paginates; create + edit work; nationality dropdown shows seeded list — bb16d68
- [x] 5.10 CSV preview → commit flow works; re-upload idempotent; tournamentId binding writes TournamentSquads — bb16d68
