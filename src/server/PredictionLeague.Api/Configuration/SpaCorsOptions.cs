namespace PredictionLeague.Api.Configuration;

// Bound from the "Cors" configuration section. Single source of truth for the SPA origins
// allowed to send credentialed (cookie) requests — consumed by both the CORS policy
// (Program.cs) and the external-login open-redirect guard (AuthController) so the two
// can never diverge. Named SpaCorsOptions to avoid clashing with ASP.NET's CorsOptions.
public sealed class SpaCorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}
