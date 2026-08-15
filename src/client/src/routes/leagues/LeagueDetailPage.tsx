import { useEffect, useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { ApiError, apiFetch } from "@/lib/api"
import type { LeagueDetailResponse } from "@/leagues/types"
import { MembersCard } from "@/components/leagues/MembersCard"
import { ScoringCard } from "@/components/leagues/ScoringCard"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"

// One league: what it is bound to, the invite code and link to share (FR-007), the scoring config,
// and the roster. The page is a composition of cards that own their own interactions — Scoring
// (S-04) the rule editor, Members (S-05) the roster plus leave and transfer.
export function LeagueDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()

  const [league, setLeague] = useState<LeagueDetailResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Which of the two copy buttons last succeeded — one mechanism, so the confirmations cannot
  // both light up at once.
  const [copied, setCopied] = useState<"code" | "link" | null>(null)

  useEffect(() => {
    if (!id) return
    void (async () => {
      try {
        setLeague(await apiFetch<LeagueDetailResponse>(`/api/leagues/${id}`))
      } catch (err) {
        // The API returns 404 both for a missing league and for one the caller may not see.
        if (err instanceof ApiError && err.status === 404) setNotFound(true)
        else setError(err instanceof Error ? err.message : "Failed to load the league.")
      } finally {
        setLoading(false)
      }
    })()
  }, [id])

  const copy = async (what: "code" | "link", value: string) => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(what)
      window.setTimeout(() => setCopied(null), 2000)
    } catch {
      setError(
        what === "code"
          ? "Could not copy the invite code — copy it by hand."
          : "Could not copy the invite link — copy the code instead.",
      )
    }
  }

  if (loading) return <div className="p-6">Loading…</div>

  if (notFound) {
    return (
      <div className="grid gap-4 p-6">
        <h1 className="text-2xl font-semibold">League not found</h1>
        <p className="text-muted-foreground">
          This league does not exist, or you are not a member of it.
        </p>
        <div>
          <Button variant="outline" onClick={() => navigate("/app/leagues")}>Back to my leagues</Button>
        </div>
      </div>
    )
  }

  if (!league) {
    return (
      <div className="grid gap-4 p-6">
        <div role="alert" className="text-sm text-destructive">{error ?? "Failed to load the league."}</div>
        <div>
          <Button variant="outline" onClick={() => navigate("/app/leagues")}>Back to my leagues</Button>
        </div>
      </div>
    )
  }

  return (
    <div className="grid gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{league.name}</h1>
        <div className="flex items-center gap-2">
          <Badge variant={league.isOrganizer ? "default" : "secondary"}>
            {league.isOrganizer ? "Organizer" : "Member"}
          </Badge>
          <Button onClick={() => navigate(`/app/leagues/${league.id}/predictions`)}>Predictions</Button>
          <Button variant="outline" onClick={() => navigate("/app/leagues")}>Back</Button>
        </div>
      </div>

      {error && <div role="alert" className="text-sm text-destructive">{error}</div>}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>Invite code</CardTitle></CardHeader>
          <CardContent className="grid gap-3">
            <div className="flex flex-wrap items-center gap-3">
              <code className="rounded border border-input bg-background px-3 py-2 text-lg tracking-widest">
                {league.inviteCode}
              </code>
              <Button
                variant="outline"
                size="sm"
                onClick={() => void copy("code", league.inviteCode)}
              >
                {copied === "code" ? "Copied" : "Copy"}
              </Button>
              {/* The link is what actually gets pasted into a chat — it carries the code and
                  survives sign-in, so a friend without an account lands back on the prefilled
                  join page. */}
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  void copy(
                    "link",
                    `${window.location.origin}/app/leagues/join/${league.inviteCode}`,
                  )
                }
              >
                {copied === "link" ? "Link copied" : "Copy invite link"}
              </Button>
            </div>
            <p className="text-sm text-muted-foreground">
              Share the code or the link with friends so they can join the league.
            </p>
            <div className="flex flex-wrap gap-4 text-sm">
              <span>{league.tournamentName}</span>
              <span className="text-muted-foreground">
                {league.memberCount} {league.memberCount === 1 ? "member" : "members"}
              </span>
            </div>
          </CardContent>
        </Card>

        <ScoringCard league={league} onLeagueChange={setLeague} />

        <MembersCard league={league} onLeagueChange={setLeague} />
      </div>
    </div>
  )
}
