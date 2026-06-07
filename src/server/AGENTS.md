# Server Guidelines

ASP.NET Core Web API (.NET 10, C#) for Prediction League. This directory is self-contained — the React client lives elsewhere and talks to this over HTTP.

## Commands

- Run: `dotnet run` (dev URL `http://localhost:5185`; sample requests in `PredictionLeague.http`)
- Build: `dotnet build`
- Solution: `prediction-league.slnx` — `.slnx` XML format, not `.sln`

No tests exist yet. Don't claim any pass.

## Conventions

- Nullable reference types and implicit usings are **on** (`PredictionLeague.csproj`). Mark non-nullable model props `required`.
- Single namespace root `PredictionLeague.*`; controllers use `[ApiController]` + `[Route("api/[controller]")]`.
- Model files (`Models/`) carry `// FR-00x` comments tying types to PRD requirements — keep them when editing.

## Traps

- **Persistence landed (F-01).** EF Core (SQL Server) + ASP.NET Core Identity (Guid keys) is wired via the layered `Domain`/`Application`/`Infrastructure`/`Api` projects. League CRUD goes through repositories (`Application/Abstractions/Persistence`, `Infrastructure/Persistence/Repositories`) — the old `static List<League>` controller is gone. Dev auto-migrates on startup; prod migrations are forward-only + human-gated. `GET /health/db` proves DB connectivity.
- **Auth wired (F-02).** Cookie-based ASP.NET Core Identity is live via `AddAuthenticationAndIdentity` (`Infrastructure/DependencyInjection.cs`); pipeline is CORS → Authentication → Authorization in `Program.cs`. Two sign-in paths share the cookie: local email/password and Google external login, both under `AuthController` (`api/auth/*`). Global admin is the `AdminOnly` policy (claim from `ApplicationUser.IsGlobalAdmin`); organizer/member stay per-league via `LeagueMembership`. Anonymous calls to `[Authorize]` routes get 401/403 (no login redirect — .NET 10 API cookie behavior).
  - **Google secrets** are not committed. Supply via user-secrets (dev): `dotnet user-secrets set "Authentication:Google:ClientId" "<id>"` and `... "Authentication:Google:ClientSecret" "<secret>"`. The Google scheme registers only when both are present — empty config boots fine, Google login just stays off. Google Cloud OAuth client (Web) authorized redirect URI: `https://localhost:7182/signin-google`. Run the **https** launch profile so the `Secure` cookie is set.

## Domain

Scoring is per-league and data-driven: `ScoringRule` maps a `ScoringParameter` (`ExactScore`, `CorrectOutcome`, `CorrectGoalScorer`, `CorrectCardCount`) to `Points`. Never hardcode point values.
