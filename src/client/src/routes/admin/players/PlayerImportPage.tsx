import { useEffect, useState } from "react"
import { useNavigate } from "react-router-dom"
import { apiFetch } from "@/lib/api"
import type {
  PlayerImportPreview,
  PlayerImportResult,
  TournamentResponse,
} from "@/admin/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Label } from "@/components/ui/label"

export function PlayerImportPage() {
  const navigate = useNavigate()
  const [file, setFile] = useState<File | null>(null)
  const [tournamentId, setTournamentId] = useState<string>("")
  const [tournaments, setTournaments] = useState<TournamentResponse[]>([])
  const [preview, setPreview] = useState<PlayerImportPreview | null>(null)
  const [result, setResult] = useState<PlayerImportResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        const list = await apiFetch<TournamentResponse[]>("/api/tournaments")
        setTournaments(list)
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load tournaments.")
      }
    })()
  }, [])

  const upload = async (dryRun: boolean) => {
    if (!file) {
      setError("Pick a CSV file first.")
      return
    }
    setBusy(true)
    setError(null)
    try {
      const form = new FormData()
      form.append("file", file)
      const params = new URLSearchParams({ dryRun: String(dryRun) })
      if (tournamentId) params.set("tournamentId", tournamentId)
      const res = await apiFetch<PlayerImportPreview | PlayerImportResult>(
        `/api/players/import?${params.toString()}`,
        { method: "POST", body: form },
      )
      if (dryRun) {
        setPreview(res as PlayerImportPreview)
        setResult(null)
      } else {
        setResult(res as PlayerImportResult)
        setPreview(null)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid gap-4 p-6">
      <h1 className="text-2xl font-semibold">Import players (CSV)</h1>
      <Card className="max-w-3xl">
        <CardHeader><CardTitle>Upload</CardTitle></CardHeader>
        <CardContent className="grid gap-4">
          <div className="grid gap-2">
            <Label htmlFor="csv">CSV file</Label>
            <input
              id="csv"
              type="file"
              accept=".csv"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              className="rounded border border-input bg-background px-3 py-2"
            />
            <p className="text-xs text-muted-foreground">
              Headers: Name,NationalityCode,Position,DateOfBirth,HeightCm,ExternalPlayerId,ClubTeam,NationalTeam.
              ClubTeam and NationalTeam name an existing team (case-insensitive) and are what make
              a player pickable as a match's first scorer; an unknown name is a row conflict, not a
              new team. Leaving them blank keeps the player's current links.
            </p>
          </div>
          <div className="grid gap-2">
            <Label htmlFor="t">Bind to tournament (optional)</Label>
            <select
              id="t"
              className="rounded border border-input bg-background px-3 py-2"
              value={tournamentId}
              onChange={(e) => setTournamentId(e.target.value)}
            >
              <option value="">— none —</option>
              {tournaments.map((t) => (
                <option key={t.id} value={t.id}>{t.name} (S{t.season})</option>
              ))}
            </select>
          </div>
          {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
          <div className="flex gap-2">
            <Button variant="outline" disabled={busy || !file} onClick={() => void upload(true)}>
              Preview
            </Button>
            <Button disabled={busy || !preview} onClick={() => void upload(false)}>
              Commit
            </Button>
            <Button variant="outline" type="button" onClick={() => navigate("/admin/players")}>Cancel</Button>
          </div>
        </CardContent>
      </Card>

      {preview && (
        <Card>
          <CardHeader><CardTitle>Preview</CardTitle></CardHeader>
          <CardContent className="grid gap-3">
            <p className="text-sm">
              Create: <b>{preview.toCreate}</b> · Update: <b>{preview.toUpdate}</b> · Skipped: <b>{preview.skipped}</b>
            </p>
            {preview.conflicts.length > 0 && (
              <div className="grid gap-1">
                <h3 className="font-semibold text-destructive">Conflicts</h3>
                <ul className="text-sm">
                  {preview.conflicts.map((c) => (
                    <li key={c.lineNumber}>Line {c.lineNumber} — {c.name}: {c.reason}</li>
                  ))}
                </ul>
              </div>
            )}
            <details>
              <summary className="cursor-pointer text-sm text-muted-foreground">Rows ({preview.rows.length})</summary>
              <table className="mt-2 w-full text-sm">
                <thead>
                  <tr className="border-b">
                    <th className="p-2 text-left">Line</th>
                    <th className="p-2 text-left">Name</th>
                    <th className="p-2 text-left">Nat</th>
                    <th className="p-2 text-left">Action</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.rows.map((r) => (
                    <tr key={r.lineNumber} className="border-b last:border-b-0">
                      <td className="p-2">{r.lineNumber}</td>
                      <td className="p-2">{r.name}</td>
                      <td className="p-2">{r.nationalityCode}</td>
                      <td className="p-2">{r.action}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </details>
          </CardContent>
        </Card>
      )}

      {result && (
        <Card>
          <CardHeader><CardTitle>Commit result</CardTitle></CardHeader>
          <CardContent>
            <p className="text-sm">
              Created: <b>{result.created}</b> · Updated: <b>{result.updated}</b> ·
              Skipped: <b>{result.skipped}</b> · Squads added: <b>{result.squadsAdded}</b>
            </p>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
