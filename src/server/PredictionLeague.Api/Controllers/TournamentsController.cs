using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Identity;

namespace PredictionLeague.Api.Controllers;

// CRUD-by-policy for Tournament (FR-003). Reads need a signed-in user; non-admins see only
// published tournaments. Writes are AdminOnly. ExternalApiId is immutable post-create so the
// F-03 ingest lookup (GetByExternalApiIdAsync) stays deterministic.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TournamentsController : ControllerBase
{
    private readonly ITournamentRepository _tournaments;
    private readonly ILeagueRepository _leagues;

    public TournamentsController(ITournamentRepository tournaments, ILeagueRepository leagues)
    {
        _tournaments = tournaments;
        _leagues = leagues;
    }

    public record TournamentResponse(
        Guid Id,
        string Name,
        string? ExternalApiId,
        int Season,
        DateOnly StartDate,
        DateOnly EndDate,
        bool IsPublished);

    public record CreateTournamentRequest(
        string Name,
        string? ExternalApiId,
        int Season,
        DateOnly StartDate,
        DateOnly EndDate);

    public record UpdateTournamentRequest(
        string Name,
        int Season,
        DateOnly StartDate,
        DateOnly EndDate);

    public record PublishTournamentRequest(bool IsPublished);

    // GET api/tournaments — admins see all; everyone else only published.
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var list = await _tournaments.ListAsync(IsAdmin(), cancellationToken);
        return Ok(list.Select(ToResponse));
    }

    // GET api/tournaments/{id} — 404 for non-admins on drafts (no information leak).
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.GetByIdAsync(id, cancellationToken);
        if (tournament is null) return NotFound();
        if (!tournament.IsPublished && !IsAdmin()) return NotFound();
        return Ok(ToResponse(tournament));
    }

    // POST api/tournaments — admin creates a draft.
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Create(CreateTournamentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem(detail: "Name is required.", statusCode: StatusCodes.Status400BadRequest);
        if (request.EndDate < request.StartDate)
            return Problem(detail: "EndDate must be on or after StartDate.", statusCode: StatusCodes.Status400BadRequest);

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ExternalApiId = string.IsNullOrWhiteSpace(request.ExternalApiId) ? null : request.ExternalApiId,
            Season = request.Season,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsPublished = false
        };

        await _tournaments.AddAsync(tournament, cancellationToken);
        await _tournaments.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = tournament.Id }, ToResponse(tournament));
    }

    // PUT api/tournaments/{id} — admin edits name/season/dates. ExternalApiId is immutable
    // post-create; sending it is silently ignored.
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Update(Guid id, UpdateTournamentRequest request, CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.GetByIdAsync(id, cancellationToken);
        if (tournament is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem(detail: "Name is required.", statusCode: StatusCodes.Status400BadRequest);
        if (request.EndDate < request.StartDate)
            return Problem(detail: "EndDate must be on or after StartDate.", statusCode: StatusCodes.Status400BadRequest);

        tournament.Name = request.Name;
        tournament.Season = request.Season;
        tournament.StartDate = request.StartDate;
        tournament.EndDate = request.EndDate;

        _tournaments.Update(tournament);
        await _tournaments.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(tournament));
    }

    // PATCH api/tournaments/{id}/publish — flip visibility.
    [HttpPatch("{id:guid}/publish")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> SetPublish(Guid id, PublishTournamentRequest request, CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.GetByIdAsync(id, cancellationToken);
        if (tournament is null) return NotFound();

        tournament.IsPublished = request.IsPublished;
        _tournaments.Update(tournament);
        await _tournaments.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // DELETE api/tournaments/{id} — blocked when any League references the tournament (S-03).
    // EF cascades Matches/MatchEvents/TournamentSquads; Teams/Players are global.
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.GetByIdAsync(id, cancellationToken);
        if (tournament is null) return NotFound();

        if (await _leagues.AnyForTournamentAsync(id, cancellationToken))
            return Problem(
                detail: "Cannot delete a tournament that has leagues.",
                statusCode: StatusCodes.Status409Conflict);

        _tournaments.Remove(tournament);
        await _tournaments.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private bool IsAdmin() => User.HasClaim(AuthorizationPolicies.AdminClaimType, "true");

    private static TournamentResponse ToResponse(Tournament t)
        => new(t.Id, t.Name, t.ExternalApiId, t.Season, t.StartDate, t.EndDate, t.IsPublished);
}
