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
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(config.GetConnectionString("DefaultConnection")));

        services.AddScoped<ILeagueRepository, LeagueRepository>();

        return services;
    }
}
