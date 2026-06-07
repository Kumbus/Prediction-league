using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Football;

namespace PredictionLeague.Api.Controllers;

// Guarded on-demand ingest for verifying F-03 before S-02 exists; S-02 later reuses the
// service. Gated to Development only — real auth is F-02. Not a public route in prod.
[ApiController]
[Route("api/[controller]")]
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

        var result = await _ingest.IngestTournamentAsync(tournamentId, season, date, cancellationToken);
        return Ok(result);
    }
}
