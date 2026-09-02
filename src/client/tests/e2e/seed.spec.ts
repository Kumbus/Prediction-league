import { expect, test } from "@playwright/test"
import { readManifest } from "./fixtures/manifest"
import { memberStatePath } from "./fixtures/run"

// ─────────────────────────────────────────────────────────────────────────────
// SEED TEST — the exemplar every generated E2E test in this repo is modelled on.
//
// What you show is what you get: whatever this file does, generated tests copy.
// If it used a CSS selector, they would too. If it slept for two seconds, they
// would too. So it deliberately demonstrates the four patterns, and nothing else:
//
//   1. Role-based locators   — getByRole / getByLabel, never CSS, XPath or DOM
//                              structure. These survive a component refactor and
//                              are what the agent actually sees in accessibility
//                              snapshots.
//   2. Test independence     — one test, one full cycle: setup, action, assertion,
//                              cleanup. Nothing here depends on another test
//                              having run, so parallel and random order are safe.
//   3. Wait for state        — web-first assertions and waitForURL. There is no
//                              waitForTimeout in this repo and there must never be:
//                              it passes on a fast laptop and flakes in CI.
//   4. Risk-tied assertion   — the name states an outcome a member would recognise,
//                              and the assertion fails if that outcome is lost.
//
// Risk anchor: the closest entry in context/foundation/test-plan.md §2 is risk #3
// (a league write that silently fails to persist). Its *authoritative* coverage is
// integration, in rollout Phase 2 — this test does not replace it. The risk-carrying
// browser specs are predictions-legibility.spec.ts (#6) and
// league-scoring-divergence.spec.ts (#2).
//
// Note what this file does NOT do: it never signs in through the UI. Authentication
// comes from storageState written by the setup:auth project. auth.spec.ts is the one
// exception in this suite, because signing in is the thing it tests.
// ─────────────────────────────────────────────────────────────────────────────

test.use({ storageState: memberStatePath })

test("a league created through the UI survives a page reload", async ({ page }) => {
  const fixture = readManifest()

  // Unique per run, so repeat runs and parallel workers never collide on a name.
  // Every fixture and test datum in this suite carries an identifier like this —
  // it is the only thing standing between a green suite and a duplicate-key failure.
  const leagueName = `Seed League ${Date.now()}`

  // ── Setup + action ─────────────────────────────────────────────────────────
  await page.goto("/app/leagues/new")

  // getByRole with an exact name, not getByLabel("Name"): label matching is case-insensitive
  // SUBSTRING matching by default, and "Tournament" contains "name" — so getByLabel("Name")
  // matches this input *and* the tournament select. That produced a strict-mode violation only
  // once the select had finished loading, i.e. an intermittent failure that looked like a race.
  // Prefer the role, and pin the name.
  await page.getByRole("textbox", { name: "Name", exact: true }).fill(leagueName)

  // The form renders "Loading tournaments…" in place of the select until the list arrives, so
  // wait for the control itself to exist before touching it. Waiting for the state you depend on
  // is the pattern — never a sleep, and never relying on a step's implicit timeout to absorb it.
  const tournament = page.getByRole("combobox", { name: "Tournament" })
  await expect(tournament).toBeVisible()
  // Selected by the tournament's id (the option's value), not by its visible label:
  // the label is "<name> (<season>)" and would break the day the format changes.
  await tournament.selectOption(fixture.tournament.id)
  // The scoring fieldset arrives with sensible parameters already ticked, so the
  // form is submittable as-is. Leaving it alone keeps this exemplar about the
  // patterns rather than about scoring configuration.
  await page.getByRole("button", { name: "Create league" }).click()

  // ── Assertion: the business outcome ────────────────────────────────────────
  // Not "the request returned 201" — a member cannot see a status code. What they
  // see is their league's page, titled with the name they typed.
  await expect(page.getByRole("heading", { name: leagueName })).toBeVisible()

  // The outcome that matters is persistence across the whole stack — auth, routing,
  // API, database. A reload is what proves the league was really written and not
  // just held in client state.
  await page.reload()
  await expect(page.getByRole("heading", { name: leagueName })).toBeVisible()

  // ── Cleanup ────────────────────────────────────────────────────────────────
  // Leaving a league you are the sole member of destroys it, which is the only
  // path in this API that removes a league (LeaguesController.cs:288-291). That
  // makes this cycle fully reversible: the test leaves nothing behind.
  //
  // The button asks for confirmation via window.confirm, and Playwright dismisses
  // dialogs by default — so accept it explicitly, before the click that opens it.
  page.once("dialog", (dialog) => void dialog.accept())
  await page.getByRole("button", { name: "Leave and delete league" }).click()

  // Cleanup is asserted, not assumed: an unverified teardown quietly accumulates
  // data until a later run fails for reasons that have nothing to do with the test.
  await page.waitForURL(/\/app\/leagues$/)
  await expect(page.getByRole("link", { name: leagueName })).toHaveCount(0)
})
