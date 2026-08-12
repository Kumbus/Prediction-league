import { useEffect, useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { apiFetch } from "@/lib/api"
import type { LeagueSummaryResponse } from "@/leagues/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"

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
    <div className="grid gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">My leagues</h1>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => navigate("/app")}>Back</Button>
          <Button variant="outline" onClick={() => navigate("/app/leagues/join")}>Join a league</Button>
          <Button onClick={() => navigate("/app/leagues/new")}>New league</Button>
        </div>
      </div>
      {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
      {loading ? (
        <p>Loading…</p>
      ) : items.length === 0 ? (
        <p className="text-muted-foreground">
          No leagues yet. Create one to invite your friends, or join one with an invite code.
        </p>
      ) : (
        <div className="grid gap-3">
          {items.map((l) => (
            <Card key={l.id}>
              <CardHeader className="flex flex-row items-center justify-between">
                <CardTitle>
                  <Link to={`/app/leagues/${l.id}`} className="hover:underline">
                    {l.name}
                  </Link>
                </CardTitle>
                <Badge variant={l.isOrganizer ? "default" : "secondary"}>
                  {l.isOrganizer ? "Organizer" : "Member"}
                </Badge>
              </CardHeader>
              <CardContent className="flex flex-wrap items-center gap-4 text-sm">
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
