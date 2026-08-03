# Organizer creates a league (S-03) Implementation Plan

## Overview

A signed-in user creates a private league bound to a published tournament, sets the league's
scoring values in the create form, and lands on a league page showing the invite code. The
creator is recorded both as `League.OrganizerUserId` and as a `LeagueMembership` row with role
`Organizer`. Roadmap S-03; PRD FR-006, FR-008 (partial), US-01.

## Current State Analysis

The persistence and auth foundations are already in place — this slice adds an API surface and
screens over an existing schema, not new tables.

- `League`, `LeagueMembership`, `ScoringRule` entities exist since F-01
  (`src/server/PredictionLeague.Domain/Entities/League.cs`, `ScoringRule.cs`). `League.InviteCode`
  is `required string`, `LeagueConfiguration.cs:15` puts a **unique index** on it and
  `HasMaxLength(32)`; both child collections cascade-delete from the league.
- `LeagueMembershipConfiguration.cs:14` already enforces one membership per `(LeagueId, UserId)`.
- `ILeagueRepository` carries only `AnyForTournamentAsync` — its own comment names
  `GetByInviteCodeAsync` as an S-03 addition. `BaseRepository<T>` supplies generic CRUD
  (`GetByIdAsync`, `AddAsync`, `Update`, `Remove`, `SaveChangesAsync`).
- **There is no `LeaguesController`** — the pre-F-01 `static List<League>` controller was deleted.
  This slice writes the first one.
- Auth is cookie-based Identity. Global admin is a claim-backed policy
  (`AuthorizationPolicies.AdminOnly`); organizer/member roles are deliberately **per-league** via
  `LeagueMembership` (`AuthorizationPolicies.cs:3-5`). S-03 is the first slice that writes those rows.
- Non-admins only ever see published tournaments (`TournamentsController.cs:69,79`) — the league
  tournament picker inherits that for free from `GET /api/tournaments`.
- Client has routing (`src/client/src/routes/index.tsx`), `RequireAuth`, `apiFetch` with
  `credentials: "include"`, and an established list/form page pattern under `routes/admin/`.
  `AppShell.tsx:31` is a placeholder that literally reads "League creation arrives in S-03."

## Desired End State

A signed-in user at `/app` can reach "My leagues", create a league against a published tournament
with six scoring values, and see the created league with its invite code and rule table. The same
league appears in their list on reload. Verified by: creating two leagues in the browser (distinct
invite codes), and confirming in SQL that each `Leagues` row has one `LeagueMemberships` row with
`Role = 0` (Organizer) and six `ScoringRules` rows.

### Key Discoveries:

- No migration is needed — every column this slice writes already exists in
  `20260530155119_InitialCreate`. Verify with `dotnet ef migrations has-pending-model-changes`.
- `ScoringParameter` (`Enums.cs:26`) is append-only with six members; iterate the enum rather than
  listing parameters by hand, so a future member surfaces in the form automatically.
- `MembershipRole.Organizer` is ordinal `0` (`Enums.cs:18`), persisted via `HasConversion<int>()`.
- Controller conventions to mirror from `TournamentsController.cs`: request/response `record`s
  nested in the controller, `Problem(detail:, statusCode:)` for validation failures,
  `CreatedAtAction` on create, `NotFound()` (not `Forbid()`) when hiding another user's resource.
- The current user's id is available as the `ClaimTypes.NameIdentifier` claim on `User`; the
  Identity user key is a `Guid` (F-01), so it parses directly — no `UserManager` round-trip needed.

## What We're NOT Doing

- **No join-by-invite-code flow** — S-05 owns `POST /api/leagues/join` and the member-side screens.
  S-03 only generates and displays the code.
- **No scoring-rule editing after create** — S-04 owns the edit surface. S-03 writes the initial
  rule rows once.
- **No league edit/rename/delete** — deliberately deferred (open question: what happens to
  memberships and predictions).
- **No standings, predictions, or match lists on the league page** — S-06/S-07.
- **No league-scoped authorization policy** — membership is checked inline in the controller;
  a reusable policy/handler is worth building only when several controllers need it.
- **No tests** — the repo has no test project, and this slice does not introduce one.

## Implementation Approach

