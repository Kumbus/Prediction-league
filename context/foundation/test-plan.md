# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-09-02

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the team
   is worried about X, and the failure would surface somewhere in <area>"
   carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting:
`src/server/PredictionLeague.Domain`, `src/server/PredictionLeague.Application`,
`src/server/PredictionLeague.Infrastructure`, `src/server/PredictionLeague.Api`,
`src/server/PredictionLeague.Functions`, `src/client/src`, `src/client/tests`.

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|---|---|---|---|
| 1 | A player is awarded a point total that does not match their league's configured rules — a parameter scores that should not, the first scorer is resolved wrongly, an own goal is credited to the wrong side | High | High | PRD Guardrail "Scoring correctness"; PRD FR-008, FR-011; interview Q1, Q4; `context/archive/2026-08-03-custom-scoring-rules/plan.md` |
| 2 | Two leagues on the same tournament produce identical points for an identical forecast — per-league custom scoring, the product's wedge, silently stops being custom | High | Medium | PRD FR-002, FR-006, FR-008 (stated core insight); `context/changes/scoring-engine-standings/plan.md` — criteria 2.3 and 5.8 verified by hand only |
| 3 | The second member joining a league, or the first new scoring rule added to an existing league, fails with a 500 instead of saving — and neither build nor lint catches it | High | High | interview Q2, Q3; `context/foundation/lessons.md` — "New children of a tracked parent need an explicit Add"; hot-spot dir `src/server/PredictionLeague.Infrastructure/Persistence/Repositories` (21 commits/30d), `src/server/PredictionLeague.Application/Abstractions/Persistence` (24 commits/30d) |
| 4 | A prediction is accepted or edited after kickoff — the PRD's anti-cheat guardrail fails | High | Medium | PRD FR-010 and the NFR "observably impossible to submit or edit"; archived slice S-06 `submit-locked-predictions`; interview Q1 |
| 5 | **[abuse]** A signed-in member of league A reads or writes league B's data — the endpoint verifies that the caller is authenticated, not that the resource is theirs | High | Medium | PRD Access Control (three roles), FR-002; `context/foundation/lessons.md` — "League organizer identity is single-sourced on OrganizerUserId, not on membership Role" (per-league authorization can drift); hot-spot dir `src/server/PredictionLeague.Api/Controllers` (14 commits/30d) |
| 6 | A member misreads the prediction screen — cannot tell what is still editable, what was saved, or what a given entry is worth — and submits something other than intended, or believes they submitted when they did not | Medium | Medium | interview Q1, Q3; hot-spot dir `src/client/src/routes/leagues` (15 commits/30d), `src/client/src/components/leagues` (9 commits/30d) |
| 7 | The standings table disagrees with the points actually awarded — ties, rank skips, a member with no predictions, or a member who left the league | Medium | Medium | PRD FR-012; `context/changes/scoring-engine-standings/plan.md` — criteria 4.2–4.5 verified by hand only; hot-spot dirs as above |

