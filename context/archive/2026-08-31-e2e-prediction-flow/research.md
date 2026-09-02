---
date: 2026-08-31T15:03:00+02:00
researcher: Kumbus
git_commit: 6ce3d27bd1795a7643ff7650ddd109acd2a6cd75
branch: testing
repository: Prediction-league
topic: "Browser-level coverage for the prediction screen (risk #6) and per-league scoring divergence (risk #2)"
tags: [research, codebase, e2e, playwright, predictions, standings, scoring]
status: complete
last_updated: 2026-08-31
last_updated_by: Kumbus
---

# Research: E2E coverage for the prediction screen and per-league scoring divergence

**Date**: 2026-08-31T15:03:00+02:00
**Researcher**: Kumbus
**Git Commit**: 6ce3d27bd1795a7643ff7650ddd109acd2a6cd75
**Branch**: testing
**Repository**: Prediction-league

## Research Question

What must a browser-level test know about this codebase to cover `test-plan.md`
risk #6 (a member cannot tell what is editable, what was saved, what an entry is
worth) and risk #2 (two leagues on one tournament must not converge on the same
total for an identical forecast)?

## Summary

**Both features are built.** `/10x-e2e`'s precondition — "the functionality
under test exists and the app runs" — is satisfied: `PredictionsPage`,
`StandingsPage`, the league-creation form with per-league scoring rules, and the
admin match editor all exist and are routed.

**Risk #6 is unusually cheap to cover, because the screen already speaks in
literal strings rather than in disabled attributes.** Every state the risk names
has a user-visible label: `"Saved"`, `"Locked — kicked off"`, `"Rejected"`,
`"Locked at kickoff."`, `"You did not forecast this match."`, `"… pts"`,
`"Saving…"`. A `getByText` / `getByRole` assertion can therefore state the risk
directly — "the member can *read* what happened" — instead of asserting on
`toBeDisabled()`, which would prove only that a control is inert, not that a
human can tell why.

**Risk #2 is provable end-to-end through the UI alone, without match events.**
Two leagues on one tournament with contrasting rules, one identical forecast in
each, one final score entered by an admin — the totals diverge. Goal-scorer and
card rules would need `PUT /api/matches/{id}/events`; a contrast between
`ExactScore` and `CorrectOutcome` needs nothing but the score, which
`PUT /api/matches/{matchId}` already carries.

**Scoring is synchronous with the admin write, so the test never polls.**
`TournamentsController` calls `ScoringTrigger.TryScoreAsync` inside the same
request that saves the result — points exist by the time the response lands.
This removes the single biggest source of E2E flake in this scenario.

**Three real obstacles**, all in test infrastructure rather than in the product:
the admin identity is gated by a secret allowlist, the Playwright config starts
no servers, and the suite has no CI job.

## Detailed Findings

### Routing and the URLs a test drives

`src/client/src/routes/index.tsx:22-63` — flat `createBrowserRouter` table.

- Public: `/` (`LandingPage`), `/sign-in` (`SignInPage`).
- Behind `RequireAuth` (`routes/index.tsx:25-40`): `/app`, `/app/leagues`,
  `/app/leagues/new`, `/app/leagues/join`, `/app/leagues/join/:code`,
  `/app/leagues/:id`, **`/app/leagues/:id/predictions`**, **`/app/leagues/:id/standings`**.
- Behind `RequireAdmin` (`routes/index.tsx:41-56`): `/admin/tournaments`,
  `/admin/tournaments/new`, `/admin/tournaments/:tournamentId/matches/new`,
  `/admin/matches/:matchId/edit`, `/admin/players/import` and siblings.

`RequireAuth.tsx:20` and `RequireAdmin.tsx:20` both redirect to `/sign-in` with
`state={{ from: location }}`; `RequireAdmin.tsx:33` sends a signed-in
non-admin to `/app?denied=admin`. A test that lands on `/sign-in` when it
expected a protected page has lost its session — that is the signature to look
for when `storageState` goes stale.

### The prediction screen — states and locators (risk #6)

`src/client/src/routes/leagues/PredictionsPage.tsx` and
`src/client/src/components/leagues/MatchPredictionRow.tsx`.

The page holds **no lock logic of its own** (`PredictionsPage.tsx:20-22`): it
renders the server's `canPredict` per match and, after a save, replaces its state
with the round view the server returns (`PredictionsPage.tsx:180`). Everything
below is therefore an assertion about server truth reaching the screen, which is
exactly what an E2E test is for.

