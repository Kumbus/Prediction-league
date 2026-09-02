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
//
// UnscoredMatchIds carries the run's partial-success verdict, the same one the manual admin write
// reports through ScoringFailed/ScoringMessage (Api/Scoring/ScoringTrigger.cs): a fixture whose
// result committed while scoring threw. Without it the run answers "N fixtures, M events" and
// reads as a clean success while those matches' points stay stale — the failure would exist only
// in a log line nobody is watching. The ids, not just a count, because the remedy is per match:
// POST /api/matches/{id}/rescore.
//
// DroppedEvents / MatchesWithDroppedEvents carry the second half of the same verdict, one layer
// down. A goal or card the mapper could not persist — an unmapped detail, no team, no player — is
// a scoring input that silently went missing: the match still scores, but against a set the API
// said was bigger. CorrectGoalScorer and the card rules read exactly what survived here, so a run
// that reports only EventsUpserted cannot be told apart from one where nothing was dropped. The
// remedy is per match too — add the missing event in the admin editor, then rescore — so the
// matches are named, and the count comes with them because "3 matches" and "3 matches, 47 events"
// are not the same incident. Subst/Var are filtered, not dropped: they are not modelled at all.
public sealed record IngestResult(
    int FixturesUpserted,
    int EventsUpserted,
    int ApiCallsUsed,
    int? QuotaRemaining,
    IReadOnlyList<Guid> UnscoredMatchIds,
    int DroppedEvents,
    IReadOnlyList<Guid> MatchesWithDroppedEvents);
