namespace PredictionLeague.Application.Abstractions.Predictions;

// A round save lost the unique-index race twice: the first collision is absorbed by re-reading and
// updating, but a third concurrent first-time submit can collide again on the retry. The
// persistence layer translates its provider-specific failure into this, so callers can answer with
// a conflict without knowing about EF Core (lessons.md:25).
public class PredictionConflictException : Exception
{
    public PredictionConflictException(Guid leagueId, Exception innerException)
        : base($"Predictions for league '{leagueId}' were written by a concurrent save.", innerException)
    {
        LeagueId = leagueId;
    }

    public Guid LeagueId { get; }
}
