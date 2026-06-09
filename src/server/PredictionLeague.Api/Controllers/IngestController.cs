using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Football;
using PredictionLeague.Infrastructure.Football;
using PredictionLeague.Infrastructure.Identity;

namespace PredictionLeague.Api.Controllers;

// On-demand ingest used by the admin verification page. Gated by the AdminOnly policy (F-02);
// the dev-only 404 guard from the F-03 walking-skeleton phase has been removed now that real
// auth + admin promotion (S-02 Phase 1) is in place.
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class IngestController : ControllerBase
{
    private readonly IFixtureIngestService _ingest;

    public IngestController(IFixtureIngestService ingest)
    {
        _ingest = ingest;
    }

    // POST api/ingest/{tournamentId}?season={season}&date={date}
    [HttpPost("{tournamentId:guid}")]
    public async Task<IActionResult> Ingest(
        Guid tournamentId,
        [FromQuery] int season,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
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
