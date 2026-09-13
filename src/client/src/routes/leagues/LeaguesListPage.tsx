import { useEffect, useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { apiFetch } from "@/lib/api"
import { stagger } from "@/lib/utils"
import type { LeagueSummaryResponse } from "@/leagues/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { SkeletonCards } from "@/components/ui/skeleton"

// Leagues the signed-in user organizes or belongs to (S-03). The API is already caller-scoped —
// no client-side filtering.
export function LeaguesListPage() {
  const [items, setItems] = useState<LeagueSummaryResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const navigate = useNavigate()

  useEffect(() => {
    void (async () => {
      try {
        setItems(await apiFetch<LeagueSummaryResponse[]>("/api/leagues"))
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load leagues.")
      } finally {
        setLoading(false)
      }
    })()
  }, [])

  return (
    <div className="grid gap-6 p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">My leagues</h1>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => navigate("/app/leagues/join")}>Join a league</Button>
          <Button onClick={() => navigate("/app/leagues/new")}>New league</Button>
        </div>
      </div>
      {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
      {loading ? (
        <SkeletonCards />
      ) : items.length === 0 ? (
        // An empty roster is the first thing a new account sees — a bordered panel rather than
        // a loose paragraph that reads like a failed load.
        <div className="rounded-xl border border-dashed border-border-strong bg-card/40 px-6 py-12 text-center">
          <div aria-hidden="true" className="mb-3 text-4xl opacity-40">⚽</div>
          <p className="text-muted-foreground">
            No leagues yet. Create one to invite your friends, or join one with an invite code.
          </p>
        </div>
      ) : (
        <div className="grid gap-3">
          {items.map((l, i) => (
            <Card key={l.id} className="surface-interactive rise-in relative" style={stagger(i)}>
              <CardHeader className="flex flex-row items-center justify-between gap-3">
                <CardTitle className="text-lg">
                  {/* The link covers the card so the whole row is the target, while the anchor
                      itself stays the accessible name the list is navigated by. */}
                  <Link
                    to={`/app/leagues/${l.id}`}
                    className="after:absolute after:inset-0 after:content-[''] hover:text-green-light"
                  >
                    {l.name}
                  </Link>
                </CardTitle>
                <Badge variant={l.isOrganizer ? "default" : "secondary"}>
                  {l.isOrganizer ? "Organizer" : "Member"}
                </Badge>
              </CardHeader>
              <CardContent className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
                <span>{l.tournamentName}</span>
                <span className="text-muted-foreground">
                  {l.memberCount} {l.memberCount === 1 ? "member" : "members"}
                </span>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}
