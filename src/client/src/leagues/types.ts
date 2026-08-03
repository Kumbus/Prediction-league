// Member-facing league shapes — mirrors LeaguesController's response records (S-03).
// Kept out of admin/types.ts: these screens are for every signed-in user, not just admins.

// Matches the C# ScoringParameter enum member names. Append-only server-side, so extending
// this union is the only change a new parameter needs here.
export type ScoringParameter =
  | "ExactScore"
  | "CorrectOutcome"
  | "CorrectGoalScorer"
  | "CorrectCardCount"
  | "CorrectYellowCards"
  | "CorrectRedCards"

export interface ScoringRuleDto {
  parameter: ScoringParameter
  points: number
}

export interface LeagueSummaryResponse {
  id: string
  name: string
  tournamentId: string
  tournamentName: string
  isOrganizer: boolean
  memberCount: number
}

export interface LeagueDetailResponse extends LeagueSummaryResponse {
  inviteCode: string
  scoringRules: ScoringRuleDto[]
}

// Prefill for the create form and the display order of the rule table. The API requires every
// parameter exactly once, so this list is also the completeness contract — drive the form off it
// rather than hand-writing an input per parameter.
export const SCORING_DEFAULTS: { parameter: ScoringParameter; label: string; points: number }[] = [
  { parameter: "ExactScore", label: "Exact score", points: 3 },
  { parameter: "CorrectOutcome", label: "Correct outcome", points: 1 },
  { parameter: "CorrectGoalScorer", label: "Correct goal scorer", points: 2 },
  { parameter: "CorrectCardCount", label: "Correct card count", points: 0 },
  { parameter: "CorrectYellowCards", label: "Correct yellow cards", points: 0 },
  { parameter: "CorrectRedCards", label: "Correct red cards", points: 0 },
]

export const SCORING_LABELS: Record<ScoringParameter, string> = Object.fromEntries(
  SCORING_DEFAULTS.map((d) => [d.parameter, d.label]),
) as Record<ScoringParameter, string>
