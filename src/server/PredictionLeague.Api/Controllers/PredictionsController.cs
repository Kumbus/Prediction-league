using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Api.Controllers;

// The member-facing prediction surface for one league (FR-009, FR-010, FR-002): read a round with
// the caller's own forecasts, write a round as a batch, and read everyone's forecasts once a match
// has kicked off. Kept out of LeaguesController, which already owns league identity, scoring config
// and membership.
//
// Two rules run through every route here:
//   * The lock is server-side and lives in exactly one expression (IsLocked). "now" is captured
//     once per request, so every item in a batch is judged against the same instant.
//   * Which optional fields exist is derived from the league's ScoringRules, never from the
//     request — per-league custom scoring stays authoritative over the input surface.
//
// Visibility mirrors LeaguesController: a league the caller cannot see and a league that does not
// exist both answer 404, so membership never leaks.
[ApiController]
[Route("api/leagues/{leagueId:guid}/predictions")]
[Authorize]
public class PredictionsController : ControllerBase
{
    private const int MaxScore = 99;
    private const int MaxCards = 99;

    private readonly ILeagueRepository _leagues;
    private readonly IMatchRepository _matches;
    private readonly IPredictionRepository _predictions;
    private readonly IPlayerRepository _players;

    public PredictionsController(
        ILeagueRepository leagues,
        IMatchRepository matches,
        IPredictionRepository predictions,
        IPlayerRepository players)
    {
        _leagues = leagues;
        _matches = matches;
        _predictions = predictions;
        _players = players;
    }

    public enum PredictionItemStatus
    {
        Saved,
        Locked,
        Invalid
    }

    // A round in the tournament, ordered by its earliest kickoff — never by the string, which is
    // free text an admin types. IsCurrent marks the round in play and does not move when the
    // caller switches rounds.
    public record RoundRefResponse(string Round, DateTimeOffset EarliestKickoffUtc, bool IsCurrent);

    public record EligibleScorerResponse(Guid PlayerId, string Name, Guid TeamId);

    public record OwnPredictionResponse(
        int HomeScore,
        int AwayScore,
        Guid? FirstScorerPlayerId,
        Guid? FirstScorerTeamId,
        int? TotalCards,
        int? YellowCards,
        int? RedCards,
        DateTimeOffset SubmittedUtc);

    public record MatchPredictionRowResponse(
        Guid MatchId,
        string Round,
        DateTimeOffset KickoffUtc,
        MatchStatus Status,
        Guid HomeTeamId,
        string HomeTeamName,
        int? HomeScore,
        Guid AwayTeamId,
        string AwayTeamName,
        int? AwayScore,
        // The server's verdict on whether this match is still writable. The client renders it; it
        // never derives the lock from a local clock.
        bool CanPredict,
        OwnPredictionResponse? Prediction,
        // Present only when the league scores CorrectGoalScorer and the match is still writable.
        IReadOnlyList<EligibleScorerResponse>? Scorers);

    public record RoundViewResponse(
        Guid LeagueId,
        string LeagueName,
        string? Round,
        IReadOnlyList<RoundRefResponse> Rounds,
        // The parameters this league scores — the client renders exactly these inputs.
        IReadOnlyList<ScoringParameter> ScoredParameters,
        IReadOnlyList<MatchPredictionRowResponse> Matches);

    public record PredictionItemRequest(
        Guid MatchId,
        int HomeScore,
        int AwayScore,
        Guid? FirstScorerPlayerId,
        Guid? FirstScorerTeamId,
        int? TotalCards,
        int? YellowCards,
        int? RedCards);

    public record SubmitPredictionsRequest(IReadOnlyList<PredictionItemRequest> Items);

    public record PredictionOutcomeResponse(Guid MatchId, PredictionItemStatus Status, string? Detail);

    public record SubmitPredictionsResponse(
        IReadOnlyList<PredictionOutcomeResponse> Outcomes,
        RoundViewResponse Round);

    public record RevealedPredictionResponse(
        Guid MatchId,
        Guid UserId,
        string DisplayName,
        int HomeScore,
        int AwayScore,
        Guid? FirstScorerPlayerId,
        string? FirstScorerName,
        Guid? FirstScorerTeamId,
        string? FirstScorerTeamName,
        int? TotalCards,
        int? YellowCards,
        int? RedCards);

    public record RevealedRoundResponse(string? Round, IReadOnlyList<RevealedPredictionResponse> Predictions);

