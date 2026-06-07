namespace PredictionLeague.Infrastructure.Identity;

// Global-admin authorization is expressed as a claim-backed policy rather than an Identity
// role: organizer/member stay per-league via LeagueMembership (FR-002). The admin claim is
// emitted by AppUserClaimsPrincipalFactory from ApplicationUser.IsGlobalAdmin.
public static class AuthorizationPolicies
{
    // Policy name applied via [Authorize(Policy = AuthorizationPolicies.AdminOnly)].
    public const string AdminOnly = "AdminOnly";

    // Claim type carrying the global-admin grant. Stable string — persisted only in the
    // issued cookie, never in the DB.
    public const string AdminClaimType = "prediction:admin";
}
