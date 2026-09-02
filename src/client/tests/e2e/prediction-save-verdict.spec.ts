import { expect, request, test } from "@playwright/test"
import type { APIRequestContext } from "@playwright/test"
import {
  createLeague,
  createMatch,
  listTeams,
  createTournament,
  deleteTournament,
  leaveLeague,
  publishTournament,
  updateMatch,
} from "./fixtures/api"
import { API_ORIGIN, adminStatePath, memberStatePath } from "./fixtures/run"

// Risk #6 (context/foundation/test-plan.md §2), the half predictions-legibility.spec.ts does not
// cover: "…believes they submitted when they did not."
//
// A match can kick off between the moment the page loads and the moment Save is pressed. The
// server answers 200 and rejects that item inside the body (PredictionsController.cs:170-176), so
// the ONLY thing standing between the member and a false belief is what the screen then says.
//
// This is browser-level by construction: that the server refuses a post-kickoff write is risk #4,
// provable at the API layer. That the member is *told* exists solely in the rendered UI.
//
// Boundaries: everything real — auth, routing, API, database. Nothing is mocked; there is no
// external service in this path, and the lock is precisely an integration behaviour.
//
// Modelled on seed.spec.ts: role-based locators, wait for state, unique run data, and a full
// self-owned cycle that cleans up after itself.

test.use({ storageState: memberStatePath })

async function apiAs(storageStatePath: string): Promise<APIRequestContext> {
  return request.newContext({
    baseURL: API_ORIGIN,
    ignoreHTTPSErrors: true,
    storageState: storageStatePath,
  })
}

test("a forecast rejected because the match kicked off is reported, not silently dropped", async ({
  page,
}) => {
  const run = `verdict-${Date.now()}`
  const admin = await apiAs(adminStatePath)
  const member = await apiAs(memberStatePath)

  // Owns its own tournament, league and match rather than borrowing the shared fixture: this test
  // must move a kickoff, and doing that to a shared match would break every spec that reads it.
  let tournamentId: string | undefined
  let leagueId: string | undefined

  try {
    // A published tournament with one match that is still open, and a league on it. The member
    // creates the league, so they are already its sole member.
    const tournament = await createTournament(admin, `${run} Cup`, 2026, "2026-08-01", "2026-12-01")
    tournamentId = tournament.id
    await publishTournament(admin, tournament.id)

    // Borrowed, not created: teams are global reference data with no DELETE route, so creating a
    // pair here would leak two rows on every run. This test does not care which teams play — only
    // that their names label the two score inputs. The setup:fixture project guarantees plenty.
    const teams = await listTeams(admin)
    expect(
      teams.length,
      "expected at least two teams to borrow — the setup:fixture project should have created them",
    ).toBeGreaterThanOrEqual(2)
    const [home, away] = teams

    const openKickoff = new Date(Date.now() + 3 * 24 * 3_600_000).toISOString()
    const match = await createMatch(admin, tournament.id, {
      homeTeamId: home.id,
      awayTeamId: away.id,
      kickoffUtc: openKickoff,
      status: "Scheduled",
      homeScore: null,
      awayScore: null,
      round: "R1",
    })

    const league = await createLeague(member, `${run} League`, tournament.id, [
      { parameter: "ExactScore", points: 5 },
    ])
    leagueId = league.id

    // The member opens the round while the match is still writable and fills in a forecast.
    await page.goto(`/app/leagues/${league.id}/predictions`)
    await page.getByRole("spinbutton", { name: home.name }).fill("2")
    await page.getByRole("spinbutton", { name: away.name }).fill("1")

    // The match kicks off while that form sits open. Moving the kickoff is the only lever a test
    // has over the lock — there is no injected clock (PredictionsController.cs:331) — and it makes
    // the race deterministic instead of something to wait for.
    await updateMatch(admin, match.id, {
      homeTeamId: home.id,
      awayTeamId: away.id,
      kickoffUtc: new Date(Date.now() - 2 * 3_600_000).toISOString(),
      status: "Live",
      homeScore: null,
      awayScore: null,
      round: "R1",
    })

    // The member presses Save, believing the forecast is going in.
    await page.getByRole("button", { name: "Save round" }).click()

    // The member is told, in their own words, that the match closed. Asserting the rendered
    // verdict is the whole point: the request itself succeeded with 200, so any test built on the
    // status code would pass while the member was misled.
    const verdict = page.getByRole("status")
    await expect(verdict).toContainText("Locked — kicked off")
    // The risk is a false belief, so the absence of the success wording is as load-bearing as the
    // presence of the failure wording.
    await expect(verdict).not.toContainText("Saved")

    // And the screen agrees with the database: nothing was stored. Without this the test would
    // prove only that a message appeared, not that the message was true.
    await expect(page.getByText("You did not forecast this match.")).toBeVisible()
  } finally {
    // Cleanup, in dependency order: a tournament cannot be deleted while a league references it.
    // Leaving as the sole member destroys the league; deleting the tournament cascades the match.
    if (leagueId) await leaveLeague(member, leagueId)
    if (tournamentId) await deleteTournament(admin, tournamentId)
    await admin.dispose()
    await member.dispose()
  }
})
