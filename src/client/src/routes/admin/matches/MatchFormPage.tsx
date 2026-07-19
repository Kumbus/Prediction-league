import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { apiFetch } from "@/lib/api"
import type {
  MatchDetailResponse,
  MatchStatus,
  TeamResponse,
} from "@/admin/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

const STATUSES: MatchStatus[] = ["Scheduled", "Live", "Finished"]

// ISO instant -> value for <input type="datetime-local"> in the browser's local zone.
function toLocalInput(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, "0")
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

export function MatchFormPage() {
  const { tournamentId: routeTournamentId, matchId } = useParams()
  const navigate = useNavigate()
  const isEdit = Boolean(matchId)

  const [tournamentId, setTournamentId] = useState(routeTournamentId ?? "")
  const [teams, setTeams] = useState<TeamResponse[]>([])
  const [homeTeamId, setHomeTeamId] = useState("")
  const [awayTeamId, setAwayTeamId] = useState("")
  const [kickoff, setKickoff] = useState("")
  const [status, setStatus] = useState<MatchStatus>("Scheduled")
  const [homeScore, setHomeScore] = useState("")
  const [awayScore, setAwayScore] = useState("")
  const [round, setRound] = useState("")

  const [newTeamName, setNewTeamName] = useState("")
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const loadTeams = async () => {
    const list = await apiFetch<TeamResponse[]>("/api/teams")
    setTeams(list)
    return list
  }

  useEffect(() => {
    void (async () => {
      try {
        await loadTeams()
        if (isEdit && matchId) {
          const m = await apiFetch<MatchDetailResponse>(`/api/matches/${matchId}`)
          setTournamentId(m.tournamentId)
          setHomeTeamId(m.homeTeamId)
          setAwayTeamId(m.awayTeamId)
          setKickoff(toLocalInput(m.kickoffUtc))
          setStatus(m.status)
          setHomeScore(m.homeScore?.toString() ?? "")
          setAwayScore(m.awayScore?.toString() ?? "")
          setRound(m.round)
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load.")
      }
    })()
  }, [matchId, isEdit])

  const addTeam = async () => {
    if (!newTeamName.trim()) return
    setError(null)
    try {
      const created = await apiFetch<TeamResponse>("/api/teams", {
        method: "POST",
        body: { name: newTeamName.trim() },
      })
      await loadTeams()
      setNewTeamName("")
      if (!homeTeamId) setHomeTeamId(created.id)
      else if (!awayTeamId) setAwayTeamId(created.id)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not add team.")
    }
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    const body = {
      homeTeamId,
      awayTeamId,
      kickoffUtc: kickoff ? new Date(kickoff).toISOString() : null,
      status,
      homeScore: homeScore === "" ? null : Number(homeScore),
      awayScore: awayScore === "" ? null : Number(awayScore),
      round: round.trim() || null,
    }
    try {
      if (isEdit) {
        await apiFetch<MatchDetailResponse>(`/api/matches/${matchId}`, { method: "PUT", body })
      } else {
        await apiFetch<MatchDetailResponse>(`/api/tournaments/${tournamentId}/matches`, {
          method: "POST",
          body,
        })
      }
      navigate(`/admin/tournaments/${tournamentId}`)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid gap-4 p-6">
      <h1 className="text-2xl font-semibold">{isEdit ? "Edit match" : "Add match"}</h1>
      <Card className="max-w-2xl">
        <CardHeader><CardTitle>Match details</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={submit}>
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label htmlFor="home">Home team</Label>
                <select
                  id="home"
                  className="rounded border border-input bg-background px-3 py-2"
                  value={homeTeamId}
                  onChange={(e) => setHomeTeamId(e.target.value)}
                  required
                >
                  <option value="">— pick —</option>
                  {teams.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                </select>
              </div>
              <div className="grid gap-2">
                <Label htmlFor="away">Away team</Label>
                <select
                  id="away"
                  className="rounded border border-input bg-background px-3 py-2"
                  value={awayTeamId}
                  onChange={(e) => setAwayTeamId(e.target.value)}
                  required
                >
                  <option value="">— pick —</option>
                  {teams.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                </select>
              </div>
            </div>

            <div className="flex items-end gap-2">
              <div className="grid flex-1 gap-2">
                <Label htmlFor="newTeam">Add a team</Label>
                <Input
                  id="newTeam"
                  value={newTeamName}
                  onChange={(e) => setNewTeamName(e.target.value)}
                  placeholder="Team name"
                />
              </div>
              <Button type="button" variant="outline" onClick={() => void addTeam()} disabled={!newTeamName.trim()}>
                Add team
              </Button>
            </div>

            <div className="grid gap-2">
              <Label htmlFor="kickoff">Kickoff (local time)</Label>
              <Input id="kickoff" type="datetime-local" value={kickoff} onChange={(e) => setKickoff(e.target.value)} required />
            </div>

            <div className="grid grid-cols-3 gap-4">
              <div className="grid gap-2">
                <Label htmlFor="status">Status</Label>
                <select
                  id="status"
                  className="rounded border border-input bg-background px-3 py-2"
                  value={status}
                  onChange={(e) => setStatus(e.target.value as MatchStatus)}
                >
                  {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
                </select>
              </div>
              <div className="grid gap-2">
                <Label htmlFor="homeScore">Home score</Label>
                <Input id="homeScore" type="number" min={0} value={homeScore} onChange={(e) => setHomeScore(e.target.value)} />
              </div>
              <div className="grid gap-2">
                <Label htmlFor="awayScore">Away score</Label>
                <Input id="awayScore" type="number" min={0} value={awayScore} onChange={(e) => setAwayScore(e.target.value)} />
              </div>
            </div>

            <div className="grid gap-2">
              <Label htmlFor="round">Round (optional)</Label>
              <Input id="round" value={round} onChange={(e) => setRound(e.target.value)} placeholder="Manual" />
            </div>

            {status === "Finished" && (homeScore === "" || awayScore === "") && (
              <p className="text-xs text-muted-foreground">A finished match needs both scores.</p>
            )}
            {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
            <div className="flex gap-2">
              <Button type="submit" disabled={busy}>{busy ? "Saving…" : "Save"}</Button>
              <Button
                type="button"
                variant="outline"
                onClick={() => navigate(tournamentId ? `/admin/tournaments/${tournamentId}` : "/admin/tournaments")}
              >
                Cancel
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
