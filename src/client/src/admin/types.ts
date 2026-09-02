export type PlayerPosition = "Unknown" | "GK" | "DEF" | "MID" | "FWD"

export interface TournamentResponse {
  id: string
  name: string
  externalApiId: string | null
  season: number
  startDate: string
  endDate: string
  isPublished: boolean
}

export interface NationalityResponse {
  id: number
  code: string
  name: string
}

export interface PlayerResponse {
  id: string
  name: string
  externalPlayerId: number
  nationalityId: number | null
  position: PlayerPosition
  dateOfBirth: string | null
  heightCm: number | null
  clubTeamId: string | null
  nationalTeamId: string | null
}

export interface PagedPlayersResponse {
  items: PlayerResponse[]
  total: number
  page: number
  pageSize: number
}

export interface MatchEventDto {
  minute: number
  minuteExtra: number | null
  code: string
  category: "Goal" | "Card" | "Other"
  playerName: string
  teamName: string
}

export interface TeamRefDto {
  id: string
  name: string
  score: number | null
}

export type MatchStatus = "Scheduled" | "Live" | "Finished"

export interface MatchWithEventsDto {
  matchId: string
  externalFixtureId: number | null
  kickoffUtc: string
  status: MatchStatus
  homeTeam: TeamRefDto
  awayTeam: TeamRefDto
  events: MatchEventDto[]
}

export interface TeamResponse {
  id: string
  name: string
  externalTeamId: number | null
  logoUrl: string | null
}

export interface MatchDetailResponse {
  id: string
  tournamentId: string
  homeTeamId: string
  awayTeamId: string
  kickoffUtc: string
  status: MatchStatus
  homeScore: number | null
  awayScore: number | null
  round: string
  // Partial success on the write endpoints: the match saved but its points did not recalculate.
  // Always false on the read.
  scoringFailed: boolean
  scoringMessage: string | null
}

export interface MatchEventTypeResponse {
  id: number
  code: string
  displayName: string
  category: "Goal" | "Card" | "Other"
}

export interface EligiblePlayerResponse {
  playerId: string
  name: string
  teamId: string
}

// One stored goal/card row, with the ids the editor's selects bind to and the names it renders.
export interface MatchEventEditDto {
  id: string
  matchEventTypeId: number
  typeCode: string
  typeDisplayName: string
  playerId: string
  playerName: string
  teamId: string
  teamName: string
  minute: number
  minuteExtra: number | null
}

export interface MatchEventsResponse {
  events: MatchEventEditDto[]
  scoringFailed: boolean
  scoringMessage: string | null
}

export interface MatchImportRow {
  lineNumber: number
  homeTeam: string
  awayTeam: string
  kickoffUtc: string
  status: MatchStatus
  homeScore: number | null
  awayScore: number | null
  round: string
}

export interface MatchImportConflict {
  lineNumber: number
  reason: string
}

export interface MatchImportPreview {
  toCreate: number
  skipped: number
  teamsToCreate: number
  rows: MatchImportRow[]
  conflicts: MatchImportConflict[]
}

export interface MatchImportResult {
  created: number
  skipped: number
  teamsCreated: number
}

export interface IngestResult {
  fixturesUpserted: number
  eventsUpserted: number
  apiCallsUsed: number
  quotaRemaining: number | null
  // Matches whose result was ingested but whose points did not follow — the run's
  // partial-success verdict. Empty means every match scored, never "nobody looked".
  unscoredMatchIds: string[]
  // Goal/card events the API reported that could not be persisted, and the matches now
  // missing them. Those matches scored against an incomplete set.
  droppedEvents: number
  matchesWithDroppedEvents: string[]
}

export interface PlayerImportRow {
  lineNumber: number
  name: string
  nationalityCode: string
  position: PlayerPosition
  dateOfBirth: string | null
  heightCm: number | null
  externalPlayerId: number | null
  action: "Create" | "Update" | "Skip"
}

export interface PlayerImportConflict {
  lineNumber: number
  name: string
  reason: string
}

export interface PlayerImportPreview {
  toCreate: number
  toUpdate: number
  skipped: number
  rows: PlayerImportRow[]
  conflicts: PlayerImportConflict[]
}

export interface PlayerImportResult {
  created: number
  updated: number
  skipped: number
  squadsAdded: number
}
