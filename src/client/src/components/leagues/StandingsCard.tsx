import { useEffect, useState } from "react"
import { Link } from "react-router-dom"
import { ApiError, apiFetch } from "@/lib/api"
import type { StandingsResponse } from "@/leagues/types"
import { cn, stagger } from "@/lib/utils"
import { RankBadge } from "@/components/leagues/RankBadge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Skeleton, SkeletonBlock } from "@/components/ui/skeleton"

// The league's table, led with on the league page (FR-012). Shows the leading rows and links to
// the full standings; the whole table on the page would drown the invite/scoring/members cards it
// sits beside.
//
// Before any match is scored every row is at zero, which is a real state, not an error — the card
// says so rather than rendering an empty table.

const TOP_ROWS = 5

interface StandingsCardProps {
  leagueId: string
}

export function StandingsCard({ leagueId }: StandingsCardProps) {
  const [standings, setStandings] = useState<StandingsResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notFound, setNotFound] = useState(false)

  useEffect(() => {
    void (async () => {
      try {
        setStandings(await apiFetch<StandingsResponse>(`/api/leagues/${leagueId}/standings`))
      } catch (err) {
        // The API returns 404 both for a missing league and for one the caller may not see —
        // same branch StandingsPage and LeagueDetailPage take. Unreachable while the parent page
        // gates visibility before mounting this card, but the card must not claim a load failure
        // if that ever stops being true.
        if (err instanceof ApiError && err.status === 404) setNotFound(true)
        else setError(err instanceof Error ? err.message : "Failed to load the standings.")
      }
    })()
  }, [leagueId])

  const rows = standings?.rows ?? []
  const nothingScored = rows.length > 0 && rows.every((r) => r.scoredMatches === 0)

  return (
    <Card>
      <CardHeader><CardTitle>Standings</CardTitle></CardHeader>
      <CardContent className="grid gap-3">
        {error && <div role="alert" className="text-sm text-destructive">{error}</div>}

        {notFound && (
          <p className="text-sm text-muted-foreground">
            This league is not available, or you are not a member of it.
          </p>
        )}

        {!error && !notFound && !standings && (
          <SkeletonBlock className="gap-2">
            {[0, 1, 2].map((i) => (
              <div key={i} className="flex items-center justify-between gap-3">
                <Skeleton className="h-4 w-40" />
                <Skeleton className="h-4 w-8" />
              </div>
            ))}
          </SkeletonBlock>
        )}

        {standings && rows.length === 0 && (
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
          <ul className="grid gap-1">
            {rows.slice(0, TOP_ROWS).map((r, i) => {
              const isCaller = r.userId === standings?.callerUserId
              return (
                <li
                  key={r.userId}
                  style={stagger(i)}
                  className={cn(
                    "rise-in flex items-center justify-between gap-3 rounded-lg px-2 py-1.5 text-sm transition-colors",
                    // The caller's own row is the one they look for first — it gets a tinted
                    // band, not just a bolder weight.
                    isCaller ? "bg-primary/10 font-medium ring-1 ring-primary/25" : "hover:bg-white/5",
                  )}
                >
                  <span className="flex items-center gap-2">
                    <RankBadge rank={r.rank} />
                    {r.displayName}
                    {isCaller && <span className="text-muted-foreground"> (you)</span>}
                  </span>
                  <span className="tabular-nums">{r.points}</span>
                </li>
              )
            })}
          </ul>
        )}

        {rows.length > TOP_ROWS && (
          <p className="text-xs text-muted-foreground">
            Showing {TOP_ROWS} of {rows.length} members.
          </p>
        )}

        <div>
          <Button asChild variant="outline" size="sm">
            <Link to={`/app/leagues/${leagueId}/standings`}>Full standings</Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
