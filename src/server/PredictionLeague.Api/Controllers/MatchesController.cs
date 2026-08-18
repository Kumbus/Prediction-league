using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Scoring;
using PredictionLeague.Infrastructure.Identity;

namespace PredictionLeague.Api.Controllers;

// Admin-facing routes that hang off a single match (FR-005, FR-011). A separate controller rather
// than a sixth match route on TournamentsController, which already owns tournaments, matches and
// CSV import — and whose /api/matches/... routes are absolute anyway, which is exactly the seam
// this split removes. Moving the existing GET/PUT/DELETE /api/matches/{matchId} off
// TournamentsController is out of scope; this only stops adding to the pile.
[ApiController]
[Route("api/matches")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class MatchesController : ControllerBase
{
    private readonly IMatchRepository _matches;
    private readonly IMatchScoringService _scoring;

    public MatchesController(
        IMatchRepository matches,
        IMatchScoringService scoring)
    {
        _matches = matches;
        _scoring = scoring;
    }

    public record RescoreResponse(Guid MatchId, int PredictionsScored, int LeaguesTouched);

    // POST api/matches/{matchId}/rescore — the escape hatch for the one failure this design can
    // produce: a result that committed while scoring failed. No body; scoring is a pure function of
    // what is recorded, so the only input is the match id.
    //
    // Existence is checked here, not in the service: an unknown id is a no-op result there by
    // design (so the triggers stay exception-free), which cannot be told apart from a match with no
    // predictions. The controller has to make that distinction to answer 404.
    [HttpPost("{matchId:guid}/rescore")]
    public async Task<IActionResult> Rescore(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _matches.GetByIdAsync(matchId, cancellationToken);
        if (match is null) return NotFound();

        var result = await _scoring.ScoreMatchAsync(matchId, cancellationToken);

        return Ok(new RescoreResponse(matchId, result.PredictionsScored, result.LeaguesTouched));
    }
}
