import { readFileSync } from "node:fs"
import { manifestPath } from "./run"

export interface ManifestMatch {
  id: string
  homeTeamName: string
  awayTeamName: string
}

export interface ForecastScore {
  home: number
  away: number
}

export interface FixtureManifest {
  runId: string
  member: { id: string; displayName: string }
  /** Published, so a league can be created on it — the seed test picks it in the form. */
  tournament: { id: string; name: string }
  round: string
  leagues: {
    a: { id: string; name: string }
    b: { id: string; name: string }
  }
  matches: {
    /** Kickoff in the future, no forecast — the editable row. */
    open: ManifestMatch
    /** Kicked off with a forecast but no result — renders the forecast and NO points. */
    lockedUnscored: ManifestMatch & { forecast: ForecastScore }
    /** Kicked off, never forecast. */
    lockedNotPredicted: ManifestMatch
    /** Kicked off, forecast, result entered — the only scored match. */
    scored: ManifestMatch & { forecast: ForecastScore; result: ForecastScore }
  }
}

export function readManifest(): FixtureManifest {
  try {
    return JSON.parse(readFileSync(manifestPath, "utf8")) as FixtureManifest
  } catch {
    throw new Error(
      `No fixture manifest at ${manifestPath}.\n` +
        "It is written by the setup:fixture project — run `npm run e2e`, not a bare `playwright test <file>`.",
    )
  }
}
