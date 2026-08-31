using PredictionLeague.Application.Abstractions.Scoring;

namespace PredictionLeague.Api.Scoring;

// The partial-success contract every admin write that scores shares (FR-011).
//
// Scoring runs *after* the match write commits — it reads the match back through a repository, so
// running it first would score the pre-edit result. That ordering means a scoring failure leaves a
// saved result with stale points. Rolling the write back is not on the table, and neither is a 500:
// an admin told "save failed" about a write that *did* land will re-save, and re-saving is exactly
// what does not repair the state. Only the rescore endpoint does.
//
// So the write answers 200 with its normal body plus ScoringFailed and a message naming the
// remedy, and the client renders that as a warning rather than a save error. Same shape as
// PredictionsController.Submit, where a well-formed request that could not be fully applied still
// answers 200 and carries the verdict in the payload.
internal static class ScoringTrigger
{
    // Scores the match, returning null on success or the admin-facing message on failure.
    public static async Task<string?> TryScoreAsync(
        IMatchScoringService scoring,
        ILogger logger,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        try
        {
            await scoring.ScoreMatchAsync(matchId, cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Scoring failed for match {MatchId} after a committed write; points stay stale until a rescore.",
                matchId);

            return $"The match was saved, but its points could not be recalculated. "
                   + $"Re-run scoring with POST /api/matches/{matchId}/rescore.";
        }
    }
}
