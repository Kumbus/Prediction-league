import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { ApiError, apiFetch } from "@/lib/api"
import type { LeagueDetailResponse } from "@/leagues/types"
import { SCORING_LABELS } from "@/leagues/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"

// One league: what it is bound to, who is in it, the invite code to share (FR-007), and the
// scoring config. Rule editing arrives in S-04; join-by-code in S-05.
export function LeagueDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()

  const [league, setLeague] = useState<LeagueDetailResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    if (!id) return
    void (async () => {
      try {
        setLeague(await apiFetch<LeagueDetailResponse>(`/api/leagues/${id}`))
      } catch (err) {
        // The API returns 404 both for a missing league and for one the caller may not see.
        if (err instanceof ApiError && err.status === 404) setNotFound(true)
        else setError(err instanceof Error ? err.message : "Failed to load the league.")
      } finally {
        setLoading(false)
      }
    })()
  }, [id])

  const copyInviteCode = async () => {
    if (!league) return
    try {
      await navigator.clipboard.writeText(league.inviteCode)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      setError("Could not copy the invite code — copy it by hand.")
    }
  }

  if (loading) return <div className="p-6">Loading…</div>

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

  if (!league) {
    return (
      <div className="grid gap-4 p-6">
        <div role="alert" className="text-sm text-destructive">{error ?? "Failed to load the league."}</div>
        <div>
          <Button variant="outline" onClick={() => navigate("/app/leagues")}>Back to my leagues</Button>
        </div>
      </div>
    )
  }

  return (
    <div className="grid gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{league.name}</h1>
        <div className="flex items-center gap-2">
          <Badge variant={league.isOrganizer ? "default" : "secondary"}>
            {league.isOrganizer ? "Organizer" : "Member"}
          </Badge>
          <Button variant="outline" onClick={() => navigate("/app/leagues")}>Back</Button>
        </div>
      </div>

      {error && <div role="alert" className="text-sm text-destructive">{error}</div>}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>Invite code</CardTitle></CardHeader>
          <CardContent className="grid gap-3">
            <div className="flex items-center gap-3">
              <code className="rounded border border-input bg-background px-3 py-2 text-lg tracking-widest">
                {league.inviteCode}
              </code>
              <Button variant="outline" size="sm" onClick={() => void copyInviteCode()}>
                {copied ? "Copied" : "Copy"}
              </Button>
            </div>
            <p className="text-sm text-muted-foreground">
              Share this code with friends so they can join the league.
            </p>
            <div className="flex flex-wrap gap-4 text-sm">
              <span>{league.tournamentName}</span>
              <span className="text-muted-foreground">
                {league.memberCount} {league.memberCount === 1 ? "member" : "members"}
              </span>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>Scoring</CardTitle></CardHeader>
          <CardContent>
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
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
