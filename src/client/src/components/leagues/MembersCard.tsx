import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { useAuth } from "@/auth/useAuth"
import { ApiError, apiFetch } from "@/lib/api"
import type { LeagueDetailResponse } from "@/leagues/types"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

// Who is in the league, and the two membership actions (S-05). Which affordance appears is decided
// from the caller's own position, so the server's 409 is never discovered by hitting it: a member
// can leave, an organizer with other members must hand the league over first, and an organizer who
// is alone in it leaves *and* deletes it. Transfer feeds the server's response straight back, so
// the page flips to the member view from the server's answer rather than a local guess.

interface MembersCardProps {
  league: LeagueDetailResponse
  onLeagueChange: (league: LeagueDetailResponse) => void
}

export function MembersCard({ league, onLeagueChange }: MembersCardProps) {
  const navigate = useNavigate()
  const { user } = useAuth()

  const [transferTo, setTransferTo] = useState("")
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const otherMembers = league.members.filter((m) => m.userId !== user?.id)
  // memberCount, not members.length: the roster drops a membership whose user row is gone, while
  // the server's own "are you alone here?" check counts membership rows. Reading the same number
  // it does keeps the buttons honest.
  const isSoleMember = league.memberCount <= 1
  const canTransfer = league.isOrganizer && otherMembers.length > 0
  const canLeave = !league.isOrganizer || isSoleMember

  const messageFor = (err: unknown, fallback: string) =>
    err instanceof ApiError
      ? err.problem?.detail ?? err.message
      : err instanceof Error
        ? err.message
        : fallback

  const leave = async () => {
    const question =
      league.isOrganizer
        ? `Leave and delete "${league.name}"? You are its only member, so the league and its ` +
          "scoring rules are removed for good."
        : `Leave "${league.name}"? You'll need the invite code to rejoin.`
    if (!window.confirm(question)) return

    setBusy(true)
    setError(null)
    try {
      await apiFetch<void>(`/api/leagues/${league.id}/membership`, { method: "DELETE" })
      navigate("/app/leagues")
    } catch (err) {
      setError(messageFor(err, "Could not leave the league."))
      setBusy(false)
    }
  }

  const transfer = async () => {
    const target = league.members.find((m) => m.userId === transferTo)
    if (!target) return
    if (
      !window.confirm(
        `Make ${target.displayName} the organizer of "${league.name}"? You stay a member, but you ` +
          "will no longer be able to change its scoring rules.",
      )
    )
      return

    setBusy(true)
    setError(null)
    try {
      const updated = await apiFetch<LeagueDetailResponse>(
        `/api/leagues/${league.id}/organizer`,
        { method: "PUT", body: { userId: target.userId } },
      )
      onLeagueChange(updated)
      setTransferTo("")
    } catch (err) {
      setError(messageFor(err, "Could not transfer the league."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle>Members</CardTitle></CardHeader>
      <CardContent className="grid gap-4">
        <ul className="grid gap-2">
          {league.members.map((m) => (
            <li key={m.userId} className="flex items-center justify-between gap-3 text-sm">
              <span>
                {m.displayName}
                {m.userId === user?.id && (
                  <span className="text-muted-foreground"> (you)</span>
                )}
              </span>
              <Badge variant={m.role === "Organizer" ? "default" : "secondary"}>
                {m.role}
              </Badge>
            </li>
          ))}
        </ul>

        {error && <div role="alert" className="text-sm text-destructive">{error}</div>}

        {canTransfer && (
          <div className="grid gap-2 border-t border-border pt-4">
            <label htmlFor="transferTo" className="text-sm font-medium">
              Hand the league to another member
            </label>
            <div className="flex flex-wrap items-center gap-2">
              <select
                id="transferTo"
                className="rounded border border-input bg-background px-3 py-2 text-sm"
                value={transferTo}
                onChange={(e) => setTransferTo(e.target.value)}
                disabled={busy}
              >
                <option value="">— pick a member —</option>
                {otherMembers.map((m) => (
                  <option key={m.userId} value={m.userId}>{m.displayName}</option>
                ))}
              </select>
              <Button
                variant="outline"
                size="sm"
                disabled={busy || transferTo === ""}
                onClick={() => void transfer()}
              >
                {busy ? "Working…" : "Transfer"}
              </Button>
            </div>
          </div>
        )}

        <div className="border-t border-border pt-4">
          {canLeave ? (
            <Button
              variant={isSoleMember && league.isOrganizer ? "destructive" : "outline"}
              size="sm"
              disabled={busy}
              onClick={() => void leave()}
            >
              {isSoleMember && league.isOrganizer ? "Leave and delete league" : "Leave league"}
            </Button>
          ) : (
            <p className="text-sm text-muted-foreground">
              You organize this league, so you have to hand it to another member before you can
              leave.
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
