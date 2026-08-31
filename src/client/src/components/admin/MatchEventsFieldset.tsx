import { useEffect, useState } from "react"
import { apiFetch } from "@/lib/api"
import type {
  EligiblePlayerResponse,
  MatchEventEditDto,
  MatchEventTypeResponse,
  MatchEventsResponse,
} from "@/admin/types"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

// The goals and cards of one match (FR-005). Without this surface CorrectGoalScorer and the card
// rules score zero for everyone forever, which is indistinguishable from a scoring bug.
//
// Replace-all, not per-row: the same semantic ingest already uses (Clear()-then-add), so one write
// path covers both writers and a correction is just "edit the list and save". Saving re-scores the
// match server-side, so the points move without a second action.

interface EventRow {
  // Local key only — MatchEvent.Id is minted fresh on every replace-all save, so it is never a
  // stable React key and never an ordering input.
  key: string
  matchEventTypeId: string
  playerId: string
  teamId: string
  minute: string
  minuteExtra: string
}

interface MatchEventsFieldsetProps {
  matchId: string
  homeTeamId: string
  homeTeamName: string
  awayTeamId: string
  awayTeamName: string
}

let nextKey = 0
const newKey = () => `row-${nextKey++}`

function toRow(e: MatchEventEditDto): EventRow {
  return {
    key: newKey(),
    matchEventTypeId: String(e.matchEventTypeId),
    playerId: e.playerId,
    teamId: e.teamId,
    minute: String(e.minute),
    minuteExtra: e.minuteExtra === null ? "" : String(e.minuteExtra),
  }
}

