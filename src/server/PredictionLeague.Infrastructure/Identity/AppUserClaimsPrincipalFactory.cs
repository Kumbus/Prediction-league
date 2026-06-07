using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace PredictionLeague.Infrastructure.Identity;

// Emits the admin claim when ApplicationUser.IsGlobalAdmin is set, so the AdminOnly policy
// (RequireClaim) can authorize without an Identity role. Runs whenever Identity builds the
// principal (sign-in, cookie validation).
public class AppUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    public AppUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.IsGlobalAdmin)
            identity.AddClaim(new Claim(AuthorizationPolicies.AdminClaimType, "true"));

        return identity;
    }
}
