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
            }
            catch (Exception ex)
            {
                // One tournament's failure must not abort the others.
                _logger.LogError(ex, "Ingest failed for tournament {TournamentId}.", tournament.Id);
            }
        }
    }
}