    // GET api/leagues/{leagueId}/predictions?round=<name> — the round view. With no round the
    // server picks the one holding the earliest unfinished match, falling back to the last round
    // when the tournament is over.
    [HttpGet]
    public async Task<IActionResult> GetRound(
        Guid leagueId,
        [FromQuery] string? round,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var league = await LoadVisibleLeagueAsync(leagueId, userId, cancellationToken);
        if (league is null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        var matches = await _matches.ListForPredictionsAsync(league.TournamentId, cancellationToken);

        var selected = SelectRound(matches, round, now);
        if (round is not null && selected is null && matches.Count > 0)
            return Problem(detail: "That round does not exist in this tournament.", statusCode: StatusCodes.Status404NotFound);

        return Ok(await BuildRoundViewAsync(league, userId, matches, selected, now, cancellationToken));
    }

    // POST api/leagues/{leagueId}/predictions — write a round as one batch. Always 200 when the
    // request itself is well-formed: the per-item outcomes carry the verdict, because a match that
    // kicked off mid-form is a fact about that match, not a malformed request. A wholly rejected
    // batch is still 200 with every item marked.
    [HttpPost]
    public async Task<IActionResult> Submit(
        Guid leagueId,
        SubmitPredictionsRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var league = await LoadVisibleLeagueAsync(leagueId, userId, cancellationToken);
        if (league is null) return NotFound();

        var items = request?.Items ?? [];
        if (items.Count == 0)
            return Problem(detail: "At least one prediction is required.", statusCode: StatusCodes.Status400BadRequest);

        // One clock for the whole batch. Reading UtcNow per item would let the boundary move
        // between two lines of the same loop, accepting match A and rejecting match B on a
        // difference no user could see.
        var now = DateTimeOffset.UtcNow;
        var matches = await _matches.ListForPredictionsAsync(league.TournamentId, cancellationToken);
        var matchesById = matches.ToDictionary(m => m.MatchId);
        var scored = league.ScoringRules.Select(r => r.Parameter).ToHashSet();

        var outcomes = new List<PredictionOutcomeResponse>(items.Count);
        var toWrite = new List<Prediction>();
        var seen = new HashSet<Guid>();

        foreach (var item in items)
        {
            if (!seen.Add(item.MatchId))
            {
                outcomes.Add(new PredictionOutcomeResponse(item.MatchId, PredictionItemStatus.Invalid,
                    "This match appears twice in the batch."));
                continue;
            }

            // A match id from another tournament (or none at all) is bad input, not a server fault.
            if (!matchesById.TryGetValue(item.MatchId, out var match))
            {
                outcomes.Add(new PredictionOutcomeResponse(item.MatchId, PredictionItemStatus.Invalid,
                    "That match is not part of this league's tournament."));
                continue;
            }

            if (IsLocked(match, now))
            {
                outcomes.Add(new PredictionOutcomeResponse(item.MatchId, PredictionItemStatus.Locked,
                    $"{match.HomeTeam.Name} v {match.AwayTeam.Name} kicked off — predictions are closed."));
                continue;
            }

            var invalid = await ValidateItemAsync(item, match, scored, league.TournamentId, cancellationToken);
            if (invalid is not null)
            {
                outcomes.Add(new PredictionOutcomeResponse(item.MatchId, PredictionItemStatus.Invalid, invalid));
                continue;
            }

            toWrite.Add(new Prediction
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                UserId = userId,
                MatchId = item.MatchId,
                PredictedHomeScore = item.HomeScore,
                PredictedAwayScore = item.AwayScore,
                PredictedFirstScorerPlayerId = item.FirstScorerPlayerId,
                PredictedFirstScorerTeamId = item.FirstScorerTeamId,
                PredictedTotalCards = item.TotalCards,
                PredictedYellowCards = item.YellowCards,
                PredictedRedCards = item.RedCards,
                SubmittedUtc = now
            });
            outcomes.Add(new PredictionOutcomeResponse(item.MatchId, PredictionItemStatus.Saved, null));
        }

        // One save for the whole round, so a partially-accepted batch cannot land half-written.
        await _predictions.UpsertManyAsync(league.Id, userId, toWrite, cancellationToken);

        // The refreshed view is the round the batch belongs to — read back from the server so the
        // client replaces its local draft with what was actually stored.
        var submittedRound = items
            .Select(i => matchesById.TryGetValue(i.MatchId, out var m) ? m.Round : null)
            .FirstOrDefault(r => r is not null);
        var selected = SelectRound(matches, submittedRound, now);
        var view = await BuildRoundViewAsync(league, userId, matches, selected, now, cancellationToken);

        return Ok(new SubmitPredictionsResponse(outcomes, view));
    }

