---
project: "Football Match Prediction App"
version: 1
status: draft
created: 2026-05-28
updated: 2026-08-03
prd_version: 1
main_goal: speed
top_blocker: external
---

# Roadmap: Football Match Prediction App

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

Free football prediction games today only run on official organizer sites for the biggest tournaments, with fixed scoring rules a group cannot tailor. This product lets a league organizer run a private, no-money pool for an admin-seeded tournament, define **custom scoring rules**, invite friends, and have the system auto-score every prediction and update standings from a football data API — no manual result entry. The product wedge — the one trait that, if removed, makes this indistinguishable from a generic prediction game — is **per-league custom scoring applied automatically to real match data**.

## North star

**S-07: System scores predictions per a league's custom rules when results arrive, and members watch their points and standings update.** — This is the validation milestone: it is the only slice that exercises the wedge (custom scoring on real data) end-to-end, and it carries the PRD's primary Success Criterion and its hardest guardrail (scoring correctness).

> North star here means the smallest end-to-end slice whose successful delivery would prove the core product hypothesis — placed as early as Prerequisites allow because everything else only matters if this works. S-07 sits late in dependency order only because it consumes rules (S-04), predictions (S-06), and ingested results (S-02/F-03); it is still the slice the whole roadmap is sequenced to reach.

## At a glance

| ID    | Change ID                  | Outcome (user can …)                                                        | Prerequisites      | PRD refs                  | Status   |
| ----- | -------------------------- | --------------------------------------------------------------------------- | ------------------ | ------------------------- | -------- |
| F-01  | layered-backend-persistence | (foundation) layered backend (Domain/Application/Infrastructure) + EF Core persistence in place | —                  | FR-002, NFR-freshness     | done     |
| F-02  | auth-oauth-scaffold        | (foundation) OAuth sign-in scaffold + identity issuing/verification wired    | —                  | FR-001, Access Control    | done     |
| F-03  | football-api-ingest        | (foundation) football data API client + scheduled ingest of fixtures/results | F-01               | FR-004, FR-005            | ready    |
| F-04  | walking-skeleton-deploy     | (foundation) app + Azure SQL deployed end-to-end; first prod migration applied (auto-migrate from CI — conscious deviation, see infrastructure-v2.md 2026-06-08 note) | F-01, F-02         | NFR-freshness, infra-v2   | done     |
| S-01  | user-sign-in               | sign in via OAuth and land in the authenticated app                          | F-02, F-04         | FR-001, US-01             | done     |
| S-02  | admin-seed-tournament      | (admin) add a tournament and have its fixtures + per-match detail ingested   | F-01, F-03         | FR-003, FR-004, FR-005    | done     |
| S-03  | organizer-create-league    | create a league tied to a seeded tournament                                  | S-01, S-02         | FR-006, US-01             | proposed |
| S-04  | custom-scoring-rules       | define custom scoring rules for a league                                     | S-03               | FR-008, US-01             | proposed |
| S-05  | invite-and-join-league     | invite friends and join a league via invite code                             | S-03               | FR-007, FR-002, US-01     | proposed |
| S-06  | submit-locked-predictions  | submit predictions for upcoming matches, locked at kickoff                   | S-05, S-02         | FR-009, FR-010, FR-002    | proposed |
| S-07  | scoring-engine-standings   | see points and standings update automatically after each match              | S-04, S-06, S-02   | FR-011, FR-012, US-01     | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                  | Chain                                                          | Note                                                                 |
| ------ | ---------------------- | ------------------------------------------------------------- | ------------------------------------------------------------------- |
| A      | League & scoring loop  | `F-01` → `S-02` → `S-03` → (`S-04` / `S-05`) → `S-06` → `S-07` | The critical path to the north star; biased first under `speed`.     |
| B      | Identity / sign-in     | `F-02` → `F-04` → `S-01`                                      | `F-04` also needs `F-01` (DB to deploy). `S-01` joins Stream A at `S-03` (organizer must be signed in). |
| C      | Match data ingest      | `F-03`                                                       | Source resolved (API-Football free); ready to build. Joins Stream A at `S-02` and feeds `S-07`. |

## Baseline

