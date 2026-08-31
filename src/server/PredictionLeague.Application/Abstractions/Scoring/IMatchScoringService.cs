namespace PredictionLeague.Application.Abstractions.Scoring;

// What one scoring run did. Reported by the rescore endpoint and logged by the ingest path;
// LeaguesTouched counts the leagues on the match's tournament, PredictionsScored the rows written.
public record MatchScoringResult(int PredictionsScored, int LeaguesTouched)
{
    // The answer for a match id that resolves to nothing — see IMatchScoringService.
    public static readonly MatchScoringResult None = new(0, 0);
}

// The one entry point everything calls to (re)score a match (FR-011). Lives in Application so both
// the Api controllers and Infrastructure's ingest service can depend on it without either
// depending on the other.
//
// Idempotent: calling it twice in a row leaves identical rows, which is what makes it safe to hang
// off every path that can change a result — the admin match save, the admin event save, ingest,
// and the explicit rescore endpoint.
public interface IMatchScoringService
{
    // Scores every prediction on the match across every league bound to its tournament, in one
    // save. A match id that does not exist is a no-op result (MatchScoringResult.None), not an
    // exception — the caller that cares about existence checks for it (the rescore endpoint does,
    // so it can answer 404).
    Task<MatchScoringResult> ScoreMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
}
