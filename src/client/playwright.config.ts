import { defineConfig } from "@playwright/test"

export default defineConfig({
  testDir: "./tests/e2e",
  use: {
    baseURL: "https://localhost:5173",
    ignoreHTTPSErrors: true,
  },
})
