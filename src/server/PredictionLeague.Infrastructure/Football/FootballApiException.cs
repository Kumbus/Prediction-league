namespace PredictionLeague.Infrastructure.Football;

// Thrown when API-Football returns a logical error (non-empty errors[]) even on HTTP 200,
// or an unexpected non-success status. Surfaces the envelope messages, not a raw crash.
public sealed class FootballApiException : Exception
{
    public FootballApiException(string message) : base(message)
    {
    }
}
