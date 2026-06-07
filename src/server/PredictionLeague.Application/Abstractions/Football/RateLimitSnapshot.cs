namespace PredictionLeague.Application.Abstractions.Football;

// Rate-limit headers surfaced from a response so the ingest service can stop a run before
// burning the free-tier budget. Null when the header was absent (e.g. a 204).
public sealed record RateLimitSnapshot(int? DailyRemaining, int? MinuteRemaining)
{
    public static readonly RateLimitSnapshot Unknown = new(null, null);
}
