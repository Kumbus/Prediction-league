using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// EF Core League repository. Inherits the generic CRUD base; league-specific queries land
// here as slices need them.
public class LeagueRepository : BaseRepository<League>, ILeagueRepository
{
    public LeagueRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<bool> AnyForTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default)
        => await Set.AnyAsync(l => l.TournamentId == tournamentId, cancellationToken);

    // Organizer OR member — the organizer also gets a membership row at create, but the OR keeps
    // the query correct even if that ever diverges.
    public async Task<IReadOnlyList<League>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Set
            .AsNoTracking()
            .Include(l => l.Memberships)
            .Where(l => l.OrganizerUserId == userId || l.Memberships.Any(m => m.UserId == userId))
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);

    public async Task<League?> GetWithDetailAsync(Guid leagueId, CancellationToken cancellationToken = default)
        => await Set
            .AsNoTracking()
            .Include(l => l.ScoringRules)
            .Include(l => l.Memberships)
            .FirstOrDefaultAsync(l => l.Id == leagueId, cancellationToken);

    public async Task<bool> InviteCodeExistsAsync(string inviteCode, CancellationToken cancellationToken = default)
        => await Set.AnyAsync(l => l.InviteCode == inviteCode, cancellationToken);
}