Deliberately not in the map: football-data API ingest failure paths. The
free tier cannot serve current fixtures for live verification and match
entry is admin-manual today, so a test there costs budget without buying
signal. Revisit if a paid tier lands.

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|---|---|---|---|---|---|
| #1 | For a given rule configuration and a given match outcome, the awarded total equals the number derived independently from the league's rules — not from reading the engine | "The engine is a pure function, so it is obviously correct" | The input contract (forecast + match events + rule set), the event-ordering rule, null/absent-detail handling | unit (Domain, no DB, no HTTP) | **Oracle problem** — expected values lifted from the implementation under test; such a test ratifies current behavior including current bugs |
| #2 | The same forecast against the same match, scored under two different rule configurations, yields two different totals, each correct for its own configuration | "Rules live per league in the database, so isolation is structural" | Where the engine reads a specific league's rules from, whether anything is cached or defaulted | unit, parameterized over at least two contrasting configurations | A single happy path with one configuration — it cannot distinguish real isolation from coincidence |
| #3 | A second write of the same kind against an already-existing aggregate succeeds as an insert rather than failing | "The first one worked, so the write path is fine" | EF change-tracker state, client-generated keys, which repositories mutate collection navigations on tracked aggregates | integration against a real relational provider | Testing only the first write (an `Added` parent masks the defect); mocking the DbContext, which removes exactly the mechanism that breaks |
| #4 | A request sent after kickoff is rejected **by the server**, even when the client sends it | "The UI disables the form, so the lock holds" | Source of kickoff time, timezone handling, where the decision is made, what status the rejection returns | integration (API level) | Asserting only on button state; hardcoding a date instead of a time expressed relative to kickoff |
| #5 | A member of league A receives 403/404 on a league B resource, and the denial body leaks none of its content | "`[Authorize]` on the controller covers per-league authorization" | How per-league role is derived, the shape of identity on a request, what the denial path returns | integration (API + test authentication scheme) | Testing only anonymous access (401) — IDOR is two *authenticated* users, not an anonymous one |
| #6 | Looking at the screen, a member correctly answers: what can I still change, what did I save, what is it worth | "The data renders, therefore the screen is understandable" | The prediction screen's states: before lock, after lock, saved, save-failed | e2e on the critical flow, plus a selective multimodal review of 1–3 screens | A meaningless component snapshot; a vision model layered over something a deterministic assertion already catches |
| #7 | The table agrees with the sum of awarded points, and ties and rank gaps follow the stated ranking rule | "Sorting by total is enough" | The ranking rule, who belongs to the table at a given moment, where the total is computed | integration (API level) | Asserting against the endpoint's current output — the oracle problem again |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|---|---|---|---|---|---|
| 1 | Scoring engine truth table | Prove awarded points match a league's own rules, and that two leagues do not converge | #1, #2 | unit | complete | `context/changes/testing-scoring-engine/` |
| 2 | Persistence write-path | Prove a second write against an existing aggregate inserts instead of throwing | #3 | integration | not started | — |
| 3 | API contract and authorization | Prove kickoff lock and per-league isolation are enforced server-side | #4, #5, #7 | integration | not started | — |
| 4 | Critical-flow e2e and selective visual review | Prove the predict to score to standings loop works end-to-end and the prediction screen reads clearly | #6, #2 | e2e, multimodal visual review | complete | `context/changes/e2e-prediction-flow/` |
| 5 | Quality-gates wiring | Lock the floor in CI and in the local agent loop | cross-cutting | gates | not started | — |

**Status vocabulary** (fixed — parser literals): `not started`,
`change opened`, `researched`, `planned`, `implementing`, `complete`.

## 4. Stack

The classic test base for this project. AI-native tools (if any) carry a
`checked:` date so future readers can see which lines need re-verification.

| Layer | Tool | Version | Notes |
|---|---|---|---|
| unit (server) | xUnit v3 + Shouldly + NSubstitute | xunit.v3 4.0.0, Shouldly 4.3.0, NSubstitute 6.2.0 | `src/server/PredictionLeague.Tests`, the solution's sixth project. Runs on **Microsoft.Testing.Platform**, not VSTest: xunit.v3 4.x embeds MTP and the .NET 10 SDK removed the VSTest target outright (`dotnet test` errors out on it), so a repo-root `global.json` selects the runner — `{"test": {"runner": "Microsoft.Testing.Platform"}}`, with no `sdk` key so it pins no SDK version. `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are deliberately absent: both are VSTest-path packages. checked: 2026-08-31 |
| integration (server) | none yet — see §3 Phase 2 | — | Candidate: `Microsoft.AspNetCore.Mvc.Testing` / `WebApplicationFactory`, with the EF Core `DbConnection` registration swapped in `ConfigureWebHost`; provider choice (SQLite in-memory vs a real SQL Server container) is an open question for Phase 2 research, because the app runs on SQL Server. checked: 2026-08-31 |
| test authentication | none yet — see §3 Phase 3 | — | Candidate: a custom `AuthenticationSchemeOptions` handler registered via `ConfigureTestServices`, so two distinct authenticated identities can be exercised for the IDOR case. checked: 2026-08-31 |
| unit (client) | none yet — see §3 Phase 4 | — | Candidate: Vitest (v4 line current as of checked date) on the existing Vite config. checked: 2026-08-31 |
| e2e | Playwright (`@playwright/test`) | ^1.60.0 | Already wired: `src/client/playwright.config.ts`, `testDir: ./tests/e2e`, `baseURL: https://localhost:5173`, run with `npm run e2e`. One spec exists (auth). |
| accessibility | none | — | Not scoped by any current risk; add only if a risk arises |
| (optional) AI-native | multimodal visual review of 1–3 screens — checked: 2026-08-31 | n/a | **When NOT to use**: for anything a deterministic assertion or a pixel diff already catches; across every page rather than the prediction and standings screens; or as a merge-blocking gate. It buys signal only on "can a member read this screen", which no assertion expresses. |

