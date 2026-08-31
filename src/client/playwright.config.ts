import { defineConfig } from "@playwright/test"

export default defineConfig({
  testDir: "./tests/e2e",
  // Every spec owns its setup, action, assertion and data, so order is never load-bearing.
  fullyParallel: true,
  // A stray .only would silently shrink the suite in CI.
  forbidOnly: !!process.env.CI,
  reporter: "list",
  // The suite drives a stack it deliberately does not start (SQL Server + API + SPA). The
  // preflight turns "nothing is running" into one actionable line instead of a wall of
  // navigation timeouts.
  globalSetup: "./tests/e2e/global-setup.ts",
  use: {
    baseURL: "https://localhost:5173",
    // The dev server serves a self-signed certificate (@vitejs/plugin-basic-ssl).
    ignoreHTTPSErrors: true,
  },
  projects: [
    // Authentication and fixture data are built once per run, never per test.
    { name: "setup", testMatch: /.*\.setup\.ts/ },
    { name: "e2e", testMatch: /.*\.spec\.ts/, dependencies: ["setup"] },
  ],
})
