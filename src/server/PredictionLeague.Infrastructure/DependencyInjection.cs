using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using PredictionLeague.Application.Abstractions.Football;
using PredictionLeague.Application.Abstractions.Leagues;
using PredictionLeague.Application.Abstractions.Matches;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Players;
using PredictionLeague.Application.Abstractions.Scoring;
using PredictionLeague.Infrastructure.Football;
using PredictionLeague.Infrastructure.Identity;
using PredictionLeague.Infrastructure.Leagues;
using PredictionLeague.Infrastructure.Persistence;
using PredictionLeague.Infrastructure.Matches;
using PredictionLeague.Infrastructure.Persistence.Repositories;
using PredictionLeague.Infrastructure.Players;
using PredictionLeague.Infrastructure.Scoring;

namespace PredictionLeague.Infrastructure;

// One call the host uses to register the EF Core context + repositories, keeping Program.cs thin.
// Connection string is read here; consumed at host startup (Phase 4).
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured. Set it via user-secrets (dev) or an app setting (prod).");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<ILeagueRepository, LeagueRepository>();
        services.AddScoped<ITournamentRepository, TournamentRepository>();
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IMatchEventTypeRepository, MatchEventTypeRepository>();
        services.AddScoped<INationalityRepository, NationalityRepository>();
        services.AddScoped<ITournamentSquadRepository, TournamentSquadRepository>();
        services.AddScoped<IPredictionRepository, PredictionRepository>();
        services.AddScoped<IMatchScoringService, MatchScoringService>();
        services.AddScoped<IPlayerCsvImporter, CsvHelperPlayerImporter>();
        services.AddScoped<IMatchCsvImporter, CsvHelperMatchImporter>();
        services.AddScoped<IInviteCodeGenerator, RandomInviteCodeGenerator>();

        return services;
    }

    // One call the host uses to wire authentication: ASP.NET Core Identity (cookie schemes +
    // SignInManager/UserManager over AppDbContext), the Google external handler, the admin
    // claims factory, and the AdminOnly authorization policy. Mirrors AddInfrastructure /
    // AddFootballIngest so Program.cs stays thin. CORS is a host concern and lives in Program.cs.
    public static IServiceCollection AddAuthenticationAndIdentity(this IServiceCollection services, IConfiguration config)
    {
        var googleSection = config.GetSection(GoogleAuthOptions.SectionName);
        services.Configure<GoogleAuthOptions>(googleSection);
        var googleOptions = googleSection.Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // Emit the admin claim from ApplicationUser.IsGlobalAdmin (replaces Identity's default
        // factory — last registration wins when Identity resolves the service).
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, AppUserClaimsPrincipalFactory>();

        // AddIdentity already set the default schemes + application cookie. Register Google only
        // when credentials are present: Google is a remote (request-handler) scheme that the
        // authentication middleware initializes on every request, and OAuthOptions.Validate()
        // throws on an empty ClientId — which would 500 every endpoint. With no creds (default dev
        // state pre-Phase-3) the scheme stays unregistered and the rest of the API works; Google
        // login activates once user-secrets supply the id/secret.
        if (!string.IsNullOrWhiteSpace(googleOptions.ClientId)
            && !string.IsNullOrWhiteSpace(googleOptions.ClientSecret))
        {
            services
                .AddAuthentication()
                .AddGoogle(o =>
                {
                    o.ClientId = googleOptions.ClientId;
                    o.ClientSecret = googleOptions.ClientSecret;
                    o.SaveTokens = true;
                });
        }

        // Cross-origin SPA cookie: SameSite=None + Secure so the cookie flows from the Vite origin.
        // Requires HTTPS (use the https launch profile locally). No OnRedirectToLogin override —
        // .NET 10 returns 401/403 for API endpoints instead of redirecting.
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.SameSite = SameSiteMode.None;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromDays(7);
            o.SlidingExpiration = true;
        });

        services.AddAuthorization(o =>
            o.AddPolicy(AuthorizationPolicies.AdminOnly, p =>
                p.RequireClaim(AuthorizationPolicies.AdminClaimType)));

        services.Configure<AdminOptions>(config.GetSection(AdminOptions.SectionName));
        services.AddSingleton<IAdminEmailAllowlist, AdminEmailAllowlist>();

        return services;
    }

    // Registers the API-Football typed client (auth + base address from ApiFootball options)
    // with a conservative, transient-only resilience pipeline. Both hosts (Api endpoint +
    // Functions timer) call this. Polly retry stays limited — never tight-retry the
    // free-tier per-minute limit.
    public static IServiceCollection AddFootballIngest(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ApiFootballOptions>(config.GetSection(ApiFootballOptions.SectionName));

        services.AddScoped<IFixtureIngestService, FixtureIngestService>();

        services.AddHttpClient<IFootballApiClient, FootballApiClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<ApiFootballOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Add("x-apisports-key", options.ApiKey);
            })
            .AddResilienceHandler("football", builder =>
            {
                builder
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        // Default ShouldHandle covers transient 5xx/408/HttpRequestException/
                        // timeout and honors Retry-After. Kept to 2 attempts so a per-minute
                        // 429 is not hammered.
                        MaxRetryAttempts = 2,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromSeconds(2)
                    })
                    .AddTimeout(TimeSpan.FromSeconds(15));
            });

        return services;
    }
}
