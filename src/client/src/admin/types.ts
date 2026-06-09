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

export interface MatchWithEventsDto {
  matchId: string
  externalFixtureId: number
  kickoffUtc: string
  status: "Scheduled" | "Live" | "Finished"
  homeTeam: TeamRefDto
  awayTeam: TeamRefDto
  events: MatchEventDto[]
}

export interface IngestResult {
  fixturesUpserted: number
  eventsReplaced: number
  playersCreated: number
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
