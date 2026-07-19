import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { apiFetch } from "@/lib/api"
import type { NationalityResponse, PlayerPosition, PlayerResponse } from "@/admin/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

const POSITIONS: PlayerPosition[] = ["Unknown", "GK", "DEF", "MID", "FWD"]

export function PlayerFormPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const isEdit = Boolean(id)

  const [name, setName] = useState("")
  const [nationalityId, setNationalityId] = useState<number | null>(null)
  const [position, setPosition] = useState<PlayerPosition>("Unknown")
  const [dateOfBirth, setDateOfBirth] = useState("")
  const [heightCm, setHeightCm] = useState<string>("")
  const [externalPlayerId, setExternalPlayerId] = useState<string>("")
  const [clubTeamId, setClubTeamId] = useState("")
  const [nationalTeamId, setNationalTeamId] = useState("")
  const [nats, setNats] = useState<NationalityResponse[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        const list = await apiFetch<NationalityResponse[]>("/api/nationalities")
        setNats(list)
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load nationalities.")
      }
    })()
  }, [])

  useEffect(() => {
    if (!isEdit || !id) return
    void (async () => {
      try {
        const p = await apiFetch<PlayerResponse>(`/api/players/${id}`)
        setName(p.name)
        setNationalityId(p.nationalityId)
        setPosition(p.position)
        setDateOfBirth(p.dateOfBirth ?? "")
        setHeightCm(p.heightCm?.toString() ?? "")
        setExternalPlayerId(p.externalPlayerId ? p.externalPlayerId.toString() : "")
        setClubTeamId(p.clubTeamId ?? "")
        setNationalTeamId(p.nationalTeamId ?? "")
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load.")
      }
    })()
  }, [id, isEdit])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    const body = {
      name,
      nationalityId,
      position,
      dateOfBirth: dateOfBirth || null,
      heightCm: heightCm ? Number(heightCm) : null,
      externalPlayerId: externalPlayerId ? Number(externalPlayerId) : null,
      clubTeamId: clubTeamId || null,
      nationalTeamId: nationalTeamId || null,
    }
    try {
      if (isEdit) {
        await apiFetch<PlayerResponse>(`/api/players/${id}`, { method: "PATCH", body })
      } else {
        await apiFetch<PlayerResponse>("/api/players", { method: "POST", body })
      }
      navigate("/admin/players")
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid gap-4 p-6">
      <h1 className="text-2xl font-semibold">{isEdit ? "Edit player" : "New player"}</h1>
      <Card className="max-w-2xl">
        <CardHeader><CardTitle>Details</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={submit}>
            <div className="grid gap-2">
              <Label htmlFor="name">Name</Label>
              <Input id="name" value={name} onChange={(e) => setName(e.target.value)} required />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label htmlFor="nat">Nationality</Label>
                <select
                  id="nat"
                  className="rounded border border-input bg-background px-3 py-2"
                  value={nationalityId ?? ""}
                  onChange={(e) => setNationalityId(e.target.value ? Number(e.target.value) : null)}
                >
                  <option value="">— none —</option>
                  {nats.map((n) => (
                    <option key={n.id} value={n.id}>{n.code} – {n.name}</option>
                  ))}
                </select>
              </div>
              <div className="grid gap-2">
                <Label htmlFor="pos">Position</Label>
                <select
                  id="pos"
                  className="rounded border border-input bg-background px-3 py-2"
                  value={position}
                  onChange={(e) => setPosition(e.target.value as PlayerPosition)}
                >
                  {POSITIONS.map((p) => <option key={p} value={p}>{p}</option>)}
                </select>
              </div>
            </div>
            <div className="grid grid-cols-3 gap-4">
              <div className="grid gap-2">
                <Label htmlFor="dob">Date of birth</Label>
                <Input id="dob" type="date" value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} />
              </div>
              <div className="grid gap-2">
                <Label htmlFor="height">Height (cm)</Label>
                <Input id="height" type="number" value={heightCm} onChange={(e) => setHeightCm(e.target.value)} />
              </div>
              <div className="grid gap-2">
                <Label htmlFor="ext">External player id</Label>
                <Input id="ext" type="number" value={externalPlayerId} onChange={(e) => setExternalPlayerId(e.target.value)} />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label htmlFor="club">Club team id (GUID)</Label>
                <Input id="club" value={clubTeamId} onChange={(e) => setClubTeamId(e.target.value)} />
              </div>
              <div className="grid gap-2">
                <Label htmlFor="natTeam">National team id (GUID)</Label>
                <Input id="natTeam" value={nationalTeamId} onChange={(e) => setNationalTeamId(e.target.value)} />
              </div>
            </div>
            {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
            <div className="flex gap-2">
              <Button type="submit" disabled={busy}>{busy ? "Saving…" : "Save"}</Button>
              <Button type="button" variant="outline" onClick={() => navigate("/admin/players")}>Cancel</Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
