# Custom Scoring Rules (S-04) Implementation Plan

## Overview

Turn a league's scoring config from write-once-at-create into an organizer-editable, **selectable** rule set: the organizer picks which `ScoringParameter`s count and what each is worth, and can change that until the tournament's first match kicks off — after which the config is frozen so nobody retunes points against known results.

This slice configures the product wedge. It writes no scoring engine (S-07 owns that) and adds no schema.

## Current State Analysis

S-03 (`organizer-create-league`) shipped the scoring config as a create-time-only, **complete** set:

- `POST /api/leagues` requires every `ScoringParameter` exactly once, points 0–1000, where `0` means "does not score" (`src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:184-210`).
- `ScoringRule` rows persist per league with a unique index on `(LeagueId, Parameter)` (`src/server/PredictionLeague.Infrastructure/Persistence/Configurations/ScoringRuleConfiguration.cs:16`). The entity has exactly `Id`, `LeagueId`, `Parameter`, `Points`.
- `LeagueDetailPage.tsx:113-133` renders the rules read-only; its header comment states outright that "Rule editing arrives in S-04".
- `ILeagueRepository` has no update path — only `ListForUserAsync`, `GetWithDetailAsync`, `InviteCodeExistsAsync`, `CreateAsync`.
- `GetWithDetailAsync` is `AsNoTracking()` (`LeagueRepository.cs:31-36`), so it cannot back a write.

Constraints discovered:

- **No predictions exist.** `Prediction` is deliberately outside `AppDbContext` (S-06 owns it, see the class comment in `AppDbContext.cs`). So an edit today invalidates nothing — but the lock must exist now so S-06/S-07 inherit a rule that already holds.
- **`Match.KickoffUtc` is already the lock instant** for predictions (FR-010, comment at `Match.cs`), so reusing it for the scoring freeze keeps one notion of "the tournament has started".
- **lessons.md — "Persistence exception types must not reach the Api layer"**: the rule-set write must go behind `ILeagueRepository`; the controller gets no EF Core reference.
- **`ScoringParameter` is append-only** with persisted int ordinals — this slice does not touch the enum.

## Desired End State

A signed-in organizer opens their league's detail page and sees the Scoring card with an **Edit** button. Editing shows one row per `ScoringParameter` with an active toggle and a points input; only active parameters are submitted, and each must be worth 1–1000. Saving replaces the league's rule set and the read view immediately shows only the active rules.

Once any match in the league's tournament has kicked off, the Edit button is gone and a short notice explains the config is locked; the API rejects the write independently of the UI.

Members (non-organizers) see the same read-only table and never see an edit affordance; the API returns 403 if they try.

Verify by: creating a league with a partial rule set, editing it, confirming the read view and a fresh `GET` both reflect the change, then confirming a league whose tournament has a past-kickoff match cannot be edited from either the UI or a direct `PUT`.

### Key Discoveries:

- `ValidateScoringRules` (`LeaguesController.cs:184`) is already the single validation seam for the rule set — both routes can share it after its completeness check is replaced.
- `ToDetailResponse` (`LeaguesController.cs:212`) is the single response shaper — the lock flag added there covers create, get, and update responses at once.
- `MatchRepository.ListByTournamentAsync` already filters `Matches` by `TournamentId`, so the kickoff probe is a one-line `AnyAsync` on the same `DbSet`.
- Client scoring inputs are already generated from `SCORING_DEFAULTS` (`src/client/src/leagues/types.ts:36`), so adding an active toggle is a change to one loop, not per-parameter markup (`LeagueFormPage.tsx:120-135`).
- `apiFetch` already serializes JSON bodies, sends cookies, and handles 204 (`src/client/src/lib/api.ts:30-81`) — PUT needs no client plumbing.

## What We're NOT Doing

