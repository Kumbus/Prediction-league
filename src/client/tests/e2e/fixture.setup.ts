import { mkdirSync, writeFileSync } from "node:fs"
import path from "node:path"
import { request, test as setup } from "@playwright/test"
import type { APIRequestContext } from "@playwright/test"
import type { MatchInput, TeamResponse } from "./fixtures/api"
import {
  createLeague,
  createMatch,
  createTeam,
  createTournament,
  me,
  publishTournament,
  submitPredictions,
  updateMatch,
} from "./fixtures/api"
import type { FixtureManifest } from "./fixtures/manifest"
import {
  API_ORIGIN,
  LEAGUE_A_EXACT_SCORE_POINTS,
  LEAGUE_B_CORRECT_OUTCOME_POINTS,
  adminStatePath,
  manifestPath,
  memberStatePath,
  runId,
} from "./fixtures/run"

const ROUND = "R1"

// One forecast per predicted match, deliberately different from each other: the specs locate a
// locked row by its rendered "Your forecast: H–A" text, so two rows sharing a score would make
// that locator ambiguous.
const UNSCORED_FORECAST = { home: 3, away: 0 }
const SCORED_FORECAST = { home: 2, away: 1 }
// Matches SCORED_FORECAST exactly, so League A's ExactScore rule fires and League B's
// CorrectOutcome rule (home win) fires — the divergence risk #2 is about.
const SCORED_RESULT = { home: 2, away: 1 }

const hoursFromNow = (h: number) => new Date(Date.now() + h * 3_600_000).toISOString()
const isoDate = (d: Date) => d.toISOString().slice(0, 10)

async function contextFor(storageStatePath: string): Promise<APIRequestContext> {
  return request.newContext({
    baseURL: API_ORIGIN,
    ignoreHTTPSErrors: true,
    storageState: storageStatePath,
  })
}

setup("fixture graph is built and the manifest is written", async () => {
  const admin = await contextFor(adminStatePath)
  const member = await contextFor(memberStatePath)

  try {
    // Identity comes from the API rather than from run.ts: the two setup projects are separate
    // processes, so their module-level runId values are not the same value.
    const profile = await me(member)

    // ---- Ordering is load-bearing; see the plan's Critical Implementation Details. ----------

    // 1. A tournament, published — an unpublished one cannot carry a league.
    const now = new Date()
    const tournament = await createTournament(
      admin,
      `${runId} Cup`,
      now.getUTCFullYear(),
      isoDate(new Date(now.getTime() - 30 * 24 * 3_600_000)),
      isoDate(new Date(now.getTime() + 30 * 24 * 3_600_000)),
    )
    await publishTournament(admin, tournament.id)

    // 2. Both leagues, before any match has kicked off — scoring config freezes at first kickoff.
    //    The member creates them, so the member is already a member of both.
    const leagueA = await createLeague(member, `${runId} League A`, tournament.id, [
      { parameter: "ExactScore", points: LEAGUE_A_EXACT_SCORE_POINTS },
    ])
    const leagueB = await createLeague(member, `${runId} League B`, tournament.id, [
      { parameter: "CorrectOutcome", points: LEAGUE_B_CORRECT_OUTCOME_POINTS },
    ])

    // 3. Eight teams. Names are globally unique server-side, hence the run suffix.
    const teamNames = ["Open", "Unscored", "Silent", "Scored"].flatMap((role) => [
      `${runId} ${role} Home`,
      `${runId} ${role} Away`,
    ])
    const teams: TeamResponse[] = []
    for (const name of teamNames) teams.push(await createTeam(admin, name))
    const [openHome, openAway, unscoredHome, unscoredAway, silentHome, silentAway, scoredHome, scoredAway] =
      teams

    const matchInput = (
      home: TeamResponse,
      away: TeamResponse,
      overrides: Partial<MatchInput>,
    ): MatchInput => ({
      homeTeamId: home.id,
      awayTeamId: away.id,
      kickoffUtc: hoursFromNow(24),
      status: "Scheduled",
      homeScore: null,
      awayScore: null,
      round: ROUND,
      ...overrides,
    })

    // 4. The three matches that need a forecast are created OPEN — a forecast can only be filed
    //    while its match is still writable.
    const openMatch = await createMatch(admin, tournament.id, matchInput(openHome, openAway, {}))
    const unscoredMatch = await createMatch(
      admin,
      tournament.id,
      matchInput(unscoredHome, unscoredAway, { kickoffUtc: hoursFromNow(48) }),
    )
    const scoredMatch = await createMatch(
      admin,
      tournament.id,
      matchInput(scoredHome, scoredAway, { kickoffUtc: hoursFromNow(72) }),
    )

    // 5. The same two forecasts in both leagues. Identical inputs, divergent totals later — that
    //    is the whole of risk #2. The open match is left alone; the risk-#6 spec fills it in.
    const forecasts = [
      {
        matchId: unscoredMatch.id,
        homeScore: UNSCORED_FORECAST.home,
        awayScore: UNSCORED_FORECAST.away,
      },
      {
        matchId: scoredMatch.id,
        homeScore: SCORED_FORECAST.home,
        awayScore: SCORED_FORECAST.away,
      },
    ]
    await submitPredictions(member, leagueA.id, forecasts)
    await submitPredictions(member, leagueB.id, forecasts)

    // 6. The never-forecast match, created already kicked off.
    const notPredictedMatch = await createMatch(
      admin,
      tournament.id,
      matchInput(silentHome, silentAway, { kickoffUtc: hoursFromNow(-3), status: "Live" }),
    )

    // 7. Lock the two forecast matches. Kickoff timestamps are the only lever over the lock —
    //    there is no injected clock (PredictionsController.cs:331).
    //    Live with no score keeps the forecast UNSCORED: MatchScoringService.cs:67 un-scores
    //    anything that is not Finished-with-both-scores, so the row renders no points at all.
    await updateMatch(
      admin,
      unscoredMatch.id,
      matchInput(unscoredHome, unscoredAway, { kickoffUtc: hoursFromNow(-2), status: "Live" }),
    )
    // Finished with a result — scoring runs inside this very request.
    await updateMatch(
      admin,
      scoredMatch.id,
      matchInput(scoredHome, scoredAway, {
        kickoffUtc: hoursFromNow(-4),
        status: "Finished",
        homeScore: SCORED_RESULT.home,
        awayScore: SCORED_RESULT.away,
      }),
    )

    const manifest: FixtureManifest = {
      runId,
      member: { id: profile.id, displayName: profile.displayName },
      tournament: { id: tournament.id, name: tournament.name },
      round: ROUND,
      leagues: {
        a: { id: leagueA.id, name: leagueA.name },
        b: { id: leagueB.id, name: leagueB.name },
      },
      matches: {
        open: {
          id: openMatch.id,
          homeTeamName: openHome.name,
          awayTeamName: openAway.name,
        },
        lockedUnscored: {
          id: unscoredMatch.id,
          homeTeamName: unscoredHome.name,
          awayTeamName: unscoredAway.name,
          forecast: UNSCORED_FORECAST,
        },
        lockedNotPredicted: {
          id: notPredictedMatch.id,
          homeTeamName: silentHome.name,
          awayTeamName: silentAway.name,
        },
        scored: {
          id: scoredMatch.id,
          homeTeamName: scoredHome.name,
          awayTeamName: scoredAway.name,
          forecast: SCORED_FORECAST,
          result: SCORED_RESULT,
        },
      },
    }

    mkdirSync(path.dirname(manifestPath), { recursive: true })
    writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8")
  } finally {
    await admin.dispose()
    await member.dispose()
  }
})
