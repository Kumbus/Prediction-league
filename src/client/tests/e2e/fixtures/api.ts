import type { APIRequestContext, APIResponse } from "@playwright/test"

// The API serializes enums as strings (Program.cs:15 registers JsonStringEnumConverter).
export type ScoringParameter =
  | "ExactScore"
  | "CorrectOutcome"
  | "CorrectGoalScorer"
  | "CorrectCardCount"
  | "CorrectYellowCards"
  | "CorrectRedCards"

export type MatchStatus = "Scheduled" | "Live" | "Finished"

export interface ScoringRuleDto {
  parameter: ScoringParameter
  points: number
}

export interface MeResponse {
  id: string
  email: string
  displayName: string
  isGlobalAdmin: boolean
}

export interface TournamentResponse {
  id: string
  name: string
}

export interface TeamResponse {
  id: string
  name: string
}

export interface LeagueResponse {
  id: string
  name: string
  inviteCode: string
}

export interface MatchResponse {
  id: string
  round: string
  status: MatchStatus
  scoringFailed: boolean
  scoringMessage: string | null
}

// UpdateMatchRequest is a full replace, not a patch — the teams travel with every edit
// (TournamentsController.cs:213-221).
export interface MatchInput {
  homeTeamId: string
  awayTeamId: string
  kickoffUtc: string
  status: MatchStatus
  homeScore: number | null
  awayScore: number | null
  round: string
}

export interface PredictionItem {
  matchId: string
  homeScore: number
  awayScore: number
}

export type PredictionItemStatus = "Saved" | "Locked" | "Invalid"

export interface PredictionOutcome {
  matchId: string
  status: PredictionItemStatus
  detail: string | null
}

async function ok(res: APIResponse, what: string): Promise<APIResponse> {
  if (!res.ok()) throw new Error(`${what} → ${res.status()} ${res.statusText()}\n${await res.text()}`)
  return res
}

async function json<T>(res: APIResponse, what: string): Promise<T> {
  return (await (await ok(res, what)).json()) as T
}

// Both match writes score inside the request (TournamentsController.cs:260,299) and still answer
// 200 when scoring failed, carrying the verdict in the body (ScoringTrigger.cs:12-16). Setup
// must never build on a 200 alone.
async function scoredOk(res: APIResponse, what: string): Promise<MatchResponse> {
  const match = await json<MatchResponse>(res, what)
  if (match.scoringFailed) {
    throw new Error(`${what} saved but scoring failed: ${match.scoringMessage ?? "(no message)"}`)
  }
  return match
}

export async function signIn(api: APIRequestContext, email: string, password: string): Promise<void> {
  await ok(await api.post("/api/auth/login", { data: { email, password } }), `sign in ${email}`)
}

// Identity answers 400 with a DuplicateUserName/DuplicateEmail validation error when the account
// is already there — the expected path for the stable admin on every run after the first.
export async function registerOrSignIn(
  api: APIRequestContext,
  email: string,
  password: string,
  displayName: string,
): Promise<void> {
  const res = await api.post("/api/auth/register", { data: { email, password, displayName } })
  if (res.ok()) return

  const body = await res.text()
  if (res.status() === 400 && /duplicate/i.test(body)) {
    await signIn(api, email, password)
    return
  }
  throw new Error(`register ${email} → ${res.status()} ${res.statusText()}\n${body}`)
}

// AdminOnly is a RequireClaim policy over "prediction:admin", and that claim is baked into the
// cookie when the principal is built — never re-derived per request. So a session can be
// authenticated, report isGlobalAdmin: true from the database, and still 403 on every admin
// write. Only an actual AdminOnly call proves the stored state is usable.
export async function assertAdminAuthorized(api: APIRequestContext): Promise<void> {
  await ok(await api.get("/api/teams"), "admin authorization probe (GET /api/teams)")
}

export async function me(api: APIRequestContext): Promise<MeResponse> {
  return json<MeResponse>(await api.get("/api/auth/me"), "GET /api/auth/me")
}

export async function createTournament(
  api: APIRequestContext,
  name: string,
  season: number,
  startDate: string,
  endDate: string,
): Promise<TournamentResponse> {
  return json<TournamentResponse>(
    await api.post("/api/tournaments", { data: { name, season, startDate, endDate } }),
    `create tournament "${name}"`,
  )
}

// A league can only be created on a PUBLISHED tournament (LeaguesController.cs:143-147).
export async function publishTournament(api: APIRequestContext, tournamentId: string): Promise<void> {
  await ok(
    await api.patch(`/api/tournaments/${tournamentId}/publish`, { data: { isPublished: true } }),
    `publish tournament ${tournamentId}`,
  )
}

export async function createTeam(api: APIRequestContext, name: string): Promise<TeamResponse> {
  return json<TeamResponse>(await api.post("/api/teams", { data: { name } }), `create team "${name}"`)
}

// Creating a league seeds the caller's own organizer membership (LeaguesController.cs:175-184),
// so the creator is already a member — no join step.
export async function createLeague(
  api: APIRequestContext,
  name: string,
  tournamentId: string,
  scoringRules: ScoringRuleDto[],
): Promise<LeagueResponse> {
  return json<LeagueResponse>(
    await api.post("/api/leagues", { data: { name, tournamentId, scoringRules } }),
    `create league "${name}"`,
  )
}

export async function createMatch(
  api: APIRequestContext,
  tournamentId: string,
  match: MatchInput,
): Promise<MatchResponse> {
  return scoredOk(
    await api.post(`/api/tournaments/${tournamentId}/matches`, { data: match }),
    `create match in round "${match.round}"`,
  )
}

export async function updateMatch(
  api: APIRequestContext,
  matchId: string,
  match: MatchInput,
): Promise<MatchResponse> {
  return scoredOk(await api.put(`/api/matches/${matchId}`, { data: match }), `update match ${matchId}`)
}

// The batch write always answers 200 and carries the verdict per item
// (PredictionsController.cs:170-176). Anything but Saved means the fixture is wrong — usually a
// match that locked before its forecast was filed.
export async function submitPredictions(
  api: APIRequestContext,
  leagueId: string,
  items: PredictionItem[],
): Promise<void> {
  const body = await json<{ outcomes: PredictionOutcome[] }>(
    await api.post(`/api/leagues/${leagueId}/predictions`, { data: { items } }),
    `submit ${items.length} prediction(s) to league ${leagueId}`,
  )

  const rejected = body.outcomes.filter((o) => o.status !== "Saved")
  if (rejected.length > 0) {
    const detail = rejected.map((o) => `${o.matchId}: ${o.status} ${o.detail ?? ""}`.trim()).join("; ")
    throw new Error(`league ${leagueId} rejected ${rejected.length} forecast(s) — ${detail}`)
  }
}
