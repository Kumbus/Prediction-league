using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Identity;

namespace PredictionLeague.Infrastructure.Persistence;

// EF Core context, Identity-aware with Guid keys so existing *UserId Guids line up
// with AspNetUsers.Id. Exposes the S-02/S-03 domain aggregates plus Prediction, which
// S-06 brought into the model.
public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<League> Leagues => Set<League>();
    public DbSet<LeagueMembership> LeagueMemberships => Set<LeagueMembership>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchEvent> MatchEvents => Set<MatchEvent>();
    public DbSet<ScoringRule> ScoringRules => Set<ScoringRule>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<MatchEventType> MatchEventTypes => Set<MatchEventType>();
    public DbSet<Nationality> Nationalities => Set<Nationality>();
    public DbSet<TournamentSquad> TournamentSquads => Set<TournamentSquad>();
    public DbSet<Prediction> Predictions => Set<Prediction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
