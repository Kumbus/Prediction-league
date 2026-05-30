# Repository Guidelines

Project-specific onboarding for AI agents. Prediction League: a private, sports-agnostic match-prediction pool MVP — users create a league for a tournament, invite friends, set custom scoring rules, and the system auto-updates standings from match data. See `@idea-notes.md` and `@context/foundation/prd.md` for product scope.

## Layout

Split stack, two independent units:

- `src/server/` — ASP.NET Core Web API (.NET 10, C#). The riskiest surface (custom scoring engine) lives here for C#'s explicit typing.
- `src/client/` — React 19 + Vite SPA (TypeScript) for member screens (standings, prediction submission). Talks to the API over HTTP. UI is built with **shadcn/ui + Tailwind v4**; primitives are vendored into `src/client/src/components/ui/` and owned/edited in-repo (not upgraded via npm). `@/*` resolves to `src/client/src/`.

The two are not wired into one build — run and deploy them separately.

## Commands

Server (`cd src/server`):

- Run: `dotnet run` (dev URL `http://localhost:5185`, see `@src/server/PredictionLeague.http`)
- Build: `dotnet build`
- Solution file: `prediction-league.slnx` (note the `.slnx` XML format, not `.sln`)

Client (`cd src/client`):

- Dev: `npm run dev`
- Build: `npm run build` (runs `tsc -b` then `vite build` — type errors fail the build)
- Lint: `npm run lint`

No test suite exists yet in either unit. Don't claim tests pass — there are none to run.

## State of the code (pre-persistence)

This is early scaffolding. Two traps:

- **No data layer yet.** `src/server/Controllers/LeaguesController.cs` uses a `static List<League>` in-memory store as a placeholder. The tech-stack plan (`@context/foundation/tech-stack.md`) calls for Entity Framework — when persistence lands, swap the static store, don't build on it.
- **No auth yet.** `Program.cs` calls `UseAuthorization()` but nothing is configured. Plan is ASP.NET Core Identity + OAuth (`has_auth: true`).

## Domain model (`src/server/Models/`)

The scoring system is the core domain concept and is configurable per league:

- A `League` is tied to one `TournamentId`, owned by an `OrganizerUserId`, joined via `InviteCode`.
- `ScoringRule` rows define a league's custom scoring: each maps a `ScoringParameter` (`ExactScore`, `CorrectOutcome`, `CorrectGoalScorer`, `CorrectCardCount`) to `Points`. Scoring is data-driven per league — don't hardcode point values.
- Model files carry `// FR-00x` comments linking back to PRD requirements; keep them when editing.

## Conventions

- Server: nullable reference types and implicit usings are **on** (`PredictionLeague.csproj`). Non-nullable model props use `required`.
- Namespace is `PredictionLeague.*`; controllers follow `[Route("api/[controller]")]`.
- Client: ESLint flat config (`eslint.config.js`), `dist/` is gitignored — never commit it.

## Out of scope (per PRD)

No payments, no realtime, no AI, no mobile app, no user-created tournaments. Don't add these.