- **No scoring engine.** Nothing consumes the rules to award points; that is S-07 (`scoring-engine-standings`).
- **No tests.** The repo has no test project and this slice does not add one — verification is manual (explicit user decision; roadmap OQ #3 stays open for S-07).
- **No schema or enum change.** No migration ships with this slice. `ScoringParameter` members, `ScoringRule` columns, and the `(LeagueId, Parameter)` unique index are untouched.
- **No data migration of existing leagues.** Leagues created under S-03 keep their six rows, zero-point ones included, until an organizer saves an edit. Pre-S-06 nothing scores, so the mixed shape is inert.
- **No league rename/delete, no invite/join, no predictions.** Still S-05 / S-06.
- **No rule-change history or audit trail.** An edit overwrites; there is no record of the previous config.
- **No transfer of organizer role.** `OrganizerUserId` is the single editor; co-organizers are not modelled.

## Implementation Approach

Two phases mirroring S-03's split (server, then client) — but unlike S-03, **the two phases are not independently shippable**. Phase 1 tightens the points floor to 1, which the shipped create form violates on every submit (it posts three parameters at 0). The phase boundary is a verification gate, not a ship gate: land both phases together in one PR, or keep Phase 1's commit unpushed until Phase 2 is verified.

The contract change — a rule set goes from *complete* to *partial* — lands on `POST` and the new `PUT` together, so there is exactly one definition of a valid config. "Not scored" is represented by **the absence of a row**; `Points = 0` stops being meaningful and is rejected, which removes the ambiguity that made selectable parameters worth having.

The lock is derived, not stored: a league's scoring is locked iff any `Match` in its tournament has `KickoffUtc <= now`. Nothing to migrate, nothing to keep in sync, and a league created after a tournament has begun is locked from birth — correct by construction.

## Critical Implementation Details

**Delete-and-reinsert collides with the unique index.** `(LeagueId, Parameter)` is unique, and EF Core does not guarantee that a `DELETE` for a row is batched ahead of the `INSERT` of a new row with the same key in one `SaveChangesAsync`. Toggling a parameter off and on, or simply re-saving an unchanged parameter, would then hit error 2601. The replace must therefore **reconcile in place**: update `Points` on rules whose parameter is still active, `Remove` rules whose parameter is no longer active, and `Add` only genuinely new parameters. No row is ever deleted and re-added with the same `(LeagueId, Parameter)` in a single save.

## Phase 1: Server — partial rule contract + scoring update endpoint

### Overview

Replace the completeness contract with a partial one, add the organizer-only `PUT` that replaces a league's rule set, derive the kickoff lock, and surface it on every league response.

### Changes Required:

#### 1. Kickoff probe

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/IMatchRepository.cs`

**Intent**: Let the Application layer ask whether a tournament has begun, without the controller knowing anything about matches beyond that question.

**Contract**: `Task<bool> AnyKickedOffAsync(Guid tournamentId, DateTimeOffset asOf, CancellationToken cancellationToken = default)` — true when at least one `Match` in the tournament has `KickoffUtc <= asOf`. `asOf` is a parameter rather than an internal `UtcNow` so the caller owns the clock.

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/MatchRepository.cs`

**Intent**: Implement the probe as a single existence query.

**Contract**: `Set.AnyAsync(m => m.TournamentId == tournamentId && m.KickoffUtc <= asOf, ...)`.

#### 2. League repository — tracked read + rule-set replace

**File**: `src/server/PredictionLeague.Application/Abstractions/Persistence/ILeagueRepository.cs`

**Intent**: Add the two members the update path needs, keeping all persistence behaviour behind the interface per the lessons.md boundary rule.

**Contract**:
- `Task<League?> GetForUpdateAsync(Guid leagueId, CancellationToken cancellationToken = default)` — the league **tracked**, with `ScoringRules` and `Memberships` included. Documented as the write-side counterpart of `GetWithDetailAsync`, which is `AsNoTracking` and must not back a write.
- `Task ReplaceScoringRulesAsync(League league, IReadOnlyList<ScoringRule> rules, CancellationToken cancellationToken = default)` — reconciles the tracked league's rule set to exactly `rules` and saves once. Takes a tracked `League` returned by `GetForUpdateAsync`.

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/LeagueRepository.cs`

**Intent**: Implement both. The replace reconciles in place — see Critical Implementation Details; a delete-then-add of the same `(LeagueId, Parameter)` is what this method exists to avoid.

**Contract**: `GetForUpdateAsync` mirrors `GetWithDetailAsync` minus `AsNoTracking()`. `ReplaceScoringRulesAsync` matches incoming rules to `league.ScoringRules` by `Parameter`: update `Points` where matched, remove unmatched existing rules, add unmatched incoming parameters with a fresh `Guid` and the league's `Id`, then one `Context.SaveChangesAsync`. Provider knowledge stops in this file.

Two mechanics worth stating, because the obvious reach for each is wrong:
- Removal goes through `Context.Set<ScoringRule>().Remove(...)`. `BaseRepository<League>.Set` is a `DbSet<League>`, so `Set.Remove(rule)` does not compile; and removing from the `league.ScoringRules` navigation would lean on EF's orphan-delete behaviour for a required cascade relationship that has no inverse navigation (`LeagueConfiguration.cs:20-24`). Be explicit instead.
- The incoming `ScoringRule` instances are read as **values** — `Parameter` and `Points` only — and are never attached to the context. Attaching them is precisely what would produce the delete-and-reinsert collision the Critical Implementation Details section exists to prevent.

#### 3. Rule-set validation — completeness out, non-empty in

**File**: `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs`

**Intent**: Change `ValidateScoringRules` from "every parameter exactly once, points 0–1000" to "at least one parameter, each known and distinct, points 1–1000". Both `Create` and the new update call it unchanged, so the two routes cannot drift.

**Contract**: `MinPointsPerRule = 1` joins the existing `MaxPointsPerRule = 1000`. The `missing`/`expected` completeness check is deleted. Rejections stay `Problem(...)` 400s with a human-readable `detail`, matching the existing style:
- empty or null set → "At least one scoring rule is required."
- unknown parameter → existing message, unchanged
- duplicate parameter → existing message, unchanged
- out-of-range points → "Points must be between 1 and 1000." (0 is no longer a way to say "does not score" — leave the parameter out instead)

Update the method's leading comment: the invariant it now guards is *non-empty and distinct*, not *complete*.

#### 4. Lock flag on the league responses

**File**: `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs`

**Intent**: Tell the client whether scoring may still be edited, so the UI hides the affordance instead of discovering the rule via a failed request.

**Contract**: `LeagueDetailResponse` gains `bool IsScoringLocked` (place it next to `IsOrganizer`). `ToDetailResponse` takes the flag as a parameter — it has no repository access — and every caller (`Get`, `Create`, the new update) computes it via `IMatchRepository.AnyKickedOffAsync(league.TournamentId, DateTimeOffset.UtcNow, ct)`. `LeagueSummaryResponse` is left alone; the list view has no edit affordance.

`LeaguesController` takes `IMatchRepository` as a fourth constructor dependency.

#### 5. The update endpoint

**File**: `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs`

**Intent**: Let the organizer replace their league's scoring config while the tournament has not started.

**Contract**: `PUT api/leagues/{id:guid}/scoring-rules`, body `UpdateScoringRulesRequest(IReadOnlyList<ScoringRuleDto> ScoringRules)`, returning `LeagueDetailResponse` (200) so the client can re-render from the server's own view. Status ladder, in evaluation order:

- no user id → 401 (matches the existing `CurrentUserId()` guard)
- league missing, **or** caller is neither organizer nor member → 404 (preserves the no-information-leak rule stated in the controller's header comment)
- caller is a member but not the organizer → **403** — the league is legitimately visible to them, so masking as 404 would be a lie
- tournament has a kicked-off match → **409 Conflict**, detail "Scoring rules are locked once the tournament has started." (a state conflict, not malformed input)
- validation failure → 400 via the shared validator
- otherwise → replace and return the refreshed detail

Read the league via `GetForUpdateAsync`. The tournament name for the response comes from `ITournamentRepository.GetByIdAsync`, as in `Get`.

#### 6. Request samples

**File**: `src/server/PredictionLeague.Api/PredictionLeague.http`

**Intent**: With no test project, this file is the slice's only repeatable harness — the manual steps below fire malformed and wrong-user requests through it.

**Contract**: Add a `PUT {{host}}/api/leagues/{id}/scoring-rules` sample alongside the existing league requests: one valid partial set, one empty array (expect 400). Update the existing `POST /api/leagues` sample to a partial rule set so it does not encode the retired all-six contract.

### Success Criteria:

#### Automated Verification:

- Server builds: `cd src/server && dotnet build`
- No new migration is generated (schema unchanged): `cd src/server/PredictionLeague.Api && dotnet ef migrations has-pending-model-changes` reports none
- `LeaguesController.cs` still has no `Microsoft.EntityFrameworkCore` import (lessons.md boundary rule)

#### Manual Verification:

- `POST /api/leagues` accepts a two-parameter rule set and rejects an empty one, a duplicate, and `Points = 0`
- `PUT /api/leagues/{id}/scoring-rules` as the organizer replaces the set; a follow-up `GET` returns exactly the new rules
- Toggling a parameter off and back on across two successive `PUT`s saves cleanly (no unique-index error)
- A member of the league gets 403; a user in neither role gets 404; an unknown id gets 404
- A league whose tournament has a match with a past `KickoffUtc` returns 409 on `PUT` and `isScoringLocked: true` on `GET`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Client — selectable rules on create, inline edit on detail

### Overview

Give both league screens one shared, toggle-aware scoring fieldset, and turn the detail page's Scoring card into an organizer-only edit surface that respects the lock.

### Changes Required:

#### 1. Shared types

**File**: `src/client/src/leagues/types.ts`

**Intent**: Mirror the server's new response field and stop implying that every parameter is always present.

**Contract**: `LeagueDetailResponse` gains `isScoringLocked: boolean`. `SCORING_DEFAULTS` keeps its shape and remains the display order and prefill source, but its comment changes: it is no longer a completeness contract — it is the catalogue of selectable parameters. Add a default-active notion so the create form still opens with a sensible config (the three parameters S-03 defaulted to non-zero: exact score, correct outcome, correct goal scorer).

#### 2. Shared scoring fieldset

**File**: `src/client/src/components/leagues/ScoringRulesFieldset.tsx` (new)

**Intent**: One component owning the "pick parameters and their points" interaction, used by both the create form and the detail page's edit mode — per the client AGENTS.md rule that feature components live under `components/<feature>/`, one per file.

**Contract**: Props: the current rule set (parameter → points for active parameters only), an `onChange` handler, and a `disabled` flag for the in-flight save. Renders one row per `SCORING_DEFAULTS` entry: a checkbox bound to active state plus a number input (`min=1`, `max=1000`) that is disabled when the parameter is inactive. Exposes the active set as `ScoringRuleDto[]` for submission. Deactivating a parameter drops it from the submitted set; its points are not preserved across a toggle (no-row-means-not-scored is the model).

No `checkbox` primitive is vendored in `components/ui/` — use a native `<input type="checkbox">` styled with Tailwind rather than adding a shadcn dependency for one control.

#### 3. Create form uses the fieldset

**File**: `src/client/src/routes/leagues/LeagueFormPage.tsx`

**Intent**: Replace the inline six-input loop with the shared fieldset and submit only active rules.

**Contract**: Local state becomes the active rule set rather than a full `Record<ScoringParameter, number>`. The submit body's `scoringRules` is the fieldset's active set. Client-side, block submit when the set is empty rather than relying on the server 400.

Also warn when the selected tournament has already started: `TournamentResponse.startDate` is already on the wire (`src/client/src/admin/types.ts:8`), so no server change is needed. Word it as a heads-up, not a claim about current state — the lock derives from `Match.KickoffUtc`, not `StartDate`, and the two can legitimately disagree. Something like "This tournament has already started — once its first match kicks off, the scoring rules are locked and cannot be changed." Creation itself is **not** blocked; starting a pool mid-tournament stays legal.

#### 4. Detail page — edit mode and lock state

**File**: `src/client/src/routes/leagues/LeagueDetailPage.tsx`

**Intent**: Make the Scoring card the edit surface for the organizer while the config is unlocked, and explain the freeze once it is not.

**Contract**: The Scoring card gains a mode toggle. Read mode is the existing table, now rendering only the rules the server returned (already the case — the server simply returns fewer). Edit mode renders `ScoringRulesFieldset` with Save / Cancel; Save issues `PUT /api/leagues/{id}/scoring-rules` via `apiFetch`, replaces local league state with the returned `LeagueDetailResponse`, and returns to read mode. Errors surface inline through the existing `ApiError` → `problem.detail` pattern used by `LeagueFormPage.tsx:63-72`.

**Hydration**: when seeding edit state from `league.scoringRules`, treat any rule with `points < 1` as **inactive**. Leagues created under S-03 carry all six rows with three typically at 0; loading those as active-with-0 would open the form already invalid under the new floor and force the organizer to repair rows they never set.

The Edit button renders only when `league.isOrganizer && !league.isScoringLocked`. When `isScoringLocked` is true, show a short muted line in the card instead — "Scoring is locked because the tournament has started." Update the header comment: rule editing has arrived; only join-by-code is still S-05.

Keep the card readable — extract the edit body if `LeagueDetailPage` starts carrying two full render trees inline.

### Success Criteria:

#### Automated Verification:

- Client builds (type errors fail the build): `cd src/client && npm run build`
- Lint passes: `cd src/client && npm run lint`

#### Manual Verification:

- Creating a league with only two parameters active succeeds; the detail page shows exactly those two rows
- The create form blocks submit when every parameter is deselected
- As the organizer, edit the rules, save, and see the table update without a page reload; reloading shows the same values
- A points input rejects 0 and values above 1000
- A member (not organizer) sees no Edit button on a league they belong to
- On a league whose tournament has started, the Edit button is absent and the lock notice is shown
- Opening Edit on a pre-S-04 league shows its zero-point parameters as inactive, not as invalid inputs
- Picking a tournament whose `startDate` has passed shows the create-form warning, and creation still succeeds

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

No automated tests — the repo has no test project in either unit and this slice deliberately does not add one (explicit decision; roadmap OQ #3 remains open and is inherited by S-07, which is where the scoring engine and its correctness harness belong).

### Manual Testing Steps:

1. Start the API on its **https** profile (`:7182`) and the SPA dev server (`:5173`) — the `Secure;SameSite=None` cookie requires both (client AGENTS.md).
2. Sign in, create a league against a published tournament with only *Exact score* and *Correct outcome* active. Confirm the detail page shows two rows.
3. Edit: deactivate *Correct outcome*, activate *Correct goal scorer* at 4 points, save. Confirm the table shows two rows with the new values, then reload and confirm again.
4. Edit again, re-activating *Correct outcome* — confirms the in-place reconcile does not trip the `(LeagueId, Parameter)` unique index.
5. Try to save with every parameter deactivated (blocked client-side) and with a `PUT` carrying an empty array via `PredictionLeague.http` (400 from the server).
6. As a second signed-in user who is a member of the league, confirm no Edit button and a 403 from a direct `PUT`. As a third user in neither role, confirm 404.
7. Using the admin match screens, give the league's tournament a match with a kickoff in the past. Reload the league: Edit is gone, the lock notice shows, and a direct `PUT` returns 409.
8. Open a league created before this slice (six rows including zeros): confirm it still renders, and that saving one edit collapses it to the active rules only.

## Performance Considerations

Each league detail read now costs one extra `EXISTS` query against `Matches` filtered by `TournamentId`. At friend-group scale this is immaterial; the list endpoint is deliberately left untouched so it does not become N+1 in the tournament count.

## Migration Notes

No schema migration. No data migration: pre-existing leagues keep their zero-point rows until an organizer saves an edit, which replaces the set with active rules only. Because `Prediction` is not yet in the model, no scoring has ever consumed these rows, so the mixed shape has no downstream effect. Phase 2's edit form treats those zero-point rows as inactive on load, so the legacy shape never surfaces as a validation error.

**Deploy the two phases together.** Phase 1 alone rejects what the shipped create form sends (three parameters at 0 points), so a server deployed without the matching client breaks league creation outright. This is a phase-ordering constraint, not a data one — there is no partial state to strand.

Roll back by reverting the commits — there is no persisted state that a revert would strand.

## References

- Roadmap slice S-04: `context/foundation/roadmap.md:161-172`
- PRD FR-008: `context/foundation/prd.md:73`
- Prior slice (S-03) plan and scope handoff: `context/archive/2026-08-03-organizer-create-league/plan.md:55-61`
- Boundary rule this plan honours: `context/foundation/lessons.md:25-30`
- Existing rule validation to modify: `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:184`
- Existing scoring UI to extract: `src/client/src/routes/leagues/LeagueFormPage.tsx:114-137`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Server — partial rule contract + scoring update endpoint

#### Automated

- [x] 1.1 Server builds: `cd src/server && dotnet build` — f1f7d61
- [x] 1.2 No new migration is generated (schema unchanged) — f1f7d61
- [x] 1.3 `LeaguesController.cs` still has no `Microsoft.EntityFrameworkCore` import — f1f7d61

#### Manual

- [x] 1.4 `POST /api/leagues` accepts a partial set and rejects empty / duplicate / zero-point rules — f1f7d61
- [x] 1.5 `PUT /api/leagues/{id}/scoring-rules` as organizer replaces the set; `GET` returns the new rules — f1f7d61
- [x] 1.6 Toggling a parameter off and back on across two `PUT`s saves cleanly — f1f7d61
- [x] 1.7 Member gets 403; non-member gets 404; unknown id gets 404 — f1f7d61
- [x] 1.8 Started tournament returns 409 on `PUT` and `isScoringLocked: true` on `GET` — f1f7d61

### Phase 2: Client — selectable rules on create, inline edit on detail

#### Automated

- [x] 2.1 Client builds: `cd src/client && npm run build` — 4bc2b2a
- [x] 2.2 Lint passes: `cd src/client && npm run lint` — 4bc2b2a

#### Manual

- [x] 2.3 Creating a league with two active parameters shows exactly those two rows — 4bc2b2a
- [x] 2.4 Create form blocks submit when every parameter is deselected — 4bc2b2a
- [x] 2.5 Organizer edits rules, saves, table updates; reload shows the same values — 4bc2b2a
- [x] 2.6 Points input rejects 0 and values above 1000 — 4bc2b2a
- [x] 2.7 A member sees no Edit button on a league they belong to — 4bc2b2a
- [x] 2.8 Started tournament: Edit button absent, lock notice shown — 4bc2b2a
- [x] 2.9 Pre-S-04 league opens Edit with zero-point parameters inactive, not invalid — 4bc2b2a
- [x] 2.10 Create form warns on a started tournament and still allows creation — 4bc2b2a
