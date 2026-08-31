using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Api.Scoring;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Scoring;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Identity;

namespace PredictionLeague.Api.Controllers;

// Routes that hang off a single match (FR-005, FR-011): the admin goal/card editor, its two
// lookups, and the rescore escape hatch. A separate controller rather than a sixth match route on
// TournamentsController, which already owns tournaments, matches and CSV import — and whose
// /api/matches/... routes are absolute anyway, which is exactly the seam this split removes. Moving
// the existing GET/PUT/DELETE /api/matches/{matchId} off TournamentsController is out of scope;
// this only stops adding to the pile.
//
// The class is [Authorize] and each route adds AdminOnly for itself: authorization attributes are
// additive, so a class-level AdminOnly could not be relaxed for the event-type dictionary, which is
// reference data every signed-in user's screens can read.
[ApiController]
[Route("api/matches")]
[Authorize]
public class MatchesController : ControllerBase
{
    private const int MaxMinute = 130;
    private const int MaxMinuteExtra = 30;

    private readonly IMatchRepository _matches;
    private readonly IMatchEventTypeRepository _eventTypes;
    private readonly IPlayerRepository _players;
    private readonly IMatchScoringService _scoring;
    private readonly ILogger<MatchesController> _logger;

    public MatchesController(
        IMatchRepository matches,
        IMatchEventTypeRepository eventTypes,
        IPlayerRepository players,
        IMatchScoringService scoring,
        ILogger<MatchesController> logger)
    {
        _matches = matches;
        _eventTypes = eventTypes;
        _players = players;
        _scoring = scoring;
        _logger = logger;
    }

    public record RescoreResponse(Guid MatchId, int PredictionsScored, int LeaguesTouched);

    public record MatchEventTypeResponse(int Id, string Code, string DisplayName, MatchEventCategory Category);

    public record EligiblePlayerResponse(Guid PlayerId, string Name, Guid TeamId);

    public record MatchEventItemRequest(
        int MatchEventTypeId,
        Guid PlayerId,
        Guid TeamId,
        int Minute,
        int? MinuteExtra);

    public record ReplaceMatchEventsRequest(IReadOnlyList<MatchEventItemRequest> Events);

    // The saved set read back, plus the partial-success verdict (see Api/Scoring/ScoringTrigger.cs).
    public record MatchEventsResponse(
        IReadOnlyList<MatchEventEditDto> Events,
        bool ScoringFailed = false,
        string? ScoringMessage = null);

