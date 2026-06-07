using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using PredictionLeague.Application.Abstractions.Football;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Infrastructure.Football;
using PredictionLeague.Infrastructure.Persistence;
using PredictionLeague.Infrastructure.Persistence.Repositories;

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
