using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PredictionLeague.Api.Configuration;
using PredictionLeague.Infrastructure.Identity;

namespace PredictionLeague.Api.Controllers;

// F-02 local credential surface: register/login/logout establish the Identity cookie session;
// `me` inspects it. Google external login (challenge/callback) is added in Phase 3. No SPA UI
// here — that is S-01; this is the API the UI will call.
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly SpaCorsOptions _corsOptions;
    private readonly IAdminEmailAllowlist _adminAllowlist;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAuthenticationSchemeProvider schemeProvider,
        IOptions<SpaCorsOptions> corsOptions,
        IAdminEmailAllowlist adminAllowlist)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _schemeProvider = schemeProvider;
        _corsOptions = corsOptions.Value;
        _adminAllowlist = adminAllowlist;
    }

    public record RegisterRequest(string Email, string Password, string DisplayName);
    public record LoginRequest(string Email, string Password);

    // POST api/auth/register — create a local account and sign it in.
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Surface Identity's validation errors (duplicate email, weak password, …) as a
            // ProblemDetails rather than an opaque 400.
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        await EnsureAdminClaimAsync(user);
        return Ok();
    }

    // POST api/auth/login — establish a session from email/password.
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await _signInManager.PasswordSignInAsync(
            request.Email, request.Password, isPersistent: false, lockoutOnFailure: false);

        if (!result.Succeeded)
            return Unauthorized();

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is not null)
            await EnsureAdminClaimAsync(user);

        return Ok();
    }

    // POST api/auth/logout — clear the session cookie.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    // GET api/auth/me — the signed-in user's profile; 401 when anonymous.
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            displayName = user.DisplayName,
            isGlobalAdmin = user.IsGlobalAdmin
        });
    }

    // GET api/auth/login/google?returnUrl= — start the server-side Google redirect flow.
    // The handler returns to ExternalCallback (which carries returnUrl through) after consent.
    [HttpGet("login/google")]
    public async Task<IActionResult> LoginGoogle([FromQuery] string? returnUrl)
    {
        // The Google scheme is registered only when credentials are configured (see
        // AddAuthenticationAndIdentity). Without them, fail cleanly instead of 500-ing on a
        // Challenge against an unknown scheme.
        if (await _schemeProvider.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null)
            return Problem(detail: "Google sign-in is not configured.", statusCode: StatusCodes.Status501NotImplemented);

        var redirectUri = Url.Action(nameof(ExternalCallback), "Auth", new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme, redirectUri);
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    // GET api/auth/external-callback?returnUrl= — complete the flow: sign in an existing linked
    // user, or provision one on first login (link the AspNetUserLogins row), then bounce the
    // browser back to a validated SPA returnUrl.
    [HttpGet("external-callback")]
    public async Task<IActionResult> ExternalCallback([FromQuery] string? returnUrl)
    {
        var target = ResolveReturnUrl(returnUrl);

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
            return Redirect(AppendError(target, "external_login_failed"));

        // Already linked? Sign in against the existing AspNetUserLogins row.
        var signIn = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (signIn.Succeeded)
        {
            var linkedUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (linkedUser is not null)
                await EnsureAdminClaimAsync(linkedUser);
            return Redirect(target);
        }

        // First login — create the local user from Google claims and persist the login link.
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return Redirect(AppendError(target, "no_email"));

        // A local (or otherwise unlinked) account already owns this email — do NOT auto-link
        // Google to it. The local side has no email-ownership proof (no confirmation flow yet),
        // so auto-linking by email is an account-takeover vector. Linking must instead be
        // initiated from an already-authenticated session (future work).
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
            return Redirect(AppendError(target, "account_exists"));

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email
        };
        var created = await _userManager.CreateAsync(user);
        if (!created.Succeeded)
            return Redirect(AppendError(target, "provisioning_failed"));

        var linked = await _userManager.AddLoginAsync(user, info);
        if (!linked.Succeeded)
            return Redirect(AppendError(target, "link_failed"));

        await _signInManager.SignInAsync(user, isPersistent: false);
        await EnsureAdminClaimAsync(user);
        return Redirect(target);
    }

    // Reconcile IsGlobalAdmin with the Admin:Emails allowlist on every sign-in: promote when the
    // email is listed, revoke when it isn't. Refresh the sign-in cookie so the admin claim is
    // re-emitted by AppUserClaimsPrincipalFactory in the current session. Idempotent: a user
    // already in the correct state is left alone.
    private async Task EnsureAdminClaimAsync(ApplicationUser user)
    {
        var shouldBeAdmin = _adminAllowlist.IsAdmin(user.Email);
        if (shouldBeAdmin == user.IsGlobalAdmin)
            return;

        user.IsGlobalAdmin = shouldBeAdmin;
        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return;

        await _signInManager.RefreshSignInAsync(user);
    }

    // Open-redirect guard: only bounce back to a same-origin local path or a configured SPA
    // origin (Cors:AllowedOrigins); anything else falls back to the first configured origin.
    private string ResolveReturnUrl(string? returnUrl)
    {
        var allowed = _corsOptions.AllowedOrigins;
        var fallback = allowed.FirstOrDefault() ?? "/";

        if (string.IsNullOrWhiteSpace(returnUrl))
            return fallback;

        if (Url.IsLocalUrl(returnUrl))
            return returnUrl;

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            var origin = uri.GetLeftPart(UriPartial.Authority);
            if (allowed.Any(o => string.Equals(o.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
                return returnUrl;
        }

        return fallback;
    }

    private static string AppendError(string url, string code)
        => url + (url.Contains('?') ? '&' : '?') + "error=" + code;
}
