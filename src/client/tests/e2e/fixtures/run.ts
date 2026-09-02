import path from "node:path"
import { fileURLToPath } from "node:url"

// tests/e2e/fixtures -> src/client
const clientRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "..")

// Matches .env.development; overridable so the suite can be pointed at another API host.
export const API_ORIGIN = process.env.VITE_API_BASE_URL ?? "https://localhost:7182"

// One id per run. Every fixture name derives from it — team names are globally unique
// server-side (TeamsController.cs:34-38 answers 409 on a duplicate), so a re-run would
// otherwise collide. There is no teardown; this is what keeps runs from colliding.
export const runId = `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`

// The stable, allowlisted admin (appsettings.Development.json -> Admin:Emails[0]). Registered
// on the first ever run, signed in on every run after: AdminEmailAllowlist is an exact-match
// set, so a per-run unique address can never be promoted.
export const ADMIN_EMAIL = "e2e-admin@example.test"
export const ADMIN_PASSWORD = "Password123!"
export const ADMIN_DISPLAY_NAME = "E2E Admin"

// The member is minted fresh per run and owns every forecast.
export const MEMBER_EMAIL = `${runId}@example.test`
export const MEMBER_PASSWORD = "Password123!"
export const MEMBER_DISPLAY_NAME = `E2E Member ${runId}`

// The two leagues' scoring configuration. Setup uses these to CONFIGURE the leagues; the specs
// assert their expected totals as literals derived from these rules by hand, never by reading
// these constants (context/foundation/test-plan.md §6.1, the oracle constraint). Changing a
// value here must turn the risk-#2 spec red — that is the point.
export const LEAGUE_A_EXACT_SCORE_POINTS = 5
export const LEAGUE_B_CORRECT_OUTCOME_POINTS = 3

export const adminStatePath = path.join(clientRoot, "playwright", ".auth", "admin.json")
export const memberStatePath = path.join(clientRoot, "playwright", ".auth", "member.json")
export const manifestPath = path.join(clientRoot, "playwright", ".fixtures", "manifest.json")
