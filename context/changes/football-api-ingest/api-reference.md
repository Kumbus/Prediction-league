# F-03 Football API reference (fetched docs)

> Companion to `api-research.md` (source decision). This = endpoint/auth/payload contracts for implementing F-03 ingest.
> Source: Context7 — api-sports.io Football v3 (`/websites/api-sports_io_football_v3`) + football-data.org v4 (`/websites/football-data_general_v4`). Fetched 2026-06-04.
> Primary = **API-Football**. football-data.org = documented fallback only (role-split, see `api-research.md` §"Two different free APIs"). Don't build fallback up front.

---

## Primary — API-Football (api-sports.io v3)

- Base URL: `https://v3.football.api-sports.io`
- Auth header: `x-apisports-key: {key}` (direct api-sports key, NOT RapidAPI).
- Envelope (all endpoints): `{ get, parameters, errors[], results, paging{current,total}, response[] }`.
  - Check `errors[]` non-empty = logical error even on HTTP 200. Treat as failure.
  - `204 No Content` = valid empty (e.g. events before kickoff). Not an error.
- Rate-limit headers to read + back off on (per `api-research.md`): `x-ratelimit-requests-remaining` (daily quota), `X-RateLimit-Remaining` (per-min). No tight retries.

### Endpoint 1 — fixtures + final scores (FR-004)

`GET /fixtures` — one call returns a day's fixtures + scores.

Key query params:
- `league` (int), `season` (int, e.g. `2019`) — scope to seeded tournament.
- `date` (`YYYY-MM-DD`) — day's slate. Daily-budget call: `?league={id}&season={yr}&date={today}`.
- `from`/`to` (`YYYY-MM-DD`), `round` (`Regular Season - 1`), `status` (`ft`=finished), `team`, `id`, `ids`, `live=all`, `next`/`last` (int), `timezone` (`Europe/London`).

Response item shape (fields F-03 maps to fixtures/results):
```jsonc
{
  "fixture": {
    "id": 239625,                         // API fixture id — persist as external key
    "date": "2020-02-06T14:00:00+00:00",  // kickoff (ISO, tz-aware) — drives S-06 lock
    "timestamp": 1580997600,
    "timezone": "UTC",
    "status": { "long": "Halftime", "short": "HT", "elapsed": 45, "extra": null }
    // status.short: NS (not started), 1H/HT/2H/LIVE, FT (finished), + others
  },
  "league": { "id": 200, "season": 2019, "round": "Regular Season - 14", "name": "...", "logo": "...", "flag": "..." },
  "teams": {
    "home": { "id": 967, "name": "Rapide Oued ZEM", "logo": "...", "winner": false },
    "away": { "id": 968, "name": "Wydad AC", "logo": "...", "winner": true }
  },
  "goals": { "home": 0, "away": 1 },      // current/final score
  "score": {
    "halftime": {...}, "fulltime": { "home": null, "away": null },
    "extratime": {...}, "penalty": {...}
  }
}
```
Ingest notes:
- Final result = `score.fulltime` when `status.short == "FT"`; `goals` is live/running.
- Only pull events (below) for fixtures whose status flipped to FT (or live). Skip `NS`/already-finished+cached.
- Logos/flags: download once, serve from own storage — don't re-fetch (CDN throttle, but free vs quota).

### Endpoint 2 — events = goal scorers + cards (FR-005, the wedge)

`GET /fixtures/events?fixture={id}` — 1 call per finished/in-play fixture.

Params: `fixture` (int, **required**), optional `team`, `player`, `type` (`goal`|`card`).
- Event types: **Goal** (Normal Goal, Own Goal, Penalty, Missed Penalty), **Card** (Yellow Card, Red Card), **Subst**, **Var** (Goal cancelled, Penalty confirmed).

`response[]` item shape:
```jsonc
{
  "time": { "elapsed": 25, "extra": null },
  "team":   { "id": 463, "name": "Aldosivi", "logo": "..." },
  "player": { "id": 6126, "name": "F. Andrada" },   // scorer / carded player
  "assist": { "id": null, "name": null },
  "type":   "Goal",                                  // "Goal" | "Card" | "subst" | "Var"
  "detail": "Normal Goal",                           // e.g. "Yellow Card", "Red Card", "Penalty", "Own Goal"
  "comments": null
}
```
Scoring map (drives `ScoringParameter`):
- `CorrectGoalScorer` ← filter `type=="Goal"` (exclude `detail=="Missed Penalty"`; decide Own Goal handling), read `player`.
- `CorrectCardCount` ← filter `type=="Card"`, count by `detail` (Yellow/Red), optionally per team.
- Trailing items may be partial (last array entry can be `{time:{...}}` only) — guard null `type`/`player` on parse.

### Staying under 100 req/day (free tier)
Per active match-day: 1× `/fixtures?...&date=today` + 1× `/fixtures/events?fixture=` per finished/in-play match. World-Cup worst case ~6 matches ⇒ ~7 calls. Cache hard, poll only in match windows. Full rationale in `api-research.md`.

---

## Fallback — football-data.org v4 (NOT built up front)

- Base URL: `https://api.football-data.org/v4`
- Auth header: `X-Auth-Token: {token}`.
- Role in split: fixtures/schedules/results on 12 major comps only (no scorers/cards on free). API-Football keeps the scarce events calls.

Endpoints:
- `GET /v4/competitions/{CODE}/matches?matchday={n}` — comp by code (`PL`, `DED`, `CL`, …).
- `GET /v4/matches` — today's matches across subscribed comps.

Rate limits: unauth = 100 req/24h, area+competition lists only. Free registered = **10 req/min** (≈14k/day); Standard 30/min.

**Catch (why fallback is costly):** different provider = different team/fixture IDs. Reconciling a football-data match ↔ API-Football events is fuzzy (date+kickoff+team-name match); bad match = wrong scorers = silent FR-011 break.

---

## .NET integration shape (from api-research.md §.NET)
- `HttpClient` via `IHttpClientFactory` typed client + `System.Text.Json` (no SDK).
- Default header `x-apisports-key`; deserialize the `response[]` envelope into DTOs, map to F-01 domain.
- Azure Functions timer trigger writes through F-01 repos (`Application/Abstractions/Persistence`, `Infrastructure/Persistence/Repositories`). Timer fires only in match windows.
