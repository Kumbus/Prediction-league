import { expect, test } from "@playwright/test"
import { readManifest } from "./fixtures/manifest"
import { memberStatePath } from "./fixtures/run"

// Proves the harness itself before any risk spec leans on it: the stored member session
// authenticates a real browser, the manifest resolves, and the fixture league renders.
test.use({ storageState: memberStatePath })

test("the stored member session opens the fixture league's predictions page", async ({ page }) => {
  const fixture = readManifest()

  await page.goto(`/app/leagues/${fixture.leagues.a.id}/predictions`)

  // Landing on /sign-in instead is the signature of a stale or unloaded storageState.
  await expect(page).toHaveURL(new RegExp(`/app/leagues/${fixture.leagues.a.id}/predictions$`))
  await expect(
    page.getByRole("heading", { name: `${fixture.leagues.a.name} — predictions` }),
  ).toBeVisible()
})