**Stack grounding tools (current session):**
- Docs: Context7 — checked ASP.NET Core integration testing (`WebApplicationFactory`, DbConnection replacement, custom test auth scheme) and confirmed the current Vitest major line; checked: 2026-08-31
- Search: Exa.ai — available, not needed; official docs answered the stack questions directly; checked: 2026-08-31
- Runtime/browser: no Playwright MCP in this session; Playwright is present as an npm dev dependency and is driven through `npm run e2e`; checked: 2026-08-31
- Provider/platform: GitHub MCP failed to connect (HTTP 401) and Azure MCP timed out in this session, so §5 gate wiring is named here but must be verified against the real workflow files during §3 Phase 5; checked: 2026-08-31

## 5. Quality Gates

The full set of gates that must pass before a change reaches production.
"Required after §3 Phase N" means the gate is enforced once that rollout
phase lands; before that, the gate is planned.

| Gate | Where | Required? | Catches |
|---|---|---|---|
| lint + typecheck (client) | local + CI | required | syntactic / type drift; `npm run build` runs `tsc -b` and fails on type errors |
| build (server) | local + CI | required | compile-time drift across the five projects |
| unit (server) | local + CI | **live** (§3 Phase 1 landed) | scoring logic regressions, per-league rule isolation. Enforced by the `Run tests` step in the `build` job of `.github/workflows/deploy-backend.yml`, positioned after `Restore` and before `Publish API`, so a red test blocks the deploy. **Known gap:** that workflow triggers only on push to `main` (plus `workflow_dispatch`) — there is no PR-triggered server workflow, so PRs are **not** gated by this suite. Closing that is §3 Phase 5's job; until then "local + CI" means post-merge CI. |
| integration (server) | local + CI | required after §3 Phase 3 | persistence write-path 500s, kickoff-lock bypass, cross-league access |
| e2e on critical flows | local (`npm run e2e`) | **live, local only** (§3 Phase 4 landed) | broken predict to score to standings loop; per-league scoring converging. **CI on PR deferred to §3 Phase 5**, deliberately: the suite drives a three-process stack (SQL Server + API + SPA) that it does not start, and there is no PR-triggered server workflow to hang it on (see the unit row's known gap). Until then nothing mechanically blocks a PR that breaks the loop. |
| post-edit hook | local (agent loop) | **live** (landed 2026-08-31, ahead of §3 Phase 5) | regressions at edit time, before review. `PostToolUse` matcher `Write\|Edit` in `.claude/settings.json` runs `.claude/hooks/post-edit-check.mjs`: client TS edits get `eslint --fix` + `tsc -b`; server `.cs` edits inside the scoring risk area (#1/#2) run the xUnit suite; other paths are a no-op. Failure exits 2, so the message reaches the agent's context. |
| multimodal visual review | local, on demand | optional (§3 Phase 4 landed one pass) | unreadable prediction / standings screens that assertions cannot express. Deliberately not a gate: it is a recorded human judgement, not a check — see §6.5 and `context/changes/e2e-prediction-flow/visual-review.md` |
| pre-prod smoke | between merge and prod | optional, after §3 Phase 5 | environment-specific failures on Azure App Service |

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once
the relevant rollout phase ships; before that, the sub-section reads
"TBD — see §3 Phase N."

### 6.1 Adding a unit test for scoring

**Where it goes.** `src/server/PredictionLeague.Tests`, the solution's sixth
project. Domain tests (the pure engine) in `Domain/Scoring/`, service tests
(repositories substituted) in `Infrastructure/Scoring/`. Run the whole suite
with `dotnet test src/server/prediction-league.slnx` from the repo root — it
must be the repo root, or a directory under it, so the runner-selecting
`global.json` is found (see §4).

**Build the inputs from `ScoringFixtures`, never by hand.**

- `Rules((ScoringParameter.ExactScore, 5), …)` — one league's configuration.
  The fixture carries **no default point values**; the caller states every
  number, so a test can never pass while reading points the league under test
  never configured.
- `Forecast(home, away, playerId, teamId, totalCards:, yellowCards:,
  redCards:, leagueId:, matchId:)` — a member's prediction. Every optional
  half defaults to `null`, so a test states only the fields it is about.
- `Result(...)` builds a `MatchOutcome` directly, for tests about the engine.
  `FinishedMatch(home, away, events)` / `MatchInStatus(status, …)` plus
  `Event(typeId, player, team, minute, minuteExtra:)` build a real match, for
  tests about derivation.
- `SeededEventTypes()` mirrors `MatchEventTypeConfiguration.HasData` exactly —
  1 NormalGoal, 2 OwnGoal, 3 Penalty, 4 MissedPenalty (all `Category = Goal`),
  5 YellowCard, 6 RedCard (both `Card`). **Never invent event-type ids.**
  `MatchOutcome.FromMatch` looks types up by exactly these numbers, and a
  fixture with its own ids passes while testing nothing real. Note that
  `MissedPenalty` sits under `Category = Goal`: goal filtering has to exclude
  it by `Code`, and a test that trusts the category alone is testing the wart
  rather than the rule.
- `LeagueWith(tournamentId, name, (parameter, points), …)` — a league whose
  rules are minted against its own id.

**The oracle constraint — the point of the whole section.** The expected total
must be derived from the league's rule configuration and ordinary football
semantics, and written into the test as a **literal**. It must never be
computed by summing the rule list or by re-deriving the engine's predicate:
that is a mirror test, and it passes against a broken engine just as happily
as a correct one. A test expecting 7 must be able to say *why* 7 — "5 for
`ExactScore` plus 2 for `CorrectOutcome`, both configured by this league" —
in a comment beside the assertion. If the sources do not settle what the
number should be, stop and ask; do not run the engine and record its answer.

Two oracle lines are deliberately **not** asserted, because the source only
restates the implementation's shape:

- the `CorrectOutcome` sign formula — assert the *behaviour* instead (home
  win / draw / away win classification, all three classes, both directions);
- the same-minute tie-break keys (`MatchEventTypeId`, `PlayerId`) — surrogate
  keys with no football meaning. Assert determinism and Id-independence, never
  which player wins a tie.

**Risk #2 shape — two leagues, one forecast.** Test this at
`MatchScoringService`, not at `PredictionScorer`: `Score` takes the rule set as
an argument, so passing two rule sets and getting two totals is close to
tautological. The divergence lives in the per-league rule lookup inside
`ScoreMatchAsync`, against a dictionary built from
`ListByTournamentWithRulesAsync` — which is filtered by **TournamentId**, not
LeagueId. Give the test two leagues on one tournament with contrasting
configurations and two predictions with *identical field values* but different
`LeagueId`, then assert **each league's specific total**. Asserting only that
the two differ is satisfied by any two wrong numbers that happen to be unequal.

**Reachability filter — what not to test.** The product forbids zero points,
negative points, and duplicate parameters in one rule set (`LeaguesController`
clamps `Points` to `1..1000` and rejects duplicates; a unique index covers
`(LeagueId, Parameter)`). The engine tolerates all three, but asserting them
would encode forbidden states as behaviour. Do not write them.

**Edge cases worth one case each.** A blank (`null`) forecast half awards
nothing rather than throwing; a member who predicted 0 cards against a
card-less match **does** award, because zero is a correct answer and not an
absent one; a league with no configured rules scores integer `0`, never
`null` — `null` means "not scored" and standings depend on the distinction.

**Prove the test is not vacuous.** After a scoring test goes green, mutate the
line it claims to guard, confirm the suite goes red, and revert. That is how
§3 Phase 1 verified the `MissedPenalty` `Code` exclusion, the absence of
`MatchEvent.Id` from the first-scorer ordering, and the per-league rule lookup.
A test whose mutant survives is a test that would not have caught the bug.

### 6.2 Adding an integration test for a repository write

- TBD — see §3 Phase 2. Must capture the provider decision, the fixture
  shape for an *already-persisted* aggregate, and how to assert an insert
  rather than an update (risk #3).

### 6.3 Adding an integration test for an API endpoint

- TBD — see §3 Phase 3. Must capture host setup, how a request is
  authenticated as a specific user, and how to exercise two distinct
  members for the cross-league case (risks #4, #5, #7).

### 6.4 Adding an e2e test

**Where it goes.** `src/client/tests/e2e/<feature>.spec.ts`, one spec file per
risk. Run the whole suite with `npm run e2e` from `src/client`.

**Bring the stack up first.** The suite deliberately has **no `webServer`**: it
needs SQL Server, the API on its https profile (`:7182`) and the SPA dev server
(`:5173`), and Playwright can start none of them. `tests/e2e/global-setup.ts`
probes all of it and aborts in ~3s naming what is missing, so a dead stack reads
as one line rather than a wall of navigation timeouts.

**Never authenticate through the UI.** Three projects run in order —
`setup:auth` → `setup:fixture` → `e2e` — and the first writes one `storageState`
per role under `playwright/.auth/`. A spec opts in with
`test.use({ storageState: memberStatePath })`. They are separate *projects*, not
files in one project, because Playwright parallelizes files **within** a project;
only a project dependency guarantees the states exist before the graph is built.
`auth.spec.ts` is the sole exception — signing in is what it tests.

**Never hardcode fixture data.** `setup:fixture` builds the graph and writes
`playwright/.fixtures/manifest.json`; specs read it with `readManifest()`. Call
that **inside a test body**, never at module scope — Playwright loads every spec
file before the setup project runs, so a module-level read fires against a
manifest that does not exist yet.

**The admin is a stable allowlisted account, and its cookie is the subtle part.**
`Admin:Emails` is an exact-match list, so a per-run unique address can never be
promoted; `e2e-admin@example.test` lives at `Admin:Emails:0` in
`appsettings.Development.json`. Configuration merges arrays **by index** and
user-secrets load later, so a personal admin in secrets must sit at index 1 or
higher. Worse: `AdminOnly` is a `RequireClaim` policy and the claim is baked into
the cookie at sign-in, so on the run that first promotes the account the session
authenticates, reports `isGlobalAdmin: true` from the database, and still 403s on
every write. `auth.setup.ts` therefore signs in **again** after promotion and
probes a real `AdminOnly` endpoint.

**Fixture ordering is load-bearing, and the obvious order fails.** A league can
only be created on a **published** tournament, and a forecast can only be filed
while its match is still open. So: tournament → publish → leagues → teams →
matches with future kickoffs → forecasts → move kickoffs into the past → enter
results. There is **no injected clock**; kickoff timestamps are the only lever
over the lock.

**Assert the rendered verdict, never the HTTP status.** The prediction batch write
answers `200` carrying per-item `Saved`/`Locked`/`Invalid`, and the admin match
write answers `200` carrying `ScoringFailed`. A test built on status alone passes
against a broken system. The fixture helpers reject both cases explicitly.

**Locators.** `getByRole` / `getByLabel` / `getByText` only. Two facts make this
possible without a single `data-testid`: the score inputs are labelled with the
**team names**, and a locked row renders its forecast and its points inside one
`<p>`, so matching the forecast text scopes an assertion to exactly that row.
Watch the characters — the score separator is an **en-dash (U+2013)** and the
points separator is `·` (U+00B7). Shared copy such as `Locked at kickoff.` cannot
be scoped to a row at all (the row has no accessible container), so assert its
**count** instead of its visibility.

**Unique identifiers always; teardown where the API allows it.** Every entity is
named with a per-run `e2e-<timestamp>-<rand>` prefix — required regardless, because
team names are globally unique server-side and a re-run would otherwise 409.

Teardown is narrower than it looks but **not** impossible. There is no
`DELETE /api/leagues/{id}` and deleting a tournament 409s while any league
references it, so the shared fixture graph is not unwound and the dev database
accumulates one member, one tournament, four teams, four matches and two leagues
per run. But **a sole member leaving a league destroys it**
(`LeaguesController.cs:288-291` — the only path that removes one), and matches and
tournaments have real `DELETE` routes. So a test that creates something it solely
owns can and should clean up after itself: `seed.spec.ts` does exactly that, and
asserts the deletion rather than assuming it.

Prefer that shape for anything a single test creates. Fall back to unique-ids-only
when the API genuinely offers no reversal.

**Typecheck covers the suite.** `tsconfig.e2e.json` is referenced from the root
`tsconfig.json`, so `npm run build` typechecks `tests/` too. It caught a real DTO
shape bug on its first run; before it existed, `tests/` compiled only at runtime.

**Prove the test is not vacuous.** Same discipline as §6.1: after a spec goes
green, break the behaviour it claims to guard, confirm it goes red, and revert. A
mutant that survives means the assertion protects nothing. Phase 4 verified two
this way — rendering `awardedPoints` unconditionally (an unscored row then reads
`· 0 pts`) and collapsing the per-league rule lookup to one tournament-wide set.
Server-side mutants need a rebuild and an API restart to take effect.

### 6.5 Adding a visual review of a screen

**Selective by design**: 1–3 screens, once, recorded. Never a per-page sweep,
never merge-blocking, and never layered over what a deterministic assertion
already catches (§4).

**Worked example**: `context/changes/e2e-prediction-flow/visual-review.md` —
the prediction and standings screens reviewed against the three questions risk #6
asks ("what can I still change, what did I save, what is it worth"), with the
prompt, the screens, and six findings recorded.

**How.** Capture full-page screenshots from the live app using a stored session,
then review them against a written prompt. Capture with a throwaway script rather
than wiring screenshots into the specs: capturing is not reviewing, and unreviewed
images just accumulate.

**What counts as a finding.** Only things an assertion cannot express — contrast,
whether an affordance is signalled positively or only by absence, whether a number
is explicable to the person reading it. "The string is present" is the specs' job.
Record a null result explicitly if the screens read cleanly; that is a valid
outcome, not a failed review.

### 6.6 Per-rollout-phase notes

**Phase 1 — Scoring engine truth table (2026-08-31, `testing-scoring-engine`).**
Landed 83 unit tests across `PredictionScorer` (per-parameter truth table,
cumulative stacking, blank forecasts, own-goal pair, null guards),
`MatchOutcome.FromMatch` (goal filtering, ordering, card counting, defensive
contracts) and `MatchScoringService` (per-league isolation, un-scoring, early
exits). No database, no HTTP, no clock.

Two things a later phase should not have to rediscover:

- The plan assumed `dotnet test` would default to VSTest with no `global.json`.
  That is false on this toolchain — see §4. The runner decision was inverted
  during implementation and approved.
- The CI gate is live but post-merge only; PRs stay ungated until Phase 5 wires
  a PR-triggered server workflow. Recorded in §5.

**Phase 4 — Critical-flow e2e and selective visual review (2026-09-02,
`e2e-prediction-flow`).** Landed nine browser tests across two specs —
`predictions-legibility.spec.ts` (risk #6) and `league-scoring-divergence.spec.ts`
(risk #2) — on a fixture layer that builds a four-match, two-league graph through
the API once per run. Both risks had been verified by hand only since
2026-08-25. Full recipe in §6.4; one visual review recorded in §6.5.

Four things a later phase should not have to rediscover:

- **Teardown of the shared graph is impractical, but per-test cleanup is not.**
  Corrected after the phase closed: the original note claimed API teardown was
  impossible. There is indeed no `DELETE /api/leagues/{id}` and deleting a
  tournament 409s while a league references it, so the fixture graph is left in
  place and every run leaks a member, a tournament, four teams, four matches and
  two leagues. But a **sole member leaving a league destroys it**
  (`LeaguesController.cs:288-291`), so a test that solely owns what it created can
  reverse itself — `seed.spec.ts` does. Unwinding the shared graph would still want
  a product change; individual tests do not need one.
- **Admin identity is gated twice over.** `Admin:Emails` is an exact-match list
  *and* configuration merges arrays by index, so the E2E admin at
  `appsettings.Development.json` index 0 is silently replaced by any personal
  admin a developer keeps in user-secrets at the same index. Separately,
  `AdminOnly` is a claim policy, so the session that first promotes the account
  holds a pre-promotion cookie and 403s despite reporting `isGlobalAdmin: true`.
  Both cost real debugging; both are now asserted in `auth.setup.ts`.
- **The e2e gate is local, not CI.** §5 was rewritten to say so rather than
  claiming a gate that does not exist. Wiring it needs the three-process stack
  and a PR-triggered workflow — Phase 5's job, alongside the same gap on the unit
  suite.
- **The plan assumed one `setup` project; two are required.** Playwright
  parallelizes files within a project, so ordering auth before fixture
  construction needs a project dependency, not file order.

## 7. What We Deliberately Don't Test

Exclusions agreed during the rollout (Phase 2 interview, Q5). Future
contributors should respect these unless the underlying assumption changes.

- **Admin UI** — small, trusted user set and a low blast radius; scoring
  correctness downstream of admin entry is covered by risks #1 and #3
  regardless of how the data was typed in. Re-evaluate if admin screens
  become self-serve or gain non-admin users. (Source: Phase 2 interview Q5.)
- **Football-data API ingest paths** — the free tier cannot serve current
  fixtures, and match entry is admin-manual today; a test there costs
  budget without buying signal. Re-evaluate if a paid tier lands.
  (Source: §2 risk-map exclusion, project decision on manual admin entry.)
- **Out-of-scope product surfaces** — payments, realtime, AI, native
  mobile, user-created tournaments. Excluded by the PRD's Non-Goals, not
  by test budget. (Source: `context/foundation/prd.md` Non-Goals.)

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-09-02
- Stack versions last verified: 2026-08-31
- AI-native tool references last verified: 2026-08-31

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
