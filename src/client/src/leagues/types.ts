// Member-facing league shapes — mirrors LeaguesController's response records (S-03, S-05).
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

// Matches the C# MembershipRole enum member names.
export type MembershipRole = "Organizer" | "Member"

export interface LeagueMember {
  userId: string
  displayName: string
  role: MembershipRole
  joinedUtc: string
}

export interface LeagueDetailResponse extends LeagueSummaryResponse {
  inviteCode: string
  // Scoring freezes once the league's tournament has its first kickoff (S-04) — the server
  // derives it, so the UI hides the edit affordance instead of discovering it via a 409.
  isScoringLocked: boolean
  scoringRules: ScoringRuleDto[]
  // The roster (S-05), oldest member first. Only the detail response carries it; the list page
  // renders memberCount instead so it does not pay for a roster per league.
  members: LeagueMember[]
}

// Server-side floor and ceiling for a rule's points. The floor is 1: a parameter that does not
// score is left out of the set entirely, so there is no zero-point row to interpret.
export const MIN_RULE_POINTS = 1
export const MAX_RULE_POINTS = 1000

// The catalogue of selectable parameters — display order for the rule table, prefill and
// default selection for the forms. Not a completeness contract: a league scores only the
// parameters it lists. Drive the form off this rather than hand-writing an input per parameter.
export const SCORING_DEFAULTS: {
  parameter: ScoringParameter
  label: string
  points: number
  defaultActive: boolean
}[] = [
  { parameter: "ExactScore", label: "Exact score", points: 3, defaultActive: true },
  { parameter: "CorrectOutcome", label: "Correct outcome", points: 1, defaultActive: true },
  { parameter: "CorrectGoalScorer", label: "Correct goal scorer", points: 2, defaultActive: true },
  { parameter: "CorrectCardCount", label: "Correct card count", points: 1, defaultActive: false },
  { parameter: "CorrectYellowCards", label: "Correct yellow cards", points: 1, defaultActive: false },
  { parameter: "CorrectRedCards", label: "Correct red cards", points: 1, defaultActive: false },
]

export const SCORING_LABELS: Record<ScoringParameter, string> = Object.fromEntries(
  SCORING_DEFAULTS.map((d) => [d.parameter, d.label]),
) as Record<ScoringParameter, string>