| State | What the member sees | Reference |
|---|---|---|
| Editable (pre-kickoff) | number inputs labelled with the **team names** | `MatchPredictionRow.tsx:88-115` |
| Locked (post-kickoff) | `"Locked at kickoff."` plus `"Your forecast: 2–1"` | `MatchPredictionRow.tsx:210-214` |
| Locked, nothing predicted | `"You did not forecast this match."` | `MatchPredictionRow.tsx:225` |
| Saved (after submit) | `role="status"` with `"Saved"` | `MatchPredictionRow.tsx:30-34,73-86` |
| Rejected by server | `"Rejected: <server's own detail>"` | `MatchPredictionRow.tsx:33,84` |
| Locked mid-save | `"Locked — kicked off"` | `MatchPredictionRow.tsx:32` |
| Saving in flight | the button reads `"Saving…"` | `PredictionsPage.tsx:268` |
| Scored | `" · 7 pts"` appended to the forecast | `MatchPredictionRow.tsx:220-222` |
| Finished but unscored | **silence — no `0`** | `MatchPredictionRow.tsx:218-219` |
| Nothing changed | `role="alert"`: `"Nothing to save — no forecast has changed."` | `PredictionsPage.tsx:167` |
| Half-filled score | `"Enter both scores for: <Home> v <Away>."` | `PredictionsPage.tsx:163` |
| Not a member / missing | heading `"League not found"` | `PredictionsPage.tsx:210` |

Locators available without adding a single `data-testid`:

- `getByRole('heading', { name: '<League> — predictions' })` — `PredictionsPage.tsx:237`
- `getByRole('button', { name: 'Save round' })` — `PredictionsPage.tsx:269`
- `getByLabel('<Home team name>')` / `getByLabel('<Away team name>')` — the score
  inputs are labelled with the team names themselves, `MatchPredictionRow.tsx:92,105`
- `getByLabel('Total cards' | 'Yellow cards' | 'Red cards')` — `MatchPredictionRow.tsx:121,129,137`
- `getByLabel('Goal credited to')` / `getByLabel('First scorer')` — `MatchPredictionRow.tsx:154,174`
- round tabs are `Button`s carrying the round name, with `• now` appended to the
  current one — `PredictionsPage.tsx:243-257`

**The distinction the risk is really about** is at `MatchPredictionRow.tsx:218-222`:
a scored forecast shows `N pts`, an unscored one shows *nothing*, deliberately —
"never a 0 that looks like a verdict". An assertion that a finished-but-unscored
row does **not** contain `pts` is the sharpest single test of risk #6.

Second reveal surface: once a match kicks off, `"Everyone's forecasts"` lists
every member with `(you)` marking the caller, and doubles as a per-match
scoreboard once points exist (`MatchPredictionRow.tsx:230-266`). It is fetched
separately (`PredictionsPage.tsx:94-96`) and its failure is swallowed so the form
survives — meaning a test must not treat a missing reveal as a page error.

### The standings screen (risk #2's read-out)

`src/client/src/routes/leagues/StandingsPage.tsx:66-131`.

- Heading `"<League> — standings"` (`:69`), card title `"Table"` (`:76`).
- Table columns: `#`, `Member`, `Points`, `Matches scored` (`:96-99` in file
  terms — the `<thead>` block), rows carry `rank`, `displayName` with `(you)`
  for the caller, `points`, `scoredMatches`.
- Pre-scoring copy: `"Nothing is scored yet. Points appear as soon as a match
  finishes and its result is entered."` (`StandingsPage.tsx:85-88`,
  `StandingsCard.tsx:66-70`) — a real state, not an error.
- `"No members yet — share the invite code to fill the table."` (`:79-81`).
- **The league's scoring rules are not shown on the standings screen.** For risk
  #2 the divergence must therefore be asserted on the two totals themselves, in
  two separate leagues — the UI offers no rule display to cross-check against.

`StandingsCard.tsx:15` caps the league page's card at `TOP_ROWS = 5`; the full
table lives on `/standings`.

### League setup through the UI

- `routes/leagues/LeagueFormPage.tsx:83-95` — heading `"New league"`, card
  `"League details"`, `getByLabel('Name')`, `getByLabel('Tournament')`.
- `components/leagues/ScoringRulesFieldset.tsx:61` — one labelled toggle per
  parameter (`scoring-active-<parameter>`), so contrasting rule sets are
  configurable through the form.
- `routes/leagues/JoinLeaguePage.tsx:59-88` — heading `"Join a league"`, card
  `"Invite code"`, `getByLabel('Code')`, submit button; `/app/leagues/join/:code`
  prefills from an invite link.

### Server contract behind the screen

`PredictionLeague.Api/Controllers/PredictionsController.cs`, routed
`api/leagues/{leagueId:guid}/predictions` with class-level `[Authorize]` (`:24-25`).

- `GET` (`:145`) returns `RoundViewResponse` (`:96-104`) carrying `ScoredParameters`
  — the client sends only the fields the league actually scores
  (`PredictionsPage.tsx:152-158`).
