import { useCallback, useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { apiFetch } from "@/lib/api"
import type {
  IngestResult,
  MatchWithEventsDto,
  TournamentResponse,
} from "@/admin/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"

export function TournamentDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [tournament, setTournament] = useState<TournamentResponse | null>(null)
  const [matches, setMatches] = useState<MatchWithEventsDto[]>([])
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [ingestResult, setIngestResult] = useState<IngestResult | null>(null)
  const [date, setDate] = useState("")
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    if (!id) return
    try {
      const [t, m] = await Promise.all([
        apiFetch<TournamentResponse>(`/api/tournaments/${id}`),
        apiFetch<MatchWithEventsDto[]>(`/api/tournaments/${id}/matches`),
      ])
      setTournament(t)
      setMatches(m)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load.")
    }
  }, [id])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void reload()
  }, [reload])

  const ingest = async () => {
    if (!tournament) return
    setBusy(true)
    setError(null)
    setIngestResult(null)
    try {
      const params = new URLSearchParams({ season: String(tournament.season) })
      if (date) params.set("date", date)
      const result = await apiFetch<IngestResult>(
        `/api/ingest/${tournament.id}?${params.toString()}`,
        { method: "POST" },
      )
      setIngestResult(result)
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : "Ingest failed.")
    } finally {
      setBusy(false)
    }
  }

  const togglePublish = async () => {
    if (!tournament) return
    try {
      await apiFetch<void>(`/api/tournaments/${tournament.id}/publish`, {
        method: "PATCH",
        body: { isPublished: !tournament.isPublished },
      })
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : "Publish failed.")
    }
  }

  const deleteMatch = async (matchId: string) => {
    if (!window.confirm("Delete this match?")) return
    try {
      await apiFetch<void>(`/api/matches/${matchId}`, { method: "DELETE" })
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed.")
    }
  }

  const toggleExpanded = (matchId: string) => {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(matchId)) next.delete(matchId)
      else next.add(matchId)
      return next
    })
  }

  if (!tournament) {
    return <div className="p-6">{error ?? "Loading…"}</div>
  }

  return (
    <div className="grid gap-4 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">{tournament.name}</h1>
          <p className="text-sm text-muted-foreground">
            Season {tournament.season} · {tournament.startDate} – {tournament.endDate}
            {tournament.externalApiId && ` · API:${tournament.externalApiId}`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Badge variant={tournament.isPublished ? "default" : "secondary"}>
            {tournament.isPublished ? "Published" : "Draft"}
          </Badge>
          <Button variant="outline" onClick={() => void togglePublish()}>
            {tournament.isPublished ? "Unpublish" : "Publish"}
          </Button>
          <Button variant="outline" onClick={() => navigate(`/admin/tournaments/${tournament.id}/edit`)}>Edit</Button>
        </div>
      </div>

      <Card>
        <CardHeader><CardTitle>Ingest now</CardTitle></CardHeader>
        <CardContent className="grid gap-3">
          <div className="flex items-end gap-3">
            <div className="grid gap-1">
              <label className="text-sm" htmlFor="date">Date (optional)</label>
              <input
                id="date"
                type="date"
                value={date}
                onChange={(e) => setDate(e.target.value)}
                className="rounded border border-input bg-background px-3 py-2"
              />
            </div>
            <Button onClick={() => void ingest()} disabled={busy || !tournament.externalApiId}>
              {busy ? "Ingesting…" : "Ingest now"}
            </Button>
            {!tournament.externalApiId && (
              <p className="text-xs text-muted-foreground">Set ExternalApiId first.</p>
            )}
          </div>
          {ingestResult && (
            <div className="space-y-1 text-sm">
              <div>
                Fixtures: <b>{ingestResult.fixturesUpserted}</b> ·
                Events: <b>{ingestResult.eventsUpserted}</b> ·
                API calls: <b>{ingestResult.apiCallsUsed}</b> ·
                Quota left: <b>{ingestResult.quotaRemaining ?? "—"}</b>
              </div>
              {/* The counts alone read as a clean success. A match that ingested but did not
                  score holds a saved result with stale points, and only a rescore repairs it —
                  so the verdict is rendered next to the counts, not left in a server log. */}
              {ingestResult.unscoredMatchIds.length > 0 && (
                <div role="alert" className="text-destructive">
                  {ingestResult.unscoredMatchIds.length} match(es) ingested but not scored — their
                  points are stale until each is rescored:{" "}
                  {ingestResult.unscoredMatchIds.join(", ")}
                </div>
              )}
              {/* A dropped goal or card is a scoring input the API sent and the mapper could not
                  keep. Nothing failed, so only this line separates "no events" from "events we
                  lost" — add them in the match editor, then rescore. */}
              {ingestResult.droppedEvents > 0 && (
                <div role="alert" className="text-destructive">
                  {ingestResult.droppedEvents} goal/card event(s) dropped across{" "}
                  {ingestResult.matchesWithDroppedEvents.length} match(es) — their points were
                  computed without them:{" "}
                  {ingestResult.matchesWithDroppedEvents.join(", ")}
                </div>
              )}
            </div>
          )}
          {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
        </CardContent>
      </Card>

      <div className="flex items-center justify-between">
        <h2 className="text-xl font-semibold">Matches ({matches.length})</h2>
        <div className="flex gap-2">
          <Button onClick={() => navigate(`/admin/tournaments/${tournament.id}/matches/new`)}>
            Add match
          </Button>
          <Button variant="outline" onClick={() => navigate(`/admin/tournaments/${tournament.id}/matches/import`)}>
            Import CSV
          </Button>
        </div>
      </div>
      <div className="grid gap-2">
        {matches.length === 0 ? (
          <p className="text-muted-foreground">No matches yet — add one manually or import a CSV.</p>
        ) : (
          matches.map((m) => (
            <Card key={m.matchId}>
              <CardHeader
                className="cursor-pointer flex flex-row items-center justify-between"
                onClick={() => toggleExpanded(m.matchId)}
              >
                <CardTitle className="text-base">
                  {m.homeTeam.name} {m.homeTeam.score ?? "–"} : {m.awayTeam.score ?? "–"} {m.awayTeam.name}
                </CardTitle>
                <div className="flex items-center gap-3 text-sm text-muted-foreground">
                  <span>{new Date(m.kickoffUtc).toLocaleString()}</span>
                  <Badge variant="secondary">{m.status}</Badge>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={(e) => { e.stopPropagation(); navigate(`/admin/matches/${m.matchId}/edit`) }}
                  >
                    Edit
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={(e) => { e.stopPropagation(); void deleteMatch(m.matchId) }}
                  >
                    Delete
                  </Button>
                </div>
              </CardHeader>
              {expanded.has(m.matchId) && (
                <CardContent>
                  {m.events.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No events.</p>
                  ) : (
                    <ul className="grid gap-1 text-sm">
                      {m.events.map((e, idx) => (
                        <li key={idx} className="flex gap-3">
                          <span className="w-12 tabular-nums">{e.minute}{e.minuteExtra ? `+${e.minuteExtra}` : ""}'</span>
                          <Badge variant="outline">{e.code}</Badge>
                          <span>{e.playerName}</span>
                          <span className="text-muted-foreground">({e.teamName})</span>
                        </li>
                      ))}
                    </ul>
                  )}
                </CardContent>
              )}
            </Card>
          ))
        )}
      </div>
    </div>
  )
}
