import { useState } from "react"
import { ApiError, apiFetch } from "@/lib/api"
import type { LeagueDetailResponse, ScoringRuleDto } from "@/leagues/types"
import { MIN_RULE_POINTS, SCORING_LABELS } from "@/leagues/types"
import { ScoringRulesFieldset } from "@/components/leagues/ScoringRulesFieldset"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

// The league's scoring config: a read table for everyone, an inline editor for the organizer
// while the tournament has not started (S-04). Save replaces local league state with the
// server's own view, so the table can never drift from what was persisted.

interface ScoringCardProps {
  league: LeagueDetailResponse
  onLeagueChange: (league: LeagueDetailResponse) => void
}

export function ScoringCard({ league, onLeagueChange }: ScoringCardProps) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState<ScoringRuleDto[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canEdit = league.isOrganizer && !league.isScoringLocked

  const startEditing = () => {
    // Leagues created under S-03 carry every parameter, typically three at 0 points. Under the
    // new floor a zero-point row means "not scored" — seed it as unticked rather than as an
    // input the organizer has to repair before they can save.
    setDraft(league.scoringRules.filter((r) => r.points >= MIN_RULE_POINTS))
    setError(null)
    setEditing(true)
  }

  const save = async (e: React.FormEvent) => {
    e.preventDefault()
    if (draft.length === 0) {
      setError("Pick at least one scoring parameter.")
      return
    }
    setBusy(true)
    setError(null)
    try {
      const updated = await apiFetch<LeagueDetailResponse>(
        `/api/leagues/${league.id}/scoring-rules`,
        { method: "PUT", body: { scoringRules: draft } },
      )
      onLeagueChange(updated)
      setEditing(false)
    } catch (err) {
      setError(
        err instanceof ApiError
          ? err.problem?.detail ?? err.message
          : err instanceof Error
            ? err.message
            : "Could not save the scoring rules.",
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0">
        <CardTitle>Scoring</CardTitle>
        {canEdit && !editing && (
          <Button variant="outline" size="sm" onClick={startEditing}>Edit</Button>
        )}
      </CardHeader>
      <CardContent className="grid gap-3">
        {editing ? (
          <form className="grid gap-4" onSubmit={save}>
            <ScoringRulesFieldset value={draft} onChange={setDraft} disabled={busy} />
            {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
            <div className="flex gap-2">
              <Button type="submit" size="sm" disabled={busy || draft.length === 0}>
                {busy ? "Saving…" : "Save"}
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={busy}
                onClick={() => { setEditing(false); setError(null) }}
              >
                Cancel
              </Button>
            </div>
          </form>
        ) : (
          <>
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border text-left text-muted-foreground">
                  <th className="py-2 font-medium">Parameter</th>
                  <th className="py-2 text-right font-medium">Points</th>
                </tr>
              </thead>
              <tbody>
                {league.scoringRules.map((r) => (
                  <tr key={r.parameter} className="border-b border-border/50 last:border-0">
                    <td className="py-2">{SCORING_LABELS[r.parameter] ?? r.parameter}</td>
                    <td className="py-2 text-right tabular-nums">{r.points}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {league.isScoringLocked && (
              <p className="text-sm text-muted-foreground">
                Scoring is locked because the tournament has started.
              </p>
            )}
            {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
          </>
        )}
      </CardContent>
    </Card>
  )
}
