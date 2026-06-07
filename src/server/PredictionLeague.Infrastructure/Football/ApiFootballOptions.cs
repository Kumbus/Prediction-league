namespace PredictionLeague.Infrastructure.Football;

// Bound from the "ApiFootball" configuration section. Real key comes from user-secrets
// (dev) / app settings (prod); never commit it.
public sealed class ApiFootballOptions
{
    public const string SectionName = "ApiFootball";

    public string BaseUrl { get; set; } = "https://v3.football.api-sports.io";

    public string ApiKey { get; set; } = string.Empty;
}
