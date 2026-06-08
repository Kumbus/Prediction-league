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