What's already in place in the codebase as of 2026-05-31 (auto-researched + user-confirmed; updated after F-01).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** partial — React 19 + Vite + Tailwind v4 + shadcn primitives (button/card/badge) vendored in `src/client/src/components/ui/`. Only the landing page exists (`src/client/src/App.tsx`); no router, data fetching, or app screens.
- **Backend / API:** present **— rebuilt in F-01.** Layered solution `Domain` / `Application` / `Infrastructure` / `Api` (`src/server`). The throwaway `static List<League>` controller is gone; persistence goes through repositories (`Application/Abstractions/Persistence`, `Infrastructure/Persistence/Repositories`).
- **Data:** present **— wired in F-01.** EF Core (SQL Server) + initial migration covering the S-02/S-03 entities and `AspNet*` Identity tables (no `Predictions` yet). Local Docker SQL for dev (auto-migrate in Development); Azure SQL Basic for prod per `tech-stack.md` / `infrastructure-v2.md`. `GET /health/db` proves connectivity.
- **Auth:** partial **— schema only (F-01).** ASP.NET Core Identity (Guid keys) tables exist; `Program.cs` still calls `UseAuthorization()` with nothing configured. OAuth sign-in is F-02 (`has_auth: true`).
- **Deploy / infra:** partial — one stale Azure Static Web Apps workflow yml under `.github/workflows/`; full Azure plan in `infrastructure-v2.md` (App Service F1 + Functions Consumption + Static Web Apps + Azure SQL Basic, GitHub Actions manual promotion). No API CI, no Functions project yet.
- **Observability:** absent — framework defaults only. Application Insights named in `infrastructure-v2.md`, not wired. Not invested under `main_goal: speed`.

## Foundations

### F-01: Layered backend + persistence

- **Outcome:** (foundation) the backend is rebuilt as a layered solution (Domain / Application / Infrastructure) with EF Core wired to a database and an initial migration covering the entities the earliest slices need.
- **Change ID:** layered-backend-persistence
- **PRD refs:** FR-002 (per-(user, league) keying from the start), NFR (freshness — persisted standings recompute)
- **Unlocks:** S-02 (tournaments/fixtures need persistence), S-03 (leagues), and every downstream data slice; F-03 (ingest writes through this layer).
- **Prerequisites:** —
- **Parallel with:** F-02
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sequenced first because the current store is throwaway and every slice writes through this layer. Scope risk: do NOT pre-build the whole schema here — establish the layered skeleton, persistence wiring, and only the entities S-02/S-03 exercise; later slices add their own migrations.
- **Status:** done

### F-02: OAuth sign-in scaffold

- **Outcome:** (foundation) ASP.NET Core Identity + OAuth sign-in is wired so accounts can be issued and requests authenticated; identity keys predictions per league.
- **Change ID:** auth-oauth-scaffold
- **PRD refs:** FR-001, Access Control (three roles), FR-002 (one identity across leagues)
- **Unlocks:** S-01 (sign in), and every user-scoped slice (S-03 organizer, S-05 member, S-06 predictions).
- **Prerequisites:** —
- **Parallel with:** F-01
- **Blockers:** —
- **Unknowns:**
  - Allow an alternative sign-in if a member lacks the chosen OAuth provider? — Owner: user. Block: no.
- **Risk:** Sequenced early because organizer/member roles gate almost every slice. Minimal scaffold only (one provider, e.g. Google); role model and provider-coverage refinement stay light to protect the deadline.
- **Status:** done

### F-03: Football data API ingest

