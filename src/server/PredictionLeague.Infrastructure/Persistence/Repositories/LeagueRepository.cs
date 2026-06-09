using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// EF Core League repository. Inherits the generic CRUD base; league-specific queries land
// here as slices need them (none for F-01).
public class LeagueRepository : BaseRepository<League>, ILeagueRepository
{
    public LeagueRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<bool> AnyForTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default)
        => await Set.AnyAsync(l => l.TournamentId == tournamentId, cancellationToken);
}