Two phases split along the stack seam: the API first (verifiable through
`src/server/PredictionLeague.Api/PredictionLeague.http` without any UI), then the screens.

League creation is a single transactional unit: the `League`, its six `ScoringRule` rows, and the
organizer's `LeagueMembership` are built in memory and persisted with **one** `SaveChangesAsync`,
so a failure can never leave a league without its scoring config or its organizer.

## Critical Implementation Details

**Invite-code uniqueness.** `InviteCode` has a unique index, so a duplicate throws
`DbUpdateException` at save time, not at generation time. Generate, check for an existing row,
and retry a bounded number of times (5); on exhaustion return 503 rather than looping. Because
the check-then-insert is racy under concurrency, the save must also catch the unique-violation
`DbUpdateException` and retry the whole create once — the index is the real guarantee, the
pre-check is only an optimization.

## Phase 1: Server — League API

### Overview

Repository queries, invite-code generation, and a `LeaguesController` with create / list / detail.

### Changes Required:

#### 1. League repository queries

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/ILeagueRepository.cs`
**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/LeagueRepository.cs`

**Intent**: Give the controller the three reads it needs: the caller's leagues, one league with its
children, and an invite-code existence probe for the generator.

**Contract**: Add to `ILeagueRepository` (implemented in `LeagueRepository` over `Set`):

```csharp
Task<IReadOnlyList<League>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);
Task<League?> GetWithDetailAsync(Guid leagueId, CancellationToken cancellationToken = default);
Task<bool> InviteCodeExistsAsync(string inviteCode, CancellationToken cancellationToken = default);
```

`ListForUserAsync` returns leagues where `OrganizerUserId == userId` **or** any
`Memberships.UserId == userId`, ordered by `Name`, `AsNoTracking`. `GetWithDetailAsync` includes
`ScoringRules` and `Memberships` (membership check is the controller's job, so the query itself is
not user-scoped).

#### 2. Invite-code generator

**File**: `src/server/PredictionLeague.Application/Abstractions/Leagues/IInviteCodeGenerator.cs` (new)
**File**: `src/server/PredictionLeague.Infrastructure/Leagues/RandomInviteCodeGenerator.cs` (new)

**Intent**: Produce a short, human-transcribable code that S-05 can ask a friend to type, and keep
the retry-on-collision logic out of the controller.

**Contract**: `Task<string> GenerateAsync(CancellationToken cancellationToken = default)` — 8
characters drawn with `System.Security.Cryptography.RandomNumberGenerator` from the alphabet
`ABCDEFGHJKMNPQRSTUVWXYZ23456789` (no `I`, `L`, `O`, `0`, `1`). Probes `InviteCodeExistsAsync`
and retries up to 5 times; throws `InvalidOperationException` when exhausted. Well under the
`HasMaxLength(32)` cap.

#### 3. LeaguesController

**File**: `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs` (new)

**Intent**: The slice's API surface — create a league, list mine, open one.

**Contract**: `[ApiController] [Route("api/[controller]")] [Authorize]`. Nested records:

```csharp
public record ScoringRuleDto(ScoringParameter Parameter, int Points);
public record CreateLeagueRequest(string Name, Guid TournamentId, IReadOnlyList<ScoringRuleDto> ScoringRules);
public record LeagueSummaryResponse(Guid Id, string Name, Guid TournamentId, string TournamentName, bool IsOrganizer, int MemberCount);
public record LeagueDetailResponse(Guid Id, string Name, Guid TournamentId, string TournamentName, string InviteCode, bool IsOrganizer, int MemberCount, IReadOnlyList<ScoringRuleDto> ScoringRules);
```

Endpoints:

- `POST /api/leagues` — validates, builds `League` + six `ScoringRule` + one
  `LeagueMembership(Role.Organizer)`, one `SaveChangesAsync`, returns `CreatedAtAction(nameof(Get), …)`
  with `LeagueDetailResponse`.
- `GET /api/leagues` — `ListForUserAsync(currentUserId)` → `LeagueSummaryResponse[]`.
- `GET /api/leagues/{id:guid}` — `GetWithDetailAsync`; **404** when the league does not exist *or*
  the caller is neither organizer nor member (no information leak, mirroring the draft-tournament
  rule at `TournamentsController.cs:79`).

