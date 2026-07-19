import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { apiFetch } from "@/lib/api"
import type { TournamentResponse } from "@/admin/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

export function TournamentFormPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const isEdit = Boolean(id)

  const [name, setName] = useState("")
  const [externalApiId, setExternalApiId] = useState("")
  const [season, setSeason] = useState<number>(new Date().getFullYear())
  const [startDate, setStartDate] = useState("")
  const [endDate, setEndDate] = useState("")
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!isEdit || !id) return
    void (async () => {
      try {
        const t = await apiFetch<TournamentResponse>(`/api/tournaments/${id}`)
        setName(t.name)
        setExternalApiId(t.externalApiId ?? "")
        setSeason(t.season)
        setStartDate(t.startDate)
        setEndDate(t.endDate)
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load.")
      }
    })()
  }, [id, isEdit])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      if (isEdit) {
        await apiFetch<TournamentResponse>(`/api/tournaments/${id}`, {
          method: "PUT",
          body: { name, season, startDate, endDate },
        })
      } else {
        await apiFetch<TournamentResponse>("/api/tournaments", {
          method: "POST",
          body: {
            name,
            externalApiId: externalApiId.trim() || null,
            season,
            startDate,
            endDate,
          },
        })
      }
      navigate("/admin/tournaments")
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid gap-4 p-6">
      <h1 className="text-2xl font-semibold">{isEdit ? "Edit tournament" : "New tournament"}</h1>
      <Card className="max-w-2xl">
        <CardHeader><CardTitle>Details</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={submit}>
            <div className="grid gap-2">
              <Label htmlFor="name">Name</Label>
              <Input id="name" value={name} onChange={(e) => setName(e.target.value)} required />
            </div>
            <div className="grid gap-2">
              <Label htmlFor="extApi">External API id</Label>
              <Input id="extApi" value={externalApiId} onChange={(e) => setExternalApiId(e.target.value)} disabled={isEdit} />
              {isEdit && <p className="text-xs text-muted-foreground">Immutable after create.</p>}
            </div>
            <div className="grid gap-2">
              <Label htmlFor="season">Season</Label>
              <Input id="season" type="number" value={season} onChange={(e) => setSeason(Number(e.target.value))} required />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label htmlFor="startDate">Start date</Label>
                <Input id="startDate" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} required />
              </div>
              <div className="grid gap-2">
                <Label htmlFor="endDate">End date</Label>
                <Input id="endDate" type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} required />
              </div>
            </div>
            {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
            <div className="flex gap-2">
              <Button type="submit" disabled={busy}>{busy ? "Saving…" : "Save"}</Button>
              <Button type="button" variant="outline" onClick={() => navigate("/admin/tournaments")}>Cancel</Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
