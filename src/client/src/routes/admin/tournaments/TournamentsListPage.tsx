import { useEffect, useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { ApiError, apiFetch } from "@/lib/api"
import type { TournamentResponse } from "@/admin/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"

export function TournamentsListPage() {
  const [items, setItems] = useState<TournamentResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const navigate = useNavigate()

  const reload = async () => {
    setLoading(true)
    setError(null)
    try {
      const list = await apiFetch<TournamentResponse[]>("/api/tournaments")
      setItems(list)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load tournaments.")
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void reload()
  }, [])

  const togglePublish = async (t: TournamentResponse) => {
    try {
      await apiFetch<void>(`/api/tournaments/${t.id}/publish`, {
        method: "PATCH",
        body: { isPublished: !t.isPublished },
      })
      void reload()
    } catch (err) {
      alert(err instanceof Error ? err.message : "Publish failed.")
    }
  }

  const remove = async (t: TournamentResponse) => {
    if (!window.confirm(`Delete tournament "${t.name}"?`)) return
    try {
      await apiFetch<void>(`/api/tournaments/${t.id}`, { method: "DELETE" })
      void reload()
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        alert(err.problem?.detail ?? "Cannot delete a tournament that has leagues.")
      } else {
        alert(err instanceof Error ? err.message : "Delete failed.")
      }
    }
  }

  return (
    <div className="grid gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Tournaments</h1>
        <Button onClick={() => navigate("/admin/tournaments/new")}>New</Button>
      </div>
      {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
      {loading ? (
        <p>Loading…</p>
      ) : items.length === 0 ? (
        <p className="text-muted-foreground">No tournaments yet.</p>
      ) : (
        <div className="grid gap-3">
          {items.map((t) => (
            <Card key={t.id}>
              <CardHeader className="flex flex-row items-center justify-between">
                <CardTitle>
                  <Link to={`/admin/tournaments/${t.id}`} className="hover:underline">
                    {t.name}
                  </Link>
                </CardTitle>
                <Badge variant={t.isPublished ? "default" : "secondary"}>
                  {t.isPublished ? "Published" : "Draft"}
                </Badge>
              </CardHeader>
              <CardContent className="flex flex-wrap items-center gap-4 text-sm">
                <span>Season {t.season}</span>
                <span>{t.startDate} – {t.endDate}</span>
                {t.externalApiId && <span className="text-muted-foreground">API:{t.externalApiId}</span>}
                <div className="ml-auto flex gap-2">
                  <Button variant="outline" size="sm" onClick={() => navigate(`/admin/tournaments/${t.id}/edit`)}>Edit</Button>
                  <Button variant="outline" size="sm" onClick={() => void togglePublish(t)}>
                    {t.isPublished ? "Unpublish" : "Publish"}
                  </Button>
                  <Button variant="destructive" size="sm" onClick={() => void remove(t)}>Delete</Button>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}