- `POST` (`:170`) is a batch write answering **200 with per-item verdicts**
  (`Saved` / `Locked` / `Invalid`, `:54-55`) rather than a 4xx — a well-formed
  request that could not be fully applied still succeeds at the HTTP level. A test
  must assert on the row's rendered verdict, never on the response status.
- `GET revealed` (`:284`) returns only matches that have kicked off (`:303`).

**The kickoff lock is one expression**: `PredictionsController.cs:331` —
`match.KickoffUtc <= now`, with `now = DateTimeOffset.UtcNow` captured **once per
request** (`:156`, `:188`, `:295`) so the boundary cannot move mid-batch. There
is no injected clock and no `TimeProvider`: a test controls lock state only by
choosing kickoff timestamps in the past or the future.

### Getting data in: the admin path

| Step | Call | Guard | Reference |
|---|---|---|---|
| Create tournament | `POST /api/tournaments` | `AdminOnly` | `TournamentsController.cs:92-93` |
| Create match (kickoff, status, score, round) | `POST /api/tournaments/{id}/matches` | `AdminOnly` | `TournamentsController.cs:209-230` |
| Set the result | `PUT /api/matches/{matchId}` | `AdminOnly` | `TournamentsController.cs:275-276` |
| Enter goals / cards | `PUT /api/matches/{matchId}/events` | `AdminOnly` | `MatchesController.cs:85-86` |
| Re-run scoring | `POST /api/matches/{matchId}/rescore` | `AdminOnly` | `MatchesController.cs:164-165` |
| Create league + rules | `POST /api/leagues` | `[Authorize]` | `LeaguesController.cs:129`, DTO `:43-47` |
| Join | `POST /api/leagues/join` | `[Authorize]` | `LeaguesController.cs:261`, code upper-cased `:269` |

`CreateMatchRequest` / `UpdateMatchRequest` (`TournamentsController.cs:209-225`)
carry `KickoffUtc`, `Status`, `HomeScore`, `AwayScore`, `Round` — so a single
admin call produces either an open row or a locked, finished, scored one.

**Scoring fires inside the write**: `TournamentsController.cs:260` and `:299` call
`ScoringTrigger.TryScoreAsync`, as does `MatchesController.cs:124`. On failure the
endpoint still answers **200** with `ScoringFailed` + `ScoringMessage`
(`ScoringTrigger.cs:12-16`, `TournamentsController.cs:205-207`). A test asserting
"points appeared" must not read a 200 as proof.

### Standings computation

`StandingsController.cs:17-18` — `api/leagues/{leagueId:guid}/standings`,
`[Authorize]`. `Rank()` at `:69-88` implements shared rank on ties with the next
distinct total skipping (`1, 2, 2, 4`). Response carries `callerUserId` (`:63`),
which is what drives the `(you)` marker.

### Test infrastructure as it stands

- `src/client/playwright.config.ts` — `testDir: ./tests/e2e`,
  `baseURL: https://localhost:5173`, `ignoreHTTPSErrors: true`. **No `webServer`
  block, no projects, no storageState**: the suite assumes API, SPA and database
  are already running.
- `src/client/tests/e2e/auth.spec.ts` — the only spec. Registers a unique account
  per run (`e2e-${Date.now()}-${random}`), asserts `/app`, signs out, then checks
  `/api/auth/me` returns 401 via `page.evaluate`. Locator style is already
  correct: `getByRole('tab'|'button')`, `getByLabel`, `expect(...).toHaveURL`, no
  `waitForTimeout`.
- **This spec is currently red** (verified this session): after sign-out the app
  lands on `/sign-in`, not `/`. `SignOutButton.tsx:16` navigates to `/` but
  `RequireAuth.tsx:20` redirects first. Since `/10x-e2e` treats the existing spec
  as the seed, this must be settled before generation.
- **CI runs no Playwright at all.** `deploy-backend.yml:61-62` runs
  `dotnet test src/server/prediction-league.slnx -c Release --no-restore`; the
  Static Web Apps workflow only builds `src/client`. E2E is a local gate today.
- `sample-data/` holds `sample-matches.csv` and `sample-players.csv` for the admin
  CSV import screens — an existing bulk path for fixture data.

## Code References

