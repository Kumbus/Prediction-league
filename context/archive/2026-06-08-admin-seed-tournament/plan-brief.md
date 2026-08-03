# S-02 Admin Seed Tournament — Plan Brief

> Full plan: `context/changes/admin-seed-tournament/plan.md`

## What & Why

Build the admin-facing surface around the already-shipped F-03 ingest so a signed-in global admin can add a tournament, optionally upload a player roster from CSV, click "Ingest now" to pull fixtures + results + events through F-03, verify the data on screen, then publish the tournament — at which point it becomes visible to S-03 (organizer-creates-league) and everything downstream. This is the FR-003 surface and the FR-004/FR-005 verification surface in one slice.

## Starting Point

F-03 (`football-api-ingest`) and F-02 (`auth-oauth-scaffold`) both already shipped: the `Tournament` entity exists with `ExternalApiId` + `Season`; `IFixtureIngestService` upserts fixtures + events idempotently; `POST api/ingest/{tournamentId}` is already `[Authorize(Policy=AdminOnly)]`-gated; the `AdminOnly` policy + `IsGlobalAdmin` claim are wired. **No path exists to grant `IsGlobalAdmin`**, no tournament/player admin controllers exist, no Nationality entity, no player personal stats, no publish gate, and the SPA has no admin section. F-04 (Azure deploy) is also done — migrations must be additive and forward-only.

## Desired End State

An allowlisted admin signs in (auto-promoted via `Admin:Emails` config), creates a Draft tournament, optionally uploads a player CSV (with dry-run preview before commit), clicks Ingest now, sees the matches list populate with scorers + cards attributed to the right player and team, then toggles Publish — at which point non-admin signed-in users see the tournament in `GET /api/tournaments`. The verification page proves FR-005 visually.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Tournament CRUD shape | Full CRUD incl. delete (block when leagues reference it) | User explicitly asked for delete; FK guard prevents wedge data loss | Plan |
| List visibility | Auth users see Published; admins see all | S-03 will reuse the same endpoint without rework | Plan |
| Admin bootstrap | Config `Admin:Emails` allowlist; auto-promote on sign-in | Idempotent, no DB hand-edits, works for local + Google sign-in identically | Plan |
| Squad model | Explicit `TournamentSquad` table | User wants admin-controlled per-tournament rosters distinct from global nationality | Plan |
| Publish state | `Tournament.IsPublished` bool (Draft ↔ Published) | User explicitly added the gate; one bool, one filter | Plan |
| Player CRUD | List + create + edit (no delete) | MatchEvent FK is non-null; deletion needs orphan handling we don't want now | Plan |
| Delete cascade | Tournament → Matches → MatchEvents (Teams/Players global) | Clean semantics; blocks when leagues reference it (S-03 safe) | Plan |
| Player schema | DOB / Position / Height / NationalityId; **no career stats** | PRD scope check — career stats don't drive scoring and aren't in API-Football free tier | Plan |
| Nationality | New table seeded with ISO 3166-1 alpha-3 | Separates citizenship from "this nation has a tournament team" | Plan |
| Bulk import | CSV only, exact `(Name + NationalityCode)` match, dry-run preview → commit | Fuzzy matching silently mis-credits scoring (FR-005 break); one parser; spreadsheets save to CSV | Plan |
| Ingest UX | Per-tournament "Ingest now" button + matches/events read endpoint | Surfaces FR-005 evidence; one consistent gate; demoable | Plan |
| Existing dev-only ingest guard | Drop it | AdminOnly is now a real policy — guard is redundant | Plan |
| ExternalApiId post-create | Immutable | Changing it would orphan ingested data | Plan |

## Scope

