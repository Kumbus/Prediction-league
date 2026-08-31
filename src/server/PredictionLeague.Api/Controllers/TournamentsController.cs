using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Api.Scoring;
using PredictionLeague.Application.Abstractions;
using PredictionLeague.Application.Abstractions.Matches;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Scoring;
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
    private const long CsvSizeCapBytes = 2 * 1024 * 1024;

    private readonly ITournamentRepository _tournaments;
    private readonly ILeagueRepository _leagues;
    private readonly IMatchRepository _matches;
    private readonly ITeamRepository _teams;
    private readonly IMatchCsvImporter _matchImporter;
    private readonly IMatchScoringService _scoring;
    private readonly ILogger<TournamentsController> _logger;

    public TournamentsController(
        ITournamentRepository tournaments,
        ILeagueRepository leagues,
        IMatchRepository matches,
        ITeamRepository teams,
        IMatchCsvImporter matchImporter,
        IMatchScoringService scoring,
        ILogger<TournamentsController> logger)
    {
        _tournaments = tournaments;
        _leagues = leagues;
        _matches = matches;
        _teams = teams;
        _matchImporter = matchImporter;
        _scoring = scoring;
        _logger = logger;
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

    // GET api/tournaments/{id}/matches — admin sees regardless of publish; non-admin gets 404
    // on a draft (no information leak). Empty tournament returns [].
    [HttpGet("{id:guid}/matches")]
    public async Task<IActionResult> Matches(Guid id, CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.GetByIdAsync(id, cancellationToken);
        if (tournament is null) return NotFound();
        if (!tournament.IsPublished && !IsAdmin()) return NotFound();

        var matches = await _matches.ListByTournamentAsync(id, cancellationToken);
        return Ok(matches);
    }

    // ---- Manual match entry (interim data source while paid ingest is deferred) ----------------

    // ScoringFailed / ScoringMessage carry the partial-success verdict on the two write endpoints
    // (see Api/Scoring/ScoringTrigger.cs). They default to "fine" so the read endpoint, which never
    // scores, keeps its shape.
    public record MatchDetailResponse(
        Guid Id,
        Guid TournamentId,
        Guid HomeTeamId,
        Guid AwayTeamId,
        DateTimeOffset KickoffUtc,
        MatchStatus Status,
        int? HomeScore,
        int? AwayScore,
        string Round,
        bool ScoringFailed = false,
        string? ScoringMessage = null);

    public record CreateMatchRequest(
        Guid HomeTeamId,
        Guid AwayTeamId,
        DateTimeOffset KickoffUtc,
        MatchStatus Status,
        int? HomeScore,
        int? AwayScore,
        string? Round);

    public record UpdateMatchRequest(
        Guid HomeTeamId,
        Guid AwayTeamId,
        DateTimeOffset KickoffUtc,
        MatchStatus Status,
        int? HomeScore,
        int? AwayScore,
        string? Round);

    // POST api/tournaments/{id}/matches — admin enters a match by hand. ExternalFixtureId stays
    // NULL so it never collides with an ingested fixture.
    [HttpPost("{id:guid}/matches")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> CreateMatch(Guid id, CreateMatchRequest request, CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.GetByIdAsync(id, cancellationToken);
        if (tournament is null) return NotFound();

        var validation = await ValidateMatchAsync(request.HomeTeamId, request.AwayTeamId, request.Status,
            request.HomeScore, request.AwayScore, request.Round, cancellationToken);
        if (validation is not null) return validation;

        var match = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = id,
            ExternalFixtureId = null,
            Season = tournament.Season,
            Round = request.Round!.Trim(),
            HomeTeamId = request.HomeTeamId,
            AwayTeamId = request.AwayTeamId,
            KickoffUtc = request.KickoffUtc,
            Status = request.Status,
            HomeScore = request.HomeScore,
            AwayScore = request.AwayScore
        };

        await _matches.AddAsync(match, cancellationToken);
        await _matches.SaveChangesAsync(cancellationToken);

        // A match created straight into Finished can already have predictions in a league whose
        // tournament this is — score after the save, never before.
        var scoringMessage = await ScoringTrigger.TryScoreAsync(_scoring, _logger, match.Id, cancellationToken);

        return CreatedAtAction(nameof(GetMatch), new { matchId = match.Id }, ToMatchResponse(match, scoringMessage));
    }

    // GET api/matches/{matchId} — single match for the edit form (admin only).
    [HttpGet("/api/matches/{matchId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> GetMatch(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _matches.GetByIdAsync(matchId, cancellationToken);
        return match is null ? NotFound() : Ok(ToMatchResponse(match));
    }

    // PUT api/matches/{matchId} — edit a match (score/status/kickoff/teams/round).
    [HttpPut("/api/matches/{matchId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> UpdateMatch(Guid matchId, UpdateMatchRequest request, CancellationToken cancellationToken)
    {
        var match = await _matches.GetByIdAsync(matchId, cancellationToken);
        if (match is null) return NotFound();

        var validation = await ValidateMatchAsync(request.HomeTeamId, request.AwayTeamId, request.Status,
            request.HomeScore, request.AwayScore, request.Round, cancellationToken);
        if (validation is not null) return validation;

        match.HomeTeamId = request.HomeTeamId;
        match.AwayTeamId = request.AwayTeamId;
        match.KickoffUtc = request.KickoffUtc;
        match.Status = request.Status;
        match.HomeScore = request.HomeScore;
        match.AwayScore = request.AwayScore;
        match.Round = request.Round!.Trim();

        _matches.Update(match);
        await _matches.SaveChangesAsync(cancellationToken);

        // The result may just have changed — score after the save so the recomputation sees the
        // edit, not the row it replaced.
        var scoringMessage = await ScoringTrigger.TryScoreAsync(_scoring, _logger, match.Id, cancellationToken);

        return Ok(ToMatchResponse(match, scoringMessage));
    }

    // DELETE api/matches/{matchId} — remove a match; EF cascades its events.
    [HttpDelete("/api/matches/{matchId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> DeleteMatch(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _matches.GetByIdAsync(matchId, cancellationToken);
        if (match is null) return NotFound();

        _matches.Remove(match);
        await _matches.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // POST api/tournaments/{id}/matches/import — bulk manual matches via CSV. dryRun previews;
    // teams are resolved by name and auto-created. Mirrors the player CSV import flow.
    [HttpPost("{id:guid}/matches/import")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [RequestSizeLimit(CsvSizeCapBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = CsvSizeCapBytes)]
    public async Task<IActionResult> ImportMatches(
        Guid id,
        IFormFile file,
        [FromQuery] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        var tournament = await _tournaments.GetByIdAsync(id, cancellationToken);
        if (tournament is null) return NotFound();

        if (file is null || file.Length == 0)
            return Problem(detail: "CSV file is required.", statusCode: StatusCodes.Status400BadRequest);

        await using var stream = file.OpenReadStream();
        try
        {
            if (dryRun)
                return Ok(await _matchImporter.PreviewAsync(id, stream, cancellationToken));

            return Ok(await _matchImporter.CommitAsync(id, stream, cancellationToken));
        }
        catch (CsvImportException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    // Shared shape checks for create/edit: distinct existing teams; a Finished match needs scores;
    // a round name is required. Round used to default to the literal "Manual", which with manual
    // entry as the primary data source put every match of a tournament in one round — and the
    // predictions screen navigates, fills and saves *by round* (S-06).
    private async Task<IActionResult?> ValidateMatchAsync(
        Guid homeTeamId, Guid awayTeamId, MatchStatus status, int? homeScore, int? awayScore,
        string? round, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(round))
            return Problem(detail: "Round is required.", statusCode: StatusCodes.Status400BadRequest);

        if (homeTeamId == awayTeamId)
            return Problem(detail: "Home and away teams must differ.", statusCode: StatusCodes.Status400BadRequest);

        if (await _teams.GetByIdAsync(homeTeamId, cancellationToken) is null)
            return Problem(detail: "Home team not found.", statusCode: StatusCodes.Status400BadRequest);
        if (await _teams.GetByIdAsync(awayTeamId, cancellationToken) is null)
            return Problem(detail: "Away team not found.", statusCode: StatusCodes.Status400BadRequest);

        if (status == MatchStatus.Finished && (homeScore is null || awayScore is null))
            return Problem(detail: "A finished match needs both scores.", statusCode: StatusCodes.Status400BadRequest);

        if ((homeScore is < 0) || (awayScore is < 0))
            return Problem(detail: "Scores cannot be negative.", statusCode: StatusCodes.Status400BadRequest);

        return null;
    }

    private static MatchDetailResponse ToMatchResponse(Match m, string? scoringMessage = null)
        => new(m.Id, m.TournamentId, m.HomeTeamId, m.AwayTeamId, m.KickoffUtc, m.Status,
            m.HomeScore, m.AwayScore, m.Round, scoringMessage is not null, scoringMessage);

    private bool IsAdmin() => User.HasClaim(AuthorizationPolicies.AdminClaimType, "true");

    private static TournamentResponse ToResponse(Tournament t)
        => new(t.Id, t.Name, t.ExternalApiId, t.Season, t.StartDate, t.EndDate, t.IsPublished);
}
