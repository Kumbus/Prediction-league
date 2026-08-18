using Microsoft.Extensions.Logging;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Scoring;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Domain.Scoring;

namespace PredictionLeague.Infrastructure.Scoring;

// Wires the pure Domain engine to data (FR-011): load the match with its events, every league
// bound to its tournament with its rules, and every prediction for that match; compute points per
// (league, prediction); hand the whole map to the repository, which owns the single save.
//
// The service computes only — it never mutates a tracked entity — so a match is never half-scored,
// and running it twice produces the same rows. That idempotence is what lets every result-changing
// path call it: the admin match save, the admin event save, ingest, and the rescore endpoint.
public sealed class MatchScoringService : IMatchScoringService
{
    private static readonly IReadOnlyList<ScoringRule> NoRules = [];

    private readonly IMatchRepository _matches;
    private readonly ILeagueRepository _leagues;
    private readonly IPredictionRepository _predictions;
    private readonly IMatchEventTypeRepository _eventTypes;
    private readonly ILogger<MatchScoringService> _logger;

    public MatchScoringService(
        IMatchRepository matches,
        ILeagueRepository leagues,
        IPredictionRepository predictions,
        IMatchEventTypeRepository eventTypes,
        ILogger<MatchScoringService> logger)
    {
        _matches = matches;
        _leagues = leagues;
        _predictions = predictions;
        _eventTypes = eventTypes;
        _logger = logger;
    }

    public async Task<MatchScoringResult> ScoreMatchAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var match = await _matches.GetWithEventsAsync(matchId, cancellationToken);
        if (match is null)
        {
            _logger.LogWarning("Scoring skipped: match {MatchId} not found.", matchId);
            return MatchScoringResult.None;
        }

        var predictions = await _predictions.ListForMatchAsync(matchId, cancellationToken);
        if (predictions.Count == 0) return MatchScoringResult.None;

        var points = new Dictionary<Guid, int?>(predictions.Count);

        // Not Finished, or missing a score: un-score rather than freeze stale points. Reverting a
        // result has to take its points with it, or a corrected mistake leaves standings asserting
        // something the recorded result no longer says.
        if (match.Status != MatchStatus.Finished || match.HomeScore is null || match.AwayScore is null)
        {
            foreach (var prediction in predictions)
                points[prediction.Id] = null;

            await _predictions.SetAwardedPointsAsync(matchId, points, cancellationToken);

            _logger.LogInformation(
                "Match {MatchId} is not a finished result ({Status}); un-scored {Count} prediction(s).",
                matchId, match.Status, points.Count);

            return new MatchScoringResult(points.Count, DistinctLeagues(predictions));
        }

        var eventTypesById = (await _eventTypes.GetAllAsync(cancellationToken))
            .ToDictionary(t => t.Id);
        var outcome = MatchOutcome.FromMatch(match, eventTypesById);

        // Rules per league, so one prediction is scored against its own league's config and no
        // other. A league on this tournament with no rules configured scores every prediction as 0.
        var rulesByLeague = (await _leagues.ListByTournamentWithRulesAsync(match.TournamentId, cancellationToken))
            .ToDictionary(l => l.Id, l => (IReadOnlyList<ScoringRule>)l.ScoringRules.ToList());

        foreach (var prediction in predictions)
        {
            var rules = rulesByLeague.GetValueOrDefault(prediction.LeagueId, NoRules);

            // Always non-null here, including 0: null means "not scored", 0 means "scored, earned
            // nothing", and standings and the UI depend on that distinction.
            points[prediction.Id] = PredictionScorer.Score(prediction, outcome, rules);
        }

        await _predictions.SetAwardedPointsAsync(matchId, points, cancellationToken);

        var leaguesTouched = DistinctLeagues(predictions);
        _logger.LogInformation(
            "Scored match {MatchId}: {Count} prediction(s) across {Leagues} league(s).",
            matchId, points.Count, leaguesTouched);

        return new MatchScoringResult(points.Count, leaguesTouched);
    }

    private static int DistinctLeagues(IReadOnlyList<Prediction> predictions)
        => predictions.Select(p => p.LeagueId).Distinct().Count();
}
