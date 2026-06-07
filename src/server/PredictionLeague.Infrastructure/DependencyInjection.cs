using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PredictionLeague.Application.Abstractions.Persistence;
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
}
