import { expect, test } from "@playwright/test"
import { readManifest } from "./fixtures/manifest"
import { memberStatePath } from "./fixtures/run"

// Risk #6 (context/foundation/test-plan.md §2): "A member misreads the prediction screen —
// cannot tell what is still editable, what was saved, or what a given entry is worth — and
// submits something other than intended, or believes they submitted when they did not."
//
// Every assertion here is on copy a member actually reads. Asserting toBeDisabled() would
// prove only that a control is inert, never that a human can tell why — which is the risk.
//
// The fixture is built once per run by the setup:fixture project; readManifest() is called
// inside each test because Playwright loads spec files before that project runs.

test.use({ storageState: memberStatePath })

test("an open match can still be edited, and says so when the forecast is saved", async ({
  page,
}) => {
  const fixture = readManifest()
  const match = fixture.matches.open

  await page.goto(`/app/leagues/${fixture.leagues.a.id}/predictions`)

  // The score inputs are labelled with the team names themselves, so this both locates the
  // row and proves it is still editable — a locked row renders no inputs at all.
  await page.getByLabel(match.homeTeamName).fill("1")
  await page.getByLabel(match.awayTeamName).fill("0")
  await page.getByRole("button", { name: "Save round" }).click()

  // The server's per-item verdict, rendered. The batch write answers 200 even when it rejects
  // an item, so the screen — not the status code — is the only honest source of truth here.
  await expect(page.getByRole("status")).toHaveText("Saved")
})

test("a kicked-off match says it is locked and shows the forecast it kept", async ({ page }) => {
  const fixture = readManifest()
  const match = fixture.matches.lockedUnscored
  const forecast = `Your forecast: ${match.forecast.home}–${match.forecast.away}`

  await page.goto(`/app/leagues/${fixture.leagues.a.id}/predictions`)

  // Three of the four fixture matches have kicked off, and each states why it is inert. The
  // count is the assertion: "Locked at kickoff." is shared copy, so it cannot be scoped to one
  // row without a DOM-structure locator.
  await expect(page.getByText("Locked at kickoff.")).toHaveCount(3)

  // Note the en-dash (U+2013) between the scores — the component renders it, a hyphen will not
  // match.
  await expect(page.getByText(forecast)).toBeVisible()
})

test("a member who did not forecast is told so, rather than shown an empty row", async ({
  page,
}) => {
  const fixture = readManifest()

  await page.goto(`/app/leagues/${fixture.leagues.a.id}/predictions`)

  // Only the never-forecast match renders this, so it needs no scoping.
  await expect(page.getByText("You did not forecast this match.")).toBeVisible()
})

test("an unscored match says nothing about points, and a scored one says what they are", async ({
  page,
}) => {
  const fixture = readManifest()
  const unscored = fixture.matches.lockedUnscored
  const scored = fixture.matches.scored

  await page.goto(`/app/leagues/${fixture.leagues.a.id}/predictions`)

  // The sharpest expression of risk #6. The forecast and the points share one <p>, so matching
  // on the forecast text scopes the assertion to exactly one row.
  //
  // A match that has kicked off but has no result yet must stay SILENT about points — never a
  // "0 pts" that a member would read as a verdict on their forecast.
  await expect(
    page.getByText(`Your forecast: ${unscored.forecast.home}–${unscored.forecast.away}`),
  ).not.toContainText("pts")

  // League A scores ExactScore and nothing else, at 5 points. This forecast matched the result
  // exactly (2–1 against 2–1), so it is worth 5 — no other rule can contribute. Literal by
  // design: test-plan.md §6.1 forbids deriving the expected total from the rule list.
  await expect(
    page.getByText(`Your forecast: ${scored.forecast.home}–${scored.forecast.away}`),
  ).toContainText("· 5 pts")
})

test("saving an unchanged round reports that nothing changed, instead of silently succeeding", async ({
  page,
}) => {
  const fixture = readManifest()

  await page.goto(`/app/leagues/${fixture.leagues.a.id}/predictions`)
  // Wait for the round to render before touching Save, so this cannot race the initial load.
  await expect(page.getByRole("button", { name: "Save round" })).toBeVisible()

  await page.getByRole("button", { name: "Save round" }).click()

  await expect(page.getByRole("alert")).toHaveText("Nothing to save — no forecast has changed.")
})

test("a half-filled score is refused by name, so the member knows which match to fix", async ({
  page,
}) => {
  const fixture = readManifest()
  const match = fixture.matches.open

  await page.goto(`/app/leagues/${fixture.leagues.a.id}/predictions`)

  // Clear both halves first: another test in this file may already have saved a forecast for
  // this match, and starting from a known-empty row is what makes the outcome independent of
  // whether it did.
  await page.getByLabel(match.homeTeamName).fill("")
  await page.getByLabel(match.awayTeamName).fill("")
  await page.getByLabel(match.homeTeamName).fill("2")

  await page.getByRole("button", { name: "Save round" }).click()

  await expect(page.getByRole("alert")).toHaveText(
    `Enter both scores for: ${match.homeTeamName} v ${match.awayTeamName}.`,
  )
})

test("once a match has kicked off, every member's forecast is revealed", async ({ page }) => {
  const fixture = readManifest()

  await page.goto(`/app/leagues/${fixture.leagues.a.id}/predictions`)

  // Rendered only for kicked-off matches that actually have forecasts — the two this member
  // predicted. The never-forecast match shows no panel at all, deliberately: an empty reveal
  // would read as "nobody predicted" rather than "nothing to show".
  //
  // Its fetch failure is swallowed so the form survives (PredictionsPage.tsx:94-96), so a
  // missing panel is never a page error — which is exactly why this is asserted, not assumed.
  await expect(page.getByText(/Everyone.s forecasts/)).toHaveCount(2)
})
