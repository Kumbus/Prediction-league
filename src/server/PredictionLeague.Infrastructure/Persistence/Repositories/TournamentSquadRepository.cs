using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// No SaveChanges here — the CSV importer owns the transaction and commits all squad inserts
// together with player upserts.
public class TournamentSquadRepository : ITournamentSquadRepository
{
    private readonly AppDbContext _context;
    private readonly DbSet<TournamentSquad> _set;

    public TournamentSquadRepository(AppDbContext context)
    {
        _context = context;
        _set = context.TournamentSquads;
    }

    public async Task<bool> ExistsAsync(Guid tournamentId, Guid playerId, CancellationToken cancellationToken = default)
        => await _set.AnyAsync(
            s => s.TournamentId == tournamentId && s.PlayerId == playerId,
            cancellationToken);

    public async Task AddAsync(TournamentSquad entry, CancellationToken cancellationToken = default)
        => await _set.AddAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<TournamentSquad>> ListByTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default)
        => await _set
            .Where(s => s.TournamentId == tournamentId)
            .ToListAsync(cancellationToken);
}
