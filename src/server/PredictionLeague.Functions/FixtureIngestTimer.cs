using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PredictionLeague.Application.Abstractions.Football;
using PredictionLeague.Application.Abstractions.Persistence;

namespace PredictionLeague.Functions;

// Scheduled production trigger: iterate active tournaments and ingest today's slate via the
// shared service. CRON comes from the FixtureIngestSchedule app setting so it can be tuned
// to match windows without a redeploy (never live-15s under the free-tier cap).
public class FixtureIngestTimer
{
    private readonly IFixtureIngestService _ingest;
    private readonly ITournamentRepository _tournaments;
    private readonly ILogger<FixtureIngestTimer> _logger;

    public FixtureIngestTimer(
        IFixtureIngestService ingest,
        ITournamentRepository tournaments,
        ILogger<FixtureIngestTimer> logger)
    {
        _ingest = ingest;
        _tournaments = tournaments;
        _logger = logger;
    }

    [Function(nameof(FixtureIngestTimer))]
    public async Task Run([TimerTrigger("%FixtureIngestSchedule%")] TimerInfo timer, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var active = await _tournaments.GetActiveAsync(today, cancellationToken);

        _logger.LogInformation("FixtureIngestTimer fired; {Count} active tournament(s).", active.Count);

        foreach (var tournament in active)
        {
            try
            {
                var result = await _ingest.IngestTournamentAsync(
                    tournament.Id, tournament.Season, today, cancellationToken);

                _logger.LogInformation(
                    "Ingested tournament {TournamentId}: {Fixtures} fixtures, {Events} events, quota remaining {Quota}.",
                    tournament.Id, result.FixturesUpserted, result.EventsUpserted, result.QuotaRemaining);

                // The run's partial-success verdict. Unattended, this log line is the only place it
                // can surface, so it must not hide inside the Information line that says the ingest
                // worked: those matches hold a saved result with stale points until someone
                // rescores them.
                if (result.UnscoredMatchIds.Count > 0)
                {
                    _logger.LogWarning(
                        "Tournament {TournamentId}: {Count} match(es) ingested but not scored — rescore each with POST /api/matches/{{id}}/rescore: {MatchIds}.",
                        tournament.Id, result.UnscoredMatchIds.Count, string.Join(", ", result.UnscoredMatchIds));
                }

                // Same reasoning for the events the mapper could not persist: those matches scored
                // against an incomplete set, so their points can be wrong without anything failing.
                if (result.DroppedEvents > 0)
                {
                    _logger.LogWarning(
                        "Tournament {TournamentId}: {Count} goal/card event(s) dropped across {Matches} match(es) — those points were computed without them: {MatchIds}.",
                        tournament.Id, result.DroppedEvents, result.MatchesWithDroppedEvents.Count,
                        string.Join(", ", result.MatchesWithDroppedEvents));
                }
            }
            catch (Exception ex)
            {
                // One tournament's failure must not abort the others.
                _logger.LogError(ex, "Ingest failed for tournament {TournamentId}.", tournament.Id);
            }
        }
    }
}
