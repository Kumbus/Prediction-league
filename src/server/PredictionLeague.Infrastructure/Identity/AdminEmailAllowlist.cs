using Microsoft.Extensions.Options;

namespace PredictionLeague.Infrastructure.Identity;

// Hashes the configured Admin:Emails list once at construction; lookups are O(1) and
// case-insensitive. Empty/whitespace input → false.
public class AdminEmailAllowlist : IAdminEmailAllowlist
{
    private readonly HashSet<string> _emails;

    public AdminEmailAllowlist(IOptions<AdminOptions> options)
    {
        _emails = new HashSet<string>(
            options.Value.Emails ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAdmin(string? email)
        => !string.IsNullOrWhiteSpace(email) && _emails.Contains(email);
}
