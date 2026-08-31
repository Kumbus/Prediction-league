using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Persistence;

namespace PredictionLeague.Api.Controllers;

// A member-facing read of their league's table (FR-012). A separate controller rather than growing
// LeaguesController, which already owns league identity, scoring config and membership — the same
// split PredictionsController got in S-06.
//
// Visibility is identical to PredictionsController: a league the caller cannot see and a league
// that does not exist both answer 404, so membership never leaks. Permission derives from
// League.OrganizerUserId or the existence of a membership row, never from LeagueMembership.Role,
// which is display metadata that can legitimately drift (lessons.md:32).
[ApiController]
[Route("api/leagues/{leagueId:guid}/standings")]
[Authorize]
public class StandingsController : ControllerBase
{
    private readonly ILeagueRepository _leagues;
    private readonly IPredictionRepository _predictions;

    public StandingsController(
        ILeagueRepository leagues,
        IPredictionRepository predictions)
    {
        _leagues = leagues;
        _predictions = predictions;
    }

    public record StandingRowResponse(
        int Rank,
        Guid UserId,
        string DisplayName,
        int Points,
        int ScoredMatches,
        int PredictionsMade);

    // CallerUserId lets the client highlight the caller's own row without re-deriving identity.
    // It can legitimately match no row: visibility is organizer-*or*-membership while the roster is
    // memberships only, so an organizer who left without transferring the league still sees a table
    // they are not in.
    public record StandingsResponse(
        Guid LeagueId,
        string LeagueName,
        Guid CallerUserId,
        IReadOnlyList<StandingRowResponse> Rows);

    // GET api/leagues/{leagueId}/standings
    [HttpGet]
    public async Task<IActionResult> Get(Guid leagueId, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var league = await _leagues.GetWithDetailAsync(leagueId, cancellationToken);
        if (league is null) return NotFound();
        if (league.OrganizerUserId != userId && league.Memberships.All(m => m.UserId != userId))
            return NotFound();

        var rows = await _predictions.ListStandingsAsync(leagueId, cancellationToken);

        return Ok(new StandingsResponse(league.Id, league.Name, userId, Rank(rows)));
    }

    // Shared rank on ties, with the next distinct total skipping accordingly (1, 2, 2, 4). A tie is
    // a tie; inventing a second key would decide the league on something no rule announced.
    // Computed here, once, so every surface that renders the table agrees.
    private static List<StandingRowResponse> Rank(IReadOnlyList<StandingRowDto> rows)
    {
        var ranked = new List<StandingRowResponse>(rows.Count);
        var rank = 0;
        int? previousPoints = null;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (previousPoints != row.Points)
            {
                rank = i + 1;
                previousPoints = row.Points;
            }

            ranked.Add(new StandingRowResponse(
                rank, row.UserId, row.DisplayName, row.Points, row.ScoredMatches, row.PredictionsMade));
        }

        return ranked;
    }

    // Identity user keys are Guids (F-01), so the NameIdentifier claim parses directly.
    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