Validation, each returning `Problem(detail:, statusCode: 400)`:

- `Name` required, trimmed, ≤ 200 chars (matches `HasMaxLength(200)`).
- Tournament must exist **and** be published — a non-admin must not bind a league to a draft.
  Admins get the same rule: publishing is what makes a tournament leaguable.
- Every `ScoringParameter` member must appear exactly once in `ScoringRules`; unknown or duplicate
  parameters are rejected rather than silently dropped.
- `Points` in `0..1000`. Zero is legal and means "this parameter does not score".

The current user id comes from `ClaimTypes.NameIdentifier`; if it is missing or unparsable, return
`Unauthorized()`.

#### 4. DI registration

**File**: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs`

**Intent**: Register the generator alongside the existing repositories.

**Contract**: `services.AddScoped<IInviteCodeGenerator, RandomInviteCodeGenerator>();` next to the
importer registrations (`DependencyInjection.cs:45-46`).

#### 5. Sample requests

**File**: `src/server/PredictionLeague.Api/PredictionLeague.http`

**Intent**: Make Phase 1 verifiable before any UI exists.

**Contract**: Append a `### Leagues (S-03)` block: create (with a full six-rule body), list, get by id.

### Success Criteria:

#### Automated Verification:

- Server builds: `cd src/server && dotnet build`
- No schema drift — the slice adds no migration: `cd src/server/PredictionLeague.Api && dotnet ef migrations has-pending-model-changes` reports none

#### Manual Verification:

- `POST /api/leagues` against a published tournament returns 201 with an 8-character invite code
- SQL check: the new league has exactly 6 `ScoringRules` rows and 1 `LeagueMemberships` row with `Role = 0`
- Two consecutive creates produce different invite codes
- `POST` against a draft (unpublished) tournament returns 400
- `GET /api/leagues` as the creator lists the league; as a different signed-in user it does not
- `GET /api/leagues/{id}` as a non-member returns 404
- Anonymous calls to all three endpoints return 401

**Implementation Note**: After completing this phase and all automated verification passes, pause
for human confirmation that the manual checks passed before starting Phase 2.

---

## Phase 2: Client — league screens

### Overview

Three member-facing screens plus routing, replacing the `AppShell` placeholder.

### Changes Required:

#### 1. Shared types

**File**: `src/client/src/leagues/types.ts` (new)

**Intent**: Mirror the Phase 1 response shapes for the league screens, kept out of `admin/types.ts`
because these are member-facing.

**Contract**: `ScoringParameter` string-union matching the C# enum member names,
`ScoringRuleDto`, `LeagueSummaryResponse`, `LeagueDetailResponse`, plus a `SCORING_DEFAULTS`
constant used to prefill the form: `ExactScore 3`, `CorrectOutcome 1`, `CorrectGoalScorer 2`,
`CorrectCardCount 0`, `CorrectYellowCards 0`, `CorrectRedCards 0`.

#### 2. League list page

**File**: `src/client/src/routes/leagues/LeaguesListPage.tsx` (new)

**Intent**: The user's home for leagues — what they organize and what they've joined.

**Contract**: Route `/app/leagues`. `apiFetch<LeagueSummaryResponse[]>("/api/leagues")` in an
effect with loading / error / empty states, card per league linking to the detail page, "New league"
button routing to the form. Follows `TournamentsListPage.tsx` structurally.

#### 3. League create form

**File**: `src/client/src/routes/leagues/LeagueFormPage.tsx` (new)

**Intent**: Name + tournament + the six scoring values in one submit.