    // GET api/matches/{matchId}/events — the match's goals and cards for the editor.
    [HttpGet("{matchId:guid}/events")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> GetEvents(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _matches.GetByIdAsync(matchId, cancellationToken);
        if (match is null) return NotFound();

        return Ok(new MatchEventsResponse(await _matches.ListEventsForEditAsync(matchId, cancellationToken)));
    }

    // PUT api/matches/{matchId}/events — the whole event list replaces the stored one, mirroring
    // how ingest already rebuilds a match's event set, so both writers share one semantic.
    // Re-scores after the save: entering a match's goals is what gives CorrectGoalScorer and the
    // card rules anything to score against, so it must not need a second admin action.
    [HttpPut("{matchId:guid}/events")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> ReplaceEvents(
        Guid matchId,
        ReplaceMatchEventsRequest request,
        CancellationToken cancellationToken)
    {
        var match = await _matches.GetByIdAsync(matchId, cancellationToken);
        if (match is null) return NotFound();

        var items = request?.Events ?? [];

        var typeIds = (await _eventTypes.GetAllAsync(cancellationToken)).Select(t => t.Id).ToHashSet();
        var eligible = await LoadEligiblePlayersAsync(match, cancellationToken);
        var eligibleIds = eligible.Select(p => p.PlayerId).ToHashSet();

        var events = new List<MatchEvent>(items.Count);
        foreach (var item in items)
        {
            var invalid = Validate(item, match, typeIds, eligibleIds);
            if (invalid is not null)
                return Problem(detail: invalid, statusCode: StatusCodes.Status400BadRequest);

            events.Add(new MatchEvent
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                MatchEventTypeId = item.MatchEventTypeId,
                PlayerId = item.PlayerId,
                TeamId = item.TeamId,
                Minute = item.Minute,
                MinuteExtra = item.MinuteExtra
            });
        }

        await _matches.ReplaceEventsAsync(matchId, events, cancellationToken);
        await _matches.SaveChangesAsync(cancellationToken);

        // After the save, never before — scoring reads the events back through a repository.
        var scoringMessage = await ScoringTrigger.TryScoreAsync(_scoring, _logger, matchId, cancellationToken);

        return Ok(new MatchEventsResponse(
            await _matches.ListEventsForEditAsync(matchId, cancellationToken),
            scoringMessage is not null,
            scoringMessage));
    }

    // GET api/matches/{matchId}/eligible-players — both squads, from the same source as the
    // member's scorer picker, so the two can never disagree about who is eligible.
    [HttpGet("{matchId:guid}/eligible-players")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> EligiblePlayers(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _matches.GetByIdAsync(matchId, cancellationToken);
        if (match is null) return NotFound();

        var eligible = await LoadEligiblePlayersAsync(match, cancellationToken);
        return Ok(eligible.Select(p => new EligiblePlayerResponse(p.PlayerId, p.Name, p.TeamId)).ToList());
    }

    // GET api/match-event-types — the seeded dictionary for the type dropdown. Authenticated only:
    // it is reference data with nothing sensitive in it. Absolute route; it is not a match sub-path.
    [HttpGet("/api/match-event-types")]
    public async Task<IActionResult> EventTypes(CancellationToken cancellationToken)
    {
        var types = await _eventTypes.GetAllAsync(cancellationToken);
        return Ok(types
            .OrderBy(t => t.Id)
            .Select(t => new MatchEventTypeResponse(t.Id, t.Code, t.DisplayName, t.Category))
            .ToList());
    }

    // POST api/matches/{matchId}/rescore — the escape hatch for the one failure this design can
    // produce: a result that committed while scoring failed. No body; scoring is a pure function of
    // what is recorded, so the only input is the match id.
    //
    // Existence is checked here, not in the service: an unknown id is a no-op result there by
    // design (so the triggers stay exception-free), which cannot be told apart from a match with no
    // predictions. The controller has to make that distinction to answer 404.
    [HttpPost("{matchId:guid}/rescore")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Rescore(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _matches.GetByIdAsync(matchId, cancellationToken);
        if (match is null) return NotFound();

        var result = await _scoring.ScoreMatchAsync(matchId, cancellationToken);

        return Ok(new RescoreResponse(matchId, result.PredictionsScored, result.LeaguesTouched));
    }

    // Both squads as one list, deduplicated: a player attached to both sides (club one, national
    // the other) appears once, and the credited team is a separate field anyway.
    private async Task<IReadOnlyList<EligibleScorerDto>> LoadEligiblePlayersAsync(Match match, CancellationToken cancellationToken)
    {
        var byTeam = await _players.ListEligibleScorersByTeamAsync(
            match.TournamentId,
            [match.HomeTeamId, match.AwayTeamId],
            cancellationToken);

        var home = byTeam.TryGetValue(match.HomeTeamId, out var h) ? h : [];
        var away = byTeam.TryGetValue(match.AwayTeamId, out var a) ? a : [];
        if (home.Count == 0) return away;

        var onHome = home.Select(s => s.PlayerId).ToHashSet();
        return [.. home, .. away.Where(s => !onHome.Contains(s.PlayerId))];
    }

    // Each rejection names what is wrong: a mis-entered event scores silently and wrongly, and the
    // admin has no standings-side signal that would tell them which row to fix.
    private static string? Validate(
        MatchEventItemRequest item,
        Match match,
        HashSet<int> typeIds,
        HashSet<Guid> eligiblePlayerIds)
    {
        if (!typeIds.Contains(item.MatchEventTypeId))
            return "That event type does not exist.";

        if (item.TeamId != match.HomeTeamId && item.TeamId != match.AwayTeamId)
            return "The credited team must be one of the two teams playing.";

        // Deliberately not required to agree with the player's own team: a player credited to the
        // opposing side is exactly how an own goal is recorded.
        if (!eligiblePlayerIds.Contains(item.PlayerId))
            return "That player is not in either team's squad for this match.";

        if (item.Minute < 0 || item.Minute > MaxMinute)
            return $"Minute must be between 0 and {MaxMinute}.";

        if (item.MinuteExtra is < 0 or > MaxMinuteExtra)
            return $"Added time must be between 0 and {MaxMinuteExtra}.";

        return null;
    }
}
