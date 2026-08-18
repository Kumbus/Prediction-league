import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { ApiError, apiFetch } from "@/lib/api"
import type { StandingsResponse } from "@/leagues/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

// The full table on its own screen (FR-012). Same data as the league page's card, with room for
// every row and the per-member detail the card leaves out.
export function StandingsPage() {
  const { id } = useParams()
  const navigate = useNavigate()

  const [standings, setStandings] = useState<StandingsResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    void (async () => {
      try {
        setStandings(await apiFetch<StandingsResponse>(`/api/leagues/${id}/standings`))
      } catch (err) {
        // The API returns 404 both for a missing league and for one the caller may not see.
        if (err instanceof ApiError && err.status === 404) setNotFound(true)
        else setError(err instanceof Error ? err.message : "Failed to load the standings.")
      } finally {
        setLoading(false)
      }
    })()
  }, [id])

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

  if (!standings) {
    return (
      <div className="grid gap-4 p-6">
        <div role="alert" className="text-sm text-destructive">
          {error ?? "Failed to load the standings."}
        </div>
        <div>
          <Button variant="outline" onClick={() => navigate("/app/leagues")}>Back to my leagues</Button>
        </div>
      </div>
    )
  }

  const rows = standings.rows
  const nothingScored = rows.length > 0 && rows.every((r) => r.scoredMatches === 0)

  return (
    <div className="grid gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{standings.leagueName} — standings</h1>
        <Button variant="outline" onClick={() => navigate(`/app/leagues/${standings.leagueId}`)}>
          Back to league
        </Button>
      </div>

      <Card>
        <CardHeader><CardTitle>Table</CardTitle></CardHeader>
        <CardContent className="grid gap-3">
          {rows.length === 0 && (
            <p className="text-sm text-muted-foreground">
              No members yet — share the invite code to fill the table.
            </p>
          )}

          {nothingScored && (
            <p className="text-sm text-muted-foreground">
              Nothing is scored yet. Points appear as soon as a match finishes and its result is
              entered.
            </p>
          )}

          {rows.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border text-left text-muted-foreground">
                    <th className="py-2 pr-3 font-medium">#</th>
                    <th className="py-2 pr-3 font-medium">Member</th>
                    <th className="py-2 pr-3 text-right font-medium">Points</th>
                    <th className="py-2 text-right font-medium">Matches scored</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr
                      key={r.userId}
                      className={
                        r.userId === standings.callerUserId
                          ? "border-b border-border font-medium"
                          : "border-b border-border"
                      }
                    >
                      <td className="py-2 pr-3 tabular-nums">{r.rank}</td>
                      <td className="py-2 pr-3">
                        {r.displayName}
                        {r.userId === standings.callerUserId && (
                          <span className="text-muted-foreground"> (you)</span>
                        )}
                      </td>
                      <td className="py-2 pr-3 text-right tabular-nums">{r.points}</td>
                      <td className="py-2 text-right tabular-nums">{r.scoredMatches}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
