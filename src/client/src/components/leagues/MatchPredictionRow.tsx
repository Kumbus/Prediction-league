import type {
  MatchPredictionRow as MatchPredictionRowData,
  PredictionOutcome,
  RevealedPrediction,
  ScoringParameter,
} from "@/leagues/types"
import type { PredictionDraft } from "@/leagues/drafts"
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

// One match: the fixture, and the inputs the league's rules actually call for (S-06). The row
// holds no server state — it renders the row it is handed and emits draft changes upward, so the
// page stays the single owner of what will be saved.

interface MatchPredictionRowProps {
  row: MatchPredictionRowData
  scored: ScoringParameter[]
  draft: PredictionDraft
  outcome?: PredictionOutcome
  disabled?: boolean
  onChange: (matchId: string, draft: PredictionDraft) => void
  // Every member's forecast for this match, present only once it has kicked off. Undefined or
  // empty renders nothing at all — no placeholder, no "hidden until kickoff" teaser that could be
  // mistaken for a value.
  revealed?: RevealedPrediction[]
  currentUserId?: string
}

const OUTCOME_TEXT: Record<PredictionOutcome["status"], string> = {
  Saved: "Saved",
  Locked: "Locked — kicked off",
  Invalid: "Rejected",
}

export function MatchPredictionRow({
  row,
  scored,
  draft,
  outcome,
  disabled,
  onChange,
  revealed,
  currentUserId,
}: MatchPredictionRowProps) {
  const scores = (p: ScoringParameter) => scored.includes(p)
  const set = (patch: Partial<PredictionDraft>) => onChange(row.matchId, { ...draft, ...patch })

  const kickoff = new Date(row.kickoffUtc).toLocaleString()
  const scorers = row.scorers ?? []
  const homeScorers = scorers.filter((s) => s.teamId === row.homeTeamId)
  const awayScorers = scorers.filter((s) => s.teamId === row.awayTeamId)

  return (
    <div className="grid gap-3 rounded border border-border p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="font-medium">
          {row.homeTeamName} <span className="text-muted-foreground">v</span> {row.awayTeamName}
        </div>
        <div className="flex items-center gap-2">
          {row.status !== "Scheduled" && (
            <Badge variant="secondary">
              {row.status}
              {row.homeScore !== null && row.awayScore !== null
                ? ` ${row.homeScore}–${row.awayScore}`
                : ""}
            </Badge>
          )}
          <span className="text-sm text-muted-foreground">{kickoff}</span>
        </div>
      </div>

      {outcome && (
        <div
          role="status"
          className={
            outcome.status === "Saved"
              ? "text-sm text-muted-foreground"
              : "text-sm text-destructive"
          }
        >
          {/* The server's own reason, not a generic failure — a rejected row has to say why. */}
          {OUTCOME_TEXT[outcome.status]}
          {outcome.detail ? `: ${outcome.detail}` : ""}
        </div>
      )}

      {row.canPredict ? (
        <div className="grid gap-3">
          <div className="flex flex-wrap items-end gap-3">
            <div className="grid gap-1">
              <Label htmlFor={`home-${row.matchId}`}>{row.homeTeamName}</Label>
              <Input
                id={`home-${row.matchId}`}
                type="number"
                min={0}
                max={99}
                className="w-20"
                value={draft.homeScore}
                disabled={disabled}
                onChange={(e) => set({ homeScore: e.target.value })}
              />
            </div>
            <div className="grid gap-1">
              <Label htmlFor={`away-${row.matchId}`}>{row.awayTeamName}</Label>
              <Input
                id={`away-${row.matchId}`}
                type="number"
                min={0}
                max={99}
                className="w-20"
                value={draft.awayScore}
                disabled={disabled}
                onChange={(e) => set({ awayScore: e.target.value })}
              />
            </div>

            {scores("CorrectCardCount") && (
              <CardInput
                id={`cards-${row.matchId}`}
                label="Total cards"
                value={draft.totalCards}
                disabled={disabled}
                onChange={(v) => set({ totalCards: v })}
              />
            )}
            {scores("CorrectYellowCards") && (
              <CardInput
                id={`yellow-${row.matchId}`}
                label="Yellow cards"
                value={draft.yellowCards}
                disabled={disabled}
                onChange={(v) => set({ yellowCards: v })}
              />
            )}
            {scores("CorrectRedCards") && (
              <CardInput
                id={`red-${row.matchId}`}
                label="Red cards"
                value={draft.redCards}
                disabled={disabled}
                onChange={(v) => set({ redCards: v })}
              />
            )}
          </div>

          {scores("CorrectGoalScorer") && (
            <div className="grid gap-3 sm:grid-cols-2">
              {/* Two controls, not one: the credited team and the player who scores. Picking one
                  team and a player from the other is an own-goal forecast — a normal two-click
                  action here rather than a hidden capability. The two clear together: half a pair
                  is not a forecast and the server rejects it, so that state stays unreachable. */}
              <div className="grid gap-1">
                <Label htmlFor={`credited-${row.matchId}`}>Goal credited to</Label>
                <select
                  id={`credited-${row.matchId}`}
                  className="rounded border border-input bg-background px-3 py-2"
                  value={draft.firstScorerTeamId}
                  disabled={disabled}
                  onChange={(e) =>
                    set(
                      e.target.value === ""
                        ? { firstScorerTeamId: "", firstScorerPlayerId: "" }
                        : { firstScorerTeamId: e.target.value },
                    )
                  }
                >
                  <option value="">— none —</option>
                  <option value={row.homeTeamId}>{row.homeTeamName}</option>
                  <option value={row.awayTeamId}>{row.awayTeamName}</option>
                </select>
              </div>
              <div className="grid gap-1">
                <Label htmlFor={`scorer-${row.matchId}`}>First scorer</Label>
                <select
                  id={`scorer-${row.matchId}`}
                  className="rounded border border-input bg-background px-3 py-2"
                  value={draft.firstScorerPlayerId}
                  disabled={disabled}
                  onChange={(e) =>
                    set(
                      e.target.value === ""
                        ? { firstScorerPlayerId: "", firstScorerTeamId: "" }
                        : { firstScorerPlayerId: e.target.value },
                    )
                  }
                >
                  <option value="">— none —</option>
                  <optgroup label={row.homeTeamName}>
                    {homeScorers.map((s) => (
                      <option key={s.playerId} value={s.playerId}>{s.name}</option>
                    ))}
                  </optgroup>
                  <optgroup label={row.awayTeamName}>
                    {awayScorers.map((s) => (
                      <option key={s.playerId} value={s.playerId}>{s.name}</option>
                    ))}
                  </optgroup>
                </select>
                {scorers.length === 0 && (
                  <p className="text-xs text-muted-foreground">
                    No players are linked to either team yet — ask an admin to import them.
                  </p>
                )}
              </div>
            </div>
          )}
        </div>
      ) : (
        <div className="grid gap-1 text-sm">
          <p className="text-muted-foreground">Locked at kickoff.</p>
          {row.prediction ? (
            <p>
              Your forecast: {row.prediction.homeScore}–{row.prediction.awayScore}
              {row.prediction.yellowCards !== null && ` · ${row.prediction.yellowCards} yellow`}
              {row.prediction.redCards !== null && ` · ${row.prediction.redCards} red`}
              {row.prediction.totalCards !== null && ` · ${row.prediction.totalCards} cards`}
              {/* Only when scored: a finished match whose points have not been computed must read
                  as "not scored yet", which is silence — never a 0 that looks like a verdict. */}
              {row.prediction.awardedPoints !== null && (
                <span className="font-medium"> · {row.prediction.awardedPoints} pts</span>
              )}
            </p>
          ) : (
            <p className="text-muted-foreground">You did not forecast this match.</p>
          )}
        </div>
      )}

      {revealed && revealed.length > 0 && (
        <div className="grid gap-1 border-t border-border pt-3 text-sm">
          <div className="text-muted-foreground">Everyone's forecasts</div>
          {revealed.map((p) => (
            <div key={p.userId} className="flex flex-wrap items-baseline gap-2">
              <span className={p.userId === currentUserId ? "font-medium" : ""}>
                {p.displayName}
                {p.userId === currentUserId && " (you)"}
              </span>
              <span className="tabular-nums">
                {p.homeScore}–{p.awayScore}
              </span>
              {p.firstScorerName && (
                // An own-goal pick reads as the player's name against the other team, because the
                // credited team is stored alongside the scorer rather than inferred from them.
                <span className="text-muted-foreground">
                  {p.firstScorerName}
                  {p.firstScorerTeamName ? ` → ${p.firstScorerTeamName}` : ""}
                </span>
              )}
              {p.totalCards !== null && (
                <span className="text-muted-foreground">{p.totalCards} cards</span>
              )}
              {p.yellowCards !== null && (
                <span className="text-muted-foreground">{p.yellowCards} yellow</span>
              )}
              {p.redCards !== null && (
                <span className="text-muted-foreground">{p.redCards} red</span>
              )}
              {/* Once the match is scored the reveal doubles as its scoreboard. Absent until
                  then, so nobody reads an unscored match as everyone earning nothing. */}
              {p.awardedPoints !== null && (
                <span className="ml-auto font-medium tabular-nums">{p.awardedPoints} pts</span>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

interface CardInputProps {
  id: string
  label: string
  value: string
  disabled?: boolean
  onChange: (value: string) => void
}

function CardInput({ id, label, value, disabled, onChange }: CardInputProps) {
  return (
    <div className="grid gap-1">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        type="number"
        min={0}
        max={99}
        className="w-24"
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  )
}
