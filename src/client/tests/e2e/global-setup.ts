import { request } from "@playwright/test"
import type { FullConfig } from "@playwright/test"

// Matches .env.development; overridable so the suite can be pointed at another API host.
const API_ORIGIN = process.env.VITE_API_BASE_URL ?? "https://localhost:7182"

const PROBE_TIMEOUT_MS = 4000

const HOW_TO_START = [
  "The E2E suite drives a running stack; it does not start one.",
  "",
  "Start all three, then re-run:",
  "  1. SQL Server — the API will not start without it",
  "  2. cd src/server && dotnet run   (https profile)",
  "  3. cd src/client && npm run dev",
].join("\n")

// Fail fast and legibly when the stack is down. Without this the whole suite dies in
// navigation timeouts that say nothing about the cause.
export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0]?.use.baseURL ?? "https://localhost:5173"
  const context = await request.newContext({ ignoreHTTPSErrors: true })
  const unreachable: string[] = []

  try {
    // The SPA: any response at all proves the dev server is serving.
    try {
      await context.get(baseURL, { timeout: PROBE_TIMEOUT_MS })
    } catch {
      unreachable.push(`SPA at ${baseURL}`)
    }

    // The API: 401 is the correct answer for an anonymous caller and proves reachability.
    // Only a transport failure means the API is down — a status code never does.
    try {
      await context.get(`${API_ORIGIN}/api/auth/me`, { timeout: PROBE_TIMEOUT_MS })
    } catch {
      unreachable.push(`API at ${API_ORIGIN}`)
    }
  } finally {
    await context.dispose()
  }

  if (unreachable.length > 0) {
    throw new Error(`Cannot reach ${unreachable.join(" and ")}.\n\n${HOW_TO_START}`)
  }
}
