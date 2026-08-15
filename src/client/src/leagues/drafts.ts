import type { MatchPredictionRow } from "@/leagues/types"

// The editable form state behind one match row (S-06). Strings, not numbers: an empty input is
// "" and must not read as 0 — a member who typed nothing has not forecast a goalless draw.
export interface PredictionDraft {
  homeScore: string
  awayScore: string
  firstScorerPlayerId: string
  firstScorerTeamId: string
  totalCards: string
  yellowCards: string
  redCards: string
}

export const EMPTY_DRAFT: PredictionDraft = {
  homeScore: "",
  awayScore: "",
  firstScorerPlayerId: "",
  firstScorerTeamId: "",
  totalCards: "",
  yellowCards: "",
  redCards: "",
}

// The saved forecast as a draft. Fields the league does not score come back null and stay "".
export function draftFromRow(row: MatchPredictionRow): PredictionDraft {
  const p = row.prediction
  if (!p) return EMPTY_DRAFT
  return {
    homeScore: String(p.homeScore),
    awayScore: String(p.awayScore),
    firstScorerPlayerId: p.firstScorerPlayerId ?? "",
    firstScorerTeamId: p.firstScorerTeamId ?? "",
    totalCards: p.totalCards?.toString() ?? "",
    yellowCards: p.yellowCards?.toString() ?? "",
    redCards: p.redCards?.toString() ?? "",
  }
}

export function sameDraft(a: PredictionDraft, b: PredictionDraft): boolean {
  return (
    a.homeScore === b.homeScore &&
    a.awayScore === b.awayScore &&
    a.firstScorerPlayerId === b.firstScorerPlayerId &&
    a.firstScorerTeamId === b.firstScorerTeamId &&
    a.totalCards === b.totalCards &&
    a.yellowCards === b.yellowCards &&
    a.redCards === b.redCards
  )
}