**In scope:**
- New entities: `Nationality` (ISO 3166 seed), `TournamentSquad`, `PlayerPosition` enum
- Additive columns: `Tournament.IsPublished`, `Player.{DateOfBirth, Position, HeightCm, NationalityId}`
- One additive migration; filtered unique index on `Tournaments.ExternalApiId`; cascade Tournament → Matches
- `TournamentsController` (CRUD + publish toggle + matches read)
- `PlayersController` (list/create/edit + CSV import w/ dry-run)
- `NationalitiesController` (read-only)
- Admin bootstrap: `Admin:Emails` config + `EnsureAdminClaim` in AuthController sign-in paths
- IngestController dev-guard removal
- React admin section: `RequireAdmin` guard; tournament list/create/edit/detail-with-ingest; player list/create/edit; CSV upload with preview; AppShell nav link

**Out of scope:**
- Player career stats; Excel/JSON import; fuzzy matching; player delete
- Tournament soft-delete / Archived state
- API-Football squad pulls (quota burn)
- Team admin surface (deferred)
- Tests (no test project exists yet)
- Prod migration automation (forward-only + human-gated)
- Any S-03 league code

## Architecture / Approach

Bottom-up, like F-03:

```
Domain entities + enum
   ↓
Fluent configs + one additive migration + Nationality seed
   ↓
AdminOptions + IAdminEmailAllowlist + AuthController promotion seam
   ↓
Tournament/Player/Nationality repositories
   ↓
TournamentsController + PlayersController + NationalitiesController
   ↓
CsvHelperPlayerImporter (Application port, Infrastructure impl)
   ↓
React admin subtree (RequireAdmin guard, pages)
```

The ingest write-path stays untouched. We add read endpoints over what F-03 already writes and a thin admin shell on top.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain + migration + admin bootstrap | Nationality (seeded), Player additions, TournamentSquad, IsPublished, one additive migration, AdminOptions, AuthController promotion seam | Forgetting `RefreshSignInAsync` after promotion (claim doesn't refresh in same session) |
| 2. Tournament API + ingest guard cleanup | CRUD + publish toggle; drop dev-env 404 on IngestController | Delete-block check (Leagues table exists but empty for now) |
| 3. Player + Nationality API + CSV import | List/create/edit, nationalities read endpoint, CsvHelper-backed dry-run + commit, optional TournamentSquad binding | Conflict detection thoroughness (unknown code, ExternalPlayerId collision) |
| 4. Tournament matches read endpoint | GET /api/tournaments/{id}/matches with eager-loaded events for FR-005 evidence | None notable; pure read |
| 5. Client admin subtree | RequireAdmin guard; tournament + player pages; CSV preview/commit UI; AppShell nav | FormData branch in `apiFetch` (skip default JSON Content-Type) |

**Prerequisites:** F-02 (auth claim wiring), F-03 (ingest service), F-04 (deploy — already done). A real API-Football key in user-secrets for end-to-end verification.

**Estimated effort:** ~4–5 after-hours sessions across 5 phases.

## Open Risks & Assumptions

- **Tournament edit must immobilize `ExternalApiId`** — if mutable, an admin could orphan ingested data; plan freezes it post-create (UI disables, API ignores).
- **CSV parser idempotency** is load-bearing: dry-run and commit must produce the same row classification or the preview becomes misleading.
- **Admin bootstrap config drift** between dev and prod — change a name in App Service settings, redeploy slot, etc. Documented in the runbook to be written.
- **`TournamentSquad` doesn't constrain ingest** — events from API-Football for an unrostered player still get a minimal-create Player (per F-03). Acceptable per the user's flow (squad is admin intent, events are reality).
- **Tournament delete cascade** is configured at the FK level. If a future slice introduces a new entity referencing `Tournament`, the cascade behavior on that edge must be revisited.

## Success Criteria (Summary)

- Allowlisted admin signs in fresh → `isGlobalAdmin: true` in `GET /api/auth/me`; Admin nav visible.
- Admin creates a Draft tournament, uploads a CSV (preview → commit), clicks Ingest now → matches + events visible with correct scorer + team attribution.
- Toggle Published → non-admin sees it in the list; toggle back → non-admin no longer sees it.
- Delete blocked when leagues reference the tournament (409); otherwise cascades cleanly.