    // GET api/leagues/{leagueId}/predictions/revealed?round=<name> — every member's forecasts for
    // the round's matches that have kicked off. A match before kickoff is absent from the response
    // entirely rather than present-but-empty: absence is what "not revealed yet" means, and there
    // is nothing in the payload for a devtools reader to find.
    [HttpGet("revealed")]
    public async Task<IActionResult> GetRevealed(
        Guid leagueId,
        [FromQuery] string? round,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var league = await LoadVisibleLeagueAsync(leagueId, userId, cancellationToken);
        if (league is null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        var matches = await _matches.ListForPredictionsAsync(league.TournamentId, cancellationToken);

        var selected = SelectRound(matches, round, now);
        if (round is not null && selected is null && matches.Count > 0)
            return Problem(detail: "That round does not exist in this tournament.", statusCode: StatusCodes.Status404NotFound);

        var revealedMatchIds = matches
            .Where(m => m.Round == selected && IsLocked(m, now))
            .Select(m => m.MatchId)
            .ToList();

        var predictions = await _predictions.ListForMatchesAsync(league.Id, revealedMatchIds, cancellationToken);

        return Ok(new RevealedRoundResponse(
            selected,
            predictions
                .Select(p => new RevealedPredictionResponse(
                    p.MatchId,
                    p.UserId,
                    p.DisplayName,
                    p.PredictedHomeScore,
                    p.PredictedAwayScore,
                    p.FirstScorerPlayerId,
                    p.FirstScorerName,
                    p.FirstScorerTeamId,
                    p.FirstScorerTeamName,
                    p.TotalCards,
                    p.YellowCards,
                    p.RedCards))
                .ToList()));
    }

    // The one expression the whole slice depends on. Fed by a single projection on both the read
    // and the write path, so the two can never disagree about when a match closed.
    private static bool IsLocked(MatchRoundDto match, DateTimeOffset now) => match.KickoffUtc <= now;

    // A league the caller can see: they organize it or hold a membership row. Membership existence
    // is the check — no permission derives from LeagueMembership.Role, which is display metadata
    // that can legitimately drift (lessons.md:32).
    private async Task<League?> LoadVisibleLeagueAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken)
    {
        var league = await _leagues.GetWithDetailAsync(leagueId, cancellationToken);
        if (league is null) return null;
        if (league.OrganizerUserId != userId && league.Memberships.All(m => m.UserId != userId)) return null;
        return league;
    }

    // Requested round when it exists, otherwise the round in play: the one holding the earliest
    // match that has not finished, falling back to the last round when every match has.
    private static string? SelectRound(IReadOnlyList<MatchRoundDto> matches, string? requested, DateTimeOffset now)
    {
        if (matches.Count == 0) return null;

        if (requested is not null)
            return matches.Any(m => m.Round == requested) ? requested : null;

        var nextUp = matches
            .Where(m => m.Status != MatchStatus.Finished)
            .OrderBy(m => m.KickoffUtc)
            .ThenBy(m => m.MatchId)
            .FirstOrDefault();
        if (nextUp is not null) return nextUp.Round;

        // Everything has finished — land on the last round played, ordered the way the switcher
        // orders rounds (earliest kickoff), never by the free-text name.
        return OrderedRounds(matches).LastOrDefault()?.Round;
    }

    // Rounds ordered by their earliest kickoff. Match.Round is free text an admin types, so a typo
    // produces its own section rather than reordering the tournament.
    private static List<RoundRefResponse> OrderedRounds(IReadOnlyList<MatchRoundDto> matches)
        => matches
            .GroupBy(m => m.Round, StringComparer.Ordinal)
            .Select(g => new RoundRefResponse(g.Key, g.Min(m => m.KickoffUtc), false))
            .OrderBy(r => r.EarliestKickoffUtc)
            .ThenBy(r => r.Round, StringComparer.Ordinal)
            .ToList();

