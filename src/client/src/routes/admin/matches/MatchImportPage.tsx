import { useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { apiFetch } from "@/lib/api"
import type { MatchImportPreview, MatchImportResult } from "@/admin/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Label } from "@/components/ui/label"

export function MatchImportPage() {
  const { tournamentId } = useParams()
  const navigate = useNavigate()
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<MatchImportPreview | null>(null)
  const [result, setResult] = useState<MatchImportResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const detailPath = `/admin/tournaments/${tournamentId}`

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
      const res = await apiFetch<MatchImportPreview | MatchImportResult>(
        `/api/tournaments/${tournamentId}/matches/import?dryRun=${dryRun}`,
        { method: "POST", body: form },
      )
      if (dryRun) {
        setPreview(res as MatchImportPreview)
        setResult(null)
      } else {
        setResult(res as MatchImportResult)
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
      <h1 className="text-2xl font-semibold">Import matches (CSV)</h1>
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
              Headers: HomeTeam,AwayTeam,KickoffUtc,Status,HomeScore,AwayScore,Round.
              Teams are matched by name and created when missing. KickoffUtc is an ISO timestamp
              (e.g. 2026-07-20T18:00:00Z). Round is required — a blank one is reported as a row
              conflict, since members fill and save predictions one round at a time.
            </p>
          </div>
          {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
          <div className="flex gap-2">
            <Button variant="outline" disabled={busy || !file} onClick={() => void upload(true)}>
              Preview
            </Button>
            <Button disabled={busy || !preview} onClick={() => void upload(false)}>
              Commit
            </Button>
            <Button variant="outline" type="button" onClick={() => navigate(detailPath)}>Cancel</Button>
          </div>
        </CardContent>
      </Card>

      {preview && (
        <Card>
          <CardHeader><CardTitle>Preview</CardTitle></CardHeader>
          <CardContent className="grid gap-3">
            <p className="text-sm">
              Create: <b>{preview.toCreate}</b> · Skipped: <b>{preview.skipped}</b> ·
              New teams: <b>{preview.teamsToCreate}</b>
            </p>
            {preview.conflicts.length > 0 && (
              <div className="grid gap-1">
                <h3 className="font-semibold text-destructive">Conflicts</h3>
                <ul className="text-sm">
                  {preview.conflicts.map((c) => (
                    <li key={c.lineNumber}>Line {c.lineNumber}: {c.reason}</li>
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
                    <th className="p-2 text-left">Match</th>
                    <th className="p-2 text-left">Kickoff</th>
                    <th className="p-2 text-left">Status</th>
                    <th className="p-2 text-left">Score</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.rows.map((r) => (
                    <tr key={r.lineNumber} className="border-b last:border-b-0">
                      <td className="p-2">{r.lineNumber}</td>
                      <td className="p-2">{r.homeTeam} vs {r.awayTeam}</td>
                      <td className="p-2">{new Date(r.kickoffUtc).toLocaleString()}</td>
                      <td className="p-2">{r.status}</td>
                      <td className="p-2">{r.homeScore ?? "–"} : {r.awayScore ?? "–"}</td>
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
          <CardContent className="grid gap-3">
            <p className="text-sm">
              Created: <b>{result.created}</b> · Skipped: <b>{result.skipped}</b> ·
              Teams created: <b>{result.teamsCreated}</b>
            </p>
            <Button variant="outline" className="w-fit" onClick={() => navigate(detailPath)}>
              Back to tournament
            </Button>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
