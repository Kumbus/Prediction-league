#!/usr/bin/env node
// Seed a running Prediction League instance with a REAL competition: real clubs, real fixtures,
// real results, plus demo members whose forecasts produce a standings table worth looking at.
//
// Everything goes through the public HTTP API — no SQL, no direct database access. That matters
// for two reasons: the same script works against localhost and against Azure without a
// connection string, and every row it writes has passed the same validation, kickoff-lock and
// scoring rules a real user's write would.
//
// Source: openfootball/football.json — public domain, no API key, auto-updated daily.
//   https://github.com/openfootball/football.json
//
// ── The one non-obvious thing this script does ────────────────────────────────────────────────
// A forecast can only be filed while its match is still open, and there is no injected clock —
// the kickoff timestamp is the only lever over the lock. So a finished match cannot simply be
// created finished and then predicted. Each round is therefore replayed in three steps:
//
//   1. create the round's matches OPEN, with a kickoff an hour out
//   2. every member files their forecast (accepted, because the matches are open)
//   3. rewrite each match to its REAL past kickoff + Finished + the real score
//
// Step 3 runs scoring inside the request. This is the same ordering tests/e2e/fixture.setup.ts
// uses, for the same reason.
//
// ── Usage ─────────────────────────────────────────────────────────────────────────────────────
//   node scripts/seed-real-league.mjs                       # Premier League 2025/26 at localhost
//   node scripts/seed-real-league.mjs --competition es.1 --season 2025-26 --played 12
//   node scripts/seed-real-league.mjs --api https://my-app.azurewebsites.net \
//     --admin-email me@example.com --admin-password '…'
//
// Run --help for every flag.

const COMPETITIONS = {
  "en.1": "Premier League",
  "es.1": "La Liga",
  "de.1": "Bundesliga",
  "it.1": "Serie A",
  "fr.1": "Ligue 1",
}

const DEFAULTS = {
  api: "https://localhost:7182",
  competition: "en.1",
  season: "2026-27",
  // Matchdays replayed with their real results, from the start of the season. Capped by what has
  // actually been played — early in a live season that is three or four, and asking for more
  // simply replays everything there is.
  played: 8,
  // Matchdays left open for forecasting, taken from immediately after the last replayed one.
  upcoming: 2,
  members: 6,
  adminEmail: "e2e-admin@example.test",
  adminPassword: "Password123!",
  memberPassword: "Password123!",
  memberPrefix: "demo",
  // Deterministic forecasts: the same seed replays the same league, which is what makes a
  // screenshot or a bug report reproducible.
  seed: 20260913,
}

// The rules the seeded league scores. Only score-shaped parameters: openfootball carries goals
// and nothing else, so CorrectGoalScorer / card parameters would have no source data and every
// member would earn a flat zero from them — a table that lies about why it looks the way it does.
const SCORING_RULES = [
  { parameter: "ExactScore", points: 5 },
  { parameter: "CorrectOutcome", points: 3 },
]

const HOUR_MS = 3_600_000
const DAY_MS = 24 * HOUR_MS

// ── Arguments ─────────────────────────────────────────────────────────────────────────────────

function parseArgs(argv) {
  const opts = { ...DEFAULTS }
  const numeric = new Set(["played", "upcoming", "members", "seed"])

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i]
    if (arg === "--help" || arg === "-h") return { help: true }
    if (!arg.startsWith("--")) throw new Error(`Unexpected argument "${arg}". Try --help.`)

    const key = arg
      .slice(2)
      .replace(/-([a-z])/g, (_, c) => c.toUpperCase())
    if (!(key in DEFAULTS)) throw new Error(`Unknown flag "${arg}". Try --help.`)

    const value = argv[++i]
    if (value === undefined) throw new Error(`"${arg}" needs a value.`)
    opts[key] = numeric.has(key) ? Number(value) : value
  }

  if (!(opts.competition in COMPETITIONS)) {
    throw new Error(
      `Unknown competition "${opts.competition}". One of: ${Object.keys(COMPETITIONS).join(", ")}`,
    )
  }
  if (!Number.isInteger(opts.played) || opts.played < 1) throw new Error("--played must be >= 1.")
  if (!Number.isInteger(opts.upcoming) || opts.upcoming < 0) throw new Error("--upcoming must be >= 0.")
  if (!Number.isInteger(opts.members) || opts.members < 1) throw new Error("--members must be >= 1.")

  return opts
}

