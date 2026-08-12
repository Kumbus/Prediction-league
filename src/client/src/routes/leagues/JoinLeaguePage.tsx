import { useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { ApiError, apiFetch } from "@/lib/api"
import type { LeagueDetailResponse } from "@/leagues/types"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

// Join a league by invite code (FR-007, S-05). One screen serves both entry paths: a code typed
// in by hand at /app/leagues/join, and a shared link at /app/leagues/join/:code that prefills it.
export function JoinLeaguePage() {
  const { code } = useParams()
  const navigate = useNavigate()

  // Prefilled from the link but never auto-submitted — following an invite should be a decision,
  // not a side effect of opening a URL.
  const [inviteCode, setInviteCode] = useState((code ?? "").toUpperCase())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const trimmed = inviteCode.trim()
    if (trimmed.length === 0) {
      setError("Enter the invite code your friend shared.")
      return
    }

    setBusy(true)
    setError(null)
    try {
      const league = await apiFetch<LeagueDetailResponse>("/api/leagues/join", {
        method: "POST",
        body: { inviteCode: trimmed },
      })
      // replace: once the join has landed, Back should return to wherever the user came from,
      // not to a join form for a league they are already in.
      navigate(`/app/leagues/${league.id}`, { replace: true })
    } catch (err) {
      // The server masks an unknown code and a league the caller cannot see behind one 404, and
      // ProblemDetails' title wins over its detail in ApiError.message — so say it plainly here.
      setError(
        err instanceof ApiError
          ? err.status === 404
            ? "No league found for that invite code."
            : err.problem?.detail ?? err.message
          : err instanceof Error
            ? err.message
            : "Could not join the league.",
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid gap-4 p-6">
      <h1 className="text-2xl font-semibold">Join a league</h1>
      <Card className="max-w-md">
        <CardHeader><CardTitle>Invite code</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={submit}>
            <div className="grid gap-2">
              <Label htmlFor="inviteCode">Code</Label>
              <Input
                id="inviteCode"
                value={inviteCode}
                // Uppercased as it is typed: the generator's alphabet is uppercase, so this shows
                // the code back in the shape it was shared in.
                onChange={(e) => setInviteCode(e.target.value.toUpperCase())}
                className="tracking-widest"
                autoComplete="off"
                maxLength={32}
                autoFocus
              />
              <p className="text-sm text-muted-foreground">
                Ask the league organizer for the code, or open the link they shared.
              </p>
            </div>

            {error && <div role="alert" className="text-sm text-destructive">{error}</div>}

            <div className="flex gap-2">
              <Button type="submit" disabled={busy || inviteCode.trim().length === 0}>
                {busy ? "Joining…" : "Join league"}
              </Button>
              <Button type="button" variant="outline" onClick={() => navigate("/app/leagues")}>
                Cancel
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