    private async Task<RoundViewResponse> BuildRoundViewAsync(
        League league,
        Guid userId,
        IReadOnlyList<MatchRoundDto> matches,
        string? selectedRound,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The "current" marker is the round in play, computed independently of what the caller is
        // looking at — switching rounds must not move it.
        var currentRound = SelectRound(matches, null, now);
        var rounds = OrderedRounds(matches)
            .Select(r => r with { IsCurrent = r.Round == currentRound })
            .ToList();

        var scored = league.ScoringRules.Select(r => r.Parameter).OrderBy(p => p).ToList();
        var scoresGoalScorer = scored.Contains(ScoringParameter.CorrectGoalScorer);

        var roundMatches = matches.Where(m => m.Round == selectedRound).ToList();
        var own = (await _predictions.ListForUserAsync(
                league.Id, userId, roundMatches.Select(m => m.MatchId).ToList(), cancellationToken))
            .ToDictionary(p => p.MatchId);

        var rows = new List<MatchPredictionRowResponse>(roundMatches.Count);
        foreach (var match in roundMatches)
        {
            var canPredict = !IsLocked(match, now);

            // Candidates are only meaningful where a scorer can still be picked; a locked row
            // renders read-only, and after kickoff the reveal surface carries the names instead.
            IReadOnlyList<EligibleScorerResponse>? scorers = null;
            if (scoresGoalScorer && canPredict)
                scorers = (await _players.ListEligibleScorersAsync(
                        league.TournamentId, match.HomeTeam.Id, match.AwayTeam.Id, cancellationToken))
                    .Select(s => new EligibleScorerResponse(s.PlayerId, s.Name, s.TeamId))
                    .ToList();

            rows.Add(new MatchPredictionRowResponse(
                match.MatchId,
                match.Round,
                match.KickoffUtc,
                match.Status,
                match.HomeTeam.Id,
                match.HomeTeam.Name,
                match.HomeTeam.Score,
                match.AwayTeam.Id,
                match.AwayTeam.Name,
                match.AwayTeam.Score,
                canPredict,
                own.TryGetValue(match.MatchId, out var p)
                    ? new OwnPredictionResponse(
                        p.PredictedHomeScore,
                        p.PredictedAwayScore,
                        p.PredictedFirstScorerPlayerId,
                        p.PredictedFirstScorerTeamId,
                        p.PredictedTotalCards,
                        p.PredictedYellowCards,
                        p.PredictedRedCards,
                        p.SubmittedUtc)
                    : null,
                scorers));
        }

        return new RoundViewResponse(league.Id, league.Name, selectedRound, rounds, scored, rows);
    }

    // Rules-driven field validation. A field is accepted only if the league scores the matching
    // parameter, and required on exactly the same condition — silently dropping an unscored field
    // would let a member believe they had forecast something the league will never award.
    private async Task<string?> ValidateItemAsync(
        PredictionItemRequest item,
        MatchRoundDto match,
        HashSet<ScoringParameter> scored,
        Guid tournamentId,
        CancellationToken cancellationToken)
    {
        if (item.HomeScore < 0 || item.HomeScore > MaxScore || item.AwayScore < 0 || item.AwayScore > MaxScore)
            return $"Scores must be between 0 and {MaxScore}.";

        var cardError =
            ValidateCard(ScoringParameter.CorrectCardCount, item.TotalCards, "Total cards", scored)
            ?? ValidateCard(ScoringParameter.CorrectYellowCards, item.YellowCards, "Yellow cards", scored)
            ?? ValidateCard(ScoringParameter.CorrectRedCards, item.RedCards, "Red cards", scored);
        if (cardError is not null) return cardError;

        if (!scored.Contains(ScoringParameter.CorrectGoalScorer))
            return item.FirstScorerPlayerId is not null || item.FirstScorerTeamId is not null
                ? "This league does not score the first goal scorer."
                : null;

        // The forecast is a *pair*: which player scores, and which side the goal is credited to.
        // One without the other is not a forecast — a player with no credited team cannot be
        // scored, and a credited team with no player says nothing.
        if (item.FirstScorerPlayerId is null || item.FirstScorerTeamId is null)
            return "Pick both a first scorer and the team the goal is credited to.";

        if (item.FirstScorerTeamId != match.HomeTeam.Id && item.FirstScorerTeamId != match.AwayTeam.Id)
            return "The credited team must be one of the two teams playing.";

        var eligible = await _players.ListEligibleScorersAsync(
            tournamentId, match.HomeTeam.Id, match.AwayTeam.Id, cancellationToken);
        if (eligible.All(s => s.PlayerId != item.FirstScorerPlayerId))
            return "That player is not in either team's squad for this match.";

        // Deliberately not required to agree: a player from one team credited to the other is an
        // own-goal forecast, which is exactly the shape MatchEvent records.
        return null;
    }

    private static string? ValidateCard(ScoringParameter parameter, int? value, string label, HashSet<ScoringParameter> scored)
    {
        if (!scored.Contains(parameter))
            return value is null ? null : $"This league does not score {label.ToLowerInvariant()}.";
        if (value is null) return $"{label} is required.";
        return value < 0 || value > MaxCards ? $"{label} must be between 0 and {MaxCards}." : null;
    }

    // Identity user keys are Guids (F-01), so the NameIdentifier claim parses directly.
    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
