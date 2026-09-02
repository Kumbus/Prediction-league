# Project guidelines

@AGENTS.md

---

<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 3, Lesson 4 (E2E Tests)

**For E2E tests, use the `/10x-e2e` skill.** It is the single source of truth
for the workflow — risk → seed test + rules → generate → review against the five
anti-patterns → re-prompt → verify. The skill's `references/` carry the full
rules, anti-patterns, seed pattern, and prompt-template.

A few hard rules that hold even before you invoke the skill:

- **Locators:** `getByRole` / `getByLabel` / `getByText` first; `getByTestId`
  only when accessibility attributes are ambiguous. Never CSS selectors, XPath,
  or DOM structure.
- **Never `page.waitForTimeout()`.** Wait for state: `toBeVisible()`,
  `waitForURL()`, `waitForResponse()`.
- **Test independence + cleanup.** Each test runs standalone — its own setup,
  action, assertion, and cleanup; unique ids (timestamp suffix) so parallel runs
  and re-runs don't collide.

Two boundaries to keep straight:

- **DOM (snapshot) is the default.** Vision (`--caps=vision`) is a supplement for
  visual-only risks (layout, z-index, animation); for pixel regression prefer
  deterministic tools (`toMatchSnapshot`, Argos, Lost Pixel). VLM model
  selection/cost is a debugging topic (Lesson 5), not testing.
- **Healer helps on selectors, harms on logic.** A changed selector → healer
  re-finds it (route through PR review). A changed business behavior → healer
  masks the bug; that failing-test-to-fix case is Lesson 5.

<!-- END @przeprogramowani/10x-cli -->

---

## E2E conventions in this repo

Project-specific additions to the rules above. Kept outside the `10x-cli` markers so a
toolkit refresh can't drop them.

- **Never sign in through the UI inside a spec.** Authentication comes from the
  `storageState` files the `setup` project writes. `tests/e2e/auth.spec.ts` is the one
  exception — it is the seed, and signing in *is* what it tests.
- **Never hardcode a fixture id, league name, or team name.** They come from the manifest
  the setup project emits; every one carries a per-run unique suffix, because team names
  are globally unique server-side and a re-run would otherwise 409.
- **The stack is not started for you.** API, SPA and SQL Server must already be running;
  `tests/e2e/global-setup.ts` fails fast and tells you which one is missing.
- **The E2E admin lives at `Admin:Emails:0`** in `appsettings.Development.json`
  (`e2e-admin@example.test`). Configuration merges arrays **by index** and user-secrets load
  *after* appsettings, so a personal admin in user-secrets must sit at `Admin:Emails:1` or
  higher — putting one at index 0 silently replaces the E2E entry, and the suite then fails
  two phases later on an `isGlobalAdmin` assertion with nothing pointing at the cause.
- **Assert the rendered verdict, never the HTTP status.** The prediction batch write and
  the admin match write both answer `200` while carrying a failure in the body
  (`Saved`/`Locked`/`Invalid`, and `ScoringFailed`). A test built on status alone passes
  against a broken system.
- **Expected point totals are literals, justified from the league's own rules** in a
  comment beside the assertion — never computed by summing a rule list
  (`context/foundation/test-plan.md` §6.1, the oracle constraint).
- **There is no injected clock.** The only lever a test has over the kickoff lock is the
  kickoff timestamp it writes.
- **The locked-row dash is an en-dash (U+2013)**: `Your forecast: 2–1`. A hyphen will not
  match.
- **A new spec that writes must claim a match no other spec reads.** Specs share one
  fixture graph.
