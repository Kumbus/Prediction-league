import { expect, test } from "@playwright/test"
import type { Page } from "@playwright/test"
import { readManifest } from "./fixtures/manifest"
import { memberStatePath } from "./fixtures/run"

// Risk #2 (context/foundation/test-plan.md §2): "Two leagues on the same tournament produce
// identical points for an identical forecast — per-league custom scoring, the product's wedge,
// silently stops being custom."
//
// One member, one forecast, one result, two leagues with contrasting rules. Both leagues are
// scored from the same match by the same engine; only the rule set differs.
//
// Each expected total below is a LITERAL, justified from that league's own configuration and
// ordinary football semantics. test-plan.md §6.1 forbids computing it from the rule list, and
// forbids asserting merely that the two totals differ — that is satisfied by any two wrong
// numbers that happen to be unequal.

test.use({ storageState: memberStatePath })

// The standings table is a real <table>: #, Member, Points, Matches scored. The member's row is
// found by their display name, and Points is that row's third cell.
function memberRow(page: Page, displayName: string) {
  return page.getByRole("row").filter({ hasText: displayName })
}

test("a league scoring only ExactScore awards its own exact-score points", async ({ page }) => {
  const fixture = readManifest()

  await page.goto(`/app/leagues/${fixture.leagues.a.id}/standings`)
  await expect(
    page.getByRole("heading", { name: `${fixture.leagues.a.name} — standings` }),
  ).toBeVisible()

  const row = memberRow(page, fixture.member.displayName)
  // Guards the locator itself: this league has exactly one member, so more than one match would
  // mean the filter is picking up something other than the member's row.
  await expect(row).toHaveCount(1)

  // League A scores ExactScore at 5 points and configures no other parameter. The member
  // forecast 2–1 and the match finished 2–1 — the score is exactly right — so the forecast is
  // worth 5. Nothing else in this league can contribute a point.
  await expect(row.getByRole("cell").nth(2)).toHaveText("5")

  // One match has a result; the open one is deliberately never scored, so it must not count.
  await expect(row.getByRole("cell").nth(3)).toHaveText("1")
})

test("a league scoring only CorrectOutcome awards its own outcome points for the same forecast", async ({
  page,
}) => {
  const fixture = readManifest()

  await page.goto(`/app/leagues/${fixture.leagues.b.id}/standings`)
  await expect(
    page.getByRole("heading", { name: `${fixture.leagues.b.name} — standings` }),
  ).toBeVisible()

  const row = memberRow(page, fixture.member.displayName)
  await expect(row).toHaveCount(1)

  // League B scores CorrectOutcome at 3 points and configures no other parameter. The same
  // 2–1 forecast called a home win, and the match finished 2–1 — a home win — so it is worth 3.
  // That the score was also exactly right earns nothing here: this league does not score
  // ExactScore. This is the divergence, and 3 is not 5.
  await expect(row.getByRole("cell").nth(2)).toHaveText("3")

  await expect(row.getByRole("cell").nth(3)).toHaveText("1")
})
