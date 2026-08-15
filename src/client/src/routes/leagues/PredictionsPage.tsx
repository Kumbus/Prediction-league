import { useEffect, useRef, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { ApiError, apiFetch } from "@/lib/api"
import type {
  BatchSubmitResponse,
  PredictionOutcome,
  PredictionSubmissionItem,
  RevealedPrediction,
  RevealedRoundResponse,
  RoundViewResponse,
  ScoringParameter,
} from "@/leagues/types"
import { useAuth } from "@/auth/useAuth"
import type { PredictionDraft } from "@/leagues/drafts"
import { draftFromRow, sameDraft } from "@/leagues/drafts"
import { MatchPredictionRow } from "@/components/leagues/MatchPredictionRow"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

// Fill one round of forecasts and save it in a single write (S-06, FR-009). The screen holds no
// lock logic of its own: it renders the server's canPredict per match, and after a save it
// replaces its state with the round view the server sends back.

function seedDrafts(view: RoundViewResponse): Record<string, PredictionDraft> {
  return Object.fromEntries(view.matches.map((m) => [m.matchId, draftFromRow(m)]))
}

const toNumber = (raw: string): number | null => (raw.trim() === "" ? null : Number(raw))

// The reveal is carried together with the round it was fetched for, so a switch can never paint
// the previous round's forecasts under the new round's matches while the fetch is in flight.
interface RevealedForRound {
  round: string
  byMatch: Record<string, RevealedPrediction[]>
}

export function PredictionsPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { user } = useAuth()

  const [view, setView] = useState<RoundViewResponse | null>(null)
  // null means "whatever the server considers the round in play" — the first load asks for it by
  // sending no round at all, so the landing round is the server's decision, not a local clock's.
  const [round, setRound] = useState<string | null>(null)
  const [drafts, setDrafts] = useState<Record<string, PredictionDraft>>({})
  const [outcomes, setOutcomes] = useState<Record<string, PredictionOutcome>>({})
  const [revealed, setRevealed] = useState<RevealedForRound | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [notFound, setNotFound] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const rowRefs = useRef<Record<string, HTMLDivElement | null>>({})
  const scrolled = useRef(false)

  // One fetch per (league, round). The round the server lands on with no ?round= is the round in
  // play — the client never computes it.
  useEffect(() => {
    if (!id) return
    let cancelled = false
    void (async () => {
      try {
        const query = round === null ? "" : `?round=${encodeURIComponent(round)}`
        const next = await apiFetch<RoundViewResponse>(`/api/leagues/${id}/predictions${query}`)
        if (cancelled) return
        setView(next)
        setDrafts(seedDrafts(next))
        setOutcomes({})
        setError(null)
      } catch (err) {
        if (cancelled) return
        // 404 covers both a missing league and one the caller may not see — same copy either way.
        if (err instanceof ApiError && err.status === 404) setNotFound(true)
        else setError(err instanceof Error ? err.message : "Failed to load the round.")
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [id, round])

  // Everyone's forecasts for the displayed round, but only for matches that have kicked off. A
  // match missing from this response is what "not revealed yet" means — the UI must never infer
  // it from a local clock, and there is nothing in the payload to peek at before kickoff.
  const displayedRound = view?.round ?? null
  useEffect(() => {
    if (!id || displayedRound === null) return
    let cancelled = false
    void (async () => {
      try {
        const response = await apiFetch<RevealedRoundResponse>(
          `/api/leagues/${id}/predictions/revealed?round=${encodeURIComponent(displayedRound)}`,
        )
        if (cancelled) return
        const byMatch: Record<string, RevealedPrediction[]> = {}
        for (const p of response.predictions) (byMatch[p.matchId] ??= []).push(p)
        setRevealed({ round: displayedRound, byMatch })
      } catch {
        // The reveal is a bonus surface on top of the round view; failing to load it must not
        // take the fillable form down with it.
        if (!cancelled) setRevealed(null)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [id, displayedRound])

  // Land on the match being played, or the next one up — read off the server's status/canPredict,
  // never off a local clock. Only on the first load: a deliberate round switch should not yank
  // the viewport around.
  useEffect(() => {
    if (!view || scrolled.current) return
    const target =
      view.matches.find((m) => m.status === "Live") ??
      view.matches.find((m) => m.canPredict) ??
      view.matches[0]
    if (!target) return
    scrolled.current = true
    rowRefs.current[target.matchId]?.scrollIntoView({ block: "center" })
  }, [view])

  const changeDraft = (matchId: string, draft: PredictionDraft) =>
    setDrafts((prev) => ({ ...prev, [matchId]: draft }))

  const save = async () => {
    if (!view || !id) return

    const scores = (p: ScoringParameter) => view.scoredParameters.includes(p)
    const items: PredictionSubmissionItem[] = []
    const incomplete: string[] = []

    for (const match of view.matches) {
      if (!match.canPredict) continue
      const draft = drafts[match.matchId]
      if (!draft || sameDraft(draft, draftFromRow(match))) continue

      const home = toNumber(draft.homeScore)
      const away = toNumber(draft.awayScore)
      if (home === null || away === null || Number.isNaN(home) || Number.isNaN(away)) {
        incomplete.push(`${match.homeTeamName} v ${match.awayTeamName}`)
        continue
      }

      items.push({
        matchId: match.matchId,
        homeScore: home,
        awayScore: away,
        // Only the fields this league scores are sent: the server rejects anything else outright,
        // which is the point — a forecast the league will never award is not silently dropped.
        firstScorerPlayerId: scores("CorrectGoalScorer") ? draft.firstScorerPlayerId || null : null,
        firstScorerTeamId: scores("CorrectGoalScorer") ? draft.firstScorerTeamId || null : null,
        totalCards: scores("CorrectCardCount") ? toNumber(draft.totalCards) : null,
        yellowCards: scores("CorrectYellowCards") ? toNumber(draft.yellowCards) : null,
        redCards: scores("CorrectRedCards") ? toNumber(draft.redCards) : null,
      })
    }

    if (incomplete.length > 0) {
      setError(`Enter both scores for: ${incomplete.join(", ")}.`)
      return
    }
    if (items.length === 0) {
      setError("Nothing to save — no forecast has changed.")
      return
    }

    setSaving(true)
    setError(null)
    try {
      const result = await apiFetch<BatchSubmitResponse>(`/api/leagues/${id}/predictions`, {
        method: "POST",
        body: { items },
      })
      const byMatch = Object.fromEntries(result.outcomes.map((o) => [o.matchId, o]))
      setOutcomes(byMatch)
      setView(result.round)
      // The server's view is the truth for anything it stored. Rows it rejected keep the member's
      // input instead, so the reason shown next to the row is something they can act on.
      const reseeded = seedDrafts(result.round)
      setDrafts((prev) =>
        Object.fromEntries(
          Object.entries(reseeded).map(([matchId, seeded]) => [
            matchId,
            byMatch[matchId]?.status === "Invalid" ? (prev[matchId] ?? seeded) : seeded,
          ]),
        ),
      )
    } catch (err) {
      setError(
        err instanceof ApiError
          ? (err.problem?.detail ?? err.message)
          : err instanceof Error
            ? err.message
            : "Could not save the round.",
      )
    } finally {
      setSaving(false)
    }
  }

  if (loading && !view) return <div className="p-6">Loading…</div>

  if (notFound) {
    return (
      <div className="grid gap-4 p-6">
        <h1 className="text-2xl font-semibold">League not found</h1>
        <p className="text-muted-foreground">
          This league does not exist, or you are not a member of it.
        </p>
        <div>
          <Button variant="outline" onClick={() => navigate("/app/leagues")}>Back to my leagues</Button>
        </div>
      </div>
    )
  }

  if (!view) {
    return (
      <div className="grid gap-4 p-6">
        <div role="alert" className="text-sm text-destructive">{error ?? "Failed to load the round."}</div>
        <div>
          <Button variant="outline" onClick={() => navigate("/app/leagues")}>Back to my leagues</Button>
        </div>
      </div>
    )
  }

  const openMatches = view.matches.filter((m) => m.canPredict).length

  return (
    <div className="grid gap-4 p-6">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">{view.leagueName} — predictions</h1>
        <Button variant="outline" onClick={() => navigate(`/app/leagues/${id}`)}>Back to league</Button>
      </div>

      {view.rounds.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {view.rounds.map((r) => (
            <Button
              key={r.round}
              size="sm"
              variant={r.round === view.round ? "default" : "outline"}
              disabled={loading || saving}
              onClick={() => {
                setLoading(true)
                setRound(r.round)
              }}
            >
              {r.round}
              {r.isCurrent && <span className="ml-1 text-xs opacity-70">• now</span>}
            </Button>
          ))}
        </div>
      )}

      {error && <div role="alert" className="text-sm text-destructive">{error}</div>}

      <Card>
        <CardHeader className="flex flex-row items-center justify-between space-y-0">
          <CardTitle>{view.round ?? "No rounds yet"}</CardTitle>
          {openMatches > 0 && (
            <Button size="sm" disabled={saving || loading} onClick={() => void save()}>
              {saving ? "Saving…" : "Save round"}
            </Button>
          )}
        </CardHeader>
        <CardContent className="grid gap-3">
          {view.matches.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              This tournament has no matches yet.
            </p>
          ) : (
            view.matches.map((m) => (
              <div
                key={m.matchId}
                ref={(el) => {
                  rowRefs.current[m.matchId] = el
                }}
              >
                <MatchPredictionRow
                  row={m}
                  scored={view.scoredParameters}
                  draft={drafts[m.matchId] ?? draftFromRow(m)}
                  outcome={outcomes[m.matchId]}
                  disabled={saving}
                  onChange={changeDraft}
                  revealed={
                    revealed?.round === view.round ? revealed.byMatch[m.matchId] : undefined
                  }
                  currentUserId={user?.id}
                />
              </div>
            ))
          )}
        </CardContent>
      </Card>
    </div>
  )
}
