import { expect, test } from "@playwright/test"

const API_ORIGIN = "https://localhost:7182"

test("register → /app → sign out → cookie cleared", async ({ page }) => {
  const unique = `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
  const email = `${unique}@example.test`
  const password = "Password123!"
  const displayName = `E2E ${unique}`

  await page.goto("/sign-in")
  await page.getByRole("tab", { name: "Register" }).click()
  await page.getByLabel("Display name").fill(displayName)
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Password").fill(password)
  await page.getByRole("button", { name: /create account/i }).click()

  await expect(page).toHaveURL(/\/app$/)
  await expect(page.getByText(displayName)).toBeVisible()

  await page.getByRole("button", { name: /sign out/i }).click()
  await expect(page).toHaveURL(/\/$/)

  const status = await page.evaluate(async (origin) => {
    const res = await fetch(`${origin}/api/auth/me`, { credentials: "include" })
    return res.status
  }, API_ORIGIN)
  expect(status).toBe(401)
})

// The swallowed sign-out. The logout POST never reaches the server, so SignInManager never clears
// the cookie — but local state drops to "anonymous" anyway (it must: the button has already left
// the guarded subtree). Before the fix that combination was reported as a clean sign-out and the
// only trace was a console.error, so the next probe silently signed the user back in.
//
// Both halves are asserted. The warning alone would pass against a UI that warns and still ends
// the session; the live cookie alone would pass against the old silent behaviour.
test("sign out with the logout request blocked → the UI says the session may still be open", async ({ page }) => {
  const unique = `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
  const email = `${unique}@example.test`
  const password = "Password123!"
  const displayName = `E2E ${unique}`

  await page.goto("/sign-in")
  await page.getByRole("tab", { name: "Register" }).click()
  await page.getByLabel("Display name").fill(displayName)
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Password").fill(password)
  await page.getByRole("button", { name: /create account/i }).click()

  await expect(page).toHaveURL(/\/app$/)

  // The failure under test: the request never lands. Registered after sign-in so only the
  // sign-out call is affected.
  await page.route("**/api/auth/logout", (route) => route.abort())

  await page.getByRole("button", { name: /sign out/i }).click()
  await expect(page).toHaveURL(/\/$/)

  await expect(
    page.getByText(/couldn't reach the server to sign you out/i),
  ).toBeVisible()
  await expect(page.getByRole("button", { name: /try again/i })).toBeVisible()

  // The state the warning is about: nothing cleared the cookie, so the session is still live.
  const status = await page.evaluate(async (origin) => {
    const res = await fetch(`${origin}/api/auth/me`, { credentials: "include" })
    return res.status
  }, API_ORIGIN)
  expect(status).toBe(200)
})
