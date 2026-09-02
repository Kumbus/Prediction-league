# Browser-level coverage for the prediction screen and per-league scoring divergence — Implementation Plan

## Overview

Stand up the E2E foundation this repository does not yet have — a fail-fast
preflight, per-role `storageState`, and an API-driven fixture graph — and use it
to land two Playwright specs: one proving a member can *read* the prediction
screen (test-plan risk #6), one proving two leagues on the same tournament do
not converge on the same total for an identical forecast (risk #2). Closes
`context/foundation/test-plan.md` §3 Phase 4.

## Current State Analysis

**Both features under test are built and routed.** `/10x-e2e`'s precondition
holds: `PredictionsPage`, `StandingsPage`, `LeagueFormPage` with per-league
`ScoringRulesFieldset`, and the admin match editor all exist
(`src/client/src/routes/index.tsx:22-63`).

**The screen already speaks in literal strings**, not in `disabled` attributes.
Every state risk #6 names has user-visible copy — `"Saved"`,
`"Locked at kickoff."`, `"You did not forecast this match."`, `" · N pts"` — so
a `getByText` assertion can state the risk directly ("the member can read what
happened") rather than asserting a control is inert.

**Scoring is synchronous with the admin write.** `TournamentsController.cs:260`
and `:299` call `ScoringTrigger.TryScoreAsync` inside the request that saves the
result, so points exist by the time the response lands. No polling, and the
single largest source of E2E flake in this scenario disappears.

**Test infrastructure is bare.** `src/client/playwright.config.ts` is five lines
— `testDir`, `baseURL`, `ignoreHTTPSErrors`. No `webServer`, no projects, no
`storageState`, no reporter, no global setup. One spec exists
(`tests/e2e/auth.spec.ts`) and **it is red**: `SignOutButton.tsx:16` calls
`navigate("/")`, but the button renders inside `AppShell` behind `RequireAuth`,
which sees `status === "anonymous"` first and redirects to `/sign-in`
(`RequireAuth.tsx:20`). `/` itself is public, so this is a render-ordering race,
not a routing rule. `/10x-e2e` models every generated test on the seed, so it
must be green before generation.

**Admin rights cannot be self-granted.** `AdminEmailAllowlist` hashes
`Admin:Emails` into an exact-match, case-insensitive `HashSet` at construction;
`AuthController.EnsureAdminClaimAsync` reconciles the flag on both register and
login. A per-run unique email can never be admin.

**Teardown through the API is impossible.** There is no `DELETE /api/leagues/{id}`,
and `DELETE /api/tournaments/{id}` returns **409 while any league references the
tournament** (`TournamentsController.cs:166-170`).

**CI runs no Playwright.** `deploy-backend.yml:61-62` runs the server suite only,
and only on push to `main`; the Static Web Apps workflow only builds the client.

## Desired End State

From `src/client`, with the API, the SPA dev server and SQL Server running,
`npm run e2e` runs green and:

- a member who has never seen the app can be shown, from the specs alone, what
  the prediction screen tells them in each of its states;
- deleting the per-league rule lookup in `MatchScoringService` turns the risk-#2
  spec red;
- rendering `awardedPoints` unconditionally (so an unscored row reads `· 0 pts`)
  turns the risk-#6 spec red;
- running with the app down fails in under five seconds with a message naming
  what to start, instead of a wall of navigation timeouts;
- `context/foundation/test-plan.md` §3 Phase 4 reads `complete`, §6.4 and §6.5
  are written, and the §5 e2e gate row honestly states "local, CI deferred to
  Phase 5".

### Key Discoveries:

- `MatchPredictionRow.tsx:210-226` — the locked-row copy is a **single `<p>`**
  holding both `Your forecast: 2–1` and the `· N pts` span. So
  `expect(page.getByText('Your forecast: 3–0')).not.toContainText('pts')` is
  correctly scoped to one row with **no `data-testid` and no product change**.
  The dash is an en-dash, U+2013.
- `MatchPredictionRow.tsx:92,105` — the two score inputs are `<Label htmlFor>`-ed
  with the **team names themselves**, so `getByLabel(homeTeamName)` scopes to a
  row whenever that row is editable.
- `MatchPredictionRow.tsx:55` — the row wrapper is a bare `<div>` with no role
  and the "X v Y" title is a `<div>`, not a heading. There is **no** accessible
  container to scope by; unique fixture team names and distinct per-match
  forecast scores are what make page-level text assertions unambiguous.
- `LeaguesController.cs:175-184` — creating a league seeds the organizer's own
  `LeagueMembership`. One member creating both leagues needs **no join step**;
  the invite-code path drops out of the fixture entirely.
- `LeaguesController.cs:143-147` — a league can only be created on a **published**
  tournament. `LeagueDetailResponse.IsScoringLocked` freezes rule config once the
  tournament's first match has kicked off.
- `Program.cs:15` registers `JsonStringEnumConverter` — the API fixture sends and
  receives enums as strings (`"ExactScore"`, `"Finished"`).
- `TeamsController.cs:34-38` — team names are globally unique (409 on duplicate),
  so fixture team names must carry the run's unique suffix.
- `PredictionsController.cs:331` — the entire kickoff rule is
  `match.KickoffUtc <= now`, with `now` captured once per request (`:156`,
  `:188`, `:295`). There is no injected clock and no `TimeProvider`: a test
  controls lock state **only** by choosing kickoff timestamps.
- `PredictionsController.cs:170-176` — the batch write answers **200 with
  per-item verdicts** (`Saved` / `Locked` / `Invalid`), never 4xx. Assertions
  must read the rendered verdict, never the HTTP status.
- `ScoringTrigger.cs:12-16` — a failed scoring run still answers **200** with
  `ScoringFailed` + `ScoringMessage`. "The write returned 200" is not proof that
  points appeared.
- `PredictionsController.GetRound` with no `round` query param selects the round
  holding the earliest unfinished match. Putting every fixture match in **one
  round** makes the default view deterministic without a query param.
- `StandingsPage.tsx:93-125` — a real `<table>` with `#`, `Member`, `Points`,
  `Matches scored`, so `getByRole('row')` / `getByRole('cell')` work; `(you)`
  marks the caller's row.
- `context/foundation/test-plan.md` §6.1 — the oracle constraint. An expected
  total is written as a **literal**, justified in a comment from the league's own
  rules, never computed by summing the rule list.

## What We're NOT Doing

- **No admin-UI coverage.** Test-plan §7 excludes admin screens deliberately;
  all fixture setup goes through the API.
- **No goal-scorer or card parameters.** They would need `TournamentSquad` and
  player fixtures plus `PUT /api/matches/{id}/events`, for rule surface the 83
  Phase-1 unit tests already cover in isolation.
- **No second member account.** Ties, rank skips and the multi-row table are
  risk #7, which test-plan assigns to Phase 3 (integration).
- **No server-verdict choreography.** The mid-save `Locked — kicked off` /
  `Rejected` verdicts are out of scope for this phase.
- **No teardown.** Unique per-run identifiers only; the dev database accumulates.
- **No `webServer`, no containers, no CI job.** The suite is a documented local
  gate; CI wiring is Phase 5 of the test plan.
- **No re-derivation of point arithmetic.** E2E proves the number survives the
  round trip to the screen; the engine's correctness is Phase 1's job.
- **No component/unit tests for the client.** Vitest remains unwired.

## Implementation Approach

Two identities. The **E2E admin** is a single stable allowlisted address that
global setup registers on first run and logs in on every run thereafter — the
only way past an exact-match allowlist without changing production security code.
The **member** is minted fresh per run with a unique email; it creates both
leagues (and is therefore auto-enrolled in both) and owns every forecast.

A Playwright `setup` project runs once, writes one `storageState` file per role,
builds the fixture graph through the API, and emits a manifest of ids. The spec
projects declare `dependencies: ['setup']` and load that manifest. The browser is
opened only on `/app/leagues/:id/predictions` and `/app/leagues/:id/standings` —
the two screens the risks actually live in.

**The graph: four matches in one round.**

| Match | Setup | Proves |
|---|---|---|
| M1 | future kickoff, unpredicted | editable inputs → `Save round` → `"Saved"` |
| M2 | predicted, then kickoff moved into the past, **no result ever** | `"Locked at kickoff."` + forecast + **no `pts`** |
| M3 | past kickoff, never predicted | `"You did not forecast this match."` |
| M4 | predicted, kickoff past, result entered | `· N pts`, and risk #2's divergence |

M4 is the only scored match and the only one both specs read; M1 is the only one
a spec mutates. That satisfies the shared-graph constraint: a spec that writes
owns a match no other spec reads.

**Two leagues, same tournament, contrasting rules.** League A scores
`ExactScore` only; League B scores `CorrectOutcome` only. The member files the
*same* forecast in both. One admin result makes the two totals diverge, and each
total is asserted as a literal against its own league's configuration.

## Critical Implementation Details

**Fixture ordering is load-bearing, and the obvious order is wrong.** The graph
must be built as: create tournament → **publish it** → create the two leagues
with their rules → create teams and matches → file forecasts → *then* move
kickoffs into the past and enter M4's result. A league cannot be created on an
unpublished tournament (`LeaguesController.cs:143-147`), and rule configuration
freezes once the tournament's first match has kicked off, so M3 (whose kickoff is
already in the past) must be created **after** both leagues exist. A forecast can
only be filed while its match is still open, so M2's and M4's kickoffs move into
the past only after their predictions are written.

**"Finished but unscored" is a kicked-off match with no result, not a ruleless
league.** A league with no matching rules scores integer `0`, which renders
`· 0 pts` — the exact ambiguity the risk is about. Only a match whose result has
never been entered leaves `AwardedPoints` null and produces the silence.

**The sign-out fix is an unmount-ordering fix, not a routing change.** Navigating
to `/` *before* awaiting `signOut()` unmounts the `RequireAuth` subtree, so its
redirect never fires. `AuthContext.signOut` swallows its own errors and always
sets `anonymous` (`AuthContext.tsx:30-36`), so navigating first cannot strand an
authenticated user on the landing page.

---

## Phase 1: Foundation and the seed

### Overview

Make the seed spec green, give the suite a config worth extending, and make the
admin identity reachable. Nothing here tests a risk; everything here is a
precondition for phases 2–4.

### Changes Required:

#### 1. The sign-out race

**File**: `src/client/src/auth/SignOutButton.tsx`

**Intent**: Honour the intent the component already declares — land on `/` after
sign-out — so a member who clicks "Sign out" is not bounced to a sign-in form.

**Contract**: The click handler's ordering. `navigate("/", { replace: true })`
must take effect before `AuthContext.signOut()` flips `status` to `anonymous`,
because `RequireAuth` is an ancestor of this button and renders
`<Navigate to="/sign-in" replace />` the moment it observes that status. Keep the
`pending` guard against double-clicks; it must not set state on an unmounted tree.

#### 2. Playwright configuration

**File**: `src/client/playwright.config.ts`

**Intent**: Replace the five-line config with one that can carry projects, a
preflight, and per-role authentication — the shape phases 2–4 depend on.

**Contract**: Keeps `baseURL: https://localhost:5173` and
`ignoreHTTPSErrors: true`. Adds `globalSetup` pointing at the preflight; a
`setup` project matching `tests/e2e/**/*.setup.ts`; an `e2e` project matching
`tests/e2e/**/*.spec.ts` with `dependencies: ['setup']`; `fullyParallel`, a
`list` reporter, and `forbidOnly` under CI. Explicitly **no** `webServer`.

#### 3. Preflight

**File**: `src/client/tests/e2e/global-setup.ts`

**Intent**: Turn "the app is not running" from a wall of navigation timeouts into
one sentence naming what to start.

**Contract**: Exported default async function. Requests the SPA `baseURL` and the
API's `/api/auth/me` (expecting 401 when anonymous — a *reachable* API, not an
authorised one), each with a short timeout, and throws a message naming the two
commands (`dotnet run` in `src/server`, `npm run dev` in `src/client`) and the
database when either is unreachable. It must not fail merely because the API
answers 401.

#### 4. The E2E admin identity

**File**: `src/server/PredictionLeague.Api/appsettings.Development.json`

**Intent**: Put one stable address on the admin allowlist so a fixture can log in
as an admin without changing `AdminEmailAllowlist`.

**Contract**: Adds an `Admin:Emails` array containing the single E2E admin
address. Development-only configuration; it grants nothing without the account's
password, and user-secrets continue to override it for real admins. The same
address is referenced by name from the fixture module in Phase 2 — defined in
exactly one place and imported, never retyped.

#### 5. Project E2E rules

**File**: `CLAUDE.md`

**Intent**: Extend the existing E2E rules block with the conventions specific to
this suite, so `/10x-e2e` generates tests that match it instead of inventing new
patterns.

**Contract**: Adds, under the existing hard rules: authentication comes from
`storageState` produced by the setup project, never from a UI login inside a
spec; fixture ids come from the manifest, never hardcoded; expected point totals
are literals justified from the league's rules per test-plan §6.1; the locked-row
dash is U+2013. Does not restate the rules already present.

#### 6. Ignore generated artifacts

**File**: `.gitignore`

**Intent**: Keep per-run auth state and the fixture manifest out of version
control.

**Contract**: The auth directory is already covered by `**/playwright/.auth/`;
add the manifest directory and Playwright's `test-results/` and
`playwright-report/` under `src/client/`.

### Success Criteria:

#### Automated Verification:

- Client type-check and lint pass: `npm run build` and `npm run lint` in `src/client`
- The seed spec passes: `npm run e2e -- tests/e2e/auth.spec.ts` in `src/client`
- The preflight aborts with its named message when the API is stopped, in under 10 seconds
- Server builds with the new configuration: `dotnet build src/server/prediction-league.slnx`

#### Manual Verification:

- Clicking "Sign out" in the running app lands on the landing page, not on `/sign-in`
- Signing in as the E2E admin address shows the admin navigation (the allowlist entry took effect)
- `git status` shows no auth state or manifest files staged

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human before
proceeding to the next phase.

---

## Phase 2: Fixture layer

### Overview

One setup run produces everything the specs need: two authenticated storage
states and a four-match, two-league graph described by a manifest. No spec in
phases 3–4 talks to the API directly.

### Changes Required:

#### 1. API client for fixtures

**File**: `src/client/tests/e2e/fixtures/api.ts`

**Intent**: A thin, typed wrapper over exactly the endpoints the fixture drives,
so the graph builder reads as a sequence of domain steps rather than URL strings.

**Contract**: Functions over a Playwright `APIRequestContext`: `register`,
`login`, `createTournament`, `publishTournament` (`PATCH /api/tournaments/{id}/publish`),
`createTeam`, `createMatch` (`POST /api/tournaments/{id}/matches`), `updateMatch`
(`PUT /api/matches/{matchId}`), `createLeague` (`POST /api/leagues`), and
`submitPredictions` (`POST /api/leagues/{leagueId}/predictions`). Request bodies
match the controller records named in Key Discoveries; enums are sent as strings.
Every function asserts the response is OK and, for the two match-write calls,
additionally asserts `scoringFailed` is false — a 200 carrying `ScoringFailed` is
a failed scoring run, and setup must not proceed on one.

#### 2. Run identity and shared constants

**File**: `src/client/tests/e2e/fixtures/run.ts`

**Intent**: One place that owns the run's unique suffix, the E2E admin address,
and the paths of the storage-state files and the manifest.

**Contract**: Exports a `runId` of the form `e2e-<timestamp>-<random>`, the admin
email and password, `adminStatePath` / `memberStatePath` under
`playwright/.auth/`, and `manifestPath`. Every fixture name is derived from
`runId` so team names (globally unique per `TeamsController.cs:34-38`) cannot
collide across runs.

#### 3. Authentication setup

**File**: `src/client/tests/e2e/auth.setup.ts`

**Intent**: Produce the two `storageState` files, registering the admin only if
it does not exist yet.

**Contract**: Admin: attempt `POST /api/auth/register`; on a validation failure
naming a duplicate email, fall back to `POST /api/auth/login`. Both paths run
`EnsureAdminClaimAsync`, so either yields an admin session. Asserts
`GET /api/auth/me` returns `isGlobalAdmin: true` — if the allowlist entry from
Phase 1 is missing, this is where the suite must fail, with a message saying so,
rather than 403-ing deep inside graph construction. Member: registers a fresh
`runId`-derived account. Saves both contexts to their state paths.

#### 4. Fixture graph

**File**: `src/client/tests/e2e/fixture.setup.ts`

**Intent**: Build the four-match, two-league graph and write the manifest.

**Contract**: Depends on the auth states from the previous file. Executes in the
order fixed by Critical Implementation Details: tournament → publish → League A
(`ExactScore`) and League B (`CorrectOutcome`) → four uniquely-named teams → M1,
M2 and M4 with future kickoffs, all in one round → member files identical
forecasts for M2 and M4 in **both** leagues → M3 created with a past kickoff →
M2's and M4's kickoffs moved into the past → M4's result entered as a `Finished`
match with a score. Writes a manifest carrying the two league ids and names, the
four match ids with their team names, the member's display name, and the forecast
and result scores. The two leagues' point values and the result are chosen so
that the two totals are unequal *and* neither is zero.

#### 5. Manifest access

**File**: `src/client/tests/e2e/fixtures/manifest.ts`

**Intent**: Give specs one typed accessor for the manifest with a clear failure
when setup did not run.

**Contract**: Reads and parses `manifestPath`, throwing a message pointing at the
setup project when the file is absent.

#### 6. Smoke spec

**File**: `src/client/tests/e2e/fixture.smoke.spec.ts`

**Intent**: Prove the harness end to end before any risk spec depends on it —
member state authenticates, the manifest resolves, and the predictions page
renders the fixture league.

**Contract**: Uses the member storage state, navigates to
`/app/leagues/<manifest league A id>/predictions`, and asserts the heading
`"<League A name> — predictions"` is visible. Landing on `/sign-in` instead is
the documented signature of stale storage state.

### Success Criteria:

#### Automated Verification:

- Lint and type-check pass over the new test sources: `npm run lint` and `npm run build`
- The setup projects complete: `npm run e2e -- --project=setup`
- Both storage-state files and the manifest exist after setup
- The smoke spec passes: `npm run e2e -- tests/e2e/fixture.smoke.spec.ts`
- Two consecutive full runs both pass — proving unique naming, not leftover state

#### Manual Verification:

- Signing in as the member in a browser shows exactly two leagues, both on the fixture tournament, with the run's suffix in their names
- League A's and League B's detail pages show the two different rule sets that were configured
- Deleting the admin allowlist entry makes `auth.setup.ts` fail with its own message, not a 403 from deeper in setup

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human before
proceeding to the next phase.

---

## Phase 3: Risk #6 — the prediction screen reads clearly

### Overview

One spec file proving that, looking at the screen, a member can answer: what can
I still change, what did I save, and what is it worth.

### Changes Required:

#### 1. Prediction screen spec

**File**: `src/client/tests/e2e/predictions-legibility.spec.ts`

**Intent**: Assert each of the three chosen states directly, in the member's own
vocabulary, against League A's predictions page.

**Contract**: Uses the member storage state and the manifest; every test is
self-contained. Four tests, each named after the risk rather than the mechanism:

- *An open match can still be edited and reports that it saved.* Fills M1's two
  inputs via `getByLabel(homeTeamName)` / `getByLabel(awayTeamName)`, clicks
  `getByRole('button', { name: 'Save round' })`, and asserts the `role="status"`
  region reads `Saved`. This is the only test that mutates the graph, and M1 is
  read by no other spec.
- *A kicked-off match shows the forecast it locked in, and says why it is locked.*
  Asserts `Locked at kickoff.` and `Your forecast: <M2 scores>` are visible.
- *A member who did not forecast is told so, rather than shown a blank row.*
  Asserts `You did not forecast this match.` is visible for M3.
- *An unscored match says nothing about points; a scored one says what they are.*
  Asserts `expect(page.getByText('Your forecast: <M2 scores>')).not.toContainText('pts')`
  and `expect(page.getByText('Your forecast: <M4 scores>')).toContainText('· <A's points> pts')`.
  This pair is the sharpest expression of the risk — it fails the moment an
  ambiguous `0` appears.

Two low-cost extras on the same page: saving with nothing changed shows the
`role="alert"` text `Nothing to save — no forecast has changed.`, and filling
only one half of a score shows `Enter both scores for: <Home> v <Away>.`. The
reveal panel is asserted only as far as `Everyone's forecasts` being visible on a
kicked-off match — with one member it cannot say more, and its fetch failure is
swallowed by design (`PredictionsPage.tsx:94-96`), so its absence must never be
read as a page error.

### Success Criteria:

#### Automated Verification:

- Lint and type-check pass: `npm run lint`, `npm run build`
- The spec passes: `npm run e2e -- tests/e2e/predictions-legibility.spec.ts`
- The spec passes when run alone and as part of the full suite
- The full suite passes twice in a row
- Mutation check: making `awardedPoints` render unconditionally in `MatchPredictionRow.tsx:220-222` (so an unscored row reads `· 0 pts`) turns the spec red; revert and it is green
- Mutation check: changing the `Locked at kickoff.` copy turns the spec red; revert

#### Manual Verification:

- Read against `references/e2e-anti-patterns.md`: no hallucinated assertion, no CSS/XPath selector, no cross-test dependency, no `waitForTimeout`, unique run-scoped data
- Each test name states a risk a member would recognise, not a mechanism
- Opening League A's predictions page by hand shows the four states the spec describes

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human before
proceeding to the next phase.

---

## Phase 4: Risk #2 — two leagues do not converge

### Overview

The product's wedge, asserted through the browser: one member, one forecast, one
result, two leagues, two different totals — each correct for its own rules.

### Changes Required:

#### 1. Per-league divergence spec

**File**: `src/client/tests/e2e/league-scoring-divergence.spec.ts`

**Intent**: Prove that the number a member reads on their standings table is
their own league's number, and that a second league on the same tournament reads
a different one.

**Contract**: Uses the member storage state and the manifest. Two tests, each
navigating to one league's `/app/leagues/:id/standings` and asserting the
member's row. The row is located by `getByRole('row')` filtered on the member's
display name; the `Points` value is read from that row's third
`getByRole('cell')`. Each expected total is a **literal** with a comment naming
its derivation — "League A scores `ExactScore` at N points and nothing else; the
forecast matched the result exactly, so N" — per test-plan §6.1. A third
assertion confirms M4 counted, via the `Matches scored` cell. Asserting only that
the two totals differ is forbidden: §6.1 notes it is satisfied by any two wrong
numbers that happen to be unequal.

### Success Criteria:

#### Automated Verification:

- Lint and type-check pass: `npm run lint`, `npm run build`
- The spec passes: `npm run e2e -- tests/e2e/league-scoring-divergence.spec.ts`
- The full suite passes twice in a row: `npm run e2e`
- Mutation check: making `MatchScoringService`'s per-league rule lookup resolve one shared rule set for the whole tournament turns this spec red; revert and it is green
- The server suite still passes: `dotnet test src/server/prediction-league.slnx` from the repo root

#### Manual Verification:

- Both leagues' standings pages, opened by hand, show the two totals the spec asserts
- The expected totals can be justified out loud from each league's rules without opening the scoring engine
- Read against `references/e2e-anti-patterns.md`, as in Phase 3

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human before
proceeding to the next phase.

---

## Phase 5: Visual review and close-out

### Overview

The second half of test-plan §3 Phase 4 — the selective multimodal review — plus
the documentation that lets the next contributor add an E2E test without
rediscovering any of this.

### Changes Required:

#### 1. Visual review

**File**: `context/changes/e2e-prediction-flow/visual-review.md`

**Intent**: Run one scripted multimodal pass over the two screens and record both
the prompt and the findings, so the review is repeatable rather than a one-off
impression.

**Contract**: Records the review prompt — asking, of a screenshot alone, "what can
I still change, what did I save, what is it worth" — the two screens reviewed
(League A predictions with M1 open alongside M2/M3/M4 locked, and League A
standings), the findings, and an explicit note of the §4 boundary: selective by
design, never a per-page sweep, never merge-blocking, and never layered over what
a deterministic assertion already catches.

#### 2. Cookbook §6.4 — adding an E2E test

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the `TBD — see §3 Phase 4` placeholder with what this phase
learned.

**Contract**: Documents where specs live, the prerequisite that API + SPA + SQL
Server run before `npm run e2e`, the setup-project/`storageState`/manifest
pattern, the fixture ordering constraint, the "assert the rendered verdict, never
the HTTP status" rule that follows from the 200-with-verdicts contract, the
absence of any injected clock (kickoff timestamps are the only lever), the
no-teardown decision and its consequence, and the mutation check as the
proof-of-non-vacuity step.

#### 3. Cookbook §6.5 — adding a visual review

**File**: `context/foundation/test-plan.md`

**Intent**: Same, for the multimodal half.

**Contract**: Points at `visual-review.md` as the worked example and restates the
"when NOT to use" boundary from §4.

#### 4. Test-plan status and gate sync

**File**: `context/foundation/test-plan.md`

**Intent**: Make the plan describe the world as it is.

**Contract**: §3 Phase 4 status → `complete`. §5's e2e row changes from
"CI on PR — required after §3 Phase 4" to a local gate required after Phase 4,
with CI on PR deferred to Phase 5 and the reason stated: no PR-triggered server
workflow exists, and the suite has a three-process dependency it does not
control. A §6.6 entry records this phase's decisions and the two things a later
phase should not have to rediscover — that API teardown is impossible without a
new endpoint, and that admin identity is gated by an exact-match allowlist.

#### 5. Change close-out

**File**: `context/changes/e2e-prediction-flow/change.md`

**Intent**: Stamp the change as implemented.

**Contract**: `status: complete`, `updated:` to the landing date.

### Success Criteria:

#### Automated Verification:

- The full suite passes from a clean state: `npm run e2e` in `src/client`
- The server suite passes: `dotnet test src/server/prediction-league.slnx` from the repo root
- Client build and lint pass: `npm run build`, `npm run lint`
- No `TBD — see §3 Phase 4` string remains in `context/foundation/test-plan.md`

#### Manual Verification:

- A contributor who has not read this plan can add a new E2E test from §6.4 alone
- The visual review names at least one thing the specs cannot express, or states plainly that it found nothing — either is a valid result
- §5's e2e gate row matches what actually runs today

---

## Testing Strategy

This change *is* tests; the strategy below is how the tests themselves are
validated.

### Automated:

- **Mutation checks** on the two risk specs (Phases 3 and 4). A spec whose mutant
  survives is a spec that would not have caught the bug — test-plan §6.1's
  discipline, lifted to E2E.
- **Run-twice** after each phase, proving the unique-identifier strategy holds
  without teardown.
- **Run-alone and run-in-suite** for each spec, proving independence under
  `fullyParallel`.

### Integration / end-to-end scenarios:

- Predict → lock → score → standings, driven once through the fixture and
  observed on two screens in two leagues.

### Manual Testing Steps:

1. Start SQL Server, `dotnet run` in `src/server`, `npm run dev` in `src/client`.
2. `npm run e2e` from `src/client` — full suite green.
3. Stop the API, re-run — preflight fails in seconds with its named message.
4. Sign in as the fixture member; open both leagues' predictions and standings
   pages and confirm the screens show what the specs assert.
5. Run the five-anti-pattern checklist over both spec files.

## Performance Considerations

Setup is the cost centre: roughly a dozen API calls plus two registrations, run
**once** per suite via the setup project rather than per test. The specs
themselves open two pages. No polling anywhere — scoring is synchronous with the
admin write — so total wall-clock is dominated by browser start-up. If setup
becomes slow, the graph is the thing to shrink, not the specs.

## Migration Notes

No data migration. Two operational notes:

- Every machine running the suite needs the E2E admin entry in its configuration.
  Phase 2's `auth.setup.ts` fails with an explicit message when it is missing, so
  the failure is self-diagnosing.
- Each run leaves one member account, one tournament, four teams, four matches
  and two leagues in the local database, none of which the API can delete. Names
  carry the `e2e-` prefix and a timestamp, so they are identifiable if manual
  cleanup is ever wanted.

## References

- Research: `context/changes/e2e-prediction-flow/research.md`
- Risk map and oracle constraint: `context/foundation/test-plan.md` §2, §3 Phase 4, §6.1
- E2E rules, seed pattern, anti-patterns: `.claude/skills/10x-e2e/references/`
- Prior manual verification of these same criteria:
  `context/changes/scoring-engine-standings/plan.md:434,483` (risk #2) and
  `context/changes/submit-locked-predictions/plan.md:520-541` (risk #6)
- The lock rule: `src/server/PredictionLeague.Api/Controllers/PredictionsController.cs:331`
- Partial-success contract: `src/server/PredictionLeague.Api/Scoring/ScoringTrigger.cs:12-16`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Foundation and the seed

#### Automated

- [x] 1.1 Client type-check and lint pass — e85a90f
- [x] 1.2 The seed spec passes — e85a90f
- [x] 1.3 The preflight aborts with its named message when the API is stopped — e85a90f
- [x] 1.4 Server builds with the new configuration — e85a90f

#### Manual

- [x] 1.5 Sign out lands on the landing page, not on /sign-in — e85a90f
- [x] 1.6 The E2E admin address shows admin navigation — e85a90f
- [x] 1.7 No auth state or manifest files staged — e85a90f

### Phase 2: Fixture layer

#### Automated

- [x] 2.1 Lint and type-check pass over the new test sources — c9473e5
- [x] 2.2 The setup projects complete — c9473e5
- [x] 2.3 Both storage-state files and the manifest exist after setup — c9473e5
- [x] 2.4 The smoke spec passes — c9473e5
- [x] 2.5 Two consecutive full runs both pass — c9473e5

#### Manual

- [x] 2.6 The member sees exactly two run-suffixed leagues on the fixture tournament — c9473e5
- [x] 2.7 Both leagues show their two different configured rule sets — c9473e5
- [x] 2.8 Removing the admin allowlist entry fails auth.setup.ts with its own message — c9473e5

### Phase 3: Risk #6 — the prediction screen reads clearly

#### Automated

- [x] 3.1 Lint and type-check pass — 081a25c
- [x] 3.2 The spec passes — 081a25c
- [x] 3.3 The spec passes alone and as part of the full suite — 081a25c
- [x] 3.4 The full suite passes twice in a row — 081a25c
- [x] 3.5 Mutation check: unconditional awardedPoints render turns the spec red — 081a25c
- [x] 3.6 Mutation check: changed "Locked at kickoff." copy turns the spec red — 081a25c

#### Manual

- [x] 3.7 Spec reviewed against the five anti-patterns — 081a25c
- [x] 3.8 Each test name states a risk, not a mechanism — 081a25c
- [x] 3.9 The four states are visible by hand on League A's predictions page — 081a25c

### Phase 4: Risk #2 — two leagues do not converge

#### Automated

- [x] 4.1 Lint and type-check pass — ce184f9
- [x] 4.2 The spec passes — ce184f9
- [x] 4.3 The full suite passes twice in a row — ce184f9
- [x] 4.4 Mutation check: a tournament-wide shared rule set turns the spec red — ce184f9
- [x] 4.5 The server suite still passes — ce184f9

#### Manual

- [x] 4.6 Both standings pages show the asserted totals by hand — ce184f9
- [x] 4.7 Each expected total is justifiable from its league's rules alone — ce184f9
- [x] 4.8 Spec reviewed against the five anti-patterns — ce184f9

### Phase 5: Visual review and close-out

#### Automated

- [x] 5.1 The full suite passes from a clean state — 389494b
- [x] 5.2 The server suite passes — 389494b
- [x] 5.3 Client build and lint pass — 389494b
- [x] 5.4 No "TBD — see §3 Phase 4" string remains in test-plan.md — 389494b

#### Manual

- [x] 5.5 A contributor can add an E2E test from §6.4 alone — 389494b
- [x] 5.6 The visual review records a finding or an explicit null result — 389494b
- [x] 5.7 §5's e2e gate row matches what actually runs today — 389494b
