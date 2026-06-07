using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
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
}