- **Outcome:** (foundation) a football data API client plus a scheduled ingest (Azure Functions timer) populates fixtures, results, and per-match detail into the data layer.
- **Change ID:** football-api-ingest
- **PRD refs:** FR-004, FR-005, NFR (results visible within minutes)
- **Unlocks:** S-02 (admin seeding depends on ingested fixtures), S-07 (scoring needs real results), and reduces Open Roadmap Question #1.
- **Prerequisites:** F-01
- **Parallel with:** F-02
- **Blockers:** — (resolved 2026-06-04). Source selected: **API-Football (api-sports.io) free tier**; direct `x-apisports-key`. See `context/changes/football-api-ingest/api-research.md` (decision) + `api-reference.md` (endpoint/payload contracts).
- **Unknowns:**
  - ~~Which API source covers fixtures + results + goal scorers + cards within budget?~~ RESOLVED — API-Football free tier; `Events` endpoint carries goals + cards at €0 (OQ #1).
- **Risk:** Was the #1 roadmap blocker; source now chosen so ingest can be built and granular scoring (FR-005) is viable at €0. New constraint: **free tier caps 100 req/day** — ingest must be poll-frugal (timer fires in match windows only) + cache hard (persist fixtures/results, pull events once per match after FT); live/15s polling is out of scope v1. Read rate-limit response headers and back off. Path if 100/day pinches: cache harder → $19 Pro (7,500/day, identical shapes) → role-split fallback (see `api-research.md`).
- **Status:** ready

### F-04: Walking-skeleton Azure deploy

- **Outcome:** (foundation) the layered API + Azure SQL are provisioned and deployed end-to-end, the prod connection string is injected as an App Service app setting, and the F-01 initial migration is applied to Azure SQL by hand (forward-only, human-gated). A thin liveness path (e.g. `/health/db` reaching prod Azure SQL) proves the deployed shape before any user-facing slice ships.
- **Change ID:** walking-skeleton-deploy
- **PRD refs:** NFR (freshness — persisted standings in the live environment), `infrastructure-v2.md`
- **Unlocks:** S-01 (needs a running deployed environment to confirm the OAuth round-trip and session in the deployed shape); de-risks every later slice by making prod real early.
- **Prerequisites:** F-01 (schema + migration to apply), F-02 (auth wired so S-01 can exercise it against the deploy)
- **Parallel with:** F-03
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Closes the roadmap gap where S-01 assumed a "deployed shape" that nothing provisioned. Keep it thin — App Service F1 + Azure SQL Basic + manual GitHub Actions promotion per infra-v2; no observability investment (`main_goal: speed`). Prod migrations stay forward-only + human-gated; never auto-migrate prod.
- **Status:** done

## Slices

### S-01: User can sign in

- **Outcome:** user can sign in via OAuth and land in the authenticated app.
- **Change ID:** user-sign-in
- **PRD refs:** FR-001, US-01
- **Prerequisites:** F-02, F-04
- **Parallel with:** S-02
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Thin slice exercising F-02 end-to-end. Low risk; mainly confirms the OAuth round-trip and session work in the deployed shape — which F-04 now provisions.
- **Status:** done

### S-02: Admin seeds a tournament with match data

- **Outcome:** an admin can add a tournament and have its fixtures and per-match detail (teams, score, scorers, cards) ingested.
- **Change ID:** admin-seed-tournament
- **PRD refs:** FR-003, FR-004, FR-005
- **Prerequisites:** F-01, F-03
- **Parallel with:** S-01
- **Blockers:** — (API selection resolved in F-03). Now gated only by prerequisites (F-03 ingest must ship first).
- **Unknowns:**
  - ~~If scorer/card detail is unavailable at the chosen API tier, is final-score-only scoring acceptable for v1?~~ RESOLVED — no degrade needed; API-Football free `Events` carries scorers + cards at €0 (OQ #2). S-02 keeps full granular detail.
- **Risk:** Gates the whole loop — no seeded matches means nothing to predict or score. External-API decision resolved; granular scoring confirmed viable, so S-04 scoring rules can be designed against full scorer/card detail.
- **Status:** done

### S-03: Organizer creates a league

- **Outcome:** a signed-in organizer can create a league tied to a seeded tournament.
- **Change ID:** organizer-create-league
- **PRD refs:** FR-006, US-01
- **Prerequisites:** S-01, S-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Straightforward CRUD over the persisted model once auth and tournaments exist. Sequenced after both because a league needs an owner and a tournament to bind to.
- **Status:** proposed

### S-04: Organizer defines custom scoring rules

- **Outcome:** an organizer can define a league's custom scoring rules — which match parameters score and how many points each.
- **Change ID:** custom-scoring-rules
- **PRD refs:** FR-008, US-01
- **Prerequisites:** S-03
- **Parallel with:** S-05
- **Blockers:** —
- **Unknowns:**
  - How is the rule engine validated to score correctly across arbitrary configs? — Owner: user + engineering. Block: no (resolvable during `/10x-plan`; surfaces the test-fixture / recompute strategy).
- **Risk:** Configures the wedge. Data-driven rules must avoid hardcoded point values. The correctness strategy is deferred to planning but flagged here because S-07 depends on it.
- **Status:** proposed

### S-05: Invite friends and join a league

- **Outcome:** an organizer can invite friends and a member can join a league via invite code, under one cross-league identity.
- **Change ID:** invite-and-join-league
- **PRD refs:** FR-007, FR-002, US-01
- **Prerequisites:** S-03
- **Parallel with:** S-04
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The social on-ramp. FR-002 requires membership keyed per (user, league) from the start so a member can play in several leagues — this slice locks that keying in.
- **Status:** proposed

### S-06: Submit predictions, locked at kickoff

- **Outcome:** a member can submit predictions for upcoming matches, and it becomes impossible to submit or edit once a match kicks off.
- **Change ID:** submit-locked-predictions
- **PRD refs:** FR-009, FR-010, FR-002
- **Prerequisites:** S-05, S-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Are kickoff times from the API accurate and timezone-safe enough to enforce the lock? — Owner: engineering. Block: no.
- **Risk:** Carries the anti-cheat guardrail. The lock depends on accurate kickoff times from ingested data (S-02); a timezone error here silently breaks the guarantee.
- **Status:** proposed

### S-07: Scoring engine updates standings

- **Outcome:** members see their points and league position update automatically after each match, scored exactly per the league's custom rules; they can view standings and upcoming + past matches.
- **Change ID:** scoring-engine-standings
- **PRD refs:** FR-011, FR-012, US-01
- **Prerequisites:** S-04, S-06, S-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Recompute behavior on late API result corrections — Owner: engineering. Block: no.
- **Risk:** The north star and the riskiest correctness surface. Wrong standings kills the product. Sequenced last because it consumes rules, predictions, and results; its correctness strategy traces back to S-04's deferred validation unknown.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID                   | Suggested issue title                                   | Ready for `/10x-plan` | Notes |
| ---------- | --------------------------- | ------------------------------------------------------- | --------------------- | ----- |
| F-01       | layered-backend-persistence | Rebuild backend into layered solution + EF Core persistence | yes               | Run `/10x-plan layered-backend-persistence` |
| F-02       | auth-oauth-scaffold         | Scaffold OAuth sign-in (ASP.NET Core Identity)          | yes                   | Run `/10x-plan auth-oauth-scaffold` |
| F-03       | football-api-ingest         | Football data API client + scheduled ingest             | yes                   | Source resolved (API-Football free); run `/10x-plan football-api-ingest` |
| F-04       | walking-skeleton-deploy     | Walking-skeleton Azure deploy (App Service + Azure SQL)  | no                    | Needs F-01, F-02; run `/10x-new walking-skeleton-deploy` then `/10x-plan` |
| S-01       | user-sign-in                | User can sign in via OAuth                               | no                    | Needs F-02, F-04 |
| S-02       | admin-seed-tournament       | Admin seeds tournament + match data ingest              | no                    | API decisions resolved; now needs F-03 + S-01 shipped first |
| S-03       | organizer-create-league     | Organizer creates a league                              | no                    | Needs S-01, S-02 |
| S-04       | custom-scoring-rules        | Organizer defines custom scoring rules                  | no                    | Needs S-03; carries scoring-validation unknown |
| S-05       | invite-and-join-league      | Invite friends and join via invite code                 | no                    | Needs S-03 |
| S-06       | submit-locked-predictions   | Submit predictions, locked at kickoff                   | no                    | Needs S-05, S-02 |
| S-07       | scoring-engine-standings    | Scoring engine updates standings (north star)           | no                    | Needs S-04, S-06, S-02 |

## Open Roadmap Questions

1. ~~**Football data API selection**~~ — RESOLVED 2026-06-04: API-Football (api-sports.io) free tier. See `context/changes/football-api-ingest/api-research.md`.
2. ~~**Granular-detail fallback**~~ — RESOLVED 2026-06-04: no degrade — free `Events` endpoint carries scorers + cards at €0. S-02 keeps granular scoring.
3. **Scoring-rule validation strategy** — Owner: user + engineering. Block: gates the *quality* of S-07 planning, not its start. How to guarantee the custom-rule engine scores correctly across arbitrary configs (test fixtures, recompute on late corrections).
4. **Auth provider coverage** — Owner: user. Block: none (F-02 ships with one provider). Allow an alternative sign-in if a member lacks the chosen OAuth provider?
5. **Confirm target_scale (qps / data_volume)** — Owner: user. Block: none. Estimated low/small for friend-group scale; revisit only if usage assumptions change.

## Parked

- **User-created tournaments** — Why parked: PRD §Non-Goals; only the global admin adds tournaments in v1.
- **Sports other than football** — Why parked: PRD §Non-Goals; football only.
- **Social-media sharing** — Why parked: PRD §Non-Goals; no share-to-socials in v1.
- **Native mobile app** — Why parked: PRD §Non-Goals; responsive web only.
- **Multi-tournament / season-long leagues** — Why parked: PRD FR-006 note; one-tournament-per-league for v1, v2 candidate.
- **Application Insights / observability** — Why parked: not invested under `main_goal: speed`; framework defaults suffice for the MVP window.

## Done

- **F-01: (foundation) layered backend (Domain/Application/Infrastructure) + EF Core persistence in place** — Archived 2026-08-03 → `context/archive/2026-05-28-layered-backend-persistence/`. Lesson: —.
- **F-02: (foundation) OAuth sign-in scaffold + identity issuing/verification wired** — Archived 2026-08-03 → `context/archive/2026-06-07-auth-oauth-scaffold/`. Lesson: —.
- **F-04: (foundation) app + Azure SQL deployed end-to-end; first prod migration applied** — Archived 2026-08-03 → `context/archive/2026-06-07-walking-skeleton-deploy/`. Lesson: —.
- **S-01: sign in via OAuth and land in the authenticated app** — Archived 2026-08-03 → `context/archive/2026-06-08-user-sign-in/`. Lesson: —.
- **S-02: (admin) add a tournament and have its fixtures + per-match detail ingested** — Archived 2026-08-03 → `context/archive/2026-06-08-admin-seed-tournament/`. Lesson: manual match entry (form + CSV) folded in as interim data source while paid API-Football ingest is deferred.
