namespace PredictionLeague.Application.Abstractions.Football;

// The single ingest seam both hosts call (Functions timer + Api manual trigger). One
// mapping/upsert implementation lives behind this.
public interface IFixtureIngestService
{
    // Ingests a tournament's fixtures + events for a day (date defaults to today UTC).
    // season is supplied by the caller (query param for the endpoint, Tournament.Season
    // for the timer).
    Task<IngestResult> IngestTournamentAsync(Guid tournamentId, int season, DateOnly? date, CancellationToken cancellationToken = default);
}

// Counts surfaced to the endpoint and logs after a run.
public sealed record IngestResult(
    int FixturesUpserted,
    int EventsUpserted,
    int ApiCallsUsed,
    int? QuotaRemaining);
