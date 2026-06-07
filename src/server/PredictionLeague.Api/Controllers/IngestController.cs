using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Football;
using PredictionLeague.Infrastructure.Football;
using PredictionLeague.Infrastructure.Identity;

namespace PredictionLeague.Api.Controllers;

// Guarded on-demand ingest for verifying F-03 before S-02 exists; S-02 later reuses the
// service. Now gated by the real AdminOnly policy (F-02); the Development 404 guard below
// stays as defense-in-depth so the route is unreachable in a deployed config.
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class IngestController : ControllerBase
{
    private readonly IFixtureIngestService _ingest;
    private readonly IWebHostEnvironment _environment;

    public IngestController(IFixtureIngestService ingest, IWebHostEnvironment environment)
    {
        _ingest = ingest;
        _environment = environment;
    }

    // POST api/ingest/{tournamentId}?season={season}&date={date}
    [HttpPost("{tournamentId:guid}")]
    public async Task<IActionResult> Ingest(
        Guid tournamentId,
        [FromQuery] int season,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        // Dev-only gate — non-dev callers get 404, so the route is not anonymously reachable
        // in a deployed config before F-02 auth lands.
        if (!_environment.IsDevelopment())
            return NotFound();

        try
        {
            var result = await _ingest.IngestTournamentAsync(tournamentId, season, date, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            // Tournament missing or misconfigured (no ExternalApiId) — a caller error, not a 500.
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (FootballApiException ex)
        {
            // Upstream API-Football failure — surface as a bad-gateway, not an opaque 500.
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
