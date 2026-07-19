import { useEffect, useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { apiFetch } from "@/lib/api"
import type { NationalityResponse, PagedPlayersResponse, TeamResponse } from "@/admin/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"

const PAGE_SIZE = 50

export function PlayersListPage() {
  const [data, setData] = useState<PagedPlayersResponse | null>(null)
  const [nats, setNats] = useState<Record<number, NationalityResponse>>({})
  const [teams, setTeams] = useState<Record<string, TeamResponse>>({})
  const [page, setPage] = useState(1)
  const [filter, setFilter] = useState("")
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const navigate = useNavigate()

  useEffect(() => {
    void (async () => {
      try {
        const [natList, teamList] = await Promise.all([
          apiFetch<NationalityResponse[]>("/api/nationalities"),
          apiFetch<TeamResponse[]>("/api/teams"),
        ])
        setNats(Object.fromEntries(natList.map((n) => [n.id, n])))
        setTeams(Object.fromEntries(teamList.map((t) => [t.id, t])))
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load reference data.")
      }
    })()
  }, [])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true)
    void (async () => {
      try {
        const res = await apiFetch<PagedPlayersResponse>(
          `/api/players?page=${page}&pageSize=${PAGE_SIZE}`,
        )
        setData(res)
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load players.")
      } finally {
        setLoading(false)
      }
    })()
  }, [page])

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1
  const filtered = data?.items.filter((p) =>
    p.name.toLowerCase().includes(filter.toLowerCase()),
  ) ?? []

  return (
    <div className="grid gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Players</h1>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => navigate("/admin/players/import")}>Import CSV</Button>
          <Button onClick={() => navigate("/admin/players/new")}>New</Button>
        </div>
      </div>
      <Input
        placeholder="Filter by name…"
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        className="max-w-md"
      />
      {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
      <Card>
        <CardContent className="p-0">
          {loading ? (
            <p className="p-4">Loading…</p>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b">
                  <th className="p-3 text-left">Name</th>
                  <th className="p-3 text-left">Nationality</th>
                  <th className="p-3 text-left">Position</th>
                  <th className="p-3 text-left">Club</th>
                  <th className="p-3 text-left">National team</th>
                  <th className="p-3"></th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((p) => (
                  <tr key={p.id} className="border-b last:border-b-0">
                    <td className="p-3">{p.name}</td>
                    <td className="p-3 text-muted-foreground">
                      {p.nationalityId ? nats[p.nationalityId]?.code ?? "—" : "—"}
                    </td>
                    <td className="p-3">{p.position}</td>
                    <td className="p-3 text-muted-foreground">
                      {p.clubTeamId ? teams[p.clubTeamId]?.name ?? "—" : "—"}
                    </td>
                    <td className="p-3 text-muted-foreground">
                      {p.nationalTeamId ? teams[p.nationalTeamId]?.name ?? "—" : "—"}
                    </td>
                    <td className="p-3 text-right">
                      <Link
                        to={`/admin/players/${p.id}/edit`}
                        className="text-primary hover:underline"
                      >
                        Edit
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>
      <div className="flex items-center justify-between text-sm">
        <span>Page {page} of {totalPages} · {data?.total ?? 0} total</span>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Prev</Button>
          <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Next</Button>
        </div>
      </div>
    </div>
  )
}