**Contract**: Route `/app/leagues/new`. Loads `GET /api/tournaments` for the tournament `<select>`
(the API already returns only published ones to non-admins; render an explicit "no tournaments
available yet" empty state rather than an empty dropdown). Six number inputs prefilled from
`SCORING_DEFAULTS`, generated by iterating the parameter list so a future enum member needs no new
markup. Submits `POST /api/leagues`, then navigates to `/app/leagues/{id}`. `ApiError.problem.detail`
is surfaced inline, not via `alert()`.

#### 4. League detail page

**File**: `src/client/src/routes/leagues/LeagueDetailPage.tsx` (new)

**Intent**: Confirm what was created and expose the invite code.

**Contract**: Route `/app/leagues/:id`. Shows name, tournament name, member count, the invite code
(with a copy-to-clipboard button), and a table of the six scoring rules. 404 renders a "league not
found or you're not a member" state, not a crash.

#### 5. Routing and shell entry

**File**: `src/client/src/routes/index.tsx`
**File**: `src/client/src/routes/AppShell.tsx`

**Intent**: Wire the three routes under the existing auth guard and remove the S-03 placeholder.

**Contract**: Add the three paths to the `RequireAuth` children block (alongside `/app`). Replace
the `AppShell` placeholder card (`AppShell.tsx:26-33`) with a link into `/app/leagues`, keeping the
existing header, admin link, and sign-out intact.

### Success Criteria:

#### Automated Verification:

- Client builds (type errors fail the build): `cd src/client && npm run build`
- Lint passes: `cd src/client && npm run lint`

#### Manual Verification:

- Signed-in user reaches "My leagues" from `/app` and creates a league end-to-end
- Scoring fields arrive prefilled; edited values are what the detail page shows afterwards
- The tournament dropdown lists only published tournaments
- Invite code is visible on the detail page and the copy button works
- Reload keeps the league in the list; a second browser profile (different user) does not see it
- Submitting an empty name surfaces the server's message inline

**Implementation Note**: Pause for human confirmation after the automated checks pass.

---

## Testing Strategy

No test project exists in either unit, so verification is the `.http` file for the API and the
browser for the UI. The checks worth walking deliberately:

### Manual Testing Steps:

1. Create a league against a published tournament — expect 201 and an 8-character code.
2. Repeat — expect a different code.
3. Attempt a create against a draft tournament — expect 400.
4. Sign in as a second user, `GET /api/leagues` — expect the first user's league to be absent, and
   `GET /api/leagues/{id}` on it to return 404.
5. In SQL: `SELECT * FROM ScoringRules WHERE LeagueId = …` returns six rows;
   `SELECT * FROM LeagueMemberships WHERE LeagueId = …` returns one row with `Role = 0`.

## Performance Considerations

Friend-group scale; no indexes beyond the existing ones are warranted. `ListForUserAsync` runs an
`OR` across `OrganizerUserId` and the memberships collection — fine at this volume, and the natural
place to revisit if league counts ever grow (S-05 makes memberships the dominant path).

## Migration Notes

None. Every column already exists in `20260530155119_InitialCreate`; the
`has-pending-model-changes` check in Phase 1 is what proves it.

## References

- Controller/validation pattern: `src/server/PredictionLeague.Api/Controllers/TournamentsController.cs:83-133`
- Repository pattern: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/TournamentRepository.cs`
- Per-league role rationale: `src/server/PredictionLeague.Infrastructure/Identity/AuthorizationPolicies.cs:3-5`
- Client list/form pattern: `src/client/src/routes/admin/tournaments/TournamentsListPage.tsx`
- Roadmap slice: `context/foundation/roadmap.md` (S-03)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Server — League API

#### Automated

- [x] 1.1 Server builds: `dotnet build`
- [x] 1.2 No schema drift: `dotnet ef migrations has-pending-model-changes`

#### Manual

- [ ] 1.3 POST /api/leagues returns 201 with an 8-character invite code
- [ ] 1.4 SQL: 6 ScoringRules rows + 1 LeagueMemberships row with Role = 0
- [ ] 1.5 Two creates produce different invite codes
- [ ] 1.6 POST against a draft tournament returns 400
- [ ] 1.7 GET /api/leagues is caller-scoped
- [ ] 1.8 GET /api/leagues/{id} as a non-member returns 404
- [ ] 1.9 Anonymous calls return 401

### Phase 2: Client — league screens

#### Automated

- [ ] 2.1 Client builds: `npm run build`
- [ ] 2.2 Lint passes: `npm run lint`

#### Manual

- [ ] 2.3 Create a league end-to-end from /app
- [ ] 2.4 Scoring fields prefilled and persisted as edited
- [ ] 2.5 Tournament dropdown lists only published tournaments
- [ ] 2.6 Invite code visible with working copy button
- [ ] 2.7 List survives reload and is not visible to another user
- [ ] 2.8 Empty name surfaces the server message inline
