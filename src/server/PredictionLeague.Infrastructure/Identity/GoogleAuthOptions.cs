namespace PredictionLeague.Infrastructure.Identity;

// Bound from the "Authentication:Google" configuration section. Real client id/secret
// come from user-secrets (dev) / app settings (prod); never commit them.
public sealed class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
