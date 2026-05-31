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
- **Auth declared, not wired.** `Program.cs` calls `UseAuthorization()` but nothing configures it. Identity *schema* exists (`AspNet*` tables); OAuth sign-in is F-02.

## Domain

Scoring is per-league and data-driven: `ScoringRule` maps a `ScoringParameter` (`ExactScore`, `CorrectOutcome`, `CorrectGoalScorer`, `CorrectCardCount`) to `Points`. Never hardcode point values.
