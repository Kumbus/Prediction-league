# F-03 Football API research + source decision

> Research for roadmap F-03 (`football-api-ingest`). Resolves OQ #1 (source) and OQ #2 (granular fallback).
> Date: 2026-06-04. Method: web search (Exa), Feb–May 2026 sources.

## Decision

**Source: API-Football (api-sports.io), free tier.** Driver: €0 budget is a hard constraint.

- Direct api-sports.io key (`x-apisports-key` header) — **not** via RapidAPI (avoids extra auth layer).
- Free tier: 100 requests/day, 10 req/min, recent seasons only, **all endpoints incl. `Events` (goals + cards)**.
- Upgrade path if needed at launch: Pro $19/mo → 7,500 req/day, 300 req/min. Data structures identical across tiers (only the key changes).

### OQ #1 — source selection → RESOLVED
API-Football. Any REST/JSON API is .NET-compatible (`HttpClient` + `System.Text.Json`, no SDK lock-in).

### OQ #2 — granular-detail fallback → RESOLVED (no degrade)
`Events` endpoint carries goals + cards even on the free tier → full FR-005 granular scoring works at €0. **No final-score-only degrade required.** S-02 keeps scorer/card scoring.

## Candidates compared

Discriminator for F-03 = scorers + cards (FR-005), not basic fixtures/results.

| API | Scorers + Cards | Free tier | Paid entry | Rate limit | Notes |
|---|---|---|---|---|---|
| **API-Football (api-sports)** ✅ chosen | ✅ `Events` all tiers | 100 req/day, all endpoints | $19/mo (7,500/day) | 10/min free, 300/min Pro | Granular detail at €0. 1,200+ leagues. |
| football-data.org | ❌ free / ✅ €29 "Deep Data" | 12 comps, fixtures+results+tables only | €29/mo for scorers+cards | 10/min free, 30/min paid | Cleanest docs, stable since 2013. Scorers+cards gated behind €29. Was the paid-budget pick. |
| apifootball.com | ✅ free | England Champ. + France Ligue 2 only | $21/mo (60+ leagues) | 180/hr free | Full granular free but 2 leagues. |
| Sportmonks | ✅ | trial only | €29/mo = 5 leagues | varies | Tiered pricing trap. Skip. |
| Goalserve / iSports / Entity / Sportradar / Opta | ✅ | trial/enterprise | $150+/mo | — | Overkill for friend-group MVP. Skip. |

## Staying under 100 req/day

Friend-group + single admin-seeded tournament = low volume **if** polled smart (not live-every-15s).

Per active match-day budget:
- `1` call → `GET /fixtures?league={id}&season={yr}&date={today}` — all day's fixtures + final scores in one shot.
- `1` call per **finished/in-play** fixture → `GET /fixtures/events?fixture={id}` — goals + cards.
- World Cup group-stage worst case ~6 matches/day → ~7 calls. Quiet days → 1–2. 100/day comfortable.

Rules:
- Timer fires ~every 30–60 min during match windows only; idle otherwise. No live-15s polling (out of scope v1).
- Only fetch events for fixtures whose status flipped to FT (or live). Skip NS / already-finished.
- Cache hard: persist fixtures/results; pull events once per match after FT. Re-poll finished match only on API-flagged correction.
- Logos/flags don't count vs quota — download once, serve from own storage (CDN has own throttle).
- Read response headers: `x-ratelimit-requests-remaining` (daily), `X-Ratelimit-Remaining` (per-min). Back off, no tight retries.

## Multi-account to double the quota — rejected

Q: run two free accounts (two keys) to get 200 req/day?

Mechanically trivial (round-robin / fail-over the `x-apisports-key` header at quota). **Rejected:**
- ToS violation — API-Sports free tier is one-per-person; multi-account to bypass limits is forbidden. They fingerprint (email/IP/usage) and firewall-ban without warning. Risk: lose **both** keys mid-tournament → standings stop → FR-011 scoring guarantee silently breaks.
- Buys little: 100→200/day, ban risk for a 2× that hard caching mostly recovers anyway.

Path if 100/day pinches (in order): (1) cache harder + measure real usage first, (2) $19 Pro = 7,500/day, legitimate, one key, identical data shapes, (3) tighten poll window to actual kickoff windows. **One legit key only.**

## Two different free APIs — documented fallback (not built up front)

Different providers = different ToS → legitimate, no ban risk. Cost is integration, not legality.

- **Redundancy (A or B, same data):** low value — still hit each one's data ceiling, double the parsing surface. Skip.
- **Split by role (A and B, each cheap at its job):** the useful shape if 100/day pinches at €0:
  - **football-data.org free** → fixtures + schedules + final results on 12 major comps (World Cup, CL, Big 5). ~10/min ≈ ~14k/day; no scorers/cards needed here.
  - **API-Football free** → spend the scarce 100/day **only** on `/fixtures/events` (goals + cards) for finished matches.
  - Reserves the tightest quota for the one thing only it gives cheaply → 100/day pinch largely disappears.

**Catch — entity reconciliation:** different providers = different team/fixture IDs. Matching a football-data.org fixture to its API-Football events (by date + kickoff + team names) is fuzzy; a bad match = wrong scorers = silent FR-011 break. Plus two clients, two schemas, two failure modes.

**Decision:** do NOT build up front (premature complexity under `main_goal: speed`, friend-group scale). Single API-Football key + hard caching almost certainly clears 100/day for one tournament. Keep the role-split as fallback only if (1) caching proves insufficient AND (2) $19 Pro is off the table.

## .NET integration shape
- `HttpClient` via `IHttpClientFactory` (typed client) + `System.Text.Json`.
- Auth header: `x-apisports-key: {key}`.
- Azure Functions timer trigger writes through F-01 repos (`Application/Abstractions/Persistence`, `Infrastructure/Persistence/Repositories`).

## Roadmap follow-ups (not yet applied)
- F-03: `blocked → ready` (OQ #1 resolved).
- S-02: drop granular-fallback blocker (OQ #2 resolved — scorers+cards free).
- Add risk: 100 req/day cap → ingest must be poll-frugal + cache; live-polling out of scope v1.
