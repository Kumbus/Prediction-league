namespace PredictionLeague.Infrastructure.Identity;

// Bound from configuration section "Admin". Emails listed here are auto-promoted to
// IsGlobalAdmin on next sign-in (local or external). Case-insensitive.
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string[] Emails { get; set; } = [];
}
