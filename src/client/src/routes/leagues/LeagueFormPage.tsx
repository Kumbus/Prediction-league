import { useEffect, useState } from "react"
import { useNavigate } from "react-router-dom"
import { ApiError, apiFetch } from "@/lib/api"
import type { TournamentResponse } from "@/admin/types"
import type { LeagueDetailResponse, ScoringParameter } from "@/leagues/types"
import { SCORING_DEFAULTS } from "@/leagues/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

// Create a league: name + tournament + the full scoring config in one submit (S-03).
// The scoring inputs are generated from SCORING_DEFAULTS, so a new parameter needs no new markup.
export function LeagueFormPage() {
  const navigate = useNavigate()

  const [name, setName] = useState("")
  const [tournamentId, setTournamentId] = useState("")
  const [points, setPoints] = useState<Record<ScoringParameter, number>>(
    () =>
      Object.fromEntries(SCORING_DEFAULTS.map((d) => [d.parameter, d.points])) as Record<
        ScoringParameter,
        number
      >,
  )
  const [tournaments, setTournaments] = useState<TournamentResponse[]>([])
  const [loadingTournaments, setLoadingTournaments] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    // The API already hides drafts from non-admins; admins see their own drafts here but the
    // server rejects them on submit, so the rule holds either way.
    void (async () => {
      try {
        const list = await apiFetch<TournamentResponse[]>("/api/tournaments")
        setTournaments(list.filter((t) => t.isPublished))
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load tournaments.")
      } finally {
        setLoadingTournaments(false)
      }
    })()
  }, [])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const league = await apiFetch<LeagueDetailResponse>("/api/leagues", {
        method: "POST",
        body: {
          name,
          tournamentId,
          scoringRules: SCORING_DEFAULTS.map((d) => ({
            parameter: d.parameter,
            points: points[d.parameter],
          })),
        },
      })
      navigate(`/app/leagues/${league.id}`)
    } catch (err) {
      // Surface the server's own message inline rather than in an alert().
      setError(
        err instanceof ApiError
          ? err.problem?.detail ?? err.message
          : err instanceof Error
            ? err.message
            : "Create failed.",
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid gap-4 p-6">
      <h1 className="text-2xl font-semibold">New league</h1>
      <Card className="max-w-2xl">
        <CardHeader><CardTitle>League details</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={submit}>
            <div className="grid gap-2">
              <Label htmlFor="name">Name</Label>
              <Input id="name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} required />
            </div>

            <div className="grid gap-2">
              <Label htmlFor="tournament">Tournament</Label>
              {loadingTournaments ? (
                <p className="text-sm text-muted-foreground">Loading tournaments…</p>
              ) : tournaments.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  No tournaments available yet. An admin has to publish one before you can create a
                  league.
                </p>
              ) : (
                <select
                  id="tournament"
                  className="rounded border border-input bg-background px-3 py-2"
                  value={tournamentId}
                  onChange={(e) => setTournamentId(e.target.value)}
                  required
                >
                  <option value="">— pick —</option>
                  {tournaments.map((t) => (
                    <option key={t.id} value={t.id}>{t.name} ({t.season})</option>
                  ))}
                </select>
              )}
            </div>

            <fieldset className="grid gap-3">
              <legend className="text-sm font-medium">Scoring</legend>
              <p className="text-sm text-muted-foreground">
                Points awarded per match. Zero means the parameter does not score.
              </p>
              <div className="grid gap-3 sm:grid-cols-2">
                {SCORING_DEFAULTS.map((d) => (
                  <div key={d.parameter} className="grid gap-2">
                    <Label htmlFor={d.parameter}>{d.label}</Label>
                    <Input
                      id={d.parameter}
                      type="number"
                      min={0}
                      max={1000}
                      value={points[d.parameter]}
                      onChange={(e) =>
                        setPoints((prev) => ({ ...prev, [d.parameter]: Number(e.target.value) }))
                      }
                      required
                    />
                  </div>
                ))}
              </div>
            </fieldset>

            {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
            <div className="flex gap-2">
              <Button type="submit" disabled={busy || tournaments.length === 0}>
                {busy ? "Creating…" : "Create league"}
              </Button>
              <Button type="button" variant="outline" onClick={() => navigate("/app/leagues")}>
                Cancel
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
