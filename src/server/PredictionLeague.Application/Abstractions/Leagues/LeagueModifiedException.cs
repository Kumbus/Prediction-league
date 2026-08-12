namespace PredictionLeague.Application.Abstractions.Leagues;

// The league changed between the read that backed this write and the write itself — the
// concurrency token on League rejected it. The persistence layer translates its provider-specific
// failure into this, so callers can answer with a conflict without knowing about EF Core.
public class LeagueModifiedException : Exception
{
    public LeagueModifiedException(Guid leagueId, Exception innerException)
        : base($"League '{leagueId}' was modified by someone else.", innerException)
    {
        LeagueId = leagueId;
    }

    public Guid LeagueId { get; }
}