export function MatchEventsFieldset({
  matchId,
  homeTeamId,
  homeTeamName,
  awayTeamId,
  awayTeamName,
}: MatchEventsFieldsetProps) {
  const [rows, setRows] = useState<EventRow[]>([])
  const [types, setTypes] = useState<MatchEventTypeResponse[]>([])
  const [players, setPlayers] = useState<EligiblePlayerResponse[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [warning, setWarning] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        const [stored, typeList, eligible] = await Promise.all([
          apiFetch<MatchEventsResponse>(`/api/matches/${matchId}/events`),
          apiFetch<MatchEventTypeResponse[]>("/api/match-event-types"),
          apiFetch<EligiblePlayerResponse[]>(`/api/matches/${matchId}/eligible-players`),
        ])
        setRows(stored.events.map(toRow))
        setTypes(typeList)
        setPlayers(eligible)
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load events.")
      }
    })()
  }, [matchId])

  const addRow = () =>
    setRows((current) => [
      ...current,
      {
        key: newKey(),
        matchEventTypeId: types[0] ? String(types[0].id) : "",
        playerId: "",
        teamId: homeTeamId,
        minute: "",
        minuteExtra: "",
      },
    ])

  const removeRow = (key: string) => setRows((current) => current.filter((r) => r.key !== key))

  const setField = (key: string, field: keyof EventRow, value: string) =>
    setRows((current) => current.map((r) => (r.key === key ? { ...r, [field]: value } : r)))

  const save = async () => {
    setBusy(true)
    setError(null)
    setNotice(null)
    setWarning(null)
    try {
      const body = {
        events: rows.map((r) => ({
          matchEventTypeId: Number(r.matchEventTypeId),
          playerId: r.playerId,
          teamId: r.teamId,
          minute: r.minute === "" ? 0 : Number(r.minute),
          minuteExtra: r.minuteExtra === "" ? null : Number(r.minuteExtra),
        })),
      }
      const saved = await apiFetch<MatchEventsResponse>(`/api/matches/${matchId}/events`, {
        method: "PUT",
        body,
      })
      // Replace local state with what actually landed, the same way the predictions screen does.
      setRows(saved.events.map(toRow))
      // The result saved even when scoring did not — a warning, not a save error.
      if (saved.scoringFailed) setWarning(saved.scoringMessage ?? "Points could not be recalculated.")
      else setNotice("Events saved. Points recalculated.")
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save events.")
    } finally {
      setBusy(false)
    }
  }

  const incomplete = rows.some((r) => !r.matchEventTypeId || !r.playerId || !r.teamId || r.minute === "")

  return (
    <fieldset className="grid gap-3" disabled={busy}>
      <legend className="text-sm font-medium">Goals and cards</legend>
      <p className="text-sm text-muted-foreground">
        The list replaces what is stored. Saving recalculates every league's points for this match.
      </p>

      {rows.length === 0 && (
        <p className="text-sm text-muted-foreground">No events recorded yet.</p>
      )}

      {rows.map((row) => (
        <div key={row.key} className="grid items-end gap-2 sm:grid-cols-[1fr_1fr_1fr_5rem_5rem_auto]">
          <div className="grid gap-1">
            <Label htmlFor={`type-${row.key}`}>Type</Label>
            <select
              id={`type-${row.key}`}
              className="rounded border border-input bg-background px-3 py-2"
              value={row.matchEventTypeId}
              onChange={(e) => setField(row.key, "matchEventTypeId", e.target.value)}
            >
              <option value="">— pick —</option>
              {types.map((t) => <option key={t.id} value={t.id}>{t.displayName}</option>)}
            </select>
          </div>

          <div className="grid gap-1">
            <Label htmlFor={`player-${row.key}`}>Player</Label>
            <select
              id={`player-${row.key}`}
              className="rounded border border-input bg-background px-3 py-2"
              value={row.playerId}
              onChange={(e) => setField(row.key, "playerId", e.target.value)}
            >
              <option value="">— pick —</option>
              {players.map((p) => <option key={p.playerId} value={p.playerId}>{p.name}</option>)}
            </select>
          </div>

          <div className="grid gap-1">
            {/* Credited team, not the player's own: a player from one side credited to the other
                is how an own goal is recorded — the same shape the member's forecast uses. */}
            <Label htmlFor={`team-${row.key}`}>Credited to</Label>
            <select
              id={`team-${row.key}`}
              className="rounded border border-input bg-background px-3 py-2"
              value={row.teamId}
              onChange={(e) => setField(row.key, "teamId", e.target.value)}
            >
              <option value={homeTeamId}>{homeTeamName}</option>
              <option value={awayTeamId}>{awayTeamName}</option>
            </select>
          </div>

          <div className="grid gap-1">
            <Label htmlFor={`minute-${row.key}`}>Minute</Label>
            <Input
              id={`minute-${row.key}`}
              type="number"
              min={0}
              max={130}
              value={row.minute}
              onChange={(e) => setField(row.key, "minute", e.target.value)}
            />
          </div>

          <div className="grid gap-1">
            <Label htmlFor={`extra-${row.key}`}>+</Label>
            <Input
              id={`extra-${row.key}`}
              type="number"
              min={0}
              max={30}
              aria-label="Added time"
              value={row.minuteExtra}
              onChange={(e) => setField(row.key, "minuteExtra", e.target.value)}
            />
          </div>

          <Button type="button" variant="outline" onClick={() => removeRow(row.key)}>
            Remove
          </Button>
        </div>
      ))}

      {players.length === 0 && (
        <p className="text-xs text-muted-foreground">
          Neither team has linked players, so no event can be entered. Link squads first.
        </p>
      )}
      {error && <div role="alert" className="text-sm text-destructive">{error}</div>}
      {warning && <div role="alert" className="text-sm text-destructive">{warning}</div>}
      {notice && <p className="text-sm text-muted-foreground">{notice}</p>}

      <div className="flex gap-2">
        <Button type="button" variant="outline" onClick={addRow} disabled={players.length === 0}>
          Add event
        </Button>
        {/* type="button": the fieldset sits inside the match form, and this save is its own write. */}
        <Button type="button" onClick={() => void save()} disabled={busy || incomplete}>
          {busy ? "Saving…" : "Save events"}
        </Button>
      </div>
    </fieldset>
  )
}