- `src/client/src/routes/index.tsx:22-63` — route table and the three auth zones
- `src/client/src/components/leagues/MatchPredictionRow.tsx:30-34` — the three verdict strings
- `src/client/src/components/leagues/MatchPredictionRow.tsx:210-226` — locked-row copy
- `src/client/src/components/leagues/MatchPredictionRow.tsx:218-222` — points shown only when scored
- `src/client/src/routes/leagues/PredictionsPage.tsx:163-169` — client-side save guards
- `src/client/src/routes/leagues/StandingsPage.tsx:63-131` — table, ranks, empty states
- `src/server/PredictionLeague.Api/Controllers/PredictionsController.cs:331` — the entire lock rule
- `src/server/PredictionLeague.Api/Controllers/PredictionsController.cs:54-55,210-233` — per-item verdicts
- `src/server/PredictionLeague.Api/Controllers/TournamentsController.cs:209-230,260,299` — match writes that score
- `src/server/PredictionLeague.Api/Controllers/StandingsController.cs:69-88` — tie/rank rule
- `src/server/PredictionLeague.Api/Scoring/ScoringTrigger.cs:17-40` — partial-success contract
- `src/client/playwright.config.ts` — no webServer, no projects
- `.github/workflows/deploy-backend.yml:61-62` — the only automated test gate

## Architecture Insights

1. **The client is a renderer of server verdicts, not a second rule engine.** The
   lock, the per-item outcome and the round selection are all server decisions
   (`PredictionsPage.tsx:20-22,43-45`). E2E therefore tests *transport and
   legibility*, and duplicating engine logic in a browser test buys nothing.
2. **Partial success is the house style.** Both the prediction batch and the admin
   match write answer 200 while carrying a failure verdict in the body. Any
   assertion built on HTTP status alone will pass against a broken system.
3. **Time is ambient.** `DateTimeOffset.UtcNow` with no injected clock means the
   only lever a test has over the lock is the kickoff timestamp it writes.
4. **Authorization is single-sourced on `OrganizerUserId`** (`lessons.md`), while
   admin rights come from the `Admin:Emails` allowlist reconciled at every sign-in
   (`AuthController.cs:184`). Two different mechanisms, and a test needs both.

## Historical Context (from prior changes)

- `context/changes/scoring-engine-standings/plan.md:434` — criterion 2.3 "Two
  leagues with different rules on one tournament get different points for
  identical forecasts" — **manual, 2026-08-25**. This is risk #2, and it has never
  been automated above the unit layer.
- `context/changes/scoring-engine-standings/plan.md:483` — criterion 5.8 "Two
  leagues on one tournament show different totals for the same member" — manual.
  The browser-visible half of the same risk.
- `context/changes/scoring-engine-standings/plan.md:464-467` — criteria 4.2–4.5
  (member with no predictions, non-member gets 404, shared rank with skip, a member
  who leaves disappears) — all manual.
- `context/changes/submit-locked-predictions/plan.md:520-526,537-541` — the
  prediction screen's states (locked row renders read-only, rejected row shows the
  server's reason, mid-form kickoff locks one row and saves the rest, reveal
  appears only post-kickoff) were verified by hand against commits `b4edfe5` /
  `006e2b6`. This is risk #6's evidence base — and it is entirely manual.
- `context/changes/testing-scoring-engine/` — Phase 1 landed 83 unit tests over
  `PredictionScorer`, `MatchOutcome.FromMatch` and `MatchScoringService`, including
  per-league rule isolation. **E2E must not re-derive point arithmetic**; its job is
  to prove the number survives the round trip to the screen.

## Related Research

- `context/foundation/test-plan.md` §2 (risk map), §3 Phase 4, §6.1 (the oracle
  constraint, which applies to E2E expected totals just as it does to unit tests)
- `context/foundation/lessons.md` — "League organizer identity is single-sourced on
  OrganizerUserId"; "New children of a tracked parent need an explicit Add" (the
  second member joining a league is exactly the shape a two-account E2E fixture
  exercises)

## Open Questions

1. **How does a test become an admin?** `Admin:Emails` lives in `dotnet
   user-secrets`, and `AuthController.cs:184` reconciles the flag at sign-in. A
   fixture cannot self-promote. Options: register the allowlisted address as the
   test admin, add a test-only allowlist entry, or seed the flag directly.
2. **UI-driven or API-driven setup?** Creating a tournament, match, two leagues and
   a membership through admin screens is many slow steps; the same state is a
   handful of `request.post` calls against the API using the same session cookie.
   The risk lives in the *prediction and standings* screens, so setup cost spent in
   the browser buys no signal.
3. **Database isolation.** The suite runs against the same local SQL Server that
   manual work uses; every run leaves an account, a tournament and leagues behind.
   Unique per-run names are the minimum; whether to add cleanup is a plan decision.
4. **The red seed test.** Is `/` or `/sign-in` the intended post-sign-out
   destination? Settle it before `/10x-e2e` copies the pattern.
5. **Does the suite start the app?** Adding a `webServer` block would make the
   suite self-contained but needs the API and the database too — a three-process
   dependency Playwright alone cannot express.
