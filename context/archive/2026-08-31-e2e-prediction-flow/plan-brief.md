# Browser-level coverage for the prediction screen and per-league scoring divergence — Plan Brief

> Full plan: `context/changes/e2e-prediction-flow/plan.md`
> Research: `context/changes/e2e-prediction-flow/research.md`

## What & Why

Two of this project's risks have only ever been checked by hand. **Risk #6** — a
member misreads the prediction screen and cannot tell what is still editable,
what was saved, or what an entry is worth. **Risk #2** — two leagues on the same
tournament silently stop producing different totals for an identical forecast,
and the product's wedge quietly stops being custom. This change gives both
browser-level coverage and closes `test-plan.md` §3 Phase 4.

## Starting Point

Both features are built and routed, so there is nothing to implement first. What
is missing is the test foundation: `playwright.config.ts` is five lines with no
projects, no `storageState` and no global setup; the single existing spec
(`auth.spec.ts`) is **red** because of a render-ordering race on sign-out; the
admin identity a fixture needs is gated behind an exact-match config allowlist;
and CI runs no Playwright at all.

## Desired End State

`npm run e2e` from `src/client` runs green against a locally running stack, and
two specs stand between the team and these risks: one that reads the prediction
screen the way a member would and fails the instant an unscored row shows an
ambiguous `0`, and one that reads two leagues' standings and asserts each
league's own number. The test plan's cookbook explains how to add the next one.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Admin identity | One fixed allowlisted E2E admin, registered once then logged in | An exact-match allowlist cannot be satisfied by a per-run unique email, and widening it would mean changing production security code | Plan |
| Fixture setup | API-driven; browser only on the two screens under test | The risks live in the prediction and standings screens, and test-plan §7 excludes admin UI from testing altogether | Plan |
| Data isolation | Unique per-run ids, no teardown | There is no `DELETE /api/leagues/{id}` and tournament deletion 409s while a league references it, so API teardown is not available | Plan |
| Seed test | `/` is the intended post-sign-out destination; fix the race | `SignOutButton` already declares that intent, and bouncing to a sign-in form on sign-out is a confusing loop | Plan |
| Risk #6 scope | Editable/Saved, locked-with-forecast, locked-never-predicted, scored vs unscored | Covers the three questions the risk names; the mid-save server-verdict choreography is deliberately excluded | Plan |
| Risk #2 assertion | Each league's specific total, as a justified literal | §6.1: asserting only "the two differ" is satisfied by any two wrong numbers that happen to be unequal | Research + Plan |
| Actors | Admin + one member | The member creating a league is auto-enrolled, so one member covers both leagues; multi-row tables are risk #7's job in Phase 3 | Plan |
| Fixture wiring | Setup project → `storageState` + a JSON manifest | The `storageState` pattern the project's E2E rules mandate, and setup runs once rather than per test | Plan |
| Servers | No `webServer`; a fail-fast preflight instead | SQL Server is external either way, so orchestration would trade a clear failure for an obscure one | Plan |
| CI gate | Local gate now; amend §5 and defer CI to Phase 5 | There is no PR-triggered server workflow at all, which §5 already assigns to Phase 5 | Plan |

## Scope

**In scope:** the seed-test fix; a real Playwright config with a preflight and
per-role auth; an API-driven fixture graph and manifest; a risk-#6 spec; a
risk-#2 spec; mutation checks on both; one scripted visual review; test-plan
§6.4/§6.5 cookbook and §5/§3 status sync.

**Out of scope:** admin-UI coverage; goal-scorer and card scoring parameters; a
second member (ties, rank skips, multi-row tables — risk #7); the mid-save
`Rejected`/`Locked` verdict choreography; any teardown; `webServer`, containers,
or a CI job; client unit tests.

## Architecture / Approach

A `setup` project runs once: it authenticates the two identities into
`storageState` files, then builds the graph through the API — tournament →
publish → two leagues with contrasting rules → four uniquely-named teams → four
matches in one round → forecasts → kickoff shifts → one result — and writes the
ids to a manifest. Spec projects declare `dependencies: ['setup']`, load the
manifest, and open only `/predictions` and `/standings`.

Four matches carry the whole story: **M1** open (editable → Saved), **M2**
kicked off with a forecast and no result (locked, and *silent* about points),
**M3** kicked off and never forecast, **M4** kicked off, forecast and scored
(`· N pts`, and the two divergent league totals). M4 is the only match both
specs read; M1 is the only one a spec writes.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Foundation and the seed | Green seed spec, real config, preflight, admin allowlist entry, E2E rules | The sign-out fix is a render race — reasoning about it is not enough, it must be run |
| 2. Fixture layer | Two storage states, the four-match two-league graph, a manifest, a smoke spec | Fixture ordering is load-bearing and the obvious order fails |
| 3. Risk #6 spec | Prediction-screen legibility, with mutation checks | A negative assertion that is not scoped to one row is decorative |
| 4. Risk #2 spec | Per-league divergence on standings, with a mutation check | Expected totals must be justified from the rules, not read off the engine |
| 5. Visual review and close-out | Visual-review record, cookbook §6.4/§6.5, test-plan status and gate sync | Doing the multimodal half as a formality rather than a real read |

**Prerequisites:** SQL Server, the API (`dotnet run` in `src/server`) and the SPA
(`npm run dev` in `src/client`) all running; the E2E admin address present in the
API's development configuration.
**Estimated effort:** ~3–4 sessions across five phases; phases 1 and 2 are the
bulk of the work, and phases 3–5 are comparatively cheap once the fixture exists.

## Open Risks & Assumptions

- The sign-out failure is a render-ordering race diagnosed from the source. The
  fix's shape is right, but the exact ordering must be confirmed against the
  running app in Phase 1.
- Without teardown, the local database grows by one member, one tournament, four
  teams, four matches and two leagues per run. Acceptable for a dev box; it will
  need revisiting if the suite ever runs frequently or shares an environment.
- Every machine running the suite needs the admin allowlist entry. Phase 2's
  setup fails with an explicit message when it is missing, so the failure is at
  least self-diagnosing.
- The specs share one fixture graph. The M1-only-writer rule keeps that safe
  today; a future spec that writes must claim its own match or the constraint
  breaks silently.
- Nothing mechanically blocks a PR that breaks the predict → score → standings
  loop until test-plan Phase 5 wires CI.

## Success Criteria (Summary)

- A member can be shown, from the specs alone, exactly what the prediction screen
  tells them in each of its states — including that an unscored match says
  nothing rather than `0`.
- Two leagues on one tournament are proven, through the browser, to award two
  different and individually-correct totals for one identical forecast.
- Both specs fail when the behaviour they guard is deliberately broken, and pass
  again when it is restored.