function printHelp() {
  const rows = Object.entries(DEFAULTS).map(([k, v]) => {
    const flag = `--${k.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)}`
    return `  ${flag.padEnd(20)} ${String(v)}`
  })
  console.log(
    [
      "Seed a Prediction League instance with a real competition.",
      "",
      "Flags (shown with their defaults):",
      ...rows,
      "",
      `Competitions: ${Object.entries(COMPETITIONS).map(([k, v]) => `${k} (${v})`).join(", ")}`,
      "",
      "The admin account must already be in the API's Admin:Emails allowlist — this script",
      "cannot promote itself. It registers the account if it does not exist yet, which only",
      "works when the address is allowlisted before the first run.",
    ].join("\n"),
  )
}

// ── HTTP ──────────────────────────────────────────────────────────────────────────────────────

// Auth is a cookie, and Node's fetch keeps no jar — so each signed-in identity carries its own.
// One session per user is also what makes the forecasts land under the right member.
class Session {
  constructor(baseUrl, label) {
    this.baseUrl = baseUrl.replace(/\/+$/, "")
    this.label = label
    this.cookies = new Map()
  }

  async request(method, path, body) {
    const headers = { accept: "application/json" }
    if (body !== undefined) headers["content-type"] = "application/json"
    if (this.cookies.size > 0) {
      headers.cookie = [...this.cookies].map(([k, v]) => `${k}=${v}`).join("; ")
    }

    const res = await fetch(`${this.baseUrl}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })

    for (const raw of res.headers.getSetCookie?.() ?? []) {
      const [pair] = raw.split(";")
      const eq = pair.indexOf("=")
      if (eq > 0) this.cookies.set(pair.slice(0, eq).trim(), pair.slice(eq + 1).trim())
    }

    return res
  }

  async send(method, path, body, what) {
    const res = await this.request(method, path, body)
    if (!res.ok) {
      throw new Error(
        `${what} → ${res.status} ${res.statusText}\n${(await res.text()).slice(0, 600)}`,
      )
    }
    const text = await res.text()
    return text.length > 0 ? JSON.parse(text) : null
  }

  get(path, what) {
    return this.send("GET", path, undefined, what)
  }
  post(path, body, what) {
    return this.send("POST", path, body, what)
  }
  put(path, body, what) {
    return this.send("PUT", path, body, what)
  }
  patch(path, body, what) {
    return this.send("PATCH", path, body, what)
  }
}

// Identity answers 400 with a duplicate-user validation error when the account already exists —
// the normal path on every run after the first, not a failure.
async function registerOrSignIn(session, email, password, displayName) {
  const res = await session.request("POST", "/api/auth/register", { email, password, displayName })
  if (res.ok) return

  const body = await res.text()
  if (res.status === 400 && /duplicate/i.test(body)) {
    await session.post("/api/auth/login", { email, password }, `sign in ${email}`)
    return
  }
  throw new Error(`register ${email} → ${res.status} ${res.statusText}\n${body.slice(0, 600)}`)
}

// Both match writes answer 200 even when scoring failed, carrying the verdict in the body. A
// seed built on the status code alone would report success over a broken table.
function assertScored(match, what) {
  if (match?.scoringFailed) {
    throw new Error(`${what} saved, but scoring failed: ${match.scoringMessage ?? "(no message)"}`)
  }
  return match
}

// ── Source data ───────────────────────────────────────────────────────────────────────────────

async function fetchCompetition(competition, season) {
  const url = `https://raw.githubusercontent.com/openfootball/football.json/master/${season}/${competition}.json`
  const res = await fetch(url)
  if (!res.ok) {
    throw new Error(
      `Could not fetch ${url} → ${res.status} ${res.statusText}.\n` +
        "Check that the season folder exists in openfootball/football.json.",
    )
  }
  return res.json()
}

// openfootball writes a full-time score two ways — {ft:[h,a], ht:[…]} and a bare [h,a] — and
// omits `score` entirely for a match that has not been played.
function fullTimeScore(match) {
  const score = match.score
  if (!score) return null
  const ft = Array.isArray(score) ? score : score.ft
  if (!Array.isArray(ft) || ft.length < 2) return null
  const [home, away] = ft
  return Number.isInteger(home) && Number.isInteger(away) ? { home, away } : null
}

// The dataset gives a local date and time with no zone. Treating them as UTC shifts a kickoff by
// at most a couple of hours, which changes nothing here: what the app cares about is whether a
// kickoff is in the past or the future, and every replayed match is moved wholesale anyway.
function sourceKickoff(match) {
  return new Date(`${match.date}T${match.time ?? "15:00"}:00Z`)
}

// Rounds in first-appearance order — "Matchday 10" sorts before "Matchday 2" alphabetically, and
// the file is already in schedule order.
function groupRounds(matches) {
  const rounds = new Map()
  for (const match of matches) {
    const round = match.round ?? "Matchday"
    if (!rounds.has(round)) rounds.set(round, [])
    rounds.get(round).push(match)
  }
  return [...rounds].map(([name, fixtures]) => ({ name, fixtures }))
}

// ── Forecasts ─────────────────────────────────────────────────────────────────────────────────

// Deterministic PRNG: the same --seed replays the same league, so a screenshot or a bug report
// can be reproduced exactly.
function mulberry32(seed) {
  let a = seed >>> 0
  return () => {
    a = (a + 0x6d2b79f5) >>> 0
    let t = Math.imul(a ^ (a >>> 15), 1 | a)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

const randInt = (rng, max) => Math.floor(rng() * (max + 1))
const outcome = ({ home, away }) => (home > away ? 1 : home < away ? -1 : 0)

// Biased toward the real result so the table separates members instead of reading as noise:
// roughly a fifth are exact, a third more call the right winner, the rest are guesses. A member
// who never lands anything still exists — that is what a real pool looks like.
function forecast(rng, result, skill) {
  if (result === null) return { home: randInt(rng, 3), away: randInt(rng, 3) }

  const roll = rng()
  if (roll < 0.12 + skill * 0.2) return { ...result }

  if (roll < 0.5 + skill * 0.2) {
    // Keep the outcome, move the scoreline.
    for (let attempt = 0; attempt < 12; attempt++) {
      const guess = { home: randInt(rng, 4), away: randInt(rng, 4) }
      if (outcome(guess) === outcome(result)) return guess
    }
  }

  return { home: randInt(rng, 3), away: randInt(rng, 3) }
}

// ── Seeding ───────────────────────────────────────────────────────────────────────────────────

const isoDate = (d) => d.toISOString().slice(0, 10)

async function main() {
  const opts = parseArgs(process.argv.slice(2))
  if (opts.help) return printHelp()

  // The dev API serves a self-signed certificate. Only relaxed for a local host — never for the
  // deployed one, where a certificate error is a real error.
  if (/^https:\/\/(localhost|127\.0\.0\.1)/.test(opts.api)) {
    process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0"
  }

  const competitionName = COMPETITIONS[opts.competition]
  console.log(`→ fetching ${competitionName} ${opts.season} from openfootball…`)
  const source = await fetchCompetition(opts.competition, opts.season)

  const rounds = groupRounds(source.matches ?? [])

  // In a live season only the leading rounds carry results, and how many is the season's business,
  // not a flag's. Take the last round that has any, replay up to --played from the start, and open
  // the rounds immediately after whatever was replayed — so a mid-season dataset leaves no gap
  // between the table and the fixtures on offer.
  const lastPlayedIndex = rounds.reduce(
    (last, round, i) => (round.fixtures.some((f) => fullTimeScore(f)) ? i : last),
    -1,
  )
  const played = rounds.slice(0, lastPlayedIndex + 1).slice(0, opts.played)
  const upcoming = rounds.slice(played.length, played.length + opts.upcoming)

  if (played.length === 0) {
    throw new Error(
      `No played matchdays found in ${opts.competition} ${opts.season}.\n` +
        "The season may not have started yet — pick an earlier one.",
    )
  }

  const fixtureCount = played.reduce((n, r) => n + r.fixtures.length, 0)
  console.log(
    `  ${source.name ?? competitionName}: ${played.length} played matchday(s) (${fixtureCount} matches), ` +
      `${upcoming.length} upcoming`,
  )

  // ── Accounts ────────────────────────────────────────────────────────────────────────────────
  console.log("→ signing in…")
  const admin = new Session(opts.api, "admin")
  await registerOrSignIn(admin, opts.adminEmail, opts.adminPassword, "League Admin")

  const profile = await admin.get("/api/auth/me", "GET /api/auth/me")
  if (!profile.isGlobalAdmin) {
    throw new Error(
      `${opts.adminEmail} is signed in but is not a global admin.\n` +
        "Add the address to the API's Admin:Emails allowlist and sign in again — the admin claim " +
        "is baked into the cookie when the principal is built, so an existing session will not " +
        "pick up a promotion.",
    )
  }

  const members = []
  for (let i = 1; i <= opts.members; i++) {
    const email = `${opts.memberPrefix}${i}@example.test`
    const displayName = `Demo Member ${i}`
    const session = new Session(opts.api, displayName)
    await registerOrSignIn(session, email, opts.memberPassword, displayName)
    members.push({ email, displayName, session, rng: mulberry32(opts.seed + i * 7919), skill: (i - 1) / opts.members })
  }
  console.log(`  admin + ${members.length} member(s) ready`)

  // ── Tournament ──────────────────────────────────────────────────────────────────────────────
  // Start/end bracket the whole run: replayed matches are stamped into the recent past and the
  // upcoming ones into the near future, so the real season's dates would not contain them.
  const now = Date.now()
  const tournamentName = `${source.name ?? `${competitionName} ${opts.season}`}`
  console.log(`→ creating tournament "${tournamentName}"…`)
  const tournament = await admin.post(
    "/api/tournaments",
    {
      name: tournamentName,
      season: Number(opts.season.slice(0, 4)),
      startDate: isoDate(new Date(now - (played.length + 2) * DAY_MS)),
      endDate: isoDate(new Date(now + (upcoming.length + 14) * DAY_MS)),
    },
    `create tournament "${tournamentName}"`,
  )
  await admin.patch(
    `/api/tournaments/${tournament.id}/publish`,
    { isPublished: true },
    "publish tournament",
  )

  // ── Teams ───────────────────────────────────────────────────────────────────────────────────
  // Team names are globally unique server-side (409 on a duplicate), so reuse what is already
  // there. That is also what makes a second run of this script cheap instead of fatal.
  const wanted = new Set()
  for (const round of [...played, ...upcoming]) {
    for (const fixture of round.fixtures) {
      wanted.add(fixture.team1)
      wanted.add(fixture.team2)
    }
  }

  const existing = await admin.get("/api/teams", "GET /api/teams")
  const teamIdByName = new Map(existing.map((t) => [t.name.toLowerCase(), t.id]))
  let created = 0
  for (const name of wanted) {
    if (teamIdByName.has(name.toLowerCase())) continue
    const team = await admin.post("/api/teams", { name }, `create team "${name}"`)
    teamIdByName.set(name.toLowerCase(), team.id)
    created++
  }
  console.log(`→ teams: ${created} created, ${wanted.size - created} reused`)

  const teamId = (name) => {
    const id = teamIdByName.get(name.toLowerCase())
    if (!id) throw new Error(`No team id for "${name}" — the reference data is inconsistent.`)
    return id
  }

  // ── League ──────────────────────────────────────────────────────────────────────────────────
  // Created before any match kicks off: the scoring configuration freezes at the first kickoff,
  // and every match this script writes afterwards is already in the past.
  const organizer = members[0]
  const leagueName = `${competitionName} Pool`
  console.log(`→ creating league "${leagueName}" (organizer: ${organizer.displayName})…`)
  const league = await organizer.session.post(
    "/api/leagues",
    { name: leagueName, tournamentId: tournament.id, scoringRules: SCORING_RULES },
    `create league "${leagueName}"`,
  )

  for (const member of members.slice(1)) {
    await member.session.post(
      "/api/leagues/join",
      { inviteCode: league.inviteCode },
      `${member.displayName} joins ${leagueName}`,
    )
  }

  // ── Replay the played matchdays ─────────────────────────────────────────────────────────────
  // Each round: open → forecast → lock with the real result. See the header note.
  let roundIndex = 0
  for (const round of played) {
    roundIndex++
    const fixtures = round.fixtures.filter((f) => fullTimeScore(f) !== null)
    if (fixtures.length === 0) continue

    // Far enough back that later rounds stay ordered, and the whole block sits in the past.
    const lockedAt = now - (played.length - roundIndex + 1) * DAY_MS

    const writes = []
    for (const fixture of fixtures) {
      const input = {
        homeTeamId: teamId(fixture.team1),
        awayTeamId: teamId(fixture.team2),
        kickoffUtc: new Date(now + HOUR_MS).toISOString(),
        status: "Scheduled",
        homeScore: null,
        awayScore: null,
        round: round.name,
      }
      const match = await admin.post(
        `/api/tournaments/${tournament.id}/matches`,
        input,
        `create ${fixture.team1} v ${fixture.team2}`,
      )
      assertScored(match, `create ${fixture.team1} v ${fixture.team2}`)
      writes.push({ fixture, matchId: match.id, input })
    }

    for (const member of members) {
      // homeScore/awayScore, not home/away: the batch DTO's ints are non-nullable, so a
      // misnamed field does not fail — it binds to 0 and every forecast is silently stored as
      // 0–0, reported as "Saved". Names have to match the contract exactly.
      const items = writes.map(({ fixture, matchId }) => {
        const pick = forecast(member.rng, fullTimeScore(fixture), member.skill)
        return { matchId, homeScore: pick.home, awayScore: pick.away }
      })
      const body = await member.session.post(
        `/api/leagues/${league.id}/predictions`,
        { items },
        `${member.displayName} forecasts ${round.name}`,
      )
      const rejected = (body.outcomes ?? []).filter((o) => o.status !== "Saved")
      if (rejected.length > 0) {
        throw new Error(
          `${member.displayName} had ${rejected.length} forecast(s) rejected in ${round.name}: ` +
            rejected.map((o) => `${o.status} ${o.detail ?? ""}`.trim()).join("; "),
        )
      }
    }

    for (const { fixture, matchId, input } of writes) {
      const result = fullTimeScore(fixture)
      // A played round's real kickoff is already behind us, which is exactly what the lock wants
      // — keep it, so the app shows the dates these matches were actually played on. The
      // synthetic past slot is the fallback for a dataset whose results are not yet in the past.
      const original = sourceKickoff(fixture)
      let kickoff = original
      if (original.getTime() > now - HOUR_MS) {
        kickoff = new Date(lockedAt)
        kickoff.setUTCHours(original.getUTCHours(), original.getUTCMinutes(), 0, 0)
      }

      const what = `finish ${fixture.team1} ${result.home}–${result.away} ${fixture.team2}`
      assertScored(
        await admin.put(
          `/api/matches/${matchId}`,
          {
            ...input,
            kickoffUtc: kickoff.toISOString(),
            status: "Finished",
            homeScore: result.home,
            awayScore: result.away,
          },
          what,
        ),
        what,
      )
    }

    console.log(`  ${round.name}: ${writes.length} match(es) played and scored`)
  }

  // ── Upcoming matchdays ──────────────────────────────────────────────────────────────────────
  // A live season's next matchdays are genuinely ahead of now, and keeping their real kickoffs is
  // most of the point of seeding the current season. Only a round that has already started — or a
  // season that is over — needs shifting, and then the whole round moves together so its fixtures
  // keep the spacing they really have.
  let upcomingIndex = 0
  for (const round of upcoming) {
    upcomingIndex++
    const kickoffs = round.fixtures.map((f) => sourceKickoff(f).getTime())
    const earliest = Math.min(...kickoffs)
    // Whole days, so a 15:00 kickoff stays a 15:00 kickoff. Shifting by the raw difference would
    // drag every fixture to whatever minute and second this script happened to run at.
    const days = Math.ceil((now + (upcomingIndex + 1) * DAY_MS - earliest) / DAY_MS)
    const shift = earliest <= now + HOUR_MS ? days * DAY_MS : 0

    for (const [i, fixture] of round.fixtures.entries()) {
      const kickoff = new Date(kickoffs[i] + shift)

      const what = `schedule ${fixture.team1} v ${fixture.team2}`
      assertScored(
        await admin.post(
          `/api/tournaments/${tournament.id}/matches`,
          {
            homeTeamId: teamId(fixture.team1),
            awayTeamId: teamId(fixture.team2),
            kickoffUtc: kickoff.toISOString(),
            status: "Scheduled",
            homeScore: null,
            awayScore: null,
            round: round.name,
          },
          what,
        ),
        what,
      )
    }
    console.log(`  ${round.name}: ${round.fixtures.length} match(es) open for forecasting`)
  }

  // ── Report ──────────────────────────────────────────────────────────────────────────────────
  const standings = await organizer.session.get(
    `/api/leagues/${league.id}/standings`,
    "GET standings",
  )

  console.log("\n─── seeded ──────────────────────────────────────────────────")
  console.log(`tournament   ${tournamentName}`)
  console.log(`league       ${leagueName}`)
  console.log(`invite code  ${league.inviteCode}`)
  console.log(`league id    ${league.id}`)
  console.log("\nstandings:")
  for (const row of standings.rows ?? []) {
    console.log(
      `  ${String(row.rank).padStart(2)}. ${row.displayName.padEnd(18)} ` +
        `${String(row.points).padStart(3)} pts  (${row.scoredMatches} scored)`,
    )
  }
  console.log("\nsign in as any of:")
  for (const member of members) console.log(`  ${member.email}  /  ${opts.memberPassword}`)
  console.log(
    "\nOr join with the invite code above from your own account.\n" +
      "─────────────────────────────────────────────────────────────",
  )
}

main().catch((err) => {
  console.error(`\n✖ ${err.message}`)
  process.exit(1)
})
